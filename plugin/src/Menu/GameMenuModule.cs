using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CS2MenuManager.API.Class;
using CS2MenuManager.API.Enum;
using CS2MenuManager.API.Interface;
using CS2MenuManager.API.Menu;
using CS2Suite.Core;

namespace CS2Suite.Menu;

/// <summary>
/// 游戏内单一整合菜单(WASD 导航)。
///
/// 交互:W/S 上下移动(面板高亮即"光标走位")、E 确认、Shift 返回上级、Tab 关闭。
/// 渲染:CS2MenuManager 的 WasdMenu(屏幕中央 HTML 高亮列表)。
///
/// 禁用规则(按需求):
///   * 竞技或死斗**进行中**时,除【我的外观】外所有项禁用;
///   * 管理员不受限制。
/// 做法:每次打开按当前状态重建菜单树(禁用态实时准确)+ 回调内二次校验(防菜单开启期间状态变化)。
/// </summary>
public sealed class GameMenuModule : ModuleBase
{
    public GameMenuModule(CS2SuitePlugin p) : base(p) { }

    public override void Initialize()
    {
        Plugin.AddCommand("menu", "CS2Suite: 打开整合菜单", (pl, info) => Open(pl, info));
        Plugin.AddCommand("cs2", "CS2Suite: 打开整合菜单", (pl, info) => Open(pl, info));
        Plugin.AddCommand("cs2menu", "CS2Suite: 打开整合菜单", (pl, info) => Open(pl, info));
    }

    // ================= 状态与门控 =================

    internal bool IsMatchRunning()
        => Plugin.Competitive.IsMatchInProgress || Plugin.Dm.IsMatchInProgress;

    internal bool CanUseNonAppearance(CCSPlayerController player)
        => !IsMatchRunning() || CS2SuitePlugin.IsPlayerAdmin(player);

    /// <summary>外观分支永远可用;其余在比赛中仅管理员可用。</summary>
    private DisableOption Gate(CCSPlayerController player, bool appearanceItem)
        => appearanceItem || CanUseNonAppearance(player) ? DisableOption.None : DisableOption.DisableShowNumber;

    /// <summary>回调内二次校验。</summary>
    private bool Blocked(CCSPlayerController player, bool appearanceItem)
    {
        if (appearanceItem || CanUseNonAppearance(player)) return false;
        Tell(player, "menu_blocked_in_match");
        return true;
    }

    private void Open(CCSPlayerController? player, CounterStrikeSharp.API.Modules.Commands.CommandInfo info)
    {
        if (player is not { IsValid: true } || player.IsBot) { info.ReplyToCommand(Lang.T("cmd_console_only")); return; }

        // WASD 菜单会冻结走位以接管 W/S;这里给出提示(避免玩家以为卡住)
        Tell(player, "menu_hint");
        ShowRoot(player);
    }

    internal void ShowRoot(CCSPlayerController player)
    {
        var m = new WasdMenu("CS2Suite 主菜单", Plugin) { ExitButton = true, MenuTime = 0 };
        var running = IsMatchRunning();
        var admin = CS2SuitePlugin.IsPlayerAdmin(player);

        m.AddItem(BuildTitle(player, running, admin), DisableOption.DisableHideNumber);

        m.AddItem("训练模式", (p, _) => { if (!Blocked(p, false)) ShowPractice(p, m); }, Gate(player, false));
        m.AddItem("死斗", (p, _) => { if (!Blocked(p, false)) ShowDeathmatch(p, m); }, Gate(player, false));
        m.AddItem("满十竞技", (p, _) => { if (!Blocked(p, false)) ShowCompetitive(p, m); }, Gate(player, false));
        // 外观:比赛中唯一允许的分支
        m.AddItem("我的外观(皮肤/贴纸/挂饰)", (p, _) => ShowAppearance(p, m), DisableOption.None);

        if (admin)
            m.AddItem("服务器设置(管理员)", (p, _) => ShowServer(p, m), DisableOption.None);

        m.Display(player, 0);
    }

    private string BuildTitle(CCSPlayerController player, bool running, bool admin)
    {
        var mode = Plugin.ActiveGameMode switch
        {
            "practice" => "训练",
            "deathmatch" => "死斗",
            "competitive" => "满十竞技",
            _ => "仅外观",
        };
        var state = running ? "比赛进行中" : "空闲";
        return $"当前模式 {mode} · {state}" + (admin ? " · 管理员" : "");
    }

