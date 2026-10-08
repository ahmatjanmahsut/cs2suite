using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CS2Suite.Core;
using System.Text.Json;

namespace CS2Suite.Game;

/// 死斗模式 — 整合自 NockyCZ/CS2-Deathmatch 核心玩法:
/// 即时重生 / 出生保护 / 武器选择 / 连杀播报 / 计分 / 生涯榜 / 偏好持久化(MySQL)。
/// /dm !guns !gun <武器>|random|knifeonly !dmsettings hud|sound !dmtop
public sealed class DeathmatchModule : ModuleBase
{
    /// <summary>死斗模式激活即视为"比赛进行中"(菜单据此禁用非外观项)。</summary>
    internal bool IsMatchInProgress => IsActive;

    /// <summary>供菜单显示当前选枪状态。</summary>
    internal string CurrentPrimary(string steam)
        => _prefs.TryGetValue(steam, out var p) ? p.GetValueOrDefault("primary", "默认") : "默认";

    internal bool KnifeOnly => _knifeOnly;

    /// <summary>菜单用:设置主武器(与 !gun 命令同一逻辑)。</summary>
    internal void MenuSetPrimary(CCSPlayerController player, string key)
    {
        var steam = SteamId64(player);
        var map = _prefs.TryGetValue(steam, out var m) ? m : _prefs[steam] = new Dictionary<string, string>();
        map["primary"] = key;
        SavePrefs(steam, map);
        Tell(player, "gun_set", ("w", key));
        if (player.PawnIsAlive && PrimaryName(key, player.Team) is { } name)
        {
            StripGuns(player);
            player.GiveNamedItem(name);
            if (Cfg.Deathmatch.GiveHelmet) player.GiveNamedItem("item_heavyassaultsuit");
        }
    }

    internal void MenuToggleKnifeOnly(CCSPlayerController player)
    {
        _knifeOnly = !_knifeOnly;
        Tell(player, _knifeOnly ? "gun_knife_on" : "gun_knife_off");
        if (_knifeOnly)
            foreach (var p in Utilities.GetPlayers())
                if (p.IsValid && !p.IsBot && p.PawnIsAlive) StripGuns(p);
    }

    internal void MenuTogglePref(CCSPlayerController player, string key)
    {
        var steam = SteamId64(player);
        var map = _prefs.TryGetValue(steam, out var m) ? m : _prefs[steam] = new Dictionary<string, string>();
        var val = Toggle(map, key);
        SavePrefs(steam, map);
        Tell(player, "dmsettings_saved", ("k", key), ("v", val));
    }

    private sealed record DmScore(int Kills, int Deaths, int Points, int Streak);
    private readonly Dictionary<string, DmScore> _board = new();
    private readonly Dictionary<string, string> _names = new();
    private readonly Dictionary<string, DateTime> _protectUntil = new();
    private readonly Dictionary<string, Dictionary<string, string>> _prefs = new();
    private uint _roundRowId;
    private bool _knifeOnly;

    private static readonly HashSet<string> PrimaryOk = new(StringComparer.OrdinalIgnoreCase)
    { "ak47","m4a4","m4a1","m4a1_silencer","galilar","famas","aug","sg553","awp","ssg08","scar20","g3sg1","mac10","mp9","mp5sd","bizon","ump45","p90","nova","xm1014","sawedoff","mag7","m249","negev" };
    private static readonly HashSet<string> PistolOk = new(StringComparer.OrdinalIgnoreCase)
    { "glock","uspsilencer","usp","usp_silencer","p2000","p250","fiveseven","tec9","deagle","elite","revolver" };

    public DeathmatchModule(CS2SuitePlugin p) : base(p) { }

