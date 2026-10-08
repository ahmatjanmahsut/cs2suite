-- ============================================================================
-- CS2Suite 整合插件 — MySQL 数据库结构 (v1.0.0)
-- 兼容 MySQL 8.0+ / MariaDB 10.6+
--
-- 说明:
--   * 所有表使用 cs2suite_ 前缀,插件启动时会自动建表(幂等),
--     本文件用于手工初始化、审查与 Web 端部署。
--   * steamid64 一律存 64 位 SteamID 字符串,避免 JS/C# 精度问题。
--   * 换肤/贴纸数据全部落在本库:Web 端写入,插件端读取并在玩家
--     连接/重生/换枪时应用(与 WeaponPaints / AstraSkins 相同机制)。
--   * 绑定码表实现"服务器生成码 -> Web 输入码 -> SteamID 绑定账号"流程。
-- ============================================================================

SET NAMES utf8mb4;

CREATE DATABASE IF NOT EXISTS cs2suite DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
-- 若使用独立库名,请同步修改插件/Web 配置文件中的 Database 字段。
USE cs2suite;

-- ----------------------------------------------------------------------------
-- 1. Web 账号
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS cs2suite_accounts (
  account_id        INT UNSIGNED NOT NULL AUTO_INCREMENT,
  username          VARCHAR(32)  NOT NULL COMMENT '登录名',
  email             VARCHAR(190) NULL COMMENT '可选,找回密码用',
  password_hash     VARCHAR(255) NOT NULL COMMENT 'bcrypt 摘要',
  role              ENUM('user','admin') NOT NULL DEFAULT 'user',
  status            ENUM('active','banned') NOT NULL DEFAULT 'active',
  created_at        DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  last_login_at     DATETIME NULL,
  PRIMARY KEY (account_id),
  UNIQUE KEY uq_username (username),
  UNIQUE KEY uq_email (email)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 一个账号可绑多个 SteamID;每个 SteamID 只归属一个账号。
CREATE TABLE IF NOT EXISTS cs2suite_account_steam (
  steamid64         VARCHAR(20)  NOT NULL,
  account_id        INT UNSIGNED NOT NULL,
  persona           VARCHAR(64)  NULL COMMENT '绑定时游戏内昵称快照',
  is_primary        TINYINT(1)   NOT NULL DEFAULT 1,
  bound_at          DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (steamid64),
  KEY idx_account (account_id),
  CONSTRAINT fk_steam_account FOREIGN KEY (account_id)
    REFERENCES cs2suite_accounts (account_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ----------------------------------------------------------------------------
-- 2. 服务器内绑定码 (/cs2bind 生成,Web /api/bind 消费,15 分钟有效)
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS cs2suite_bind_codes (
  code              CHAR(6)      NOT NULL COMMENT '大写字母+数字,去除易混字符',
  steamid64         VARCHAR(20)  NOT NULL COMMENT '服务器内发起绑定的玩家',
  persona           VARCHAR(64)  NULL,
  issued_at         DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  expires_at        DATETIME NOT NULL,
  used_by_account   INT UNSIGNED NULL,
  used_at           DATETIME NULL,
  PRIMARY KEY (code),
  KEY idx_steam (steamid64),
  CONSTRAINT fk_bind_account FOREIGN KEY (used_by_account)
    REFERENCES cs2suite_accounts (account_id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ----------------------------------------------------------------------------
-- 3. 自定义外观(武器皮肤/贴纸/探员/手套/音乐包/徽章)
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS cs2suite_loadouts (
  steamid64         VARCHAR(20) NOT NULL,
  updated_at        DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (steamid64)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT '外观行存在即代表该玩家已启用自定义外观';

-- 虚拟 defindex 约定(与插件 Skins/Loadout.cs 一致):
--   -1 手套(knife_target_defindex=手套def)  -2 探员(nametag=模型名)
--   -3 音乐包(knife_target_defindex=musicId) -4 徽章(knife_target_defindex=1..16)
-- 刀: weapon_defindex=42 基准, knife_target_defindex 存目标刀型(如 M9=503)
CREATE TABLE IF NOT EXISTS cs2suite_loadout_items (
  steamid64           VARCHAR(20) NOT NULL,
  weapon_defindex     INT         NOT NULL,
  knife_target_defindex INT       NOT NULL DEFAULT 0,
  paintkit            INT         NOT NULL DEFAULT 0,
  paint_seed          INT         NOT NULL DEFAULT 0,
  paint_wear          FLOAT       NOT NULL DEFAULT 0.12,
  nametag             VARCHAR(128) NULL,
  stattrak            TINYINT(1)  NOT NULL DEFAULT 0,
  stattrak_count      INT         NOT NULL DEFAULT 0,
  keychain_id         INT         NOT NULL DEFAULT 0 COMMENT '挂饰 charm id',
  updated_at          DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (steamid64, weapon_defindex),
  CONSTRAINT fk_item_steam FOREIGN KEY (steamid64)
    REFERENCES cs2suite_loadouts (steamid64) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 贴纸: 每件武器最多 5 槽(0-4),支持偏移/旋转/缩放/独立磨损(参考 WeaponPaints)。
CREATE TABLE IF NOT EXISTS cs2suite_loadout_stickers (
  steamid64         VARCHAR(20) NOT NULL,
  weapon_defindex   INT         NOT NULL,
  slot              TINYINT     NOT NULL,
  sticker_id        INT         NOT NULL COMMENT '游戏内 sticker 模板 id',
  offset_x          FLOAT       NOT NULL DEFAULT 0,
  offset_y          FLOAT       NOT NULL DEFAULT 0,
  rotation          FLOAT       NOT NULL DEFAULT 0,
  scale             FLOAT       NOT NULL DEFAULT 1.0,
  wear              FLOAT       NOT NULL DEFAULT 0.12,
  PRIMARY KEY (steamid64, weapon_defindex, slot),
  CONSTRAINT fk_sticker_steam FOREIGN KEY (steamid64)
    REFERENCES cs2suite_loadouts (steamid64) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- CT/T 分套外观(WeaponPaints 同款行为)。team: 2=T 3=CT
CREATE TABLE IF NOT EXISTS cs2suite_loadout_teams (
  steamid64           VARCHAR(20) NOT NULL,
  team                TINYINT     NOT NULL,
  weapon_defindex     INT         NOT NULL,
  knife_target_defindex INT       NOT NULL DEFAULT 0,
  paintkit            INT         NOT NULL DEFAULT 0,
  paint_seed          INT         NOT NULL DEFAULT 0,
  paint_wear          FLOAT       NOT NULL DEFAULT 0.12,
  nametag             VARCHAR(128) NULL,
  stattrak            TINYINT(1)  NOT NULL DEFAULT 0,
  stattrak_count      INT         NOT NULL DEFAULT 0,
  keychain_id         INT         NOT NULL DEFAULT 0,
  updated_at          DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (steamid64, team, weapon_defindex),
  CONSTRAINT fk_team_steam FOREIGN KEY (steamid64)
    REFERENCES cs2suite_loadouts (steamid64) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ----------------------------------------------------------------------------
-- 4. 玩家偏好(死斗音效/HUD/自动排队等 JSON 开关)
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS cs2suite_player_prefs (
  steamid64         VARCHAR(20) NOT NULL,
  prefs             JSON        NOT NULL,
  updated_at        DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (steamid64)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ----------------------------------------------------------------------------
-- 5. 死斗统计与排行榜
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS cs2suite_dm_stats (
  steamid64         VARCHAR(20) NOT NULL,
  persona           VARCHAR(64) NULL,
  kills             INT UNSIGNED NOT NULL DEFAULT 0,
  deaths            INT UNSIGNED NOT NULL DEFAULT 0,
  headshots         INT UNSIGNED NOT NULL DEFAULT 0,
  killstreak_best   INT UNSIGNED NOT NULL DEFAULT 0,
  points            INT UNSIGNED NOT NULL DEFAULT 0 COMMENT '击杀得分(爆头+1)',
  sessions          INT UNSIGNED NOT NULL DEFAULT 0,
  updated_at        DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (steamid64)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS cs2suite_dm_rounds (
  round_id          INT UNSIGNED NOT NULL AUTO_INCREMENT COMMENT '一场死斗会话',
  map_name          VARCHAR(32)  NOT NULL,
  started_at        DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  ended_at          DATETIME NULL,
  PRIMARY KEY (round_id),
  KEY idx_started (started_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS cs2suite_dm_scores (
  round_id          INT UNSIGNED NOT NULL,
  steamid64         VARCHAR(20) NOT NULL,
  persona           VARCHAR(64) NULL,
  kills             INT NOT NULL DEFAULT 0,
  deaths            INT NOT NULL DEFAULT 0,
  points            INT NOT NULL DEFAULT 0,
  PRIMARY KEY (round_id, steamid64),
  CONSTRAINT fk_score_round FOREIGN KEY (round_id)
    REFERENCES cs2suite_dm_rounds (round_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ----------------------------------------------------------------------------
-- 6. 满十竞技 (5v5): 队列流水 / Elo / 比赛 / 比赛成员
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS cs2suite_queue_log (
  log_id            BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  event             ENUM('join','leave','start','cancel','ready','notready','kick') NOT NULL,
  steamid64         VARCHAR(20) NOT NULL,
  persona           VARCHAR(64) NULL,
  map_name          VARCHAR(32) NULL,
  created_at        DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (log_id),
  KEY idx_steam_time (steamid64, created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS cs2suite_elo (
  steamid64         VARCHAR(20) NOT NULL,
  elo               INT NOT NULL DEFAULT 1200,
  wins              INT UNSIGNED NOT NULL DEFAULT 0,
  losses            INT UNSIGNED NOT NULL DEFAULT 0,
  draws             INT UNSIGNED NOT NULL DEFAULT 0,
  updated_at        DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (steamid64),
  KEY idx_elo (elo)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS cs2suite_matches (
  match_id          INT UNSIGNED NOT NULL AUTO_INCREMENT,
  map_name          VARCHAR(32) NOT NULL,
  started_at        DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  ended_at          DATETIME NULL,
  score1            INT NOT NULL DEFAULT 0 COMMENT '蓝队(高Elo均分队)',
  score2            INT NOT NULL DEFAULT 0,
  knife_winner      VARCHAR(20) NULL COMMENT '刀局获胜者 steamid',
  status            ENUM('live','finished','cancelled') NOT NULL DEFAULT 'live',
  demo_name         VARCHAR(120) NULL,
  PRIMARY KEY (match_id),
  KEY idx_status (status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS cs2suite_match_players (
  match_id          INT UNSIGNED NOT NULL,
  steamid64         VARCHAR(20) NOT NULL,
  persona           VARCHAR(64) NULL,
  team              TINYINT NOT NULL COMMENT '2=T 3=CT(最终)',
  slot_team         TINYINT NOT NULL COMMENT '1/2 队列队',
  kills             INT NOT NULL DEFAULT 0,
  deaths            INT NOT NULL DEFAULT 0,
  assists           INT NOT NULL DEFAULT 0,
  headshots         INT NOT NULL DEFAULT 0,
  elo_delta         INT NOT NULL DEFAULT 0,
  PRIMARY KEY (match_id, steamid64),
  CONSTRAINT fk_mp_match FOREIGN KEY (match_id)
    REFERENCES cs2suite_matches (match_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ----------------------------------------------------------------------------
-- 7. 训练模式位点(传送点/预瞄点保存,参考 ProxTricky + OpenPrefirePrac)
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS cs2suite_practice_positions (
  pos_id            INT UNSIGNED NOT NULL AUTO_INCREMENT,
  steamid64         VARCHAR(20) NOT NULL,
  map_name          VARCHAR(32) NOT NULL,
  name              VARCHAR(48) NOT NULL,
  x                 FLOAT NOT NULL, y FLOAT NOT NULL, z FLOAT NOT NULL,
  yaw               FLOAT NOT NULL DEFAULT 0,
  pitch             FLOAT NOT NULL DEFAULT 0,
  kind              ENUM('tp','prefire','nade') NOT NULL DEFAULT 'tp',
  saved_at          DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (pos_id),
  UNIQUE KEY uq_owner_name (steamid64, map_name, name, kind)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ----------------------------------------------------------------------------
-- 8. 服务器注册表(多服共库时每台服务器一条心跳,插件自动 upsert)
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS cs2suite_servers (
  server_id         VARCHAR(64) NOT NULL,
  name              VARCHAR(96) NULL,
  ip                VARCHAR(64) NULL,
  port              INT NULL,
  current_map       VARCHAR(32) NULL,
  current_mode      VARCHAR(32) NULL,
  players           INT NOT NULL DEFAULT 0,
  last_heartbeat    DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (server_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ----------------------------------------------------------------------------
-- 8b. 变更事件队列(实现 Web 改外观 → 游戏内秒级自动生效)
--     Web 保存/绑定/解绑时 INSERT 一行;各服务器插件每 SyncSeconds 秒按 event_id
--     游标轮询消费并重载对应在线玩家外观。多服各自维护游标,互不影响。
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS cs2suite_events (
  event_id        BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  steamid64       VARCHAR(20) NOT NULL,
  type            VARCHAR(16) NOT NULL COMMENT 'loadout | bind | unbind',
  created_at      DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (event_id),
  KEY idx_steam (steamid64),
  KEY idx_created (created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ----------------------------------------------------------------------------
-- 9. 迁移记录(插件启动时按 version 增量执行)
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS cs2suite_migrations (
  version           INT NOT NULL,
  applied_at        DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (version)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT IGNORE INTO cs2suite_migrations (version) VALUES (1);
INSERT IGNORE INTO cs2suite_migrations (version) VALUES (2);
