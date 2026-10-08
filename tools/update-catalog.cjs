"use strict";
/**
 * CS2Suite 目录一键更新 — CS2 出新皮肤后运行,把最新皮肤/贴纸/挂饰/探员同步到 Web 端。
 *
 * 用法:  node tools/update-catalog.cjs            # 全量更新(拉取+探测+重建 icon-map)
 *        node tools/update-catalog.cjs --no-probe # 只拉目录,不探测新图(更快)
 *
 * 数据来源(均为社区权威镜像,非 Valve 官方接口):
 *   1) Ayrton09/AstraSkins  data/*.json        —— 皮肤/刀/手套/探员/贴纸/挂饰/音乐包 目录(名称+ID)
 *   2) LielXD/CS2-WeaponPaints-Website src/data —— 图标清单(weapon-paint / sticker-id / keychain-id)
 *   3) Nereziel/cs2-WeaponPaints website/img    —— 官方图包 CDN(对目录里没有的项做定点探测)
 */
const fs = require("fs");
const path = require("path");

const ROOT = path.resolve(__dirname, "..");
const DATA = path.join(ROOT, "web", "data");
const TOOLS = path.join(ROOT, "tools");
const ICONS = path.join(TOOLS, "icons");
const UP = path.join(TOOLS, "upstream");
const NO_PROBE = process.argv.includes("--no-probe");

const SRC_ASTRA = "https://raw.githubusercontent.com/Ayrton09/AstraSkins/main/data/";
const SRC_LIEL = "https://raw.githubusercontent.com/LielXD/CS2-WeaponPaints-Website/master/src/data/";
const CDN = "https://raw.githubusercontent.com/Nereziel/cs2-WeaponPaints/main/website/img/skins/";

const WEAPON_DEF = {
  weapon_deagle: 1, weapon_elite: 2, weapon_fiveseven: 3, weapon_glock: 4, weapon_ak47: 7,
  weapon_aug: 8, weapon_awp: 9, weapon_famas: 10, weapon_g3sg1: 11, weapon_galilar: 13,
  weapon_m249: 14, weapon_m4a1: 16, weapon_mac10: 17, weapon_p90: 19, weapon_mp5sd: 23,
  weapon_ump45: 24, weapon_xm1014: 25, weapon_bizon: 26, weapon_mag7: 27, weapon_negev: 28,
  weapon_sawedoff: 29, weapon_tec9: 30, weapon_hkp2000: 32, weapon_mp7: 33, weapon_mp9: 34,
  weapon_nova: 35, weapon_p250: 36, weapon_scar20: 38, weapon_sg556: 39, weapon_ssg08: 40,
  weapon_m4a1_silencer: 60, weapon_usp_silencer: 61, weapon_cz75a: 63, weapon_revolver: 64,
};

const ASTRAs = ["weapons.json", "knives.json", "gloves.json", "agents.json", "stickers.json", "keychains.json", "music_kits.json", "categories.json"];
const LIELs = ["skins.json", "stickers.json", "keychains.json", "agents.json", "gloves.json", "music.json"];

fs.mkdirSync(DATA, { recursive: true });
fs.mkdirSync(ICONS, { recursive: true });
fs.mkdirSync(UP, { recursive: true });

async function dl(url, dest) {
  const ctl = new AbortController();
  const to = setTimeout(() => ctl.abort(), 180000);
  try {
    const r = await fetch(url, { signal: ctl.signal });
    if (!r.ok) throw new Error("HTTP " + r.status);
    const buf = Buffer.from(await r.arrayBuffer());
    fs.writeFileSync(dest, buf);
    return buf.length;
  } finally { clearTimeout(to); }
}
const readJson = (p) => JSON.parse(fs.readFileSync(p, "utf8").replace(/^\uFEFF/, ""));
async function head(url) {
  try {
    const ctl = new AbortController();
    const to = setTimeout(() => ctl.abort(), 9000);
    const r = await fetch(url, { signal: ctl.signal, headers: { Range: "bytes=0-64" } });
    clearTimeout(to);
    return r.ok || r.status === 206;
  } catch { return false; }
}
async function pool(items, conc, worker) {
  let i = 0; const out = [];
  const run = async () => { while (i < items.length) { const k = i++; try { out[k] = await worker(items[k]); } catch { out[k] = null; } } };
  await Promise.all(Array.from({ length: conc }, run));
  return out;
}

