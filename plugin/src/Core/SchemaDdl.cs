namespace CS2Suite.Core;

/// <summary>
/// 与 db/schema.sql 同步的内嵌 DDL(插件自动建表)。
/// 若已手工导入 schema.sql,这里的语句幂等,无副作用。
/// 表注释等装饰仅保留在 schema.sql;此处以可执行为准。
/// </summary>
internal static class SchemaDdl
{
    public static readonly string[] Statements =
    [
        """
        CREATE TABLE IF NOT EXISTS cs2suite_accounts (
            account_id INT UNSIGNED NOT NULL AUTO_INCREMENT,
            username VARCHAR(32) NOT NULL,
            email VARCHAR(190) NULL,
            password_hash VARCHAR(255) NOT NULL,
            role ENUM('user','admin') NOT NULL DEFAULT 'user',
            status ENUM('active','banned') NOT NULL DEFAULT 'active',
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            last_login_at DATETIME NULL,
            PRIMARY KEY (account_id), UNIQUE KEY uq_username (username), UNIQUE KEY uq_email (email)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS cs2suite_account_steam (
            steamid64 VARCHAR(20) NOT NULL,
            account_id INT UNSIGNED NOT NULL,
            persona VARCHAR(64) NULL,
            is_primary TINYINT(1) NOT NULL DEFAULT 1,
            bound_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            PRIMARY KEY (steamid64), KEY idx_account (account_id),
            CONSTRAINT fk_steam_account FOREIGN KEY (account_id)
                REFERENCES cs2suite_accounts (account_id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS cs2suite_bind_codes (
            code CHAR(6) NOT NULL,
            steamid64 VARCHAR(20) NOT NULL,
            persona VARCHAR(64) NULL,
            issued_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            expires_at DATETIME NOT NULL,
            used_by_account INT UNSIGNED NULL,
            used_at DATETIME NULL,
            PRIMARY KEY (code), KEY idx_steam (steamid64),
            CONSTRAINT fk_bind_account FOREIGN KEY (used_by_account)
                REFERENCES cs2suite_accounts (account_id) ON DELETE SET NULL
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS cs2suite_loadouts (
            steamid64 VARCHAR(20) NOT NULL,
            updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            PRIMARY KEY (steamid64)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS cs2suite_loadout_items (
            steamid64 VARCHAR(20) NOT NULL,
            weapon_defindex INT NOT NULL,
            knife_target_defindex INT NOT NULL DEFAULT 0,
            paintkit INT NOT NULL DEFAULT 0,
            paint_seed INT NOT NULL DEFAULT 0,
            paint_wear FLOAT NOT NULL DEFAULT 0.12,
            nametag VARCHAR(128) NULL,
            stattrak TINYINT(1) NOT NULL DEFAULT 0,
            stattrak_count INT NOT NULL DEFAULT 0,
            keychain_id INT NOT NULL DEFAULT 0,
            updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            PRIMARY KEY (steamid64, weapon_defindex),
            CONSTRAINT fk_item_steam FOREIGN KEY (steamid64)
                REFERENCES cs2suite_loadouts (steamid64) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS cs2suite_loadout_stickers (
            steamid64 VARCHAR(20) NOT NULL,
            weapon_defindex INT NOT NULL,
            slot TINYINT NOT NULL,
            sticker_id INT NOT NULL,
            offset_x FLOAT NOT NULL DEFAULT 0,
            offset_y FLOAT NOT NULL DEFAULT 0,
            rotation FLOAT NOT NULL DEFAULT 0,
            scale FLOAT NOT NULL DEFAULT 1.0,
            wear FLOAT NOT NULL DEFAULT 0.12,
            PRIMARY KEY (steamid64, weapon_defindex, slot),
            CONSTRAINT fk_sticker_steam FOREIGN KEY (steamid64)
                REFERENCES cs2suite_loadouts (steamid64) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS cs2suite_loadout_teams (
            steamid64 VARCHAR(20) NOT NULL,
            team TINYINT NOT NULL,
            weapon_defindex INT NOT NULL,
            knife_target_defindex INT NOT NULL DEFAULT 0,
            paintkit INT NOT NULL DEFAULT 0,
            paint_seed INT NOT NULL DEFAULT 0,
            paint_wear FLOAT NOT NULL DEFAULT 0.12,
            nametag VARCHAR(128) NULL,
            stattrak TINYINT(1) NOT NULL DEFAULT 0,
            stattrak_count INT NOT NULL DEFAULT 0,
            keychain_id INT NOT NULL DEFAULT 0,
            updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            PRIMARY KEY (steamid64, team, weapon_defindex),
            CONSTRAINT fk_team_steam FOREIGN KEY (steamid64)
                REFERENCES cs2suite_loadouts (steamid64) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS cs2suite_player_prefs (
            steamid64 VARCHAR(20) NOT NULL,
            prefs JSON NOT NULL,
            updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            PRIMARY KEY (steamid64)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS cs2suite_dm_stats (
            steamid64 VARCHAR(20) NOT NULL,
            persona VARCHAR(64) NULL,
            kills INT UNSIGNED NOT NULL DEFAULT 0,
            deaths INT UNSIGNED NOT NULL DEFAULT 0,
            headshots INT UNSIGNED NOT NULL DEFAULT 0,
            killstreak_best INT UNSIGNED NOT NULL DEFAULT 0,
            points INT UNSIGNED NOT NULL DEFAULT 0,
            sessions INT UNSIGNED NOT NULL DEFAULT 0,
            updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            PRIMARY KEY (steamid64)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS cs2suite_dm_rounds (
            round_id INT UNSIGNED NOT NULL AUTO_INCREMENT,
            map_name VARCHAR(32) NOT NULL,
            started_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            ended_at DATETIME NULL,
            PRIMARY KEY (round_id), KEY idx_started (started_at)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS cs2suite_dm_scores (
            round_id INT UNSIGNED NOT NULL,
            steamid64 VARCHAR(20) NOT NULL,
            persona VARCHAR(64) NULL,
            kills INT NOT NULL DEFAULT 0,
            deaths INT NOT NULL DEFAULT 0,
            points INT NOT NULL DEFAULT 0,
            PRIMARY KEY (round_id, steamid64),
            CONSTRAINT fk_score_round FOREIGN KEY (round_id)
                REFERENCES cs2suite_dm_rounds (round_id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS cs2suite_queue_log (
            log_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
            event ENUM('join','leave','start','cancel','ready','notready','kick') NOT NULL,
            steamid64 VARCHAR(20) NOT NULL,
            persona VARCHAR(64) NULL,
            map_name VARCHAR(32) NULL,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            PRIMARY KEY (log_id), KEY idx_steam_time (steamid64, created_at)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS cs2suite_elo (
            steamid64 VARCHAR(20) NOT NULL,
            elo INT NOT NULL DEFAULT 1200,
            wins INT UNSIGNED NOT NULL DEFAULT 0,
            losses INT UNSIGNED NOT NULL DEFAULT 0,
            draws INT UNSIGNED NOT NULL DEFAULT 0,
            updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            PRIMARY KEY (steamid64), KEY idx_elo (elo)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS cs2suite_matches (
            match_id INT UNSIGNED NOT NULL AUTO_INCREMENT,
            map_name VARCHAR(32) NOT NULL,
            started_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            ended_at DATETIME NULL,
            score1 INT NOT NULL DEFAULT 0,
            score2 INT NOT NULL DEFAULT 0,
            knife_winner VARCHAR(20) NULL,
            status ENUM('live','finished','cancelled') NOT NULL DEFAULT 'live',
            demo_name VARCHAR(120) NULL,
            PRIMARY KEY (match_id), KEY idx_status (status)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS cs2suite_match_players (
            match_id INT UNSIGNED NOT NULL,
            steamid64 VARCHAR(20) NOT NULL,
            persona VARCHAR(64) NULL,
            team TINYINT NOT NULL,
            slot_team TINYINT NOT NULL,
            kills INT NOT NULL DEFAULT 0,
            deaths INT NOT NULL DEFAULT 0,
            assists INT NOT NULL DEFAULT 0,
            headshots INT NOT NULL DEFAULT 0,
            elo_delta INT NOT NULL DEFAULT 0,
            PRIMARY KEY (match_id, steamid64),
            CONSTRAINT fk_mp_match FOREIGN KEY (match_id)
                REFERENCES cs2suite_matches (match_id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS cs2suite_practice_positions (
            pos_id INT UNSIGNED NOT NULL AUTO_INCREMENT,
            steamid64 VARCHAR(20) NOT NULL,
            map_name VARCHAR(32) NOT NULL,
            name VARCHAR(48) NOT NULL,
            x FLOAT NOT NULL, y FLOAT NOT NULL, z FLOAT NOT NULL,
            yaw FLOAT NOT NULL DEFAULT 0,
            pitch FLOAT NOT NULL DEFAULT 0,
            kind ENUM('tp','prefire','nade') NOT NULL DEFAULT 'tp',
            saved_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            PRIMARY KEY (pos_id), UNIQUE KEY uq_owner_name (steamid64, map_name, name, kind)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS cs2suite_servers (
            server_id VARCHAR(64) NOT NULL,
            name VARCHAR(96) NULL,
            ip VARCHAR(64) NULL,
            port INT NULL,
            current_map VARCHAR(32) NULL,
            current_mode VARCHAR(32) NULL,
            players INT NOT NULL DEFAULT 0,
            last_heartbeat DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            PRIMARY KEY (server_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS cs2suite_events (
            event_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
            steamid64 VARCHAR(20) NOT NULL,
            type VARCHAR(16) NOT NULL,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            PRIMARY KEY (event_id), KEY idx_steam (steamid64)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS cs2suite_migrations (
            version INT NOT NULL,
            applied_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            PRIMARY KEY (version)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        "INSERT IGNORE INTO cs2suite_migrations (version) VALUES (1)",
        "INSERT IGNORE INTO cs2suite_migrations (version) VALUES (2)",
    ];
}
