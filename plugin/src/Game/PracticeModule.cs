using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CS2Suite.Core;

namespace CS2Suite.Game;

/// 训练模式 — 整合自 MatchZy PracticeMode(无限弹药/无冻结/即时重生/机器人/位点)
/// 与 ProxTricky・OpenPrefirePrac(位点保存传送)。
/// /practice !god !savepos <名> !tp <名> !positions !delpos <名> /addbot ct|t
public sealed class PracticeModule : ModuleBase
{
    private readonly Dictionary<string, bool> _godMode = new();

    public PracticeModule(CS2SuitePlugin p) : base(p) { }

    public override void Initialize()
    {
        Plugin.AddCommand("practice", "CS2Suite: enter practice mode", (pl, info) =>
        {
            if (pl is not { IsValid: true }) { info.ReplyToCommand(Lang.T("cmd_console_only")); return; }
            if (!CS2SuitePlugin.IsPlayerAdmin(pl) && !(Cfg.Modes.AllowPlayerModeVote && Cfg.Practice.Enabled))
            {
                Tell(pl, "mode_admin_only");
                return;
            }
            Plugin.SwitchGameMode("practice");
        });
        Plugin.AddCommand("god", "CS2Suite: toggle god mode (practice)", OnGod);
        Plugin.AddCommand("savepos", "CS2Suite: save position !savepos <name>", OnSavePos);
        Plugin.AddCommand("tp", "CS2Suite: teleport !tp <name>", OnTp);
        Plugin.AddCommand("positions", "CS2Suite: list my positions", OnListPos);
        Plugin.AddCommand("delpos", "CS2Suite: delete position !delpos <name>", OnDelPos);
        Plugin.AddCommand("addbot", "CS2Suite: /addbot [ct|t]", OnAddBot);
        Plugin.AddCommand("clearweapons", "CS2Suite: remove ground weapons", (pl, info) =>
        {
            if (pl is { IsValid: true } && IsActive)
            {
                RemoveGround();
                Tell(pl, "cleared");
            }
        });
    }

    protected override void OnActivated()
    {
        if (!Cfg.Practice.Enabled) return;
        ApplyConvars();
        Server.ExecuteCommand("mp_restartgame 1");
    }
    protected override void OnReactivated() => ApplyConvars();

    protected override void OnDeactivated()
    {
        foreach (var k in _godMode.Keys.ToList()) SetGod(k, false);
        _godMode.Clear();
    }

    private void ApplyConvars()
    {
        var pc = Cfg.Practice;
        Cvar("game_mode", "1");
        Cvar("game_type", "0");
        Cvar("mp_freezetime", pc.NoFreezetime ? "0" : "6");
        Cvar("mp_roundtime", "60");
        Cvar("mp_roundtime_defuse", "60");
        Cvar("mp_maxrounds", "0");
        Cvar("mp_timelimit", "0");
        Cvar("mp_respawn_on_death_ct", pc.InstantRespawn ? "1" : "0");
        Cvar("mp_respawn_on_death_t", pc.InstantRespawn ? "1" : "0");
        Cvar("mp_ignore_round_win_conditions", "1");
        Cvar("sv_infinite_ammo", pc.InfiniteAmmo ? "1" : "0");
        Cvar("mp_autoteambalance", "0");
        Cvar("mp_limitteams", "30");
        Cvar("mp_buy_anywhere", "1");
        Cvar("mp_buytime", "9999");
        Cvar("sv_cheats", "1");
        Cvar("mp_disable_autokick", "1");
    }

    internal void OnPlayerSpawn(CCSPlayerController player)
    {
        if (!IsActive || player.IsBot) return;
        if (Cfg.Practice.EnableGod && _godMode.GetValueOrDefault(SteamId64(player)))
            SetGod(SteamId64(player), true);
    }

    internal void OnRoundPrestart()
    {
        if (IsActive) RemoveGround();
    }

    private static readonly string[] WeaponDesigners =
    [
        "weapon_ak47","weapon_m4a1","weapon_m4a1_silencer","weapon_awp","weapon_ssg08","weapon_deagle",
        "weapon_glock","weapon_usp_silencer","weapon_p2000","weapon_p250","weapon_fiveseven","weapon_tec9","weapon_elite","weapon_revolver",
        "weapon_famas","weapon_galilar","weapon_aug","weapon_sg556","weapon_mp9","weapon_mac10","weapon_mp7","weapon_mp5sd",
        "weapon_ump45","weapon_p90","weapon_bizon","weapon_negev","weapon_m249","weapon_nova","weapon_xm1014","weapon_sawedoff","weapon_mag7",
        "weapon_hegrenade","weapon_flashbang","weapon_smokegrenade","weapon_molotov","weapon_incendiarygrenade","weapon_decoy",
    ];