    public override void Initialize()
    {
        Plugin.AddCommand("dm", "CS2Suite: enter deathmatch mode", (pl, info) =>
        {
            if (pl is not { IsValid: true }) { info.ReplyToCommand(Lang.T("cmd_console_only")); return; }
            if (!CS2SuitePlugin.IsPlayerAdmin(pl) && !(Cfg.Modes.AllowPlayerModeVote && Cfg.Deathmatch.Enabled))
            {
                Tell(pl, "mode_admin_only");
                return;
            }
            Plugin.SwitchGameMode("deathmatch");
        });
        Plugin.AddCommand("guns", "CS2Suite: DM weapon menu", (pl, info) => OnGun(pl, info, "list"));
        Plugin.AddCommand("gun", "CS2Suite: !gun <weapon>|random|knifeonly", (pl, info) =>
            OnGun(pl, info, info.ArgCount > 1 ? info.ArgByIndex(1) : "list"));
        Plugin.AddCommand("dmsettings", "CS2Suite: toggle personal DM preferences", OnPrefs);
        Plugin.AddCommand("dmtop", "CS2Suite: DM leaderboard", OnTop);
    }

    protected override void OnActivated()
    {
        _knifeOnly = false;
        _board.Clear();
        ApplyConvars();
        OpenSession();
        Server.ExecuteCommand("mp_restartgame 1");
        Broadcast("dm_started");
    }
    protected override void OnReactivated() => ApplyConvars();

    protected override void OnDeactivated()
    {
        CloseSession();
        _protectUntil.Clear();
    }

    private void ApplyConvars()
    {
        Cvar("game_type", "1");
        Cvar("game_mode", "3");
        Cvar("mp_respawn_on_death_ct", "1");
        Cvar("mp_respawn_on_death_t", "1");
        Cvar("mp_freezetime", "0");
        Cvar("mp_maxmoney", "16000");
        Cvar("mp_startmoney", "16000");
        Cvar("mp_buytime", "9999");
        Cvar("mp_buy_anywhere", "1");
        Cvar("mp_maxrounds", "0");
        Cvar("mp_timelimit", "0");
        Cvar("mp_roundtime", "60");
        Cvar("mp_halftime", "0");
        Cvar("mp_autoteambalance", "0");
        Cvar("mp_limitteams", "0");
        Cvar("mp_allowspectators", "1");
        Cvar("mp_spectators_max", "10");
        Cvar("mp_friendlyfire", "0");
        Cvar("sv_infinite_ammo", "0");
        Cvar("sv_cheats", "0");
        Cvar("mp_disable_autokick", "1");
        Cvar("mp_ignore_round_win_conditions", "1");
        Cvar("mp_forcerespawn", "0");
    }

    private void OpenSession()
    {
        _roundRowId = 0;
        if (!Db.Ready) return;
        var map = Server.MapName; // 游戏线程快照
        _ = Task.Run(async () =>
        {
            _roundRowId = (uint)await Db.InsertReturnIdAsync(
                "INSERT INTO cs2suite_dm_rounds (map_name, started_at) VALUES (@m, NOW());",
                cmd => Db.P(cmd, "@m", map));
        });
    }

    private void CloseSession()
    {
        if (!Db.Ready || _roundRowId == 0) return;
        var snapshot = _board.Select(kv => (kv.Key, kv.Value)).ToList();
        var rid = _roundRowId;
        _roundRowId = 0;
        _ = Task.Run(async () =>
        {
            await Db.ExecuteAsync("UPDATE cs2suite_dm_rounds SET ended_at=NOW() WHERE round_id=@r;", cmd => Db.P(cmd, "@r", rid));
            // dm_scores 已在每次击杀/死亡时即时累加;这里只兜底补写内存中的最终值并记 sessions
            foreach (var (steam, s) in snapshot)
            {
                _names.TryGetValue(steam, out var name);
                await Db.ExecuteAsync(
                    "INSERT INTO cs2suite_dm_scores (round_id, steamid64, persona, kills, deaths, points) VALUES (@r,@s,@p,@k,@d,@pt) " +
                    "ON DUPLICATE KEY UPDATE persona=@p;",
                    cmd =>
                    {
                        Db.P(cmd, "@r", rid); Db.P(cmd, "@s", steam); Db.P(cmd, "@p", name ?? "");
                        Db.P(cmd, "@k", s.Kills); Db.P(cmd, "@d", s.Deaths); Db.P(cmd, "@pt", s.Points);
                    });
                await Db.ExecuteAsync(
                    "INSERT INTO cs2suite_dm_stats (steamid64, sessions) VALUES (@s,1) ON DUPLICATE KEY UPDATE sessions=sessions+1;",
                    cmd => Db.P(cmd, "@s", steam));
            }
        });
    }