    // ================= 训练 =================

    private void ShowPractice(CCSPlayerController player, IMenu parent)
    {
        var m = new WasdMenu("训练模式", Plugin) { PrevMenu = parent, ExitButton = true, MenuTime = 0 };
        var active = Plugin.Practice.IsActive;
        var god = Plugin.Practice.GodOn(player);

        m.AddItem($"当前:{(active ? "已在训练模式" : "未在训练模式")}", DisableOption.DisableHideNumber);
        m.AddItem(active ? "(已在该模式)" : "进入训练模式", (p, _) =>
        {
            if (Blocked(p, false)) return;
            if (!Plugin.Practice.IsActive) Plugin.SwitchGameMode("practice");
        }, active ? DisableOption.DisableShowNumber : DisableOption.None);

        m.AddItem($"无敌:{(god ? "开" : "关")}(切换)", (p, _) => { if (!Blocked(p, false)) Plugin.Practice.MenuToggleGod(p); });
        m.AddItem("保存当前位点", (p, _) => { if (!Blocked(p, false)) Tell(p, "menu_pos_hint_save"); });
        m.AddItem("传送到位点(用命令 !tp <名>)", DisableOption.DisableShowNumber);
        m.AddItem("列出我的位点", (p, _) => { if (!Blocked(p, false)) Tell(p, "menu_pos_hint_list"); });
        m.AddItem("添加机器人(T)", (p, _) => { if (!Blocked(p, false)) Plugin.Practice.MenuAddBot(p, "t"); });
        m.AddItem("添加机器人(CT)", (p, _) => { if (!Blocked(p, false)) Plugin.Practice.MenuAddBot(p, "ct"); });
        m.AddItem("清除地面掉落武器", (p, _) => { if (!Blocked(p, false)) Plugin.Practice.MenuClearGround(p); });

        m.Display(player, 0);
    }

    // ================= 死斗 =================

    private static readonly (string Key, string Label)[] DmPrimaries =
    [
        ("ak47", "AK-47"), ("m4a4", "M4A4"), ("m4a1", "M4A1-S"), ("awp", "AWP"),
        ("galilar", "Galil AR"), ("famas", "FAMAS"), ("aug", "AUG"), ("sg553", "SG 553"),
        ("ssg08", "SSG 08"), ("mac10", "MAC-10"), ("mp9", "MP9"), ("mp5sd", "MP5-SD"),
        ("ump45", "UMP-45"), ("p90", "P90"), ("nova", "Nova"), ("xm1014", "XM1014"),
        ("negev", "Negev"), ("m249", "M249"),
    ];

    private void ShowDeathmatch(CCSPlayerController player, IMenu parent)
    {
        var m = new WasdMenu("死斗", Plugin) { PrevMenu = parent, ExitButton = true, MenuTime = 0 };
        var active = Plugin.Dm.IsActive;
        var steam = SteamId64(player);

        m.AddItem($"当前:{(active ? "死斗进行中" : "未在死斗模式")} · 主武器 {Plugin.Dm.CurrentPrimary(steam)}", DisableOption.DisableHideNumber);
        m.AddItem(active ? "(已在该模式)" : "进入死斗模式", (p, _) =>
        {
            if (Blocked(p, false)) return;
            if (!Plugin.Dm.IsActive) Plugin.SwitchGameMode("deathmatch");
        }, active ? DisableOption.DisableShowNumber : DisableOption.None);

        m.AddItem("选择主武器…", (p, _) => { if (!Blocked(p, false)) ShowDmWeapons(p, m); });
        m.AddItem("随机武器", (p, _) => { if (!Blocked(p, false)) Plugin.Dm.MenuSetPrimary(p, "random"); });
        m.AddItem($"刀战模式:{(Plugin.Dm.KnifeOnly ? "开" : "关")}(切换)", (p, _) => { if (!Blocked(p, false)) Plugin.Dm.MenuToggleKnifeOnly(p); });
        m.AddItem("击杀音效(切换)", (p, _) => { if (!Blocked(p, false)) Plugin.Dm.MenuTogglePref(p, "dm_killsound"); });
        m.AddItem("击杀提示 HUD(切换)", (p, _) => { if (!Blocked(p, false)) Plugin.Dm.MenuTogglePref(p, "dm_hud"); });
        m.AddItem("查看排行榜", (p, _) => { if (!Blocked(p, false)) Tell(p, "menu_top_hint"); });

        m.Display(player, 0);
    }