    private static void RemoveGround()
    {
        // 只清地面掉落(OwnerEntity 无效,与 WeaponPaints 判定一致),不碰玩家手持
        foreach (var dn in WeaponDesigners)
        {
            foreach (var ent in Utilities.FindAllEntitiesByDesignerName<CBasePlayerWeapon>(dn))
            {
                try
                {
                    if (!ent.IsValid) continue;
                    if (ent.OwnerEntity is not null && ent.OwnerEntity.IsValid) continue;
                    ent.AddEntityIOEvent("Kill", ent, null, "", 0.01f);
                }
                catch { }
            }
        }
    }

    // ---------------- god ----------------
    private void OnGod(CCSPlayerController? player, CommandInfo info)
    {
        if (player is not { IsValid: true } || player.IsBot) { info.ReplyToCommand(Lang.T("cmd_console_only")); return; }
        if (!IsActive) { Tell(player, "practice_only"); return; }
        if (!Cfg.Practice.EnableGod) { Tell(player, "disabled"); return; }
        var steam = SteamId64(player);
        var on = !_godMode.GetValueOrDefault(steam);
        _godMode[steam] = on;
        SetGod(steam, on);
        Tell(player, on ? "god_on" : "god_off");
    }

    private void SetGod(string steam, bool on)
    {
        foreach (var p in Utilities.GetPlayers())
            if (p.IsValid && p.SteamID.ToString() == steam && p.PlayerPawn?.Value is { } pawn && pawn.IsValid)
                pawn.TakesDamage = !on;
    }

    // ---------------- 位点 ----------------
    private void OnSavePos(CCSPlayerController? player, CommandInfo info)
    {
        if (player is not { IsValid: true } || player.IsBot) { info.ReplyToCommand(Lang.T("cmd_console_only")); return; }
        if (!IsActive) { Tell(player, "practice_only"); return; }
        if (!Cfg.Practice.SaveTeleportPositions) { Tell(player, "disabled"); return; }

        var name = Safe(info.ArgCount > 1 ? info.ArgByIndex(1) : player.PlayerName, 48);
        if (string.IsNullOrEmpty(name)) { Tell(player, "pos_need_name"); return; }
        var pawn = player.PlayerPawn?.Value;
        if (pawn?.IsValid != true || pawn.AbsOrigin is null) return;

        var pos = pawn.AbsOrigin;
        var ang = pawn.EyeAngles;
        var steam = SteamId64(player);
        var map = Server.MapName;
        _ = Task.Run(async () =>
        {
            var cnt = await Db.QueryAsync(
                "SELECT COUNT(*) c FROM cs2suite_practice_positions WHERE steamid64=@s AND map_name=@m AND kind='tp';",
                cmd => { Db.P(cmd, "@s", steam); Db.P(cmd, "@m", map); });
            if (cnt.Count > 0 && Db.L(cnt[0], "c") >= Cfg.Practice.MaxPositionsPerMap)
            {
                Server.NextFrame(() => { if (player.IsValid) Tell(player, "pos_limit", ("n", Cfg.Practice.MaxPositionsPerMap)); });
                return;
            }
            await Db.ExecuteAsync(
                "INSERT INTO cs2suite_practice_positions (steamid64,map_name,name,x,y,z,yaw,pitch,kind) " +
                "VALUES (@s,@m,@n,@x,@y,@z,@yaw,@pitch,'tp') " +
                "ON DUPLICATE KEY UPDATE x=@x,y=@y,z=@z,yaw=@yaw,pitch=@pitch,saved_at=NOW();",
                cmd =>
                {
                    Db.P(cmd, "@s", steam); Db.P(cmd, "@m", map); Db.P(cmd, "@n", name);
                    Db.P(cmd, "@x", pos.X); Db.P(cmd, "@y", pos.Y); Db.P(cmd, "@z", pos.Z);
                    Db.P(cmd, "@yaw", ang.Y); Db.P(cmd, "@pitch", ang.X);
                });
            Server.NextFrame(() => { if (player.IsValid) Tell(player, "pos_saved", ("n", name)); });
        });
    }