    internal void OnPlayerSpawn(CCSPlayerController player)
    {
        if (!IsActive || player.IsBot) return;
        var steam = SteamId64(player);
        _board.TryAdd(steam, new DmScore(0, 0, 0, 0));
        _names[steam] = player.PlayerName;   // 名字快照,供异步结算使用
        LoadPrefs(steam);

        if (Cfg.Deathmatch.SpawnProtectionSeconds > 0)
            _protectUntil[steam] = DateTime.UtcNow.AddSeconds(Cfg.Deathmatch.SpawnProtectionSeconds);

        AddTimer(0.15f, () =>
        {
            if (!player.IsValid || !player.PawnIsAlive) return;
            if (_protectUntil.TryGetValue(steam, out var until) && DateTime.UtcNow < until)
            {
                player.PlayerPawn?.Value?.TakesDamage = false;
                AddTimer((float)Math.Max(0.1, (until - DateTime.UtcNow).TotalSeconds), () =>
                {
                    if (player.IsValid && player.PlayerPawn?.Value is { } pawn && pawn.IsValid)
                        pawn.TakesDamage = true;
                });
            }
            GiveKit(player);
        });
    }

    internal void OnPlayerDeath(CCSPlayerController victim, EventPlayerDeath ev)
    {
        if (!IsActive) return;
        var vSteam = SteamId64(victim);
        _board[vSteam] = Get(vSteam) with { Deaths = Get(vSteam).Deaths + 1, Streak = 0 };

        // ★ 死者名字也先快照(victim 实体随后可能失效)
        _names[vSteam] = victim.IsValid ? victim.PlayerName : _names.GetValueOrDefault(vSteam, "");

        var attacker = ev.Attacker;
        if (attacker is not { IsValid: true } || attacker.IsBot || attacker.SteamID == victim.SteamID)
        {
            if (Db.Ready) _ = OnDeathAsync(vSteam, _names[vSteam]);
            return;
        }

        var aSteam = attacker.SteamID.ToString();
        var a = Get(aSteam);
        var hs = ev.Headshot;
        var pts = Cfg.Deathmatch.PointsKill + (hs ? Cfg.Deathmatch.PointsHeadshotBonus : 0);
        var streak = a.Streak + 1;
        _board[aSteam] = a with { Kills = a.Kills + 1, Points = a.Points + pts, Streak = streak };

        foreach (var th in Cfg.Deathmatch.KillstreakAnnounceAt)
            if (streak == th && attacker.IsValid)
                Broadcast("dm_streak", ("player", attacker.PlayerName), ("n", th));

        if (Db.Ready)
        {
            var aName = attacker.IsValid ? attacker.PlayerName : _names.GetValueOrDefault(aSteam, "");
            _names[aSteam] = aName;
            _ = OnKillAsync(aSteam, aName, hs, pts);
            _ = OnDeathAsync(vSteam, _names[vSteam]);
        }
    }

    /// <summary>击杀结算:生涯表 + 当场 dm_scores 即时累加(不依赖关场,换图/重启不丢)。</summary>
    private async Task OnKillAsync(string steam, string name, bool hs, int pts)
    {
        await Db.ExecuteAsync(
            "INSERT INTO cs2suite_dm_stats (steamid64, persona, kills, deaths, headshots, points) " +
            "VALUES (@s,@p,1,0,@h,@pt) " +
            "ON DUPLICATE KEY UPDATE persona=@p, kills=kills+1, headshots=headshots+@h, points=points+@pt;",
            cmd => { Db.P(cmd, "@s", steam); Db.P(cmd, "@p", name); Db.P(cmd, "@h", hs ? 1 : 0); Db.P(cmd, "@pt", pts); });
        var streak = _board.TryGetValue(steam, out var s) ? s.Streak : 0;
        await Db.ExecuteAsync(
            "UPDATE cs2suite_dm_stats SET killstreak_best=GREATEST(killstreak_best,@b) WHERE steamid64=@s;",
            cmd => { Db.P(cmd, "@b", streak); Db.P(cmd, "@s", steam); });
        await RoundScoreAsync(steam, name, 1, 0, pts);
    }

