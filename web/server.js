#!/usr/bin/env node
/*
 * CS2Suite Web — 账号 + 绑定码 + 武器皮肤/贴纸自定义管理面板
 * 与 CS2Suite 插件共用同一个 MySQL 库(所有数据存 MySQL,连接参数在 config.json)。
 * 流程:网页注册 → 服务器内 /cs2bind 拿绑定码 → 本面板输入码绑定 SteamID → 配置外观 → 进服自动生效。
 */
const express = require("express");
const mysql = require("mysql2/promise");
const bcrypt = require("bcryptjs");
const crypto = require("crypto");
const fs = require("fs");
const path = require("path");

// ---------- 异步路由包装:express4 不会自动捕获 async 异常,未处理会崩进程 ----------
// (必须定义在任何使用点之前:const 存在 TDZ,后置声明会让进程启动即崩)
const ah = (fn) => (req, res, next) => Promise.resolve(fn(req, res, next)).catch(next);

const CFG = loadConfig();
function loadConfig() {
  const p = path.join(__dirname, "config.json");
  if (!fs.existsSync(p)) {
    console.error("[CS2Suite-Web] 缺少 config.json — 请复制 config.example.json 为 config.json 并填写。");
    process.exit(1);
  }
  const c = JSON.parse(fs.readFileSync(p, "utf8").replace(/^\uFEFF/, ""));
  if (!c.database || !c.web) { console.error("[CS2Suite-Web] config.json 需要 database 与 web 两段"); process.exit(1); }
  return c;
}

const pool = mysql.createPool({
  host: CFG.database.host, port: CFG.database.port || 3306,
  user: CFG.database.user, password: CFG.database.password,
  database: CFG.database.database, waitForConnections: true,
  connectionLimit: 10, charset: "utf8mb4_unicode_ci",
});