async function main() {
  const stamp = new Date().toISOString().replace("T", " ").slice(0, 19);

  // ---------- 1. 拉取目录 ----------
  console.log("[1/4] 拉取上游目录…");
  const changed = [];
  for (const f of ASTRAs) {
    const dest = path.join(DATA, f);
    const before = fs.existsSync(dest) ? fs.statSync(dest).size : -1;
    const size = await dl(SRC_ASTRA + f, dest);
    if (size !== before) changed.push("data/" + f);
  }
  for (const f of LIELs) {
    const dest = path.join(ICONS, "liel_" + f);
    const before = fs.existsSync(dest) ? fs.statSync(dest).size : -1;
    const size = await dl(SRC_LIEL + f, dest);
    if (size !== before) changed.push("icons/liel_" + f);
  }
  console.log("    变更文件: " + (changed.length ? changed.join(", ") : "无(上游与本地一致)"));

  // ---------- 2. 重建 icon-map ----------
  console.log("[2/4] 重建 icon-map…");
  const wpSkins = readJson(path.join(ICONS, "liel_skins.json"));
  const wpGloves = readJson(path.join(ICONS, "liel_gloves.json"));
  const wpStickers = readJson(path.join(ICONS, "liel_stickers.json"));
  const wpKeys = readJson(path.join(ICONS, "liel_keychains.json"));
  const wpAgents = readJson(path.join(ICONS, "liel_agents.json"));

  // --no-probe 时载入现有映射做基底,避免"快速模式"把此前探测到的图覆盖丢失
  let S = {}, G = {}, ST = {}, K = {}, AG = {};
  const iconMapPath = path.join(DATA, "icon-map.json");
  if (NO_PROBE && fs.existsSync(iconMapPath)) {
    try {
      const prev = readJson(iconMapPath);
      S = prev.S || {}; G = prev.G || {}; ST = prev.ST || {}; K = prev.K || {}; AG = prev.AG || {};
      console.log("    (快速模式:保留现有 " + Object.keys(S).length + " 张皮肤图作为基底)");
    } catch { /* 旧文件损坏则从零重建 */ }
  }
  for (const it of wpSkins) {
    const def = parseInt(it.weapon_defindex), paint = parseInt(it.paint);
    if (Number.isFinite(def) && it.image) S[def + ":" + (Number.isFinite(paint) ? paint : 0)] = it.image;
  }
  for (const it of wpGloves) {
    const paint = parseInt(it.paint);
    if (Number.isFinite(paint) && paint > 1000 && it.image) G[paint] = it.image;
  }
  for (const it of wpStickers) { const id = parseInt(it.id); if (Number.isFinite(id) && it.image) ST[id] = it.image; }
  for (const it of wpKeys) { const id = parseInt(it.id); if (Number.isFinite(id) && it.image) K[id] = it.image; }
  for (const it of wpAgents) {
    if (!it.model || !it.image) continue;
    const m = String(it.model).replace(/^agents\/models\//, "").replace(/^agents\//, "").replace(/^models\//, "").replace(/\.vmdl$/, "");
    AG[m + "|" + (it.team === 3 ? 3 : 2)] = it.image;
  }

  // ---------- 3. 对缺失项做 CDN 探测(新皮肤的关键:清单没收录时按命名规则定点探测) ----------
  const ast = readJson(path.join(DATA, "stickers.json"));
  const ack = readJson(path.join(DATA, "keychains.json"));
  const agl = readJson(path.join(DATA, "gloves.json"));
  const aw = readJson(path.join(DATA, "weapons.json"));

  const missSt = ast.filter(t => !ST[t.stickerId]).map(t => t.stickerId);
  const missK = ack.filter(t => !K[t.keychainId]).map(t => t.keychainId);
  const missG = [];
  for (const g of agl) for (const s of (g.skins || [])) if (s.paintKit > 0 && !G[s.paintKit]) missG.push([g.id, s.paintKit]);
  const missW = [];
  for (const w of aw) { const def = WEAPON_DEF[w.entityName]; if (!def) continue; for (const s of (w.skins || [])) if (s.paintKit > 0 && !S[def + ":" + s.paintKit]) missW.push([w.entityName, def, s.paintKit]); }

  const totalMiss = missSt.length + missK.length + missG.length + missW.length;
  let addSt = 0, addK = 0, addG = 0, addW = 0;
  if (NO_PROBE) {
    console.log("[3/4] 跳过探测(--no-probe),未命中: 贴纸" + missSt.length + " 挂饰" + missK.length + " 手套" + missG.length + " 枪皮" + missW.length);
  } else {
    console.log("[3/4] CDN 探测新图(未命中 " + totalMiss + " 项,较慢)…");
    const r1 = await pool(missSt, 24, id => head(CDN + "sticker-" + id + ".png").then(ok => ok ? [id, CDN + "sticker-" + id + ".png"] : null));
    for (const r of r1) if (r) { ST[r[0]] = r[1]; addSt++; }
    const r2 = await pool(missK, 16, id => head(CDN + "keychain-" + id + ".png").then(ok => ok ? [id, CDN + "keychain-" + id + ".png"] : null));
    for (const r of r2) if (r) { K[r[0]] = r[1]; addK++; }
    const r3 = await pool(missG, 16, ([gid, p]) => head(CDN + gid + "-" + p + ".png").then(ok => ok ? [p, CDN + gid + "-" + p + ".png"] : null));
    for (const r of r3) if (r) { G[r[0]] = r[1]; addG++; }
    const r4 = await pool(missW, 24, ([ent, def, p]) => head(CDN + ent + "-" + p + ".png").then(ok => ok ? [def + ":" + p, CDN + ent + "-" + p + ".png"] : null));
    for (const r of r4) if (r) { S[r[0]] = r[1]; addW++; }
    console.log("    新增图: 贴纸+" + addSt + " 挂饰+" + addK + " 手套+" + addG + " 枪皮+" + addW);
  }

  fs.writeFileSync(path.join(DATA, "icon-map.json"), JSON.stringify({ S, G, ST, K, AG, WEAPON_DEF }));

  // ---------- 4. 覆盖率报告 + meta ----------
  let hitW = 0, totW = 0, hitK = 0, totK = 0, hitG2 = 0, totG2 = 0, hitS = 0;
  for (const w of aw) { const def = WEAPON_DEF[w.entityName]; if (!def) continue; for (const s of (w.skins || [])) { totW++; if (S[def + ":" + s.paintKit]) hitW++; } }
  for (const k of readJson(path.join(DATA, "knives.json"))) for (const s of (k.skins || [])) { const d = s.itemDefinitionIndex ?? k.itemDefinitionIndex; totK++; if (S[d + ":" + s.paintKit]) hitK++; }
  for (const g of agl) for (const s of (g.skins || [])) { totG2++; if (G[s.paintKit]) hitG2++; }
  for (const t of ast) if (ST[t.stickerId]) hitS++;

  const meta = {
    updatedAt: stamp,
    sources: { astra: SRC_ASTRA, icons: SRC_LIEL, cdn: CDN },
    counts: { weapons: aw.length, knives: readJson(path.join(DATA, "knives.json")).length, gloves: agl.length, stickers: ast.length, keychains: ack.length },
    icons: { skins: Object.keys(S).length, gloves: Object.keys(G).length, stickers: Object.keys(ST).length, keychains: Object.keys(K).length, agents: Object.keys(AG).length },
    coverage: { weaponSkins: hitW + "/" + totW, knifeSkins: hitK + "/" + totK, gloves: hitG2 + "/" + totG2, stickers: hitS + "/" + ast.length },
    changed,
  };
  fs.writeFileSync(path.join(DATA, "meta.json"), JSON.stringify(meta, null, 2));

  console.log("[4/4] 完成。覆盖率 枪皮 " + meta.coverage.weaponSkins + " | 刀 " + meta.coverage.knifeSkins +
    " | 手套 " + meta.coverage.gloves + " | 贴纸 " + meta.coverage.stickers);
  console.log("    快照时间: " + stamp + "  (web/data/meta.json)");
  console.log("    提示: Web 端重启后生效(node server.js);插件无需更新。");
}

main().then(() => process.exit(0)).catch(e => { console.error("更新失败:", e && e.message); process.exit(1); });
