/* CS2Suite Web 前端 — 无依赖 ES2020 */
"use strict";
const $ = (s, r) => (r || document).querySelector(s);
const $$ = (s, r) => Array.from((r || document).querySelectorAll(s));
const view = $("#view");
let ME = null, CAT = null, S = null, IC = null;
const imgOf = {
  skin: (def, p) => (IC && IC.S && IC.S[def + ":" + (p | 0)]) || null,
  glove: (p) => (IC && IC.G && IC.G[p]) || null,
  sticker: (id) => (IC && IC.ST && IC.ST[id]) || null,
  keychain: (id) => (IC && IC.K && IC.K[id]) || null,
  agent: (model, team) => (IC && IC.AG && (IC.AG[model + "|" + team] || IC.AG[model + "|" + (team === 3 ? 2 : 3)])) || null,
};
const imgTag = (src, cls = "skimg") => src ? '<img class="' + cls + '" src="' + esc(src) + '" loading="lazy" referrerpolicy="no-referrer" onerror="this.outerHTML=\'<div class=&quot;skph&quot;&gt;&lt;/div&gt;\'">' : "";

async function api(path, opts = {}) {
  let r, j;
  try {
    r = await fetch(path, { headers: { "Content-Type": "application/json" }, credentials: "same-origin", ...opts });
    j = await r.json().catch(() => ({ ok: false, error: "bad_json" }));
  } catch (e) {
    toast("无法连接服务器,请确认 Web 服务已启动", "bad");
    return { ok: false, error: "net_down" };
  }
  // 401:会话过期/被封禁 → 清本地态并引导重新登录(此前静默,表现为"点了没反应")
  if (r.status === 401) {
    ME = null;
    refreshNavUI();
    toast(t(j.error) || "登录状态已失效,请重新登录", "bad");
    if (hashPath() !== "/auth") location.hash = "/auth";
    return j;
  }
  if (r.status === 403 && j.error) { toast(t(j.error), "bad"); return j; }
  if (!j.ok && j.error) toast(t(j.error), "bad");
  return j;
}
const ERRS = {
  unauthorized: "请先登录", bad_code: "绑定码格式不对(服务器输入 /cs2bind)", code_invalid: "绑定码无效或已过期,请回服务器重新获取",
  steam_already_bound: "该 SteamID 已绑定其他账号", too_many_steamids: "每个账号最多绑定 5 个 SteamID",
  bad_username: "用户名需 3-32 位中文/字母/数字/下划线", bad_password: "密码至少 8 位",
  username_taken: "用户名已存在", bad_credentials: "用户名或密码错误", registration_disabled: "该服务器已关闭注册",
  rate_limited: "操作过于频繁,请稍后再试", server_error: "服务器内部错误", not_your_steamid: "请先绑定该 SteamID",
  net_down: "无法连接服务器", empty_fields: "请填写用户名和密码",
  account_banned: "账号已被封禁,请联系管理员", forbidden: "没有管理员权限",
  bad_defindex: "提交的武器/槽位不在支持列表内(可能目录版本过旧)",
};
const t = (e) => ERRS[e] || e;
function toast(msg, cls = "") {
  const d = document.createElement("div");
  d.className = cls; d.textContent = msg;
  $("#toast").appendChild(d);
  setTimeout(() => d.remove(), 3600);
}
const esc = (s) => String(s ?? "").replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
const num = (v) => Math.max(0, Math.floor(Number(v) || 0));

