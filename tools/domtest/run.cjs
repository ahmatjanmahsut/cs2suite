
const { JSDOM } = require('D:/cs2项目/cs2suite/tools/domtest/node_modules/jsdom');
const errors = [];
const net = [];
JSDOM.fromURL('http://127.0.0.1:8788/#/auth', {
  runScripts: 'dangerously', resources: 'usable',
  pretendToBeVisual: true,
  beforeParse(window) {
    const realFetch = (...a) => { net.push(String(a[0])); return fetch(...a); };
    window.fetch = (u, o) => {
      const abs = String(u).startsWith('http') ? u : 'http://127.0.0.1:8788' + u;
      net.push((o && o.method || 'GET') + ' ' + u);
      if (o && o.body && o.headers) o.headers = Object.assign({}, o.headers);
      return fetch(abs, o).then(r => r);
    };
    window.confirm = () => true;
    window.addEventListener('error', e => errors.push('window.error: ' + e.message));
    const c = window.console;
    c.error = (...a) => errors.push('console.error: ' + a.map(String).join(' ').slice(0, 200));
  }
}).then(async dom => {
  const w = dom.window;
  await new Promise(r => setTimeout(r, 2500)); // boot + landing/api 完成
  console.log('errors after boot:', JSON.stringify(errors));
  console.log('net after boot:', JSON.stringify(net));
  console.log('hash:', w.location.hash);
  // 检查 auth 表单是否挂载
  const login = w.document.querySelector('#f-login');
  console.log('#f-login present:', !!login);
  if (login) {
    login.querySelector('input[name=username]').value = 'tester01';
    login.querySelector('input[name=password]').value = 'Passw0rd!23';
    // 用 jsdom 无法原生提交(无 submit 事件?) 直接 dispatch submit
    login.dispatchEvent(new w.Event('submit', { bubbles: true, cancelable: true }));
    await new Promise(r => setTimeout(r, 1500));
    console.log('net after submit:', JSON.stringify(net));
    console.log('errors after submit:', JSON.stringify(errors));
    console.log('hash now:', w.location.hash, ' whoami:', w.document.querySelector('#whoami')?.textContent);
  }
  process.exit(0);
}).catch(e => { console.log('FATAL', e.message); process.exit(1); });