using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using CS2Suite.Core;
using CS2Suite.Game;
using CS2Suite.Menu;
using CS2Suite.Skins;

namespace CS2Suite;

[MinimumApiVersion(376)]
public sealed class CS2SuitePlugin : BasePlugin, IPluginConfig<CS2SuiteConfig>
{
    public override string ModuleName => "CS2Suite";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "CS2Suite";
    public override string ModuleDescription =>
        "Practice / 10-man Competitive / Deathmatch + web-managed weapon skins & stickers (SteamID bind-code gate).";

    public static CS2SuitePlugin? Instance { get; private set; }

    public CS2SuiteConfig Config { get; set; } = new();
    internal Db Db = null!;
    internal Lang L = null!;
    internal EconApplier Econ = null!;
    internal PracticeModule Practice = null!;
    internal DeathmatchModule Dm = null!;
    internal CompetitiveModule Competitive = null!;
    internal SkinsModule Skins = null!;
    internal BindModule Bind = null!;
    internal GameMenuModule GameMenu = null!;

    internal string ActiveGameMode = "none";

    public override void Load(bool hotReload)
    {
        Instance = this;
        L = new Lang(ModuleDirectory, Config.Language).Fallback(ModuleDirectory);
        Db = new Db(Config.Database, Logger);
        Db.ServerIdRef = Config.ServerId;
        Econ = new EconApplier(Logger);

        Practice = new PracticeModule(this);
        Dm = new DeathmatchModule(this);
        Competitive = new CompetitiveModule(this);
        Bind = new BindModule(this);
        Skins = new SkinsModule(this);
        GameMenu = new GameMenuModule(this);

        foreach (var m in new ModuleBase[] { Practice, Dm, Competitive, Bind, Skins, GameMenu })
            m.Initialize();

        AddCommand("cs2mode", "CS2Suite: switch game mode /cs2mode practice|dm|competitive|none", OnModeCommand);
        AddCommand("cs2suite", "CS2Suite: version & help", OnVersionCommand);

        RegisterEventHandler<EventPlayerConnectFull>(OnClientConnectedFull, HookMode.Post);
        RegisterEventHandler<EventPlayerDisconnect>(OnClientDisconnect, HookMode.Post);
        RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn, HookMode.Post);
        RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath, HookMode.Post);
        RegisterEventHandler<EventRoundPrestart>(OnRoundPrestart, HookMode.Post);
        RegisterEventHandler<EventRoundEnd>(OnRoundEnd, HookMode.Post);
        RegisterEventHandler<EventItemPurchase>(OnItemPurchaseOld, HookMode.Post);
        RegisterEventHandler<EventItemPickup>(OnItemPickup, HookMode.Post);

        _ = Task.Run(async () =>
        {
            var ok = await Db.TryEnsureSchemaAsync();
            if (!ok)
                Logger.LogError("[CS2Suite] MySQL init failed; skins/binding/stats disabled. Check config Database section.");
            else
                Logger.LogInformation("[CS2Suite] MySQL ready. Web: " + Config.PublicWebUrl);
        });

        // DB 启动时不可用 → 每 5 分钟自动重试建表(网络数据库常见瞬断场景)
        AddTimer(300f, async () =>
        {
            if (Db.Ready) return;
            Logger.LogInformation("[CS2Suite] MySQL 重试中…");
            await Db.TryEnsureSchemaAsync();
        }, TimerFlags.REPEAT);

        AddTimer(60f, async () =>
        {
            try { await Db.HeartbeatAsync(ActiveGameMode); } catch { }
        }, TimerFlags.REPEAT);

        Server.NextFrame(() =>
        {
            ActiveGameMode = NormalizeMode(Config.Modes.DefaultMode);
            SwitchGameMode(ActiveGameMode, announce: false);
            Logger.LogInformation("[CS2Suite] loaded, mode: " + ActiveGameMode);
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var m in new ModuleBase[] { Practice, Dm, Competitive, Bind, Skins, GameMenu })
            {
                try { m.Unload(); } catch { }
            }
            try { Db.Dispose(); } catch { }
            Instance = null;
        }
        base.Dispose(disposing);
    }

    public void OnConfigParsed(CS2SuiteConfig config)
    {
        config.Database.Port = Math.Clamp(config.Database.Port, 1, 65535);
        config.Modes.DefaultMode = NormalizeMode(config.Modes.DefaultMode);
        config.Competitive.PlayersRequired = Math.Max(2, config.Competitive.PlayersRequired);
        config.Skins.MaxStickersPerWeapon = Math.Clamp(config.Skins.MaxStickersPerWeapon, 0, 5);
        if (string.IsNullOrWhiteSpace(config.Language)) config.Language = "zh";
    }

    // ================= 玩法切换 =================
    internal static string NormalizeMode(string? m) => m?.ToLowerInvariant() switch
    {
        "practice" => "practice",
        "dm" or "deathmatch" => "deathmatch",
        "competitive" or "comp" or "premier" => "competitive",
        _ => "none",
    };

    internal void SwitchGameMode(string mode, bool announce = true)
    {
        mode = NormalizeMode(mode);
        Practice.SetActive(mode == "practice");
        Dm.SetActive(mode == "deathmatch");
        Competitive.SetActive(mode == "competitive");
        ActiveGameMode = mode;
        Server.NextFrame(() =>
        {
            Db.HeartbeatSafe(mode);
            if (announce)
                Server.PrintToChatAll(" " + ChatColors.Green + L.T("mode_switched", ("mode", L.T("mode_" + mode))));
        });
    }

    private void OnModeCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (player is not { IsValid: true }) { info.ReplyToCommand(L.T("cmd_console_only")); return; }
        if (!Config.Modes.AllowAdminModeSwitch || !IsPlayerAdmin(player)) { info.ReplyToCommand(L.T("need_admin")); return; }
        var mode = info.ArgCount > 1 ? info.ArgByIndex(1) : "";
        var norm = NormalizeMode(mode);
        if (norm == "none" && mode.Length > 0) { info.ReplyToCommand(L.T("mode_usage")); return; }
        SwitchGameMode(norm);
    }

    private void OnVersionCommand(CCSPlayerController? player, CommandInfo info)
    {
        info.ReplyToCommand($"CS2Suite v{ModuleVersion} | mode: {ActiveGameMode} | web: {Config.PublicWebUrl}");
    }

    internal static bool IsPlayerAdmin(CCSPlayerController? player)
    {
        if (player is null) return false;
        try
        {
            if (player.IsBot) return false;
            return AdminManager.PlayerHasPermissions(player, "@css/kick")
                || AdminManager.PlayerHasPermissions(player, "@root")
                || AdminManager.PlayerHasPermissions(player, "@custom/cs2suite_admin");
        }
        catch { return false; }
    }

    // ================= 事件分发 =================
    private HookResult OnClientConnectedFull(EventPlayerConnectFull ev, GameEventInfo info)
    {
        var player = ev.Userid;
        if (player is not { IsValid: true } || player.IsBot) return HookResult.Continue;
        Skins.OnClientConnectedFull(player);
        Dm.LoadPrefs(player.SteamID.ToString());
        return HookResult.Continue;
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn ev, GameEventInfo info)
    {
        var p = ev.Userid;
        if (p is not { IsValid: true } || p.IsBot) return HookResult.Continue;
        Skins.OnPlayerSpawn(p);
        Practice.OnPlayerSpawn(p);
        Dm.OnPlayerSpawn(p);
        Competitive.OnPlayerSpawn(p);
        return HookResult.Continue;
    }

    private HookResult OnClientDisconnect(EventPlayerDisconnect ev, GameEventInfo info)
    {
        // 断线事件里 Userid 仍短暂有效;只取轻量字段,不碰实体引用
        try
        {
            var p = ev.Userid;
            if (p is { IsValid: true } && !p.IsBot)
            {
                var steam = p.SteamID.ToString();
                Skins.Forget(p);
                Dm.OnPlayerLeft(p);
                Competitive.OnPlayerLeft(p);
            }
        }
        catch { /* 引擎在断线瞬间给旧句柄属正常 */ }
        return HookResult.Continue;
    }

    private HookResult OnItemPickup(EventItemPickup ev, GameEventInfo info)
    {
        // 捡枪后补一次皮肤(与购买同款时序)
        var p = ev.Userid;
        if (p is not { IsValid: true } || p.IsBot) return HookResult.Continue;
        AddTimer(0.1f, () => { if (p.IsValid && p.PawnIsAlive) Skins.OnPlayerSpawn(p); });
        return HookResult.Continue;
    }

    private HookResult OnItemPurchaseOld(EventItemPurchase ev, GameEventInfo info)
    {
        var p = ev.Userid;
        if (p is not { IsValid: true } || p.IsBot) return HookResult.Continue;
        AddTimer(0.1f, () => { if (p.IsValid && p.PawnIsAlive) Skins.OnPlayerSpawn(p); });
        return HookResult.Continue;
    }

    private HookResult OnPlayerDeath(EventPlayerDeath ev, GameEventInfo info)
    {
        var v = ev.Userid;
        if (v is not { IsValid: true } || v.IsBot) return HookResult.Continue;
        Dm.OnPlayerDeath(v, ev);
        Competitive.OnPlayerDeath(v, ev);
        return HookResult.Continue;
    }

    private HookResult OnRoundPrestart(EventRoundPrestart ev, GameEventInfo info)
    {
        Skins.OnRoundPrestart();
        Practice.OnRoundPrestart();
        Dm.OnRoundPrestart();
        Competitive.OnRoundPrestart();
        return HookResult.Continue;
    }

    private HookResult OnRoundEnd(EventRoundEnd ev, GameEventInfo info)
    {
        Competitive.OnRoundEnd(ev);
        return HookResult.Continue;
    }

}
