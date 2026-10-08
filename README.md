# CS2Suite — CS2 多功能整合插件 + 网页换肤面板

CS2 社区插件**的玩法整合为**一个 CounterStrikeSharp 插件**,
配一个**网页端(Web)**完成武器皮肤 / 贴纸的自定义;所有数据统一存放 **MySQL**(连接参数由用户在配置文件中提供)。

## 一、整合来源对照(GitHub)

| 需求品类 | 采用的插件 | 说明 |
|---|---|---|
| 练习模式 (practice mode) | **[MatchZy](https://github.com/shobhit-pathak/MatchZy)** ★504 | 业界标准训练/热身插件:无限弹药、冻结时间、位点保存传送、机器人管理、去掉落清除等玩法开关全部整合进 `PracticeModule` |
| 满十竞技 (10 人排位) | **[MatchZy](https://github.com/shobhit-pathak/MatchZy)** 的 Ready 系统 + 刀局流程 | `CompetitiveModule`:排队 → 满 10 人自动分队(Elo 蛇形平衡)→ 全员准备 → 刀局选边 → MR12 正赛 → 结算写入 Elo/战绩 |
| 死斗 (Deathmatch) | **[NockyCZ/CS2-Deathmatch](https://github.com/NockyCZ/CS2-Deathmatch)** ★133 | 同类下载量最高:`DeathmatchModule` 整合其即时重生、出生保护、连杀播报、选枪(!guns)、计分与排行,统计全部入 MySQL |
| 武器皮肤 | **[Nereziel/cs2-WeaponPaints](https://github.com/Nereziel/cs2-WeaponPaints)** ★415 | CS2 事实标准换肤插件(本身就是 MySQL 后端):注入引擎(Econ 属性注入、刀型 subclass、手套刷新、探员模型、音乐包、徽章)移植进 `EconApplier` |
| 贴纸 | **[Ayrton09/AstraSkins](https://github.com/Ayrton09/AstraSkins)** | 贴纸属性最完整的新一代插件(偏移/旋转/缩放/独立磨损):5 贴纸槽 + 挂饰(keychain) + StatTrak 实现取自其 `EconAttributeApplicator`;其 MIT 数据文件(`data/*.json`,11,134 张贴纸/1,400+ 皮肤/20 刀型/8 手套/63 探员)作为网页端目录数据源 |

> 整合方式:提炼各插件的核心机制重写成统一的 C# 工程(单一 DLL、单一配置、单一数据库),而非多个插件并装;
> 数据文件(皮肤/贴纸目录 JSON)沿用 AstraSkins(MIT License)以保证名称与 ID 权威准确。

## 二、功能总览

### 服务器插件(单 DLL)
- **🎮 游戏内整合菜单(v1.4 新增)**:`!menu` / `!cs2` 一键打开**单一菜单**,涵盖全部功能与外观调整。
  用 **W/S 移动高亮(面板光标走位)、E 确认、Shift 返回上级、Tab 关闭**(基于 CS2MenuManager 的 WASD 菜单)。
  - 菜单树:训练模式 / 死斗 / 满十竞技 / **我的外观** / 服务器设置(管理员)
  - **禁用规则:竞技或死斗进行中,除【我的外观】外全部灰显禁用;管理员不受限制**
- **玩法模式**:`/cs2mode practice|dm|competitive|none`(管理员),或配置默认模式。非玩法时段为"仅外观"模式。
- **训练模式**:`/practice`、`/god`、`/savepos <名字>`、`/tp <名字>`、`/positions`、`/delpos`、`/addbot ct|t`、`/clearweapons`
- **死斗**:`/dm`、`/guns`、`/gun ak47|awp|random|knifeonly`、`/dmsettings hud|sound`、`/dmtop`(排行榜)
- **满十竞技**:`/queue`、`/unqueue`、`/ready`、`/notready`、`/match`、`/choose ct|t`、`/elo`、`/matchcancel`
- **外观系统**:
  - `/cs2bind` —— **获取绑定码**(6 位,默认 15 分钟有效)
  - `/cs2whoami` —— 查看 SteamID 与绑定状态
  - `/cs2sk` —— 查看网页地址与帮助;`/cs2skrefresh` —— 改完外观立刻生效
  - `/cs2unbind` + `/cs2unbind_confirm` —— 解绑
- 未安装 CounterStrikeSharp?插件依赖 CSS(含 RUNTIME)与 MySQL 8 / MariaDB 10.6+。

### 网页端(Node.js)
- **注册/登录**(用户名+密码,bcrypt 哈希存储)
- **绑定码绑定 SteamID**:网页注册 → 进服 `/cs2bind` 拿码 → 网页输入码 → SteamID 与账号永久关联
- **外观自定义**:皮肤(中/英文名搜索、稀有度色条)、磨损滑条+六档预设、种子、改名标签、StatTrak、挂饰、**5 贴纸槽位**(偏移/旋转/缩放/独立磨损)、刀型、手套、探员(CT/T)、MVP 音乐包、徽章
- **我的战绩**:死斗 K/D/爆头/连杀、竞技 Elo 与近期比赛
- **服务器状态页**:插件心跳自动上报地图/模式/人数
- **管理后台**:封禁、设管理员(第一个账号注册后可在数据库把 role 改成 admin)
- 所有数据只读写同一个 MySQL 库,Web 与插件零 HTTP 通信,天然支持多服+多 Web 实例。

## 三、目录结构

```
cs2suite/
├── plugin/                  # C# CounterStrikeSharp 插件源码
│   ├── CS2Suite.csproj      # net10.0,引用 CounterStrikeSharp.API 1.0.376 + MySqlConnector
│   ├── src/
│   │   ├── Plugin.cs        # 入口:事件分发/模式切换/注册命令
│   │   ├── Core/            # 配置、MySQL、自动建表 DDL、本地化
│   │   ├── Game/            # PracticeModule / DeathmatchModule / CompetitiveModule
│   │   └── Skins/           # EconApplier(注入核心)/ BindModule(绑定码)/ SkinsModule(应用)
│   ├── gamedata/cs2suite.json   # 属性注入签名(Windows+Linux)
│   └── lang/zh.json en.json     # 游戏内双语
├── web/                     # 网页端(Node.js + 原生前端,无框架)
│   ├── server.js            # Express API + 会话 + 限流
│   ├── config.example.json  # ← 用户复制为 config.json 填 MySQL 参数
│   ├── public/              # 单文件前端(HTML/CSS/JS)
│   └── data/*.json          # 皮肤/贴纸目录(来自 AstraSkins,MIT)
├── db/schema.sql            # MySQL 全量表结构(插件也会自动幂等建表)
├── deploy/                  # 可直接上传到游戏服 game/csgo/ 的成品目录
└── README.md                # 本文件
```

## 四、快速部署(三步)

### 1) 数据库
```sql
CREATE DATABASE cs2suite DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE USER 'cs2suite'@'%' IDENTIFIED BY '你的强密码';
GRANT ALL ON cs2suite.* TO 'cs2suite'@'%';
```
(建表不必手工做:插件首启自动建;**或**导入 `db/schema.sql` 也行,二者等价。)
(建表不必手工做:插件首启自动建;**或**导入 `db/schema.sql` 也行,二者等价。)

### 2) 游戏服插件
前置:[安装 CounterStrikeSharp(带 RUNTIME)](https://docs.cssharp.dev/docs/guides/getting-started.html)。
1. 把 `deploy/game/csgo/addons/counterstrikesharp/` 合并覆盖到服务器 `game/csgo/addons/counterstrikesharp/`
   (含游戏内菜单所需:CS2MenuManager 伴随插件与 `shared/CS2MenuManager/config.toml`)
   (得到 `plugins/CS2Suite/`、`gamedata/cs2suite.json`、`configs/plugins/CS2Suite/CS2Suite.json`、`www? 无`)
2. 编辑 `configs/plugins/CS2Suite/CS2Suite.json` 的 `Database` 段(Host/Port/User/Password/Database)和 `PublicWebUrl`(你的网页地址)
3. **关键**:`configs/core.json` 里 `"FollowCS2ServerGuidelines": false`(换肤需要写物品属性,官方规则默认禁止——只在内战/娱乐服使用)
4. 重启服务器。日志出现 `[CS2Suite] MySQL ready` 即成功;`/cs2version`? → 用 `/cs2suite` 查看版本。

> 从源码编译:`cd plugin && dotnet build -c Release`(.NET 10 SDK);产物在 `bin/Release/net10.0/`。

### 3) 网页端
前置:Node.js 18+。
```
cd web
npm install                      # express / mysql2 / bcryptjs
copy config.example.json config.json   # 填入与插件相同的 MySQL 参数、改 sessionSecret、端口
npm start                        # 默认监听 0.0.0.0:8788
```
生产建议:用 `pm2 start server.js --name cs2suite-web` 常驻;Nginx 反代 + HTTPS。
第一个注册的用户获得账号后,管理员可在网页【管理】页把需要的账号设为 admin,或直接:
```sql
UPDATE cs2suite_accounts SET role='admin' WHERE username='你的名字';
```

## 五、玩家使用流程

1. 网页注册账号 → 登录
2. 进服聊天框输入 `/cs2bind` → 获得 6 位绑定码
3. 网页【账号与绑定】输入绑定码 → SteamID 绑定成功(可绑多个号)
4. 网页【外观自定义】:选武器 → 皮肤 → 磨损/种子/改名/StatTrak/挂饰 → 添加贴纸(可调位置角度)→ 【保存全部】
5. 回服 `/cs2skrefresh`(或下次重生)立即生效
6. 未绑定的玩家无法使用自定义外观(需求规定),插件会提示绑定。

## 六、玩法命令速查

| 命令 | 说明 |
|---|---|
| `/cs2mode practice|dm|competitive|none` | 切换玩法(管理员) |
| `/practice` `/dm` `/queue` | 玩家自助进入训练/死斗/竞技排队 |
| `/god` `/savepos 名` `/tp 名` `/positions` `/delpos 名` `/addbot ct` | 训练工具 |
| `/guns` `/gun awp` `/gun random` `/gun knifeonly` `/dmtop` | 死斗选枪/排行 |
| `/ready` `/match` `/choose ct` `/elo` `/matchcancel` | 竞技流程 |
| `/cs2bind` `/cs2whoami` `/cs2sk` `/cs2skrefresh` `/cs2unbind` | 外观与绑定 |

## 七、安全与合规提示

- 换肤依赖 `FollowCS2ServerGuidelines: false` —— **只用于社区自娱服**,官方/VAC 服勿开;外观为服务器侧下发,只影响本服玩家视觉,与其他服无关。
- 网页会话为 HMAC 签名 Cookie;`sessionSecret` 务必改成随机长字符串;绑定码有速率限制与 15 分钟 TTL。
- 数据库账号只需 `cs2suite` 库权限;建议 Web 与插件共用同一低权限账号。
- 探员/音乐包等若服务器未下载对应内容会自动回退默认(不报错)。

## 八、验证记录(本次构建)
- `dotnet build` .NET 10 SDK:**0 错误 0 警告**,产出 CS2Suite.dll + MySqlConnector.dll
- 内嵌 18 张表 DDL 在全新 MariaDB 11.4 库执行:**建表成功**,与 db/schema.sql 一致
- Web 端到端:注册 → 登录 → 绑定码消费 → 保存皮肤/贴纸/刀/手套/探员 → 回读校验:**全部通过**
- 前端 app.js / 后端 server.js 均通过 `node --check` 语法验证

## 八a-1、v1.4 更新(游戏内整合菜单)

按需求采用**方案 B**:引入 [CS2MenuManager](https://github.com/schwarper/CS2MenuManager)(★49)的 `WasdMenu`,
把全部功能与外观调整整合进**单一游戏内菜单**。

### 交互
- 打开:`!menu` / `!cs2` / `!cs2menu`(控制台或聊天框都可用)
- **W / S** 上下移动高亮(即面板上的光标走位)、**E** 确认、**Shift** 返回上级、**Tab** 关闭
- 打开时主动聊天提示,避免玩家误以为走位卡住(菜单会冻结移动以接管 W/S)

### 菜单树
```
CS2Suite 主菜单
├── 训练模式        进入模式 / 无敌开关 / 位点保存·传送·列表 / 加机器人 / 清场地
├── 死斗            进入模式 / 选主武器(18 种) / 随机枪 / 刀战开关 / 音效·HUD / 排行榜
├── 满十竞技        进入模式·排队 / 准备·取消 / Elo 与阶段显示 / 选边 / 比赛详情
├── 我的外观        ★ 比赛进行中仍可用:刷新外观 / 绑定码 / 网页面板入口
└── 服务器(管理员)   切换四种玩法模式 / 取消比赛
```

### 禁用规则(按你的要求实现)
- 竞技进行中(刀局 / 选边 / 正赛)或死斗进行中 → 除【我的外观】外所有项**灰显禁用**
- **管理员不受限制**(`@css/kick` / `@root` / `@custom/cs2suite_admin`);服务器设置项仅管理员可见
- 实现:每次打开菜单**按当前状态重建菜单树**(禁用态实时准确),并在每个回调内**二次校验**,
  防止「菜单已打开、此时比赛刚好开始」的漏判

### 依赖与部署(重要)
菜单框架是**独立伴随插件 + 共享 API**结构,`deploy/` 已含全部所需文件:
```
addons/counterstrikesharp/
├── plugins/CS2Suite/                     CS2Suite.dll + CS2MenuManager.dll + Dapper.dll + Tomlyn.dll + MySqlConnector.dll + lang/
├── plugins/CS2MenuManager_MenuManager/   菜单伴随插件(含中文语言包)
└── shared/CS2MenuManager/                config.toml(按键/配色,默认已是 W/S/E/Shift/Tab)+ 运行库
```
改按键或配色:编辑 `shared/CS2MenuManager/config.toml` 的 `[Buttons]` 与 `[WasdMenu]` 段。

### 工程上避掉的一个坑
引入菜单包后 dotnet 默认会把它**全部传递依赖(43 个 DLL)**复制进插件目录,其中 **36 个**
(`Microsoft.Extensions.*`、`Serilog.*`、`FastGenericNew`、`McMaster.*` 等)是 **CSS 宿主已自带**的,
重复分发会导致版本冲突(社区插件常见翻车点)。已在 `CS2Suite.csproj` 增加
`PruneHostProvidedAssemblies` 构建目标自动剔除 → **最终只分发 5 个 DLL**。

### 许可证变更
因 CS2MenuManager 为 **GPL-3.0-only**,本项目许可证**由 MIT 变更为 GNU GPL-3.0**(见 `LICENSE`),
并顺带更正第三方声明中 MatchZy 的错误标注(实为 MIT)。
## 八a、全仓库代码审计(2026-10 本轮)

本轮审计范围:web 前后端(Node/浏览器双端)、MySQL schema、插件增量改动、部署包一致性。
发现并修复 **9 处真实缺陷**:

| # | 位置 | 缺陷 | 影响 | 修复 |
|---|---|---|---|---|
| 1 | server.js | 会话 Cookie 内自带 role,且 auth 中间件不查账号实时状态 | **封禁/降权账号的旧 Cookie 最长 14 天仍具管理员权限** | auth 增加账号状态校验(15s 缓存),管理员操作后即时失效;实测封禁后旧会话 401 |
| 2 | server.js | 全部 async 路由裸写,express4 不捕获 Promise 异常 | 任意 SQL 错误 → **未处理 rejection → 进程崩溃** | 统一 ah() 包装转发到错误中间件 |
| 3 | server.js | `const ah` 声明在首次使用之后 | **TDZ:进程启动即崩**(node --check 查不出) | 定义提前;已实测启动成功 |
| 4 | server.js | 限流桶永不回收 | 长期运行内存持续增长 | 5 分钟定时清理 + 限流键改用路由模板(避免 /loadout/1、/loadout/2 各开一桶绕过) |
| 5 | server.js | /api/bind 返回 bound_to_account | 绑定码被枚举时可探测他人账号 id | 改为只返回错误码 |
| 6 | server.js | /api/loadout 未校验 defindex | 脏数据入库后插件按 def 匹配应用出怪外观 | 启动构建白名单(目录武器/刀/手套 + 42/-1..-4),非法 def 返回 400 bad_defindex |
| 7 | app.js | 401/403 静默 | **会话过期后所有操作无反馈**,用户误以为按钮失灵 | 401 清态 + 提示 + 跳登录;403 显示具体原因 |
| 8 | app.js | 切换 SteamID 直接重载 | 未保存的外观改动静默丢失 | 切换前确认;退出登录/关页面同样提示 |
| 9 | app.js | stickerAdjust 无空值守卫;服务器离线时长按 UTC 解析 | 极端路径 TypeError;在线状态显示时区偏差 8 小时 | 加守卫;age_sec 由数据库 TIMESTAMPDIFF 计算 |

顺带清理:插件事件/绑定码表按小时清理(原来 24h 才清一次)、DM 名字快照随断线回收、
`_pollBusy` 置位提到 Task 之前(消除 2s tick 重入窗口)、删除模板残留 `Class1.cs`、deploy 包重新同步。

验证:插件 Release 0 错误 0 警告;两份前端/后端脚本通过 node --check;
jsdom 双套回归全绿(登录、33 槽位、非法 def 拒绝、空槽守卫、403 拦截、零 JS 错误);
HTTP 直测:伪造 Cookie/无 Cookie → 401,封禁后旧会话 → 401;数据库孤儿数据检查 0 条。

## 八b、游戏内逻辑安全审计(v1.3)

对插件 C# 全部模块做了逐文件逻辑审计,修复 11 处真实缺陷:

| # | 缺陷 | 影响 | 修复 |
|---|---|---|---|
| 1 | LAST_INSERT_ID() 在连接池下跨连接读取 | 死斗场次/竞技比赛结算行永远拿不到 id,成绩无法关联 | 新增 Db.InsertReturnIdAsync 单连接 INSERT+回读,DM/Comp 改用 |
| 2 | 异步 Task 里调用 CSS 游戏 API(遍历玩家/读实体名) | 非游戏线程触碰原生句柄,偶发崩溃/未定义行为 | 全部改为游戏线程先快照(名字/阵营/在线表),Task 只碰字符串与 DB |
| 3 | 未注册 player_disconnect 事件 | 断线玩家的外观缓存、死斗/竞技队列残留泄漏 | 注册并派发 Forget / OnPlayerLeft |
| 4 | 捡枪(item_pickup)不补皮肤 | 捡起别人掉落的枪外观丢失直到下次重生 | 注册 EventItemPickup 重新应用 |
| 5 | bind 事件重载后仍被旧绑定缓存门控挡住 | Web 刚绑定的玩家进服最长 5 分钟看不到皮肤 | 事件消费时同步更新 BindCache |
| 6 | 竞技比分阵营映射靠手动 halftime 取反 | 加时换边后比分归错队;平局引擎可能死锁 | 改为每回合 round_prestart 实测蓝队阵营(多数决);禁用引擎加时,自计数判终点,满轮平局按 draws 结算 |
| 7 | 刀局未剥枪 | 所谓刀局实为拼枪 | 刀局阶段 spawn 后强制清除非刀武器 |
| 8 | 模式切换后 sv_cheats 1 残留 | 从训练切到竞技/死斗后玩家可用作弊指令 | practice 之外所有模式显式设 0 |
| 9 | 解绑剥离路径不清改名标签 | 解绑后名字牌残留 | entry=null 分支显式清 CustomName;手套 lastinv 刷新 |
| 10 | 排队者断线/换图后队列句柄失效 | Ready 计数虚高,开赛凑不齐人 | Tick 周期清理失效句柄;落选者收到 queue_not_picked 提示 |
| 11 | MySQL 启动时不可达则功能永久禁用 | 数据库瞬断后插件不自愈 | 每 5 分钟自动重试建表 |

其余审计确认项(无问题):SQL 全参数化;绑定码 32^6 空间+限流防枚举;输出走 lang 字典,玩家文本不进 SQL;
连接串加 AllowPublicKeyRetrieval 兼容 MySQL8 caching_sha2_password;StatTrak 刀品质 9 修正;
Release 重新编译 0 错误 0 警告,deploy 包已同步新 DLL。

## 八c、v1.2 功能更新

1. **皮肤/贴纸/挂饰/探员 全量预览图**:新增 `web/data/icon-map.json`(约 1MB,离线可用,
   由 `tools/build-iconmap.cjs` + `tools/enrich2.cjs` 整合三方数据源生成:LielXD 清单 + WeaponPaints/Nereziel 官方图包 CDN + 逐 URL 探测补全)。
   覆盖率:枪皮 2063 张、手套 94/94、贴纸 11145、挂饰 78/78、探员 64。
   前端皮肤格子/大图预览/贴纸格子/挂饰格子全部显示缩略图;`<img onerror>` 自动降级为占位块;新增 `GET /api/iconmap`。
2. **改名标签 / StatTrak / 挂饰 / 贴纸 不再依赖皮肤**:未选皮肤(游戏默认外观)时这些配置照常显示、保存并在游戏内生效
   (对齐 AstraSkins 行为;插件 `EconApplier.ApplyExtras` 无条件应用,修复原"没皮肤就整段跳过")。
3. **挂件(挂饰)选择器重做**:由隐藏的下拉框改为常驻的横向图块选择条(`kcell`,含"无"),
   任何武器/刀都能直接看到并点选 —— 解决"找不到挂件更换入口"。
4. **武器皮肤入口明确化**:左侧槽位列表在"仅外观"模式下也完整可点(之前若玩法模块异常会被跳过);
   每把枪格子里第一款就是"默认外观",选中即清除皮肤保留贴纸,文案已写明。
5. **登录/注册按钮"无效"根因修复**:HTML5 表单原生校验(`required`/`minlength`)在部分浏览器下,
   字段不合规会**静默拦截 submit**(无 toast 无跳转,看起来像按钮失灵)。已移除原生 `required/minlength`,
   改由 JS 判空 + 服务端校验统一弹 toast;fetch 失败/网络异常新增"无法连接服务器"提示(之前无任何反馈)。
   同时:静态资源改 `Cache-Control: no-store` 并加 `?v=` 版本号 —— 彻底排除改版后浏览器仍跑旧缓存页的"点了没反应";
   监听改为双栈(不固定 0.0.0.0),`localhost` 的 IPv6 解析也能访问。
6. 回归(jsdom 无头):登录 OK → 33 槽位 → AK 61 款皮肤全部带图 → 大图预览 → 挂饰 78 图块 →
   未选皮肤的 AWP 仍可贴贴纸/挂挂饰(存库验证 def=9 paint=0 kc=11 + 1 贴纸)→ 保存事件入队,零 JS 错误。

## 八d、v1.1 功能更新

1. **Web 改外观 → 游戏内即时生效**:新增 `cs2suite_events` 事件队列表。Web 保存/绑定/解绑时写入事件,
   插件每 `Skins.SyncSeconds`(默认 2 秒)按自增游标消费,对**在线玩家当场重载并应用外观**(无需重生、无需手动 `/cs2skrefresh`,
   后者仍保留作手动兜底)。解绑事件会把场上武器/手套当场恢复默认。多服务器各自维护游标互不影响;事件 24 小时自动清理。
2. **修复网页登录/注册按钮无反应**:前端 hash 路由未去掉 `#` 前缀导致所有页面回退首页(`#/auth` 匹配不到 `/auth`)。
   已修复并用无头浏览器全流程回归:登录 → 武器槽 33 件 → AK 61 款皮肤 → 贴纸选择器(200 款)→ 保存 → 事件入库,零 JS 错误。
3. **修复武器目录缺 defindex**:AstraSkins 的 weapons.json 不带 defindex,现按 WeaponPaints 权威映射表补齐(34 把枪全部命中)。
4. **放宽字段**:改名标签列扩到 VARCHAR(128)(探员模型名较长);皮肤 paintkit 上限扩到 10 万(手套 10000+ 系列)。

## 九、CS2 出新皮肤后,数据如何进入系统?(重要)

系统分三层,**新皮肤只影响第 3 层**,插件与 gamedata 都不用动:

### 第 1 层:游戏内应用 —— 插件不需要任何皮肤目录
插件从不内置皮肤名单。它把 Web 传来的 **paintkit 数字**(以及 seed / 磨损 / 贴纸 id / 挂饰 id)直接写进物品属性
(`set item texture prefab` / `sticker slot N id` 等,见 `EconApplier`)。
因此:CS2 新增皮肤后,只要社区数据里出现该 paintkit,**旧插件直接就能用,无需升级**。

### 第 2 层:gamedata 签名 —— 只在引擎大版本变动时才需要更新
`plugin/gamedata/cs2suite.json` 里是 `CAttributeList::SetOrAddAttributeValueByName` 的字节签名,**与"新皮肤"无关**,
只在 Valve 改动引擎内部结构(通常是大版本)导致签名失配时才需要替换。
失效症状:控制台出现该签名找不到、皮肤不生效。修复方式:用同期社区插件(WeaponPaints / AstraSkins)的最新 gamedata 覆盖同名条目。

### 第 3 层:Web 目录 —— 用一条命令同步
Web 端的皮肤/贴纸下拉来自 `web/data/*.json`(目录快照)+ `web/data/icon-map.json`(预览图映射)。
CS2 出新皮肤后,在上游社区数据更新后执行:

```bash
cd cs2suite
node tools/update-catalog.cjs              # 全量(推荐):拉目录 + 重建预览图映射 + CDN 探测新图 + 覆盖率报告
node tools/update-catalog.cjs --no-probe   # 快速:只拉目录,保留已有预览图(新皮肤图暂缺,显示占位块)
```

> 两种模式都会**保留**已探测到的预览图;快速模式只是跳过新图探测,适合"只想尽快看到新皮肤名字"的场景。
> 想补齐新皮肤预览图时再跑一次全量模式即可(约 3-6 分钟,取决于新增条目数)。

脚本做的事:
1. 从 `Ayrton09/AstraSkins` 拉取最新 `data/*.json`(名称 + paintkit/贴纸/挂饰 ID 目录)
2. 从 `LielXD/CS2-WeaponPaints-Website` 拉取图标清单
3. 重建 `icon-map.json`(weapon-paint 命名规则)
4. 对清单里没有的**新条目**按 `weapon-<paint>.png` / `sticker-<id>.png` / `keychain-<id>.png` 规则,
   直接向 WeaponPaints 官方图包 CDN 定点探测(HEAD 请求),命中即收录 — 这就是新皮肤预览图的来源
5. 写出 `web/data/meta.json`(快照时间/条目数/覆盖率,首页会展示)

完成后重启 Web(`node server.js`)即可。**插件与数据库无需任何改动**。

> 说明:预览图来自社区镜像(raw.githubusercontent 上的 WeaponPaints 官方图包)。
> 上游尚未补图的新皮肤会显示占位块,等上游更新后再跑一次脚本即可自动补上。
>
> **上游目录滞后时的兜底**(社区目录通常比 Valve 晚几天,但插件本身已支持):
> ```sql
> -- 让某玩家立刻用上新皮肤:直接插入外观行(Web 界面看不到,但游戏内已生效)
> INSERT INTO cs2suite_loadouts (steamid64) VALUES ('76561198xxxxxxxxx')
>   ON DUPLICATE KEY UPDATE updated_at=NOW();
> INSERT INTO cs2suite_loadout_items (steamid64, weapon_defindex, paintkit, paint_seed, paint_wear)
>   VALUES ('76561198xxxxxxxxx', 7, <新皮肤paintkit>, 0, 0.05)
>   ON DUPLICATE KEY UPDATE paintkit=VALUES(paintkit);
> -- 触发即时同步(插件每 SyncSeconds 秒消费)
> INSERT INTO cs2suite_events (steamid64, type) VALUES ('76561198xxxxxxxxx', 'loadout');
> ```
> 注意:服务端只校验"武器 defindex 是否在目录内",**不校验 paintkit**,所以任意新 paintkit 都能直接写入生效;
> 只有**全新武器**(新 defindex)才必须等目录更新。

## 十、开源许可

**本项目以 GNU GPL-3.0 分发。** 原因是游戏内菜单链接了
[schwarper/CS2MenuManager](https://github.com/schwarper/CS2MenuManager)(**GPL-3.0-only**),
GPL 的传染性要求衍生作品同样以 GPL-3.0 发布。完整条款见 `LICENSE`(文末附第三方声明)。

第三方组件与来源:

| 项目 | 许可证 | 使用方式 |
|---|---|---|
| schwarper/CS2MenuManager | **GPL-3.0** | 链接为菜单框架(提供 WASD 菜单),随 `deploy/` 分发 |
| Ayrton09/AstraSkins | MIT | `web/data/*.json` 目录数据(武器/刀/手套/探员/贴纸/挂饰/音乐包) |
| Nereziel/cs2-WeaponPaints | GPL-3.0 | 预览图**仅按 URL 引用**其图包 CDN(仓库内不分发图片);注入机制按同款公开 gamedata 签名重实现 |
| LielXD/CS2-WeaponPaints-Website | 见上游 | 图标清单,由 `tools/update-catalog.cjs` 拉取 |
| shobhit-pathak/MatchZy | **MIT**(此前误标为 GPL-3.0,已更正) | 竞技流程思路重写,未复制代码 |
| NockyCZ/CS2-Deathmatch | 见上游 | 死斗玩法思路重写,未复制代码 |

Counter-Strike 2 与 Steam 为 Valve 财产;本项目与 Valve 无关。