    private async Task OnDeathAsync(string steam, string name)
    {
        await Db.ExecuteAsync(
            "INSERT INTO cs2suite_dm_stats (steamid64, persona, deaths) VALUES (@s,@p,1) " +
            "ON DUPLICATE KEY UPDATE persona=@p, deaths=deaths+1;",
            cmd => { Db.P(cmd, "@s", steam); Db.P(cmd, "@p", name); });
        await RoundScoreAsync(steam, name, 0, 1, 0);
    }

    private async Task RoundScoreAsync(string steam, string name, int kills, int deaths, int pts)
    {
        var rid = _roundRowId;
        if (rid == 0) return;
        await Db.ExecuteAsync(
            "INSERT INTO cs2suite_dm_scores (round_id, steamid64, persona, kills, deaths, points) VALUES (@r,@s,@p,@k,@d,@pt) " +
            "ON DUPLICATE KEY UPDATE kills=kills+@k, deaths=deaths+@d, points=points+@pt, persona=@p;",
            cmd =>
            {
                Db.P(cmd, "@r", rid); Db.P(cmd, "@s", steam); Db.P(cmd, "@p", name);
                Db.P(cmd, "@k", kills); Db.P(cmd, "@d", deaths); Db.P(cmd, "@pt", pts);
            });
    }

    internal void OnPlayerLeft(CCSPlayerController player)
    {
        var steam = SteamId64(player);
        _board.Remove(steam);
        _protectUntil.Remove(steam);
        _names.Remove(steam);
    }

    internal void OnRoundPrestart()
    {
        if (IsActive && _roundRowId == 0 && Db.Ready) OpenSession();
    }

    private DmScore Get(string steam) => _board.TryGetValue(steam, out var v) ? v : new DmScore(0, 0, 0, 0);

    private void GiveKit(CCSPlayerController player)
    {
        if (!Cfg.Deathmatch.AllowWeaponSelection) return;
        var steam = SteamId64(player);
        if (_knifeOnly) { StripGuns(player); return; }
        if (!_prefs.TryGetValue(steam, out var pr) || pr.GetValueOrDefault("primary", "") is "" or "random")
            return;

        var primary = pr.GetValueOrDefault("primary", "");
        var pistol = pr.GetValueOrDefault("pistol", "");
        AddTimer(0.3f, () =>
        {
            if (!player.IsValid || !player.PawnIsAlive) return;
            StripGuns(player);
            var team = player.Team;
            if (!string.IsNullOrEmpty(primary))
            {
                var name = PrimaryName(primary, team);
                if (name is not null) player.GiveNamedItem(name);
                if (Cfg.Deathmatch.GiveHelmet) player.GiveNamedItem("item_heavyassaultsuit");
            }
            if (!string.IsNullOrEmpty(pistol))
                player.GiveNamedItem("weapon_" + (pistol == "uspsilencer" ? "usp_silencer" : pistol));
            if (team == CsTeam.CounterTerrorist)
                player.GiveNamedItem("item_defuser");
        });
    }

    private static string? PrimaryName(string key, CsTeam team) => key.ToLowerInvariant() switch
    {
        "ak47" => "weapon_ak47",
        "m4a4" => "weapon_m4a1",
        "m4a1" or "m4a1_silencer" => team == CsTeam.CounterTerrorist ? "weapon_m4a1_silencer" : "weapon_m4a1",
        "galilar" => "weapon_galilar",
        "famas" => "weapon_famas",
        "aug" => "weapon_aug",
        "sg553" or "sg556" => "weapon_sg556",
        "awp" => "weapon_awp",
        "ssg08" => "weapon_ssg08",
        "scar20" => "weapon_scar20",
        "g3sg1" => "weapon_g3sg1",
        "mac10" => "weapon_mac10",
        "mp9" => "weapon_mp9",
        "mp5sd" => "weapon_mp5sd",
        "bizon" => "weapon_bizon",
        "ump45" => "weapon_ump45",
        "p90" => "weapon_p90",
        "nova" => "weapon_nova",
        "xm1014" => "weapon_xm1014",
        "sawedoff" => "weapon_sawedoff",
        "mag7" => "weapon_mag7",
        "m249" => "weapon_m249",
        "negev" => "weapon_negev",
        _ => null,
    };