    private void ShowDmWeapons(CCSPlayerController player, IMenu parent)
    {
        var m = new WasdMenu("选择主武器", Plugin) { PrevMenu = parent, ExitButton = true, MenuTime = 0 };
        foreach (var (key, label) in DmPrimaries)
        {
            var k = key;
            m.AddItem(label, (p, _) => { if (!Blocked(p, false)) Plugin.Dm.MenuSetPrimary(p, k); });
        }
        m.Display(player, 0);
    }

    // ================= 满十竞技 =================

    private void ShowCompetitive(CCSPlayerController player, IMenu parent)
    {
        var m = new WasdMenu("满十竞技", Plugin) { PrevMenu = parent, ExitButton = true, MenuTime = 0 };
        var inQueue = Plugin.Competitive.InQueue(player);
        var active = Plugin.Competitive.IsActive;

        m.AddItem($"状态:{Plugin.Competitive.PhaseText} · 队列 {Plugin.Competitive.QueueCount}/{Cfg.Competitive.PlayersRequired} · 已准备 {Plugin.Competitive.ReadyCount}",
            DisableOption.DisableHideNumber);
        m.AddItem($"我的 Elo:{Plugin.Competitive.MyElo(player)}", DisableOption.DisableHideNumber);

        m.AddItem(active ? (inQueue ? "退出队列" : "加入队列") : "进入竞技模式",
            (p, _) =>
            {
                if (Blocked(p, false)) return;
                if (!Plugin.Competitive.IsActive) Plugin.SwitchGameMode("competitive");
                else Plugin.Competitive.MenuToggleQueue(p);
            });

        m.AddItem("我已准备就绪", (p, _) => { if (!Blocked(p, false)) Plugin.Competitive.MenuSetReady(p, true); });
        m.AddItem("取消准备", (p, _) => { if (!Blocked(p, false)) Plugin.Competitive.MenuSetReady(p, false); });
        m.AddItem("刀局选边 CT(仅胜方)", (p, _) => { if (!Blocked(p, false)) Tell(p, "menu_choose_hint"); });
        m.AddItem("查看比赛详情", (p, _) => { if (!Blocked(p, false)) Tell(p, "menu_match_hint"); });

        m.Display(player, 0);
    }

    // ================= 我的外观(比赛中仍可用) =================

    private void ShowAppearance(CCSPlayerController player, IMenu parent)
    {
        var m = new WasdMenu("我的外观", Plugin) { PrevMenu = parent, ExitButton = true, MenuTime = 0 };
        var url = Cfg.PublicWebUrl;

        m.AddItem(Plugin.Skins.SkinSummary(player), DisableOption.DisableHideNumber);
        m.AddItem("立即刷新外观(从数据库)", (p, _) => Plugin.Skins.MenuRefresh(p));
        m.AddItem("浏览器换肤面板…", (p, _) =>
        {
            Tell(p, "menu_web_hint", ("url", url));
        });
        m.AddItem("获取绑定码(/cs2bind)", (p, _) => Tell(p, "menu_bind_hint"));

        m.AddItem("说明:网页端可精细调皮肤/贴纸参数", DisableOption.DisableHideNumber);
        m.Display(player, 0);
    }

    // ================= 服务器(管理员) =================

    private void ShowServer(CCSPlayerController player, IMenu parent)
    {
        var m = new WasdMenu("服务器设置(管理员)", Plugin) { PrevMenu = parent, ExitButton = true, MenuTime = 0 };
        foreach (var (mode, label) in new[] { ("none", "仅外观"), ("practice", "训练模式"), ("deathmatch", "死斗"), ("competitive", "满十竞技") })
        {
            var mm = mode;
            var on = Plugin.ActiveGameMode == mm;
            m.AddItem(on ? $"当前:{label}" : $"切换到 {label}", (p, _) =>
            {
                if (!CS2SuitePlugin.IsPlayerAdmin(p)) { Tell(p, "need_admin"); return; }
                Plugin.SwitchGameMode(mm);
            }, on ? DisableOption.DisableShowNumber : DisableOption.None);
        }
        m.AddItem("取消当前比赛", (p, _) =>
        {
            if (!CS2SuitePlugin.IsPlayerAdmin(p)) { Tell(p, "need_admin"); return; }
            Tell(p, "menu_cancel_hint");
        });
        m.Display(player, 0);
    }
}