    private void OnTp(CCSPlayerController? player, CommandInfo info)
    {
        if (player is not { IsValid: true } || player.IsBot) { info.ReplyToCommand(Lang.T("cmd_console_only")); return; }
        if (!IsActive) { Tell(player, "practice_only"); return; }
        var name = Safe(info.ArgCount > 1 ? info.ArgByIndex(1) : "", 48);
        if (string.IsNullOrEmpty(name)) { Tell(player, "pos_need_name"); return; }
        var steam = SteamId64(player);
        var map = Server.MapName;
        _ = Task.Run(async () =>
        {
            var rows = await Db.QueryAsync(
                "SELECT * FROM cs2suite_practice_positions WHERE steamid64=@s AND map_name=@m AND name=@n AND kind='tp' LIMIT 1;",
                cmd => { Db.P(cmd, "@s", steam); Db.P(cmd, "@m", map); Db.P(cmd, "@n", name); });
            Server.NextFrame(() =>
            {
                if (!player.IsValid) return;
                if (rows.Count == 0) { Tell(player, "pos_not_found", ("n", name)); return; }
                var r = rows[0];
                var pawn = player.PlayerPawn?.Value;
                if (pawn?.IsValid != true) return;
                pawn.Teleport(
                    new Vector((float)Db.L(r, "x"), (float)Db.L(r, "y"), (float)Db.L(r, "z")),
                    new QAngle(Fcol(r, "pitch"), Fcol(r, "yaw"), 0),
                    new Vector(0, 0, 0));
                Tell(player, "pos_tp", ("n", name));
            });
        });
    }

    private void OnListPos(CCSPlayerController? player, CommandInfo info)
    {
        if (player is not { IsValid: true } || player.IsBot) return;
        var steam = SteamId64(player);
        var map = Server.MapName;
        _ = Task.Run(async () =>
        {
            var rows = await Db.QueryAsync(
                "SELECT name FROM cs2suite_practice_positions WHERE steamid64=@s AND map_name=@m ORDER BY name LIMIT 50;",
                cmd => { Db.P(cmd, "@s", steam); Db.P(cmd, "@m", map); });
            Server.NextFrame(() =>
            {
                if (!player.IsValid) return;
                if (rows.Count == 0) { Tell(player, "pos_none"); return; }
                Tell(player, "pos_list_header", ("map", map));
                foreach (var r in rows)
                    TellRaw(player, $"   {ChatColors.Grey}> {ChatColors.Default}{Db.S(r, "name")}");
            });
        });
    }

    private void OnDelPos(CCSPlayerController? player, CommandInfo info)
    {
        if (player is not { IsValid: true } || player.IsBot) return;
        var name = Safe(info.ArgCount > 1 ? info.ArgByIndex(1) : "", 48);
        if (string.IsNullOrEmpty(name)) { Tell(player, "pos_need_name"); return; }
        var steam = SteamId64(player);
        var map = Server.MapName;
        _ = Task.Run(async () =>
        {
            await Db.ExecuteAsync(
                "DELETE FROM cs2suite_practice_positions WHERE steamid64=@s AND map_name=@m AND name=@n;",
                cmd => { Db.P(cmd, "@s", steam); Db.P(cmd, "@m", map); Db.P(cmd, "@n", name); });
            Server.NextFrame(() => { if (player.IsValid) Tell(player, "pos_deleted", ("n", name)); });
        });
    }

    private void OnAddBot(CCSPlayerController? player, CommandInfo info)
    {
        if (player is not { IsValid: true } || player.IsBot) { info.ReplyToCommand(Lang.T("cmd_console_only")); return; }
        if (!IsActive) { Tell(player, "practice_only"); return; }
        var side = (info.ArgCount > 1 ? info.ArgByIndex(1) : "").ToLowerInvariant();
        Server.ExecuteCommand(side switch { "ct" => "bot_add_ct", "t" => "bot_add_t", _ => "bot_add" });
        Tell(player, "bot_added");
    }

    private static float Fcol(Dictionary<string, object?> r, string k)
    {
        if (!r.TryGetValue(k, out var v) || v is null or DBNull) return 0f;
        try { return Convert.ToSingle(v); } catch { return 0f; }
    }
}