    private static void StripGuns(CCSPlayerController player)
    {
        var ws = player.PlayerPawn?.Value?.WeaponServices;
        if (ws is null) return;
        foreach (var w in ws.MyWeapons.ToList())
        {
            if (w?.Value is not { } we || !we.IsValid) continue;
            var dn = we.DesignerName ?? "";
            if (!dn.StartsWith("weapon_")) continue;
            if (dn.Contains("knife", StringComparison.OrdinalIgnoreCase) || dn.Contains("bayonet", StringComparison.OrdinalIgnoreCase)) continue;
            try { we.AddEntityIOEvent("Kill", we, null, "", 0.01f); } catch { }
        }
    }

    private void OnGun(CCSPlayerController? player, CommandInfo info, string arg)
    {
        if (player is not { IsValid: true } || player.IsBot) { info.ReplyToCommand(Lang.T("cmd_console_only")); return; }
        if (!IsActive) { Tell(player, "dm_only"); return; }
        var steam = SteamId64(player);
        arg = arg.ToLowerInvariant();

        if (arg is "list" or "")
        {
            Tell(player, "gun_header");
            TellRaw(player, "   " + ChatColors.Grey + Lang.T("gun_hint") + ChatColors.Default);
            TellRaw(player, "   " + ChatColors.Yellow + "!gun ak47|m4a4|awp|deagle|..." + ChatColors.Default + "  " + ChatColors.Grey + Lang.T("gun_primary") + ChatColors.Default);
            TellRaw(player, "   " + ChatColors.Yellow + "!gun random" + ChatColors.Default + "  " + ChatColors.Grey + Lang.T("gun_random") + ChatColors.Default);
            TellRaw(player, "   " + ChatColors.Yellow + "!gun knifeonly" + ChatColors.Default + "  " + ChatColors.Grey + Lang.T("gun_knife") + ChatColors.Default);
            var cur = _prefs.GetValueOrDefault(steam) ?? new();
            TellRaw(player, "   " + ChatColors.Grey + Lang.T("gun_current") + ": " + cur.GetValueOrDefault("primary", "-") + ChatColors.Default);
            return;
        }

        if (arg == "knifeonly")
        {
            _knifeOnly = !_knifeOnly;
            Tell(player, _knifeOnly ? "gun_knife_on" : "gun_knife_off");
            if (_knifeOnly)
                foreach (var p in Utilities.GetPlayers())
                    if (p.IsValid && !p.IsBot && p.PawnIsAlive) StripGuns(p);
            return;
        }

        if (arg == "random")
        {
            var map0 = _prefs.TryGetValue(steam, out var m0) ? m0 : _prefs[steam] = new();
            map0["primary"] = "random";
            SavePrefs(steam, map0);
            Tell(player, "gun_set", ("w", Lang.T("gun_random")));
            return;
        }

        if (PrimaryOk.Contains(arg) || PrimaryName(arg, player.Team) is not null)
        {
            var map1 = _prefs.TryGetValue(steam, out var m1) ? m1 : _prefs[steam] = new();
            map1["primary"] = arg;
            SavePrefs(steam, map1);
            Tell(player, "gun_set", ("w", arg));
            if (player.PawnIsAlive) { StripGuns(player); GiveKitNow(player, arg); }
            return;
        }

        if (PistolOk.Contains(arg))
        {
            var map2 = _prefs.TryGetValue(steam, out var m2) ? m2 : _prefs[steam] = new();
            map2["pistol"] = arg;
            SavePrefs(steam, map2);
            Tell(player, "gun_set", ("w", arg));
            player.GiveNamedItem("weapon_" + (arg == "uspsilencer" ? "usp_silencer" : arg));
            return;
        }

        Tell(player, "gun_unknown", ("w", arg));
    }

