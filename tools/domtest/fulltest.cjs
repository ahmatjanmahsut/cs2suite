
const { JSDOM } = require('D:/cs2项目/cs2suite/tools/domtest/node_modules/jsdom');
const errors = [];
const net = [];
JSDOM.fromURL('http://127.0.0.1:8788/', {
  runScripts: 'dangerously', resources: 'usable', pretendToBeVisual: true,
  beforeParse(window) {
    let jar = '';
    window.fetch = (u, o) => {
      const abs = String(u).startsWith('http') ? u : 'http://127.0.0.1:8788' + u;
      net.push((o && o.method || 'GET') + ' ' + u);
      o = o || {};
      const h = Object.assign({}, o.headers);
      if (jar) h['Cookie'] = jar;
      o.headers = h;
      return fetch(abs, o).then(r => {
        const sc = r.headers.get('set-cookie');
        if (sc) jar = sc.split(';')[0];
        return r;
      });
    };
    window.confirm = () => true;
    window.addEventListener('error', e => errors.push('win: ' + e.message));
    const c = window.console; c.error = (...a) => errors.push('console.error: ' + a.map(String).join(' ').slice(0, 300));
  }
}).then(async dom => {
  const w = dom.window, D = w.document;
  const wait = ms => new Promise(r => setTimeout(r, ms));
  await wait(2000);
  console.log('== boot ==', JSON.stringify({ errors, net, rootChildren: D.querySelector('#view').children.length }));
  // 1. 导航到登录页(模拟点击 nav 链接)
  w.location.hash = '#/auth'; await wait(300);
  const login = D.querySelector('#f-login');
  console.log('login form present:', !!login);
  if (!login) { console.log('STILL BROKEN', errors); process.exit(1); }
  // 2. 提交登录
  login.querySelector('input[name=username]').value = 'tester01';
  login.querySelector('input[name=password]').value = 'Passw0rd!23';
  login.dispatchEvent(new w.Event('submit', { bubbles: true, cancelable: true }));
  await wait(1200);
  console.log('after login hash:', w.location.hash, 'whoami:', D.querySelector('#whoami').textContent, 'errors:', JSON.stringify(errors));
  // 3. 进皮肤页
  w.location.hash = '#/skins'; await wait(1500);
  const slots = D.querySelectorAll('.slot');
  console.log('skins page slots:', slots.length, '| steamPick:', D.querySelector('#steamPick')?.options.length);
  // 4. 点击 AK-47 slot(def=7)
  const ak = Array.from(slots).find(s => s.dataset.def === '7');
  if (ak) {
    ak.dispatchEvent(new w.MouseEvent('click', { bubbles: true }));
    await wait(400);
    const grid = D.querySelector('#skinGrid');
    console.log('AK skin grid cells:', grid ? grid.children.length : 'NONE');
    // 选第一个皮肤
    const first = grid && grid.querySelector('.skn');
    if (first) { first.dispatchEvent(new w.MouseEvent('click', { bubbles: true })); await wait(200); }
    const paint = D.querySelector('#paintPanel');
    console.log('paint panel has wear slider:', !!(paint && paint.querySelector('[data-ew]')), '| sticker UI:', !!(paint && paint.querySelector('[data-add]')));
    // 打开贴纸选择器
    const addBtn = paint && paint.querySelector('[data-add]');
    if (addBtn) { addBtn.dispatchEvent(new w.MouseEvent('click', { bubbles: true })); await wait(300); console.log('sticker picker cells:', D.querySelectorAll('#stkGrid .skn').length); }
    // 保存
    D.querySelector('#saveBtn').dispatchEvent(new w.MouseEvent('click', { bubbles: true }));
    await wait(800);
    console.log('save net:', JSON.stringify(net.filter(n => n.startsWith('POST /api/loadout'))), 'errors:', JSON.stringify(errors));
  } else console.log('AK slot NOT FOUND');
  // 5. 其他页面巡检:刀/手套/探员/音乐/徽章/战绩
  const kinds = { knife: 42, gloves: -1, agent: -2, music: -3, pin: -4 };
  for (const [name, def] of Object.entries(kinds)) {
    const s = Array.from(D.querySelectorAll('.slot')).find(x => x.dataset.def === String(def));
    if (!s) { console.log(name + ': slot missing'); continue; }
    s.dispatchEvent(new w.MouseEvent('click', { bubbles: true })); await wait(120);
    const d = D.querySelector('#slotDetail');
    console.log(name + ': detail rendered ->', d.textContent.slice(0, 40).replace(/\s+/g, ' '));
  }
  w.location.hash = '#/stats'; await wait(1000);
  console.log('stats text:', D.querySelector('#statsBody')?.textContent.replace(/\s+/g, ' ').slice(0, 120));
  w.location.hash = '#/admin'; await wait(700);
  console.log('admin page (non-admin should redirect or empty):', JSON.stringify(errors.slice(-2)));
  console.log('ALL ERRORS:', JSON.stringify(errors));
  process.exit(0);
}).catch(e => { console.log('FATAL', e.stack && e.stack.slice(0, 400)); process.exit(1); });