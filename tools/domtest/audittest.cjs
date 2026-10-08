"use strict";
const { JSDOM } = require("D:/cs2项目/cs2suite/tools/domtest/node_modules/jsdom");
const errors = [], net = [];
let jar = "";
const wait = ms => new Promise(r => setTimeout(r, ms));
(async () => {
  const dom = await JSDOM.fromURL("http://127.0.0.1:8788/", {
    runScripts: "dangerously", resources: "usable", pretendToBeVisual: true,
    beforeParse(w) {
      w.fetch = (u, o) => {
        const abs = String(u).startsWith("http") ? u : "http://127.0.0.1:8788" + u;
        net.push(((o && o.method) || "GET") + " " + u);
        o = o || {};
        const h = Object.assign({}, o.headers);
        if (jar) h["Cookie"] = jar;
        return fetch(abs, Object.assign({}, o, { headers: h })).then(r => {
          const sc = r.headers.get("set-cookie");
          if (sc) jar = sc.split(";")[0];
          return r;
        });
      };
      w.confirm = () => true;
      w.addEventListener("error", e => errors.push("win: " + e.message));
      const c = w.console;
      c.error = (...a) => errors.push("console: " + a.map(String).join(" ").slice(0, 200));
    },
  });
  const w = dom.window, D = w.document;
  const q = s => D.querySelector(s), qa = s => Array.from(D.querySelectorAll(s));
  await wait(2200);

  const srv = await (await fetch("http://127.0.0.1:8788/api/servers")).json();
  console.log("1) servers age_sec 字段:", srv.servers.length ? ("age_sec" in srv.servers[0]) : "无服务器行(跳过)");

  w.location.hash = "#/skins"; await wait(500);
  console.log("2) 未登录访问 #/skins 后 hash:", w.location.hash, "(应 #/auth)");

  q("#f-login input[name=username]").value = "tester01";
  q("#f-login input[name=password]").value = "Passw0rd!23";
  q("#f-login").dispatchEvent(new w.Event("submit", { bubbles: true, cancelable: true }));
  await wait(1300);
  console.log("3) 登录:", q("#whoami").textContent === "tester01" ? "OK" : "FAIL");

  w.location.hash = "#/skins"; await wait(2000);
  console.log("4) 槽位:", qa(".slot").length);

  const badRes = await w.fetch("/api/loadout/76561198012345678", { method: "POST", headers: { "Content-Type": "application/json", Cookie: jar },
    body: JSON.stringify({ items: [{ def: 9999, paint: 1 }], stickers: [] }) });
  console.log("5) 非法 defindex:", badRes.status, JSON.stringify(await badRes.json()), "(应 400 bad_defindex)");

  const okRes = await w.fetch("/api/loadout/76561198012345678", { method: "POST", headers: { "Content-Type": "application/json", Cookie: jar },
    body: JSON.stringify({ items: [{ def: 7, paint: 1449, seed: 1, wear: 0.1, nametag: "audit", st: false, stCount: 0, kc: 3 }], stickers: [{ def: 7, slot: 0, id: 10429, ox: 0, oy: 0, rot: 0, scale: 1, wear: 0.1 }] }) });
  console.log("6) 合法提交:", okRes.status, JSON.stringify(await okRes.json()));

  try { w.eval("stickerAdjust(3, [null,null,null,null,null])"); console.log("7) stickerAdjust 空槽守卫: OK"); }
  catch (e) { console.log("7) stickerAdjust 空槽守卫: FAIL " + e.message); }

  const forged = jar.replace(/cs2suite_auth=[^;]*/, "cs2suite_auth=abc.def");
  const fRes = await w.fetch("/api/me", { headers: { Cookie: forged } });
  console.log("8) 篡改 Cookie /api/me:", fRes.status, "(应 401)");

  const aRes = await w.fetch("/api/admin/users", { headers: { Cookie: jar } });
  console.log("9) 普通用户访问管理接口:", aRes.status, "(应 403)");

  console.log("10) JS 错误:", JSON.stringify(errors));
  process.exit(0);
})().catch(e => { console.error("FATAL", e && e.message); process.exit(1); });