// ---------- 目录数据(AstraSkins 数据文件,MIT) ----------
// 武器 defindex 映射(与 WeaponPaints 内置表一致):AstraSkins 的 weapons.json 不带 defindex,这里补齐
const WEAPON_DEF = {
  weapon_deagle: 1, weapon_elite: 2, weapon_fiveseven: 3, weapon_glock: 4, weapon_ak47: 7,
  weapon_aug: 8, weapon_awp: 9, weapon_famas: 10, weapon_g3sg1: 11, weapon_galilar: 13,
  weapon_m249: 14, weapon_m4a1: 16, weapon_mac10: 17, weapon_p90: 19, weapon_mp5sd: 23,
  weapon_ump45: 24, weapon_xm1014: 25, weapon_bizon: 26, weapon_mag7: 27, weapon_negev: 28,
  weapon_sawedoff: 29, weapon_tec9: 30, weapon_hkp2000: 32, weapon_mp7: 33, weapon_mp9: 34,
  weapon_nova: 35, weapon_p250: 36, weapon_scar20: 38, weapon_sg556: 39, weapon_ssg08: 40,
  weapon_m4a1_silencer: 60, weapon_usp_silencer: 61, weapon_cz75a: 63, weapon_revolver: 64,
};
const DATA = {};
function loadCatalog() {
  const read = (f) => JSON.parse(fs.readFileSync(path.join(__dirname, "data", f), "utf8").replace(/^\uFEFF/, ""));
  try {
    const weapons = read("weapons.json");
    DATA.weapons = weapons.filter(w => w.enabled !== false).map(w => ({
      entity: w.entityName, name: w.displayName, zh: w.displayNameZh || null, category: w.category,
      defindex: w.itemDefinitionIndex ?? WEAPON_DEF[w.entityName] ?? null,
      skins: (w.skins || []).filter(s => s.enabled !== false).map(s => ({
        p: s.paintKit, name: s.displayName, zh: s.displayNameZh || null, r: s.rarity || null, legacy: !!s.legacyModel,
      })),
    }));
    const knives = read("knives.json");
    DATA.knives = knives.filter(k => k.enabled !== false).map(k => ({
      entity: k.entityName || k.id, name: k.displayName, zh: k.displayNameZh || null, defindex: k.itemDefinitionIndex,
      skins: (k.skins || []).filter(s => s.enabled !== false).map(s => ({
        p: s.paintKit, name: s.displayName, zh: s.displayNameZh || null, r: s.rarity || null, legacy: !!s.legacyModel, d: s.itemDefinitionIndex ?? k.itemDefinitionIndex,
      })),
    }));
    const gloves = read("gloves.json");
    DATA.gloves = gloves.filter(g => g.enabled !== false).map(g => ({
      id: g.id, name: g.displayName, zh: g.displayNameZh || null, defindex: g.itemDefinitionIndex,
      skins: (g.skins || []).filter(s => s.enabled !== false).map(s => ({
        p: s.paintKit, name: s.displayName, zh: s.displayNameZh || null, r: s.rarity || null, d: s.itemDefinitionIndex ?? g.itemDefinitionIndex,
      })),
    }));
    const agents = read("agents.json");
    DATA.agents = agents.filter(a => a.enabled !== false).map(a => ({
      id: a.id, name: a.displayName, zh: a.displayNameZh || null, team: a.team,
      model: String(a.model || "").replace(/^agents\/$/, "").replace(/\.vmdl$/, "").replace(/^models\//, "").replace(/^agents\//, ""),
      raw: a.model, rarity: a.rarity || null,
    }));
    const stickers = read("stickers.json");
    DATA.stickers = stickers.filter(s => s.enabled !== false).map(s => ({
      id: s.stickerId, name: s.displayName, zh: s.displayNameZh || null,
      g: s.group || null, gz: s.groupZh || null, cap: s.capsule || null, r: s.rarity || null,
    }));
    const kc = read("keychains.json");
    DATA.keychains = kc.filter(k => k.enabled !== false).map(k => ({
      id: k.keychainId, name: k.displayName, zh: k.displayNameZh || null, g: k.group || null, gz: k.groupZh || null, r: k.rarity || null,
    }));
    const mk = read("music_kits.json");
    DATA.music = mk.map(m => ({ id: m.musicKit ?? m.id, name: m.displayName, zh: m.displayNameZh || null }));
    // defindex 目录(武器 key 映射)
    DATA.weaponDefByEntity = {};
    for (const w of DATA.weapons) if (w.defindex) DATA.weaponDefByEntity[w.entity] = w.defindex;
    DATA.defNames = {};
    for (const w of DATA.weapons) if (w.defindex) DATA.defNames[w.defindex] = { name: w.name, zh: w.zh, entity: w.entity };
    console.log(`[CS2Suite-Web] 目录: ${DATA.weapons.length} 武器 / ${DATA.knives.length} 刀 / ${DATA.gloves.length} 手套 / ${DATA.agents.length} 探员 / ${DATA.stickers.length} 贴纸 / ${DATA.keychains.length} 挂饰`);
  } catch (e) {
    console.error("[CS2Suite-Web] 目录数据加载失败(检查 web/data/*.json):", e.message);
    process.exit(1);
  }
}

// 目录快照元信息(web/data/meta.json,由 tools/update-catalog.cjs 生成)
let CATMETA = null;
function loadCatalogMeta() {
  try { CATMETA = JSON.parse(fs.readFileSync(path.join(__dirname, "data", "meta.json"), "utf8").replace(/^\uFEFF/, "")); }
  catch { CATMETA = null; }
}

// 图标映射(icon-map.json:由 tools/update-catalog.cjs 生成整合,skin/glove/sticker/keychain/agent 预览图 URL)
let ICONMAP = null;
function loadIconMap() {
  try {
    ICONMAP = JSON.parse(fs.readFileSync(path.join(__dirname, "data", "icon-map.json"), "utf8").replace(/^\uFEFF/, ""));
    console.log("[CS2Suite-Web] icon-map: S=" + Object.keys(ICONMAP.S || {}).length + " G=" + Object.keys(ICONMAP.G || {}).length +
      " ST=" + Object.keys(ICONMAP.ST || {}).length + " K=" + Object.keys(ICONMAP.K || {}).length + " AG=" + Object.keys(ICONMAP.AG || {}).length);
  } catch (e) { console.warn("[CS2Suite-Web] 无 icon-map.json(预览图将回退为色块占位)"); }
}

// ---------- 会话(HMAC 签名 Cookie,无状态) ----------
const SECRET = CFG.web.sessionSecret || "change-me";
function signToken(payload) {
  const body = Buffer.from(JSON.stringify(payload)).toString("base64url");
  const sig = crypto.createHmac("sha256", SECRET).update(body).digest("base64url");
  return body + "." + sig;
}
function verifyToken(tok) {
  if (!tok || typeof tok !== "string" || !tok.includes(".")) return null;
  const [body, sig] = tok.split(".");
  const want = crypto.createHmac("sha256", SECRET).update(body).digest("base64url");
  if (sig.length !== want.length || !crypto.timingSafeEqual(Buffer.from(sig), Buffer.from(want))) return null;
  try {
    const p = JSON.parse(Buffer.from(body, "base64url").toString("utf8"));
    if (!p.exp || Date.now() > p.exp) return null;
    return p;
  } catch { return null; }
}
function parseCookies(req) {
  const h = req.headers.cookie || "";
  const out = {};
  for (const part of h.split(";")) {
    const i = part.indexOf("=");
    if (i > 0) out[part.slice(0, i).trim()] = decodeURIComponent(part.slice(i + 1).trim());
  }
  return out;
}
const auth = ah(async (req, res, next) => {
  const tok = parseCookies(req).cs2suite_auth;
  const p = verifyToken(tok);
  if (!p) return res.status(401).json({ ok: false, error: "unauthorized" });
  // ★ 会话内角色不可信:封禁/降权/删号后必须立即失效,否则旧 Cookie 仍能当管理员
  const st = await accountState(p.uid);
  if (st.status !== "active" || !st.role)
    return res.status(401).json({ ok: false, error: st.status === "banned" ? "account_banned" : "unauthorized" });
  req.user = { uid: p.uid, username: p.username, role: st.role };
  next();
});
function adminOnly(req, res, next) {
  if (req.user?.role !== "admin") return res.status(403).json({ ok: false, error: "forbidden" });
  next();
}

// ---------- 简易限流(每 IP 每分钟计数) ----------
const buckets = new Map();
function rate(limit, windowMs) {
  return (req, res, next) => {
    // 用路由模板而非实际 path,避免 /api/loadout/1 与 /api/loadout/2 各自开桶绕过限流
    const key = (req.ip || "?") + "|" + (req.route?.path || req.path);
    const now = Date.now();
    let b = buckets.get(key);
    if (!b || now > b.reset) { b = { n: 0, reset: now + windowMs }; buckets.set(key, b); }
    if (++b.n > limit) return res.status(429).json({ ok: false, error: "rate_limited" });
    next();
  };
}
// 桶定期清理,防止长期运行内存增长
setInterval(() => {
  const now = Date.now();
  for (const [k, v] of buckets) if (now > v.reset) buckets.delete(k);
}, 300000).unref?.();

// ---------- 账号状态缓存(封禁/降权后旧会话立即失效) ----------
const acctCache = new Map(); // uid -> { status, role, at }
async function accountState(uid) {
  const hit = acctCache.get(uid);
  if (hit && Date.now() - hit.at < 15000) return hit;
  const [rows] = await pool.query("SELECT role, status FROM cs2suite_accounts WHERE account_id=?", [uid]);
  const st = rows[0] ? { role: rows[0].role, status: rows[0].status, at: Date.now() } : { role: null, status: "gone", at: Date.now() };
  acctCache.set(uid, st);
  return st;
}

// ---------- 工具 ----------
const clean = (s, max) => String(s ?? "").replace(/[\u0000-\u001f]/g, "").trim().slice(0, max);
const num = (v, min, max, dflt = 0) => {
  const x = Number(v);
  return Number.isFinite(x) ? Math.min(max, Math.max(min, x)) : dflt;
};

async function init() {
  await pool.query("SELECT 1");
  loadCatalog();
  loadIconMap();
  loadCatalogMeta();
  buildWhitelist();
}

const app = express();
app.use(express.json({ limit: "1mb" }));

// 静态前端(禁用启发式缓存:改版后用户不会再看到旧页面导致"按钮无效")
app.use(express.static(path.join(__dirname, "public"), {
  maxAge: 0,
  setHeaders: (res) => res.setHeader("Cache-Control", "no-store, must-revalidate"),
}));
app.get("/api/health", ah(async (_, res) => {
  try { await pool.query("SELECT 1"); res.json({ ok: true }); } catch (e) { res.status(500).json({ ok: false, error: "db_down" }); }
}));

// ================= 账号 =================
app.post("/api/register", rate(10, 60000), ah(async (req, res) => {
  if (!CFG.web.allowRegistration) return res.status(403).json({ ok: false, error: "registration_disabled" });
  const username = clean(req.body.username, 32);
  const password = String(req.body.password || "");
  const email = clean(req.body.email, 190) || null;
  if (!/^[A-Za-z0-9_\u4e00-\u9fa5]{3,32}$/.test(username))
    return res.json({ ok: false, error: "bad_username" });
  if (password.length < 8 || password.length > 72)
    return res.json({ ok: false, error: "bad_password" });
  const hash = await bcrypt.hash(password, 10);
  try {
    const [r] = await pool.query(
      "INSERT INTO cs2suite_accounts (username, email, password_hash) VALUES (?,?,?)",
      [username, email, hash]);
    issueSession(res, { uid: r.insertId, username, role: "user" });
    res.json({ ok: true });
  } catch (e) {
    if (e.code === "ER_DUP_ENTRY") return res.json({ ok: false, error: "username_taken" });
    throw e;
  }
}));

app.post("/api/login", rate(10, 60000), ah(async (req, res) => {
  const username = clean(req.body.username, 32);
  const password = String(req.body.password || "");
  const [rows] = await pool.query(
    "SELECT account_id, username, password_hash, role, status FROM cs2suite_accounts WHERE username=?", [username]);
  const u = rows[0];
  if (!u || u.status === "banned" || !(await bcrypt.compare(password, u.password_hash)))
    return res.json({ ok: false, error: "bad_credentials" });
  pool.query("UPDATE cs2suite_accounts SET last_login_at=NOW() WHERE account_id=?", [u.account_id]).catch(() => {});
  issueSession(res, { uid: u.account_id, username: u.username, role: u.role });
  res.json({ ok: true });
}));

function issueSession(res, payload) {
  payload.exp = Date.now() + 1000 * 60 * 60 * 24 * 14; // 14 天
  const tok = signToken(payload);
  res.setHeader("Set-Cookie", `cs2suite_auth=${tok}; Path=/; HttpOnly; SameSite=Lax; Max-Age=1209600`);
}

app.post("/api/logout", (req, res) => {
  res.setHeader("Set-Cookie", "cs2suite_auth=; Path=/; HttpOnly; Max-Age=0");
  res.json({ ok: true });
});

app.get("/api/me", auth, ah(async (req, res) => {
  const [steams] = await pool.query(
    "SELECT steamid64, persona, is_primary, bound_at FROM cs2suite_account_steam WHERE account_id=?", [req.user.uid]);
  res.json({ ok: true, username: req.user.username, role: req.user.role, steamids: steams });
}));

// ================= 绑定码(插件在服务器内生成,Web 消费) =================
app.post("/api/bind", auth, rate(15, 60000), ah(async (req, res) => {
  const code = clean(req.body.code, 8).toUpperCase();
  if (!/^[A-Z2-9]{6}$/.test(code)) return res.json({ ok: false, error: "bad_code" });
  const conn = await pool.getConnection();
  try {
    await conn.beginTransaction();
    const [rows] = await conn.query(
      "SELECT steamid64, persona FROM cs2suite_bind_codes WHERE code=? AND used_by_account IS NULL AND expires_at > NOW() FOR UPDATE", [code]);
    if (!rows.length) { await conn.rollback(); return res.json({ ok: false, error: "code_invalid" }); }
    const { steamid64, persona } = rows[0];
    const [dup] = await conn.query("SELECT account_id FROM cs2suite_account_steam WHERE steamid64=?", [steamid64]);
    // 不返回对方 account_id:避免通过枚举绑定码探测他人账号信息
    if (dup.length) { await conn.rollback(); return res.json({ ok: false, error: "steam_already_bound" }); }
    const [owned] = await conn.query("SELECT COUNT(*) c FROM cs2suite_account_steam WHERE account_id=?", [req.user.uid]);
    if (owned[0].c >= (CFG.web.maxSteamidsPerAccount || 5)) { await conn.rollback(); return res.json({ ok: false, error: "too_many_steamids" }); }
    await conn.query(
      "INSERT INTO cs2suite_account_steam (steamid64, account_id, persona, is_primary) VALUES (?,?,?,?)",
      [steamid64, req.user.uid, persona || null, owned[0].c === 0 ? 1 : 0]);
    await conn.query("UPDATE cs2suite_bind_codes SET used_by_account=?, used_at=NOW() WHERE code=?", [req.user.uid, code]);
    await conn.query("INSERT INTO cs2suite_events (steamid64, type) VALUES (?, 'bind')", [steamid64]);
    await conn.commit();
    res.json({ ok: true, steamid: steamid64 });
  } catch (e) { await conn.rollback(); throw e; } finally { conn.release(); }
}));

app.delete("/api/steam/:steamid", auth, ah(async (req, res) => {
  const steam = clean(req.params.steamid, 20);
  if (!/^\d{17,20}$/.test(steam)) return res.json({ ok: false, error: "bad_steamid" });
  const [r] = await pool.query("DELETE FROM cs2suite_account_steam WHERE steamid64=? AND account_id=?", [steam, req.user.uid]);
  if (r.affectedRows > 0) pool.query("INSERT INTO cs2suite_events (steamid64, type) VALUES (?, 'unbind')", [steam]).catch(() => {});
  res.json({ ok: r.affectedRows > 0 });
}));

// ================= 外观(读写同一批 cs2suite_loadout_* 表) =================
// 合法 defindex 集合:目录内武器/刀/手套 + 虚拟槽位(42 刀基准 / -1 手套 / -2 探员 / -3 音乐 / -4 徽章)
let DEF_WHITELIST = null;
function buildWhitelist() {
  const s = new Set([42, -1, -2, -3, -4]);
  for (const w of DATA.weapons) if (w.defindex) s.add(w.defindex);
  for (const k of DATA.knives) if (k.defindex) s.add(k.defindex);
  for (const g of DATA.gloves) if (g.defindex) s.add(g.defindex);
  DEF_WHITELIST = s;
  return s;
}
async function ownedSteam(steamid, uid) {
  const [rows] = await pool.query(
    "SELECT 1 FROM cs2suite_account_steam WHERE steamid64=? AND account_id=?", [steamid, uid]);
  return rows.length > 0;
}

app.get("/api/loadout/:steam", auth, ah(async (req, res) => {
  const steam = clean(req.params.steam, 20);
  if (!(await ownedSteam(steam, req.user.uid))) return res.status(403).json({ ok: false, error: "not_your_steamid" });
  const [items] = await pool.query("SELECT * FROM cs2suite_loadout_items WHERE steamid64=?", [steam]);
  const [stickers] = await pool.query("SELECT * FROM cs2suite_loadout_stickers WHERE steamid64=?", [steam]);
  res.json({ ok: true, items, stickers });
}));

// body: { steam, items: [{def, knifeDef, paint, seed, wear, nametag, st, stCount, kc}], stickers: [{def, slot, id, ox, oy, rot, scale, wear}] }
app.post("/api/loadout/:steam", auth, rate(30, 60000), ah(async (req, res) => {
  const steam = clean(req.params.steam, 20);
  if (!(await ownedSteam(steam, req.user.uid))) return res.status(403).json({ ok: false, error: "not_your_steamid" });
  const items = Array.isArray(req.body.items) ? req.body.items.slice(0, 64) : [];
  const stickers = Array.isArray(req.body.stickers) ? req.body.stickers.slice(0, 64 * 5) : [];
  // ★ 白名单校验:拒绝目录外的 defindex(防脏数据写库后被插件按 def 匹配应用出怪外观)
  const wl = DEF_WHITELIST || buildWhitelist();
  const badDef = items.map(it => num(it.def, -4, 65535)).filter(d => !wl.has(d));
  if (badDef.length) return res.status(400).json({ ok: false, error: "bad_defindex" });
  const badSDef = stickers.map(s => num(s.def, 0, 65535)).filter(d => !wl.has(d));
  if (badSDef.length) return res.status(400).json({ ok: false, error: "bad_defindex" });
  const conn = await pool.getConnection();
  try {
    await conn.beginTransaction();
    await conn.query(
      "INSERT INTO cs2suite_loadouts (steamid64) VALUES (?) ON DUPLICATE KEY UPDATE updated_at=NOW()", [steam]);
    await conn.query("DELETE FROM cs2suite_loadout_items WHERE steamid64=?", [steam]);
    await conn.query("DELETE FROM cs2suite_loadout_stickers WHERE steamid64=?", [steam]);
    for (const it of items) {
      const def = num(it.def, -4, 65535);
      const paint = num(it.paint, 0, 100000);
      const wear = num(it.wear, 0, 1);
      const seed = num(it.seed, 0, 1000000);
      const tag = clean(it.nametag, 128) || null;
      await conn.query(
        "INSERT INTO cs2suite_loadout_items (steamid64, weapon_defindex, knife_target_defindex, paintkit, paint_seed, paint_wear, nametag, stattrak, stattrak_count, keychain_id) " +
        "VALUES (?,?,?,?,?,?,?,?,?,?)",
        [steam, def, num(it.knifeDef, 0, 65535), paint, seed, wear, tag, it.st ? 1 : 0, num(it.stCount, 0, 999999), num(it.kc, 0, 65535)]);
    }
    for (const s of stickers) {
      await conn.query(
        "INSERT INTO cs2suite_loadout_stickers (steamid64, weapon_defindex, slot, sticker_id, offset_x, offset_y, rotation, scale, wear) VALUES (?,?,?,?,?,?,?,?,?) " +
        "ON DUPLICATE KEY UPDATE sticker_id=VALUES(sticker_id), offset_x=VALUES(offset_x), offset_y=VALUES(offset_y), rotation=VALUES(rotation), scale=VALUES(scale), wear=VALUES(wear)",
        [steam, num(s.def, 0, 65535), num(s.slot, 0, 4), num(s.id, 0, 999999),
         num(s.ox, -1, 1), num(s.oy, -1, 1), num(s.rot, -360, 360), num(s.scale, 0.3, 2, 1), num(s.wear, 0, 1, 0.12)]);
    }
    await conn.query("INSERT INTO cs2suite_events (steamid64, type) VALUES (?, 'loadout')", [steam]);
    await conn.commit();
    res.json({ ok: true });
  } catch (e) { await conn.rollback(); throw e; } finally { conn.release(); }
}));

// ================= 目录 & 统计 =================
app.get("/api/catalog", (_, res) => {
  res.json({ ok: true, weapons: DATA.weapons, knives: DATA.knives, gloves: DATA.gloves,
             agents: DATA.agents, stickers: DATA.stickers, keychains: DATA.keychains, music: DATA.music });
});

// 预览图映射(前端按 def:paint / stickerId / keychainId / model|team 查 URL)
app.get("/api/iconmap", (_, res) => res.json(ICONMAP || { S: {}, G: {}, ST: {}, K: {}, AG: {} }));

// 目录快照信息:让玩家/管理员知道皮肤数据是哪天同步的
app.get("/api/catalog/meta", (_, res) => res.json({ ok: true, meta: CATMETA }));

app.get("/api/stats/:steam", auth, ah(async (req, res) => {
  const steam = clean(req.params.steam, 20);
  if (!(await ownedSteam(steam, req.user.uid))) return res.status(403).json({ ok: false, error: "not_your_steamid" });
  const [[dm]] = await pool.query("SELECT kills,deaths,headshots,killstreak_best,points,sessions,persona FROM cs2suite_dm_stats WHERE steamid64=?", [steam]);
  const [[elo]] = await pool.query("SELECT elo,wins,losses,draws FROM cs2suite_elo WHERE steamid64=?", [steam]);
  const [matches] = await pool.query(
    "SELECT mp.map_name, mp.started_at, mp.score1, mp.score2, p.team, p.kills, p.deaths, p.elo_delta " +
    "FROM cs2suite_match_players p JOIN cs2suite_matches mp ON mp.match_id=p.match_id " +
    "WHERE p.steamid64=? ORDER BY mp.match_id DESC LIMIT 10", [steam]);
  res.json({ ok: true, dm: dm || null, elo: elo || null, matches });
}));

// 服务器列表(心跳表,展示用)
app.get("/api/servers", ah(async (_, res) => {
  // 离线分钟由数据库算,避免前端把 DATETIME 当 UTC 解析导致时区偏差
  const [rows] = await pool.query(
    "SELECT server_id, name, current_map, current_mode, players, last_heartbeat, " +
    "TIMESTAMPDIFF(SECOND, last_heartbeat, NOW()) AS age_sec " +
    "FROM cs2suite_servers ORDER BY last_heartbeat DESC LIMIT 20");
  res.json({ ok: true, servers: rows });
}));

// ================= 管理 =================
app.get("/api/admin/users", auth, adminOnly, ah(async (_, res) => {
  const [rows] = await pool.query(
    "SELECT a.account_id, a.username, a.role, a.status, a.created_at, a.last_login_at, " +
    "GROUP_CONCAT(s.steamid64 SEPARATOR ',') steamids " +
    "FROM cs2suite_accounts a LEFT JOIN cs2suite_account_steam s ON s.account_id=a.account_id " +
    "GROUP BY a.account_id ORDER BY a.account_id DESC LIMIT 500");
  res.json({ ok: true, users: rows });
}));
app.post("/api/admin/status", auth, adminOnly, ah(async (req, res) => {
  const id = num(req.body.account_id, 1, 99999999);
  const status = req.body.status === "banned" ? "banned" : "active";
  await pool.query("UPDATE cs2suite_accounts SET status=? WHERE account_id=?", [status, id]);
  acctCache.delete(id);
  res.json({ ok: true });
}));
app.post("/api/admin/role", auth, adminOnly, ah(async (req, res) => {
  const id = num(req.body.account_id, 1, 99999999);
  const role = req.body.role === "admin" ? "admin" : "user";
  await pool.query("UPDATE cs2suite_accounts SET role=? WHERE account_id=?", [role, id]);
  acctCache.delete(id);
  res.json({ ok: true });
}));

// 全局错误
app.use((err, req, res, next) => {
  console.error("[CS2Suite-Web]", err);
  res.status(500).json({ ok: false, error: "server_error" });
});

init().then(() => {
  const port = CFG.web.port || 8788;
  const host = CFG.web.host;
  // host 省略时 Node 监听 :::port(双栈:localhost 的 IPv4/IPv6 都能访问)
  const srv = host && host !== "0.0.0.0" ? app.listen(port, host) : app.listen(port);
  srv.on("listening", () => console.log(`[CS2Suite-Web] listening on :${port}`));
}).catch(e => {
  console.error("[CS2Suite-Web] 启动失败(检查 MySQL 连接与 config.json):", e.message);
  process.exit(1);
});
