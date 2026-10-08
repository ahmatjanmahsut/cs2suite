"use strict";
const { JSDOM } = require("D:/cs2项目/cs2suite/tools/domtest/node_modules/jsdom");
const errors = [];
const net = [];
let jar = "";
async function boot() {
  const dom = await JSDOM.fromURL("http://127.0.0.1:8788/", {
    runScripts: "dangerously", resources: "usable", pretendToBeVisual: true,
    beforeParse(window) {
      window.fetch = (u, o) => {
        const abs = String(u).startsWith("http") ? u : "http://127.0.0.1:8788" + u;
        net.push((o && o.method || "GET") + " " + u);
        o = o || {};
        const h = Object.assign({}, o.headers);
        if (jar) h["Cookie"] = jar;
        return fetch(abs, Object.assign({}, o, { headers: h })).then(r => {
          const sc = r.headers.get("set-cookie");
          if (sc) jar = sc.split(";")[0];
          return r;
        });
      };
      window.confirm = () => true;
      window.addEventListener("error", e => errors.push("win: " + e.message));
      const c = window.console;
      c.error = (...a) => errors.push("console.error: " + a.map(String).join(" ").slice(0, 250));
    },
  });
  return dom;
}
const wait = ms => new Promise(r => setTimeout(r, ms));
(async () => {
  const dom = await boot();
  const w = dom.window, D = w.document;
  const $ = s => D.querySelector(s), $$ = s => Array.from(D.querySelectorAll(s));
  const click = el => el.dispatchEvent(new w.MouseEvent("click", { bubbles: true }));
  await wait(2200);
  // 登录
  w.location.hash = "#/auth"; await wait(400);
  $("#f-login input[name=username]").value = "tester01";
  $("#f-login input[name=password]").value = "Passw0rd!23";
  $("#f-login").dispatchEvent(new w.Event("submit", { bubbles: true, cancelable: true }));
  await wait(1400);
  console.log("登录:", $("#whoami").textContent === "tester01" ? "OK" : "FAIL", "errors:", errors.length);
  // 皮肤页
  w.location.hash = "#/skins"; await wait(2200);
  console.log("槽位:", $$(".slot").length, "| iconmap 已载:", !!w.eval("typeof IC !== 'undefined' && IC"));
  const ak = $$(".slot").find(s => s.dataset.def === "7");
  click(ak); await wait(500);
  const imgs = $$("#skinGrid .skn img").length;
  console.log("AK 皮肤格含图:", imgs + "/61");
  // 选第一款皮肤
  const first = $("#skinGrid .skn");
  click(first); await wait(300);
  console.log("大图预览 .pv:", $$("#paintPanel .pv").length, "| 挂饰选择器 kcell:", $$(".kcell").length);
  // 不选皮肤也能配置:先点掉皮肤(选默认)——重新选 def=9 AWP 不选皮肤看挂件是否可见
  const awp = $$(".slot").find(s => s.dataset.def === "9");
  click(awp); await wait(300);
  console.log("AWP 未选皮肤时: 改名输入:", !!$("[data-tag]") && true, "| 挂饰 kcell:", $$(".kcell").length, "| 贴纸槽按钮:", $$("[data-add]").length);
  // 给默认皮肤 AWP 贴一张贴纸 + 挂挂饰 + 改名
  const addBtn = $$("[data-add]")[0];
  if (addBtn) { click(addBtn); await wait(400); console.log("贴纸选择器含图:", $$("#stkGrid .skn img").length + "/200"); const st = $("#stkGrid .skn"); if (st) { click(st); await wait(250); } }
  const kcells = $$(".kcell");
  if (kcells.length > 3) click(kcells[3]);
  const tag = $("[data-tag]"); if (tag) { tag.value = "NakedSkinTest"; }
  click($("#saveBtn")); await wait(900);
  console.log("保存:", JSON.stringify(net.filter(n => n.startsWith("POST /api/loadout")).length), "errors:", JSON.stringify(errors));
  // DB 校验:AWP(def 9)应有 sticker+kc+nametag 且 paintkit=0
  process.exit(0);
})().catch(e => { console.error("FATAL", e); process.exit(1); });