    private void GiveKitNow(CCSPlayerController player, string primary)
    {
        var name = PrimaryName(primary, player.Team);
        if (name is null) return;
        player.GiveNamedItem(name);
        if (Cfg.Deathmatch.GiveHelmet) player.GiveNamedItem("item_heavyassaultsuit");
    }

    private void SavePrefs(string steam, Dictionary<string, string> map)
    {
        if (!Db.Ready) return;
        var json = JsonSerializer.Serialize(map);
        _ = Task.Run(() => Db.ExecuteAsync(
            "INSERT INTO cs2suite_player_prefs (steamid64, prefs) VALUES (@s, CAST(@p AS JSON)) ON DUPLICATE KEY UPDATE prefs=CAST(@p AS JSON);",
            cmd => { Db.P(cmd, "@s", steam); Db.P(cmd, "@p", json); }));
    }

    internal void LoadPrefs(string steamid64)
    {
        if (!Db.Ready || _prefs.ContainsKey(steamid64)) return;
        _prefs[steamid64] = new Dictionary<string, string>();
        _ = Task.Run(async () =>
        {
            var rows = await Db.QueryAsync("SELECT prefs FROM cs2suite_player_prefs WHERE steamid64=@s;", cmd => Db.P(cmd, "@s", steamid64));
            if (rows.Count == 0) return;
            try
            {
                var map = JsonSerializer.Deserialize<Dictionary<string, string>>(Db.S(rows[0], "prefs") ?? "{}");
                if (map is not null) Server.NextFrame(() => _prefs[steamid64] = map);
            }
            catch { }
        });
    }

    private void OnPrefs(CCSPlayerController? player, CommandInfo info)
    {
        if (player is not { IsValid: true } || player.IsBot) { info.ReplyToCommand(Lang.T("cmd_console_only")); return; }
        var steam = SteamId64(player);
        var map = _prefs.TryGetValue(steam, out var m) ? m : _prefs[steam] = new();
        var key = (info.ArgCount > 1 ? info.ArgByIndex(1) : "").ToLowerInvariant();
        string val;
        switch (key)
        {
            case "hud": val = Toggle(map, "dm_hud"); break;
            case "sound": val = Toggle(map, "dm_killsound"); break;
            default: Tell(player, "dmsettings_hint"); return;
        }
        SavePrefs(steam, map);
        Tell(player, "dmsettings_saved", ("k", key), ("v", val));
    }

    private static string Toggle(Dictionary<string, string> map, string key)
    {
        var on = map.GetValueOrDefault(key) != "1";
        map[key] = on ? "1" : "0";
        return on ? "on" : "off";
    }

    private void OnTop(CCSPlayerController? player, CommandInfo info)
    {
        if (player is not { IsValid: true } || player.IsBot) { info.ReplyToCommand(Lang.T("cmd_console_only")); return; }
        if (!Db.Ready) { Tell(player, "db_down"); return; }
        var n = Math.Clamp(Cfg.Deathmatch.LeaderboardRows, 1, 25);
        _ = Task.Run(async () =>
        {
            var rows = await Db.QueryAsync(
                "SELECT persona, kills, deaths, headshots, points FROM cs2suite_dm_stats ORDER BY points DESC LIMIT @n;",
                cmd => Db.P(cmd, "@n", n));
            Server.NextFrame(() =>
            {
                if (!player.IsValid) return;
                if (rows.Count == 0) { Tell(player, "dm_top_empty"); return; }
                Tell(player, "dm_top_header");
                var i = 1;
                foreach (var r in rows)
                {
                    var k = Db.L(r, "kills"); var d = Db.L(r, "deaths");
                    var kd = d == 0 ? k : (double)k / d;
                    TellRaw(player, "   " + ChatColors.Yellow + i++ + ". " + ChatColors.Default + (Db.S(r, "persona") ?? "?") +
                        " " + ChatColors.Grey + "K" + k + " D" + d + " KD" + kd.ToString("F2") + " P" + Db.L(r, "points") + ChatColors.Default);
                }
            });
        });
    }
}