const ROUTES = { "": landing, "/": landing, "/auth": authView, "/account": accountView, "/skins": skinsView, "/stats": statsView, "/admin": adminView };
function hashPath() { const raw = (location.hash || "#/").replace(/^#/, ""); return raw.startsWith("/") ? raw : "/"; }
function nav() {
  const h = hashPath();
  const fn = ROUTES[h] || landing;
  if (["/account", "/skins", "/stats", "/admin"].includes(h) && !ME) { location.hash = "/auth"; return; }
  if (h === "/auth" && ME) { location.hash = "/"; return; }
  view.innerHTML = "";
  Promise.resolve(fn()).catch(e => { console.error(e); view.innerHTML = '<p class="hint">加载失败</p>'; });
  refreshNavUI();
}
window.addEventListener("hashchange", nav);
function refreshNavUI() {
  const h = hashPath();
  $$("nav a").forEach(a => a.classList.toggle("on", a.getAttribute("href") === "#" + h));
  $$(".n-account,.n-skins,.n-stats").forEach(a => a.classList.toggle("hidden", !ME));
  $(".n-admin").classList.toggle("hidden", ME?.role !== "admin");
  $(".n-auth").classList.toggle("hidden", !!ME);
  $("#logout").classList.toggle("hidden", !ME);
  $("#whoami").textContent = ME ? ME.username : "";
}
function tpl(id) { return $("#" + id).content.cloneNode(true); }
function mount(id) { view.innerHTML = ""; view.appendChild(tpl(id)); }

async function landing() {
  mount("tpl-landing");
  const m = await api("/api/catalog/meta");
  if (m && m.ok && m.meta) {
    const c = m.meta.counts || {}, cov = m.meta.coverage || {};
    const el = document.createElement("p");
    el.className = "hint";
    el.style.marginTop = "4px";
    el.textContent = "皮肤数据快照:" + (m.meta.updatedAt || "未知") + " · 武器 " + (c.weapons || 0) +
      " / 贴纸 " + (c.stickers || 0) + " / 挂饰 " + (c.keychains || 0) +
      " · 预览图覆盖 枪皮 " + (cov.weaponSkins || "-") + "、贴纸 " + (cov.stickers || "-");
    $(".hero", view)?.appendChild(el);
  }
  const j = await api("/api/servers");
  if (j.ok && j.servers.length) {
    $("#servers").innerHTML = j.servers.map(s => {
      const m = s.age_sec != null ? s.age_sec / 60 : mins(s.last_heartbeat);
      return '<div class="srv"><b>' + esc(s.server_id) + '</b><br>地图 ' + esc(s.current_map || "-") +
        ' · 模式 ' + esc(modeName(s.current_mode)) + ' · 玩家 ' + s.players + '<br>' +
        (m < 3 ? '<span class="live">● 在线</span>' : '<span>○ 离线(' + m.toFixed(0) + ' 分钟前)</span>') + '</div>';
    }).join("");
  } else {
    $("#servers").innerHTML = '<p class="hint">暂无服务器心跳 — 部署插件后自动显示。</p>';
  }
}
const modeName = (m) => ({ practice: "训练", deathmatch: "死斗", competitive: "满十竞技", none: "仅外观" }[m] || m || "-");
const mins = (iso) => (Date.now() - new Date(String(iso).replace(" ", "T") + (String(iso).includes("+") || String(iso).includes("Z") ? "" : "Z")).getTime()) / 60000;


async function authView() {
  mount("tpl-auth");
  $$(".tab").forEach(b => b.onclick = () => {
    $$(".tab").forEach(x => x.classList.toggle("on", x === b));
    $("#f-login").classList.toggle("hidden", b.dataset.t !== "login");
    $("#f-register").classList.toggle("hidden", b.dataset.t !== "register");
  });
  $("#f-login").onsubmit = async (e) => {
    e.preventDefault();
    const f = new FormData(e.target);
    if (!String(f.get("username") || "") || !String(f.get("password") || "")) { toast(t("empty_fields"), "bad"); return; }
    const j = await api("/api/login", { method: "POST", body: JSON.stringify({ username: f.get("username"), password: f.get("password") }) });
    if (j.ok) { await me(); location.hash = "/"; toast("登录成功", "ok"); }
  };
  $("#f-register").onsubmit = async (e) => {
    e.preventDefault();
    const f = new FormData(e.target);
    if (!String(f.get("username") || "") || !String(f.get("password") || "")) { toast(t("empty_fields"), "bad"); return; }
    const j = await api("/api/register", { method: "POST", body: JSON.stringify({ username: f.get("username"), email: f.get("email") || null, password: f.get("password") }) });
    if (j.ok) { await me(); location.hash = "/account"; toast("注册成功!去服务器输入 /cs2bind 拿绑定码", "ok"); }
  };
}

async function accountView() {
  mount("tpl-account");
  drawSteam();
  $("#f-bind").onsubmit = async (e) => {
    e.preventDefault();
    const code = String(new FormData(e.target).get("code") || "").toUpperCase();
    const j = await api("/api/bind", { method: "POST", body: JSON.stringify({ code }) });
    if (j.ok) { toast("绑定成功!若在服内将自动同步外观", "ok"); await me(); drawSteam(); }
  };
}
async function drawSteam() {
  await me();
  const list = (ME && ME.steamids) || [];
  $("#mySteam").innerHTML = list.length ? list.map(s =>
    '<div class="row"><span class="grow">' + esc(s.persona || "(无昵称)") + " <code>" + esc(s.steamid64) + "</code> " + (s.is_primary ? "★主号" : "") + "</span>" +
    '<button class="btn mini" data-del="' + esc(s.steamid64) + '">解绑</button></div>').join("")
    : '<p class="hint">还没有绑定 SteamID。进入服务器输入 /cs2bind 获取绑定码。</p>';
  $$("#mySteam [data-del]").forEach(b => b.onclick = async () => {
    if (!confirm("确定解绑该 SteamID?皮肤配置保留但不再对该 SteamID 生效。")) return;
    const j = await api("/api/steam/" + b.dataset.del, { method: "DELETE" });
    if (j.ok) { toast("已解绑", "ok"); await me(); drawSteam(); }
  });
}

async function me() {
  const j = await api("/api/me");
  ME = j.ok ? j : null;
  refreshNavUI();
  return ME;
}


/* ---------- 外观编辑器 ---------- */
const CATS = [["rifles", "步枪"], ["pistols", "手枪"], ["snipers", "狙击枪"], ["smgs", "冲锋枪"], ["shotguns", "霰弹枪"], ["machineguns", "机枪"]];
const WEAR_PRESETS = [["FN", 0.01], ["MW", 0.10], ["FT", 0.26], ["W", 0.41], ["BS", 0.48], ["WW", 0.62]];
const rar = (r) => (r || "").replace("rarity_", "").replace("_weapon", "").replace("_character", "").replace("_ancient", "");
const sknCell = (s, on, attrs, img) =>
  '<div class="skn r-' + rar(s.r) + (on ? " on" : "") + '" ' + attrs + '>' + (img ? imgTag(img) : "") +
  '<span class="nm">' + esc(s.zh || s.name) + '</span><span class="zh">' + esc(s.name) + "</span></div>";

async function skinsView() {
  mount("tpl-skins");
  if (!CAT) { const j = await api("/api/catalog"); if (j.ok) CAT = j; }
  if (!CAT) { view.innerHTML = '<p class="hint">目录加载失败</p>'; return; }
  if (!IC) { const m = await api("/api/iconmap"); if (m && (m.S || m.ST)) IC = m; }
  const steams = (ME && ME.steamids) || [];
  if (!steams.length) { view.innerHTML = '<section class="card"><h2>需要先绑定 SteamID</h2><p class="hint">进服输入 <code>/cs2bind</code>,然后到【账号与绑定】输入绑定码。</p><a class="btn primary" href="#/account">去绑定</a></section>'; return; }
  $("#steamPick").innerHTML = steams.map(s => '<option value="' + esc(s.steamid64) + '">' + esc(s.persona || s.steamid64) + "</option>").join("");
  $("#teamPick").remove();
  S = { steam: steams[0].steamid64, items: {}, stickers: {}, dirty: false, sel: 0, cur: null };
  $("#steamPick").onchange = (e) => {
    if (S.dirty && !confirm("当前外观有未保存改动,切换账号会丢失。确定切换?")) { e.target.value = S.steam; return; }
    S.steam = e.target.value;
    S.dirty = false;
    loadSaved();
  };
  $("#saveBtn").onclick = saveAll;
  $("#resetBtn").onclick = () => { if (S.sel) { delete S.items[S.sel]; S.stickers[S.sel] = null; markDirty(); detail(); slotList(); } };
  await loadSaved();
}

async function loadSaved() {
  const j = await api("/api/loadout/" + S.steam);
  S.items = {}; S.stickers = {};
  if (j.ok) {
    for (const it of j.items) S.items[it.weapon_defindex] = {
      def: it.weapon_defindex, knifeDef: it.knife_target_defindex, paint: it.paintkit, seed: it.paint_seed,
      wear: Number(it.paint_wear), nametag: it.nametag || "", st: !!it.stattrak, stCount: it.stattrak_count, kc: it.keychain_id,
    };
    for (const st of j.stickers) {
      (S.stickers[st.weapon_defindex] || (S.stickers[st.weapon_defindex] = [null, null, null, null, null]))[st.slot] =
        { id: st.sticker_id, ox: Number(st.offset_x), oy: Number(st.offset_y), rot: Number(st.rotation), scale: Number(st.scale), wear: Number(st.wear) };
    }
  }
  slotList();
}

function markDirty() { if (S) { S.dirty = true; const f = $("#dirtyFlag"); if (f) f.textContent = "● 未保存"; } }

function slotList() {
  const box = $("#slotList");
  const html = [];
  html.push('<div class="cat">刀具</div>', slotRow("knife", "刀 / 刺刀", 42));
  html.push('<div class="cat">手部</div>', slotRow("gloves", "手套", -1));
  html.push('<div class="cat">角色 / 音乐 / 徽章</div>', slotRow("agent", "探员", -2), slotRow("music", "音乐包", -3), slotRow("pin", "徽章", -4));
  for (const [cat, label] of CATS) {
    const ws = CAT.weapons.filter(w => w.category === cat && w.defindex);
    if (!ws.length) continue;
    html.push('<div class="cat">' + label + "</div>");
    for (const w of ws) html.push(slotRow("weapon", w.zh || w.name, w.defindex));
  }
  box.innerHTML = html.join("");
  $$(".slot", box).forEach(el => el.onclick = () => {
    S.sel = Number(el.dataset.def); S.cur = el.dataset.kind;
    $$(".slot", box).forEach(x => x.classList.toggle("on", x === el));
    detail();
  });
}
function slotRow(kind, name, def) {
  const it = S.items[def];
  const has = it && (it.paint > 0 || it.knifeDef > 0 || (it.nametag || "").replace("|", "") !== "");
  const on = S.sel === def ? " on" : "";
  return '<div class="slot' + on + '" data-def="' + def + '" data-kind="' + kind + '"><span>' + esc(name) + "</span>" + (has ? '<span class="has">✓</span>' : "") + "</div>";
}

function item(def) { return S.items[def] || (S.items[def] = { def, knifeDef: 0, paint: 0, seed: 0, wear: 0.12, nametag: "", st: false, stCount: 0, kc: 0 }); }

function detail() {
  const d = $("#slotDetail");
  if (!S.sel) return;
  const it = item(S.sel);
  if (S.cur === "knife") return knifeUI(d, it);
  if (S.cur === "gloves") return glovesUI(d, it);
  if (S.cur === "agent") return agentUI(d, it);
  if (S.cur === "music") return musicUI(d, it);
  if (S.cur === "pin") return pinUI(d, it);
  weaponUI(d, S.sel, it);
}



function wearUI(it) {
  return '<div class="field"><label>磨损 ' + (it.wear * 100).toFixed(1) + "% (" + wearTier(it.wear) + ")</label>" +
    '<input type="range" data-ew min="0" max="1" step="0.001" value="' + it.wear + '" style="width:100%">' +
    '<div class="wearbar"><div class="mk" style="left:' + it.wear * 100 + '%"></div></div>' +
    '<div class="presets">' + WEAR_PRESETS.map(p => '<button type="button" class="btn mini" data-wear="' + p[1] + '">' + p[0] + "</button>").join("") + "</div></div>";
}
const wearTier = (w) => w < 0.07 ? "崭新出厂" : w < 0.15 ? "略有磨损" : w < 0.38 ? "久经沙场" : w < 0.46 ? "破损不堪" : "战痕累累";

function wireWear(box, it) {
  const r = $("[data-ew]", box);
  if (r) r.oninput = (e) => {
    it.wear = Number(e.target.value); markDirty();
    $(".wearbar .mk", box).style.left = it.wear * 100 + "%";
    const lbl = r.parentElement.querySelector("label");
    if (lbl) lbl.textContent = "磨损 " + (it.wear * 100).toFixed(1) + "% (" + wearTier(it.wear) + ")";
  };
  $$(".presets button", box).forEach(b => b.onclick = () => { it.wear = Number(b.dataset.wear); markDirty(); detail(); });
}

function weaponUI(d, def, it) {
  const w = CAT.weapons.find(x => x.defindex === def);
  if (!w) return;
  const skin = it.paint > 0 ? w.skins.find(s => s.p === it.paint) : null;
  d.innerHTML = "<h3>" + esc(w.zh || w.name) + ' <span class="hint">' + esc(w.name) + " · " + def + "</span></h3>" +
    '<input class="search" id="skinSearch" placeholder="搜索皮肤(中/英)...">' +
    '<div class="grid" id="skinGrid"></div><div id="paintPanel"></div>';
  const draw = (q = "") => {
    const list = w.skins.filter(s => !q || ((s.name || "") + " " + (s.zh || "")).toLowerCase().includes(q.toLowerCase()));
    $("#skinGrid").innerHTML = list.slice(0, 300).map(s => sknCell(s, s.p === it.paint, 'data-p="' + s.p + '"', imgOf.skin(def, s.p))).join("");
    $$("#skinGrid .skn").forEach(el => el.onclick = () => { it.paint = Number(el.dataset.p); markDirty(); weaponUI(d, def, it); });
  };
  $("#skinSearch").oninput = (e) => draw(e.target.value);
  draw();
  const pp = $("#paintPanel");
  const previewUrl = it.paint > 0 ? imgOf.skin(def, it.paint) : imgOf.skin(def, 0);
  const nameOf = it.paint > 0 ? (skin ? (skin.zh || skin.name) : "#" + it.paint) : "默认外观";
  pp.innerHTML = '<div class="pv"><img src="' + esc(previewUrl || "") + '" onerror="this.parentNode.classList.add(\'noimg\')">' +
    '<div class="pvmeta"><b>' + esc(w.zh || w.name) + " · " + esc(nameOf) + "</b>" +
    '<span class="hint">' + (it.paint > 0 ? wearTier(it.wear) + " · " + (100 - it.wear * 100).toFixed(1) + "% 完好" : "游戏默认(仍可直接贴贴纸 / 挂挂饰 / 改名)") + "</span></div></div>" +
    (it.paint > 0 ? '<div class="row2"><div>' + wearUI(it) +
      '<div class="field"><label>磨损种子 Seed</label><input type="number" data-seed value="' + it.seed + '" min="0" max="1000000" style="width:130px"> <button type="button" class="btn mini" data-rseed>随机</button></div></div>' + extrasCol(it) + "</div>"
      : '<div class="row2"><div>' + extrasCol(it) + "</div><div></div></div>") +
    kcPickerUI(it);
  wireWear(pp, it);
  const seedEl = $("[data-seed]", pp);
  if (seedEl) seedEl.oninput = (e) => { it.seed = num(e.target.value); markDirty(); };
  const rseedEl = $("[data-rseed]", pp);
  if (rseedEl) rseedEl.onclick = () => { it.seed = Math.floor(Math.random() * 1000000); markDirty(); detail(); };
  const tagEl = $("[data-tag]", pp);
  if (tagEl) tagEl.oninput = (e) => { it.nametag = e.target.value; markDirty(); };
  const stEl = $("[data-st]", pp);
  if (stEl) stEl.onchange = (e) => { it.st = e.target.checked; markDirty(); };
  const stcEl = $("[data-stc]", pp);
  if (stcEl) stcEl.oninput = (e) => { it.stCount = num(e.target.value); markDirty(); };
  $$("[data-kcpick]", pp).forEach(b => b.onclick = () => { it.kc = Number(b.dataset.kcpick); markDirty(); detail(); });
  stickerUI(pp, def, skin);
}

function extrasCol(it) {
  return '<div><div class="field"><label>改名标签(≤32 字,默认皮肤也可用)</label><input data-tag maxlength="32" value="' + esc(it.nametag) + '" style="width:100%" placeholder="例如:This is my AK-47"></div>' +
    '<div class="field"><label><input type="checkbox" data-st ' + (it.st ? "checked" : "") + '> StatTrak™ 计数</label> <input type="number" data-stc value="' + it.stCount + '" min="0" style="width:90px"></div></div>';
}

function kcPickerUI(it) {
  if (!CAT.keychains.length) return "";
  const cell = (k, id) => '<div class="kcell' + (it.kc === id ? " on" : "") + '" data-kcpick="' + id + '" title="' + esc(k) + '">' +
    (id === 0 ? '<div class="skph">∅</div>' : imgTag(imgOf.keychain(id))) + '<span>' + esc(k) + "</span></div>";
  return '<div class="field"><label>挂件 / 钥匙圈(挂饰,任何武器可选,不依赖皮肤)</label><div class="krow">' +
    cell("无", 0) +
    CAT.keychains.map(k => cell(k.zh || k.name, k.id)).join("") + "</div></div>";
}

function stickerUI(pp, def, skin) {
  const arr = S.stickers[def] || (S.stickers[def] = [null, null, null, null, null]);
  const box = document.createElement("div");
  box.innerHTML = "<h4>贴纸(最多 5 枚)</h4>" + arr.map((s, i) =>
    '<div class="stk"><span class="n">' + (i + 1) + '</span>' + (s ? imgTag(imgOf.sticker(s.id), "stkimg") : "") + '<span class="grow">' + (s ? esc(stickerName(s.id)) : "<i>空槽位</i>") + "</span>" +
    (s ? '<button type="button" class="btn mini" data-adj="' + i + '">调位置</button><button type="button" class="btn mini" data-rm="' + i + '">移除</button>'
       : '<button type="button" class="btn mini" data-add="' + i + '">选贴纸</button>') + "</div>").join("") + '<div id="stkAdj"></div>';
  pp.appendChild(box);
  $$("[data-add]", box).forEach(b => b.onclick = () => stickerPicker(Number(b.dataset.add), arr));
  $$("[data-rm]", box).forEach(b => b.onclick = () => { arr[Number(b.dataset.rm)] = null; markDirty(); detail(); });
  $$("[data-adj]", box).forEach(b => b.onclick = () => stickerAdjust(Number(b.dataset.adj), arr));
}
const stickerName = (id) => { const s = CAT.stickers.find(x => x.id === Number(id)); return s ? (s.zh || s.name) : "sticker#" + id; };

function stickerPicker(slot, arr) {
  const adj = $("#stkAdj");
  const groups = Array.from(new Set(CAT.stickers.map(s => s.gz || s.g || "其他")));
  adj.innerHTML = '<div class="field"><label>选择贴纸(槽位 ' + (slot + 1) + ") · 可按赛事/胶囊过滤</label>" +
    '<select id="stkGroup"><option value="">全部</option>' + groups.map(g => "<option>" + esc(g) + "</option>").join("") + "</select>" +
    '<input class="search" id="stkQ" placeholder="搜索贴纸名/战队标..." style="margin-top:6px"></div><div class="grid" id="stkGrid"></div>';
  const draw = () => {
    const g = $("#stkGroup").value, q = $("#stkQ").value.toLowerCase();
    const list = CAT.stickers.filter(s => (!g || (s.gz || s.g) === g) && (!q || ((s.name || "") + " " + (s.zh || "")).toLowerCase().includes(q))).slice(0, 200);
    $("#stkGrid").innerHTML = list.map(s => sknCell(s, false, 'data-id="' + s.id + '"', imgOf.sticker(s.id))).join("");
    $$("#stkGrid .skn").forEach(el => el.onclick = () => {
      arr[slot] = { id: Number(el.dataset.id), ox: 0, oy: 0, rot: 0, scale: 1, wear: 0.12 };
      markDirty(); detail();
    });
  };
  $("#stkGroup").onchange = draw; $("#stkQ").oninput = draw; draw();
}

function stickerAdjust(slot, arr) {
  const s = arr[slot];
  if (!s) { detail(); return; }   // 槽位已被清空时避免 s.ox 取空崩溃
  const adj = $("#stkAdj");
  adj.innerHTML = '<div class="row2"><div>' +
    '<div class="field"><label>X 偏移 <b id="v-ox">' + s.ox.toFixed(2) + '</b></label><input type="range" data-ox min="-0.5" max="0.5" step="0.005" value="' + s.ox + '" style="width:100%"></div>' +
    '<div class="field"><label>Y 偏移 <b id="v-oy">' + s.oy.toFixed(2) + '</b></label><input type="range" data-oy min="-0.5" max="0.5" step="0.005" value="' + s.oy + '" style="width:100%"></div></div><div>' +
    '<div class="field"><label>旋转 <b id="v-rot">' + s.rot.toFixed(0) + '°</b></label><input type="range" data-rot min="-180" max="180" step="1" value="' + s.rot + '" style="width:100%"></div>' +
    '<div class="field"><label>缩放 <b id="v-sc">' + s.scale.toFixed(2) + '</b></label><input type="range" data-sc min="0.5" max="1.6" step="0.01" value="' + s.scale + '" style="width:100%"></div>' +
    '<div class="field"><label>贴纸磨损 <b id="v-w">' + s.wear.toFixed(2) + '</b></label><input type="range" data-sw min="0" max="1" step="0.01" value="' + s.wear + '" style="width:100%"></div></div></div>';
  const set = (sel, key, fmt) => { $("[data-" + sel + "]", adj).oninput = (e) => { s[key] = Number(e.target.value); markDirty(); $("#v-" + fmt).textContent = key === "rot" ? s[key].toFixed(0) + "°" : s[key].toFixed(2); }; };
  set("ox", "ox", "ox"); set("oy", "oy", "oy"); set("rot", "rot", "rot"); set("sc", "scale", "sc"); set("sw", "wear", "w");
}


function knifeUI(d, it) {
  const kn = CAT.knives;
  const cur = kn.find(k => k.defindex === it.knifeDef);
  d.innerHTML = '<h3>刀</h3><div class="field"><label>刀型</label><select id="knifeModel"><option value="0">默认(不替换)</option>' +
    kn.map(k => '<option value="' + k.defindex + '"' + (k.defindex === it.knifeDef ? " selected" : "") + ">" + esc(k.zh || k.name) + "</option>").join("") +
    '</select></div><div id="knifeSkins"></div>';
  $("#knifeModel").onchange = (e) => { it.knifeDef = Number(e.target.value); it.paint = 0; markDirty(); slotList(); detail(); };
  if (!cur) return;
  const skin = cur.skins.find(s => s.p === it.paint);
  const box = $("#knifeSkins");
  box.innerHTML = '<input class="search" id="knSearch" placeholder="搜索涂装..."><div class="grid" id="knGrid"></div><div id="knPanel"></div>';
  const draw = (q = "") => {
    const list = cur.skins.filter(s => !q || ((s.name || "") + " " + (s.zh || "")).toLowerCase().includes(q.toLowerCase()));
    $("#knGrid").innerHTML = list.slice(0, 300).map(s => sknCell(s, s.p === it.paint, 'data-p="' + s.p + '"', imgOf.skin(s.d || cur.defindex, s.p))).join("");
    $$("#knGrid .skn").forEach(el => el.onclick = () => { it.paint = Number(el.dataset.p); markDirty(); knifeUI(d, it); });
  };
  $("#knSearch").oninput = (e) => draw(e.target.value); draw();
  const pp = $("#knPanel");
  const pv = imgOf.skin(it.knifeDef, it.paint) || imgOf.skin(it.knifeDef, 0);
  pp.innerHTML = '<div class="pv"><img src="' + esc(pv || "") + '" onerror="this.parentNode.classList.add(\'noimg\')"><div class="pvmeta"><b>' +
    esc((cur.zh || cur.name)) + " · " + esc(it.paint > 0 ? (skin ? skin.zh || skin.name : "#" + it.paint) : "默认") + "</b>" +
    '<span class="hint">' + (it.paint > 0 ? wearTier(it.wear) : "默认钢色(仍可贴贴纸/挂挂饰/改名)") + "</span></div></div>" +
    (it.paint > 0 ? wearUI(it) + '<div class="field"><label>Seed</label><input type="number" data-seed value="' + it.seed + '" style="width:130px"> <button type="button" class="btn mini" data-rseed>随机</button></div>' : "") +
    '<div class="field"><label>改名(≤32 字)</label><input data-tag maxlength="32" value="' + esc(it.nametag) + '"></div>' +
    '<div class="field"><label><input type="checkbox" data-st ' + (it.st ? "checked" : "") + '> StatTrak™</label> 计数 <input type="number" data-stc value="' + it.stCount + '"></div>' +
    kcPickerUI(it);
  wireWear(pp, it);
  const se = $("[data-seed]", pp); if (se) se.oninput = (e) => { it.seed = num(e.target.value); markDirty(); };
  const rs = $("[data-rseed]", pp); if (rs) rs.onclick = () => { it.seed = Math.floor(Math.random() * 1000000); markDirty(); knifeUI(d, it); };
  $("[data-tag]", pp).oninput = (e) => { it.nametag = e.target.value; markDirty(); };
  $("[data-st]", pp).onchange = (e) => { it.st = e.target.checked; markDirty(); };
  $("[data-stc]", pp).oninput = (e) => { it.stCount = num(e.target.value); markDirty(); };
  $$("[data-kcpick]", pp).forEach(b => b.onclick = () => { it.kc = Number(b.dataset.kcpick); markDirty(); knifeUI(d, it); });
  stickerUI(pp, 42, skin);
}

function glovesUI(d, it) {
  const g = CAT.gloves.find(x => x.defindex === it.knifeDef);
  d.innerHTML = '<h3>手套</h3><div class="field"><label>手套型号</label><select id="glModel"><option value="0">默认</option>' +
    CAT.gloves.map(x => '<option value="' + x.defindex + '"' + (x.defindex === it.knifeDef ? " selected" : "") + ">" + esc(x.zh || x.name) + "</option>").join("") +
    '</select></div><div id="glSkins"></div>';
  $("#glModel").onchange = (e) => { it.knifeDef = Number(e.target.value); it.paint = 0; markDirty(); slotList(); detail(); };
  if (!g) return;
  const box = $("#glSkins");
  box.innerHTML = '<input class="search" id="glSearch" placeholder="搜索涂装..."><div class="grid" id="glGrid"></div><div id="glPanel"></div>';
  const draw = (q = "") => {
    const list = g.skins.filter(s => !q || ((s.name || "") + " " + (s.zh || "")).toLowerCase().includes(q.toLowerCase()));
    $("#glGrid").innerHTML = list.slice(0, 200).map(s => sknCell(s, s.p === it.paint, 'data-p="' + s.p + '"', imgOf.glove(s.p))).join("");
    $$("#glGrid .skn").forEach(el => el.onclick = () => { it.paint = Number(el.dataset.p); markDirty(); glovesUI(d, it); });
  };
  $("#glSearch").oninput = (e) => draw(e.target.value); draw();
  const pp = $("#glPanel");
  pp.innerHTML = it.paint > 0 ? wearUI(it) : "";
  wireWear(pp, it);
}

function agentUI(d, it) {
  const cur = (it.nametag || "").split("|");
  const ct = CAT.agents.filter(a => a.team === "ct");
  const tn = CAT.agents.filter(a => a.team !== "ct");
  d.innerHTML = '<h3>探员模型</h3><p class="hint">社区服可能未下载付费探员模型,缺失时服务器自动回退默认。留空 = 默认探员。</p>' +
    '<div class="row2"><div class="field"><label>CT 探员</label><select id="agCt"><option value="">默认</option>' +
    ct.map(a => '<option value="' + esc(a.model) + '"' + (a.model === cur[0] ? " selected" : "") + ">" + esc(a.zh || a.name) + "</option>").join("") +
    '</select></div><div class="field"><label>T 探员</label><select id="agT"><option value="">默认</option>' +
    tn.map(a => '<option value="' + esc(a.model) + '"' + (a.model === cur[1] ? " selected" : "") + ">" + esc(a.zh || a.name) + "</option>").join("") +
    "</select></div></div>";
  const sync = () => { it.nametag = $("#agCt").value + "|" + $("#agT").value; markDirty(); slotList(); agPreview(); };
  const agPreview = () => {
    const c = $("#agCt").value, t = $("#agT").value;
    $("#agPv").innerHTML = '<div class="pv2">' +
      (c ? '<figure><img src="' + esc(imgOf.agent(c, 3) || "") + '" onerror="this.parentNode.style.display=\'none\'"><figcaption>CT · ' + esc(c.split("/").pop()) + "</figcaption></figure>" : "") +
      (t ? '<figure><img src="' + esc(imgOf.agent(t, 2) || "") + '" onerror="this.parentNode.style.display=\'none\'"><figcaption>T · ' + esc(t.split("/").pop()) + "</figcaption></figure>" : "") +
      "</div>";
  };
  $("#agCt").onchange = sync; $("#agT").onchange = sync;
  d.innerHTML += '<div id="agPv"></div>';
  agPreview();
}

function musicUI(d, it) {
  d.innerHTML = '<h3>MVP 音乐包</h3><div class="field"><select id="mkPick"><option value="0">默认</option>' +
    CAT.music.map(m => '<option value="' + m.id + '"' + (m.id === it.knifeDef ? " selected" : "") + ">" + esc(m.zh || m.name) + "</option>").join("") +
    "</select></div>";
  $("#mkPick").onchange = (e) => { it.knifeDef = Number(e.target.value); markDirty(); };
}

function pinUI(d, it) {
  const names = ["无", "铜 I", "铜 II", "铜 III", "铜 IV", "银 I", "银 II", "银 III", "银 IV", "金 I", "金 II", "金 III", "金 IV", "巅峰 I", "巅峰 II", "巅峰 III", "巅峰 IV"];
  d.innerHTML = '<h3>徽章(展示)</h3><div class="field"><select id="pinPick">' +
    names.map((n, i) => '<option value="' + i + '"' + (i === it.knifeDef ? " selected" : "") + ">" + n + "</option>").join("") +
    "</select></div>";
  $("#pinPick").onchange = (e) => { it.knifeDef = Number(e.target.value); markDirty(); };
}

async function saveAll() {
  const items = Object.values(S.items).filter(it =>
    it.paint > 0 || it.knifeDef > 0 || (it.nametag || "").replace("|", "") !== "" || it.kc > 0 || it.st);
  const stickers = [];
  for (const def of Object.keys(S.stickers)) {
    const arr = S.stickers[def];
    if (!arr) continue;
    arr.forEach((s, i) => { if (s) stickers.push({ def: Number(def), slot: i, id: s.id, ox: s.ox, oy: s.oy, rot: s.rot, scale: s.scale, wear: s.wear }); });
  }
  const j = await api("/api/loadout/" + S.steam, { method: "POST", body: JSON.stringify({ items, stickers }) });
  if (j.ok) { S.dirty = false; $("#dirtyFlag").textContent = ""; toast("已保存 — 服务器内约 2 秒自动生效", "ok"); slotList(); }
}

async function statsView() {
  mount("tpl-stats");
  const steams = (ME && ME.steamids) || [];
  if (!steams.length) { $("#statsBody").innerHTML = '<p class="hint">先绑定 SteamID。</p>'; return; }
  const j = await api("/api/stats/" + steams[0].steamid64);
  if (!j.ok) return;
  const dm = j.dm || {}, elo = j.elo || {};
  const kd = (dm.deaths || 0) === 0 ? (dm.kills || 0) : ((dm.kills || 0) / dm.deaths).toFixed(2);
  $("#statsBody").innerHTML = '<div class="statgrid">' +
    "<div class=\"stat\"><b>" + (dm.kills || 0) + "</b>死斗击杀</div>" +
    "<div class=\"stat\"><b>" + kd + "</b>K/D</div>" +
    "<div class=\"stat\"><b>" + (dm.headshots || 0) + "</b>爆头</div>" +
    "<div class=\"stat\"><b>" + (dm.killstreak_best || 0) + "</b>最高连杀</div>" +
    "<div class=\"stat\"><b>" + (elo.elo ?? "-") + "</b>竞技 Elo</div>" +
    "<div class=\"stat\"><b>" + (elo.wins || 0) + "胜/" + (elo.losses || 0) + "负</b>满十竞技</div></div>" +
    "<h3>最近比赛</h3><table><tr><th>地图</th><th>时间</th><th>比分</th><th>K/D</th><th>Elo</th></tr>" +
    ((j.matches || []).map(m => "<tr><td>" + esc(m.map_name) + "</td><td>" + esc(m.started_at) + "</td><td>" + m.score1 + " : " + m.score2 +
      "</td><td>" + m.kills + "/" + m.deaths + "</td><td style=\"color:" + (m.elo_delta >= 0 ? "var(--ok)" : "var(--bad)") + "\">" +
      (m.elo_delta > 0 ? "+" : "") + m.elo_delta + "</td></tr>").join("") || '<tr><td colspan="5" class="hint">暂无记录</td></tr>') + "</table>";
}

async function adminView() {
  mount("tpl-admin");
  const j = await api("/api/admin/users");
  if (!j.ok) return;
  $("#adminBody").innerHTML = "<table><tr><th>ID</th><th>用户名</th><th>SteamID</th><th>角色</th><th>状态</th><th>操作</th></tr>" +
    j.users.map(u => "<tr><td>" + u.account_id + "</td><td>" + esc(u.username) + '</td><td class="hint">' + esc(u.steamids || "-") +
      "</td><td>" + u.role + "</td><td>" + u.status + '</td><td><button type="button" class="btn mini" data-role="' + u.account_id + '">' + (u.role === "admin" ? "降为普通" : "设为管理") + "</button> " +
      '<button type="button" class="btn mini" data-ban="' + u.account_id + '">' + (u.status === "banned" ? "解封" : "封禁") + "</button></td></tr>").join("") + "</table>";
  $$("#adminBody [data-role]").forEach(b => b.onclick = async () => {
    const u = j.users.find(x => String(x.account_id) === b.dataset.role);
    await api("/api/admin/role", { method: "POST", body: JSON.stringify({ account_id: u.account_id, role: u.role === "admin" ? "user" : "admin" }) });
    adminView();
  });
  $$("#adminBody [data-ban]").forEach(b => b.onclick = async () => {
    const u = j.users.find(x => String(x.account_id) === b.dataset.ban);
    await api("/api/admin/status", { method: "POST", body: JSON.stringify({ account_id: u.account_id, status: u.status === "banned" ? "active" : "banned" }) });
    adminView();
  });
}

window.addEventListener("beforeunload", (e) => {
  if (S && S.dirty) { e.preventDefault(); e.returnValue = ""; }
});

$("#logout").onclick = async (e) => {
  e.preventDefault();
  if (S && S.dirty && !confirm("有未保存的外观改动,确定退出登录?")) return;
  await api("/api/logout", { method: "POST" });
  ME = null; S = null; refreshNavUI(); nav();
};

(async function boot() {
  await me();
  nav();
})();

