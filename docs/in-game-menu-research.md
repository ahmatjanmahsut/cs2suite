# 游戏内菜单方案调研(GitHub)

> 调研时间:2026-10-08。目的:把插件所有功能与皮肤调整整合为**单一游戏内菜单**,以 WASD 导航。

## 结论先行:CS2 服务端菜单有三个技术档位

| 档位 | 形态 | WASD 导航 | 真鼠标光标 | 代表实现 |
|---|---|---|---|---|
| A 聊天菜单 | 聊天框内编号列表,点数字选择 | 否 | 否 | 我们当前实现 |
| **B WASD 菜单** | **屏幕中央 HTML 面板 + 按键高亮移动 + 冻结走位** | 是 | 否(键控) | CS2MenuManager 的 WasdMenu、AstraSkins 内置菜单 |
| C Panorama UI | 服务端驱动的真图形面板(XML/CSS),可真光标点击 | 是 | 是 | PanoramaManager(基于 CCSCustomHudLayout) |

## 方案 B:CS2MenuManager(星标 49,NuGet 包 CS2MenuManager 1.0.43)

- 仓库:https://github.com/schwarper/CS2MenuManager
- 支持 6 种菜单:ChatMenu / ConsoleMenu / CenterHtmlMenu / **WasdMenu** / PanoramaVote / PlayerMenu
- WasdMenu 实现要点(已核源码 CS2MenuManager/API/Menu/WasdMenu.cs):
  - 每页 5 项(NumPerPage => 5)
  - 用 RegisterListener<OnTick> 逐帧读取 PlayerButtons 按钮状态
  - 按键可配:ScrollUpKey / ScrollDownKey / SelectKey / PrevKey / ExitKey
  - 打开时 SaveSpeed + FreezePlayer(冻结走位,避免 W/S 与移动冲突)
  - 渲染走 DisplayString(屏幕中央 HTML),**没有鼠标光标**
- 优势:一个 NuGet 包、零客户端文件、立即可用;与我们使用的 CSS 1.0.376 兼容。

## 方案 C:PanoramaManager(星标 5,NuGet 包 PanoramaManager 0.4.4)—— 真图形面板

- 仓库:https://github.com/Next-il/PanoramaManager
- 基于 CounterStrikeSharp 1.0.374+ 的 CCSCustomHudLayout API(我们已是 1.0.376,满足)
- 能力:服务端写布局变量、切 class、**接收点击事件**;CaptureInput 打开时会**拉起真鼠标光标**
  (README 原话:CaptureInput = false 用于纯阅读型通知,"so a notification cannot pull up a cursor and stop them aiming")
- **无需 gamedata**:引擎侧由 CSS 提供,CS2 更新只需更新 CSS
- **代价**:需把 Panorama 布局(XML/CSS)打进 **workshop addon**,路径必须是 panorama/layout/custom_game 与 panorama/styles/custom_game,再 mount 到服务器;玩家经 workshop 自动订阅
- 限制:Panorama 布局**无法接收键盘输入**(README 明说 a Panorama layout cannot take a keystroke),文字输入需用它提供的 TextPrompt 借聊天框

## 其他相关项目

| 项目 | 星 | 用途 |
|---|---|---|
| T3Marius/T3Menu-API | 15 | 玩家按键驱动的菜单 API(与 WasdMenu 同类,按键可配) |
| exkludera-cssharp/custom-menu | 14 | 现成菜单插件 |
| nvmxre/cs2-ui-kit | 4 | CS2 风格 UI 套件(疑似同类 Panorama 方案) |
| M-archand/ScreenMenuPrimer | 4 | CS2MenuManager 屏幕菜单示例 |
| karola3vax/MenuManagerCS2 | 2 | 共享菜单 API |

## 建议

分两步走,风险与工作量递增:

1. **先做方案 B(WasdMenu)**:满足"用 WASD 在面板上走位选择"的核心诉求,零客户端依赖,改动最小。
   根菜单结构建议:训练 / 死斗 / 满十竞技 / 我的外观 / 服务器设置 / 管理员工具。
2. **若必须"真图形化 + 鼠标点击"**,再上方案 C:需要额外准备 workshop addon 与 Panorama 布局,
   并评估社区服玩家订阅体验。

## 重要注意事项

- 方案 B 的 WasdMenu 会**冻结玩家走位**(它靠接管 W/S 作上下键)。
  因此只应在玩家主动打开菜单时启用,关闭后立即恢复;竞技比赛进行中应默认禁止(防误开影响比赛)。
- 方案 C 的 UI 资源需随 workshop addon 下发,首次进服有下载等待。