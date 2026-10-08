using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using CS2Suite.Core;

namespace CS2Suite.Skins;

/// 武器皮肤 + 贴纸 + 手套/探员/音乐包/徽章模块。
/// 外观数据来源:cs2suite_loadout_* 表(Web 端写入) — 插件不内置任何商城。
/// 绑定门控:Skins.RequireBoundAccount = true 时,未绑定 Web 账号的 SteamID 不应用任何自定义外观。
public sealed class SkinsModule : ModuleBase
{
    private readonly Dictionary<string, PlayerLoadout> _cache = new();
    private readonly Dictionary<string, DateTime> _nagged = new();
    private readonly Dictionary<string, DateTime> _refreshCooldown = new();

    // Web 改外观 → 游戏内秒级生效:消费 cs2suite_events 事件队列(每服独立游标)
    private long _lastEventId = -1;
    private int _eventTimerTicks = 0;

    public SkinsModule(CS2SuitePlugin p) : base(p) { }

    public override void Initialize()
    {
        Plugin.AddCommand("cs2sk", "CS2Suite: web skins help & refresh", OnSkinsHelp);
        Plugin.AddCommand("cs2skrefresh", "CS2Suite: reload my loadout from database", OnRefresh);

        // 每 30s 异步刷新在线玩家绑定状态(不阻塞游戏帧);新绑定的玩家立即拉取外观
        AddTimer(30f, async () =>
        {
            if (!Db.Ready || !Cfg.Skins.Enabled) return;
            var humans = Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot).ToList();
            HashSet<string> newlyBound;
            try { newlyBound = await Plugin.Bind.RefreshCacheAsync(humans); }
            catch { return; }
            foreach (var steam in newlyBound)
                foreach (var p in humans.Where(p => p.SteamID.ToString() == steam))
                {
                    Server.NextFrame(() =>
                    {
                        if (!p.IsValid) return;
                        Tell(p, "bind_success_applied", ("url", Cfg.PublicWebUrl));
                        _ = LoadInto(p);
                    });
                }
        }, TimerFlags.REPEAT);

        // —— 事件轮询:Web 保存/绑定/解绑后 SyncSeconds 秒内对在线玩家即时生效 ——
        AddTimer(Math.Clamp(Cfg.Skins.SyncSeconds, 1, 30), PollEventsAsync, TimerFlags.REPEAT);
    }

    private volatile bool _pollBusy;

    private void PollEventsAsync()
    {
        if (!Db.Ready || _pollBusy) return;

        // ★ 游戏线程:先抓在线玩家快照(CSS API 绝不允许在 Task 线程调用)
        var online = new List<(string Steam, CCSPlayerController Ctl)>();
        foreach (var p in Utilities.GetPlayers())
            if (p.IsValid && !p.IsBot) online.Add((p.SteamID.ToString(), p));

        _pollBusy = true;   // 置位必须在启动 Task 之前,否则 2s 后的下一次 tick 可能重入
        _ = Task.Run(async () =>
        {
            try
            {
                if (_lastEventId < 0)
                {
                    // 首次:游标定位到队尾,只消费"启动之后"的事件,历史不回放
                    var mx = await Db.QueryAsync("SELECT COALESCE(MAX(event_id),0) m FROM cs2suite_events;");
                    _lastEventId = mx.Count > 0 ? Db.L(mx[0], "m") : 0;
                    return;
                }
                var rows = await Db.QueryAsync(
                    "SELECT event_id, steamid64, type FROM cs2suite_events WHERE event_id > @c ORDER BY event_id LIMIT 300;",
                    cmd => Db.P(cmd, "@c", _lastEventId));
                if (rows.Count == 0) return;
                _lastEventId = Db.L(rows[^1], "event_id");

                var reloaded = new HashSet<string>();
                var unbound = new HashSet<string>();
                foreach (var r in rows)
                {
                    var st = Db.S(r, "steamid64") ?? "";
                    var tp = Db.S(r, "type") ?? "";
                    if (tp == "unbind") { unbound.Add(st); reloaded.Remove(st); }
                    else if (tp is "loadout" or "bind") { if (!unbound.Contains(st)) reloaded.Add(st); }
                }

                // 周期性清理(约每小时一次):旧事件 + 过期绑定码,防止表无限膨胀
                if (++_eventTimerTicks >= 1800)
                {
                    _eventTimerTicks = 0;
                    await Db.ExecuteAsync("DELETE FROM cs2suite_events WHERE created_at < DATE_SUB(NOW(), INTERVAL 1 DAY);");
                    await Db.ExecuteAsync("DELETE FROM cs2suite_bind_codes WHERE expires_at < DATE_SUB(NOW(), INTERVAL 1 DAY);");
                }

                // ★ 更新绑定缓存(修复:bind 事件后立刻放行,不等 30s 轮询)
                foreach (var (steam, _) in online)
                {
                    if (unbound.Contains(steam)) Plugin.Bind.BindCache[steam] = (false, DateTime.UtcNow);
                    else if (reloaded.Contains(steam)) Plugin.Bind.BindCache[steam] = (true, DateTime.UtcNow);
                }

                foreach (var (steam, p) in online)
                {
                    if (unbound.Contains(steam))
                    {
                        Server.NextFrame(() =>
                        {
                            if (!p.IsValid) return;
                            _cache.Remove(steam);
                            StripAppearance(p);
                            Tell(p, "unbind_skinstrip");
                        });
                        continue;
                    }
                    if (!reloaded.Contains(steam)) continue;
                    var lo = await LoadFromDbAsync(steam);
                    if (lo is null) continue;
                    lo.Bound = true;
                    Server.NextFrame(() =>
                    {
                        if (!p.IsValid) return;
                        _cache[steam] = lo;
                        if (p.PawnIsAlive) ApplyTo(p);
                        Tell(p, "web_applied");
                    });
                }
            }
            catch { /* 网络抖动下轮再试,不影响游戏 */ }
            finally { _pollBusy = false; }
        });
    }

    /// 解绑玩家:把场上武器与手套恢复为服务器默认(不等重生)。
    internal void StripAppearance(CCSPlayerController player)
    {
        if (!Plugin.Econ.Available) return;
        try
        {
            var cfg = Cfg.Skins;
            if (player.PlayerPawn?.Value?.WeaponServices?.MyWeapons is { } weapons)
                foreach (var handle in weapons)
                {
                    var w = handle.Value;
                    if (w is not { IsValid: true }) continue;
                    Plugin.Econ.ApplyWeapon(player, w, null, null, cfg);
                }
            if (cfg.EnableGloves) Plugin.Econ.ApplyGloves(player, null);
        }
        catch { }
    }

    private async Task LoadInto(CCSPlayerController player)
    {
        var lo = await LoadFromDbAsync(SteamId64(player));
        if (lo is not null && player.IsValid)
        {
            _cache[SteamId64(player)] = lo;
            ApplyTo(player);
        }
    }

    internal void OnClientConnectedFull(CCSPlayerController player)
    {
        var steam = SteamId64(player);
        _ = Task.Run(async () =>
        {
            var bound = await Plugin.Bind.CheckBoundAsync(steam);
            PlayerLoadout? lo = null;
            if (bound || !Cfg.Skins.RequireBoundAccount)
                lo = await LoadFromDbAsync(steam);

            Server.NextFrame(() =>
            {
                if (!player.IsValid) return;
                if (lo is not null) { lo.Bound = bound; _cache[steam] = lo; }
                if (bound) Tell(player, "skins_enabled", ("url", Cfg.PublicWebUrl));
                else if (Cfg.Skins.RequireBoundAccount)
                    Tell(player, "need_bind_hint", ("url", Cfg.PublicWebUrl));
                AddTimer(1.5f, () => { if (player.IsValid && player.PawnIsAlive) ApplyTo(player); });
            });
        });
    }

    /// <summary>菜单用:立即从数据库刷新我的外观(等价 /cs2skrefresh,菜单里不受冷却限制之外的约束)。</summary>
    internal void MenuRefresh(CCSPlayerController player)
    {
        if (!Cfg.Skins.Enabled) { Tell(player, "disabled"); return; }
        var steam = SteamId64(player);
        _refreshCooldown.Remove(steam);   // 菜单主动刷新不受命令冷却限制
        _ = Task.Run(async () =>
        {
            var bound = await Plugin.Bind.CheckBoundAsync(steam);
            var lo = (bound || !Cfg.Skins.RequireBoundAccount) ? await LoadFromDbAsync(steam) : null;
            Server.NextFrame(() =>
            {
                if (!player.IsValid) return;
                if (lo is not null) { lo.Bound = bound; _cache[steam] = lo; ApplyTo(player); Tell(player, "refresh_done", ("n", lo.Items.Count)); }
                else if (!bound && Cfg.Skins.RequireBoundAccount) Tell(player, "need_bind_hint", ("url", Cfg.PublicWebUrl));
                else Tell(player, "refresh_empty", ("url", Cfg.PublicWebUrl));
            });
        });
    }

    /// <summary>菜单用:当前绑定的 SteamID 是否已配置外观(用于菜单副标题)。</summary>
    internal string SkinSummary(CCSPlayerController player)
    {
        var steam = SteamId64(player);
        if (Cfg.Skins.RequireBoundAccount && !Plugin.Bind.IsBoundCached(steam)) return "未绑定网页账号";
        return _cache.TryGetValue(steam, out var lo) ? $"已配置 {lo.Items.Count} 件(含 {lo.Stickers.Count} 组贴纸)" : "尚未配置外观";
    }

    internal void Forget(CCSPlayerController player)
    {
        var steam = SteamId64(player);
        _cache.Remove(steam);
        _nagged.Remove(steam);
        _refreshCooldown.Remove(steam);
    }

    internal void OnPlayerSpawn(CCSPlayerController player)
    {
        if (!Cfg.Skins.Enabled || player.IsBot) return;
        AddTimer(0.12f, () =>
        {
            if (player.IsValid && player.PawnIsAlive) ApplyTo(player);
        });
    }

    internal void OnRoundPrestart()
    {
        if (!Cfg.Skins.Enabled) return;
        foreach (var p in Utilities.GetPlayers())
            if (p.IsValid && !p.IsBot && p.PawnIsAlive && p.Team is CsTeam.CounterTerrorist or CsTeam.Terrorist)
                ApplyTo(p);
    }

    private void ApplyTo(CCSPlayerController player)
    {
        var steam = SteamId64(player);
        if (!_cache.TryGetValue(steam, out var lo)) return;

        if (Cfg.Skins.RequireBoundAccount && !Plugin.Bind.IsBoundCached(steam))
        {
            if (!_nagged.TryGetValue(steam, out var t) || (DateTime.UtcNow - t).TotalMinutes > 5)
            {
                _nagged[steam] = DateTime.UtcNow;
                Tell(player, "need_bind_hint", ("url", Cfg.PublicWebUrl));
            }
            return;
        }

        var cfg = Cfg.Skins;
        if (!Plugin.Econ.Available) return;

        if (cfg.EnableGloves) Plugin.Econ.ApplyGloves(player, lo.Gloves);
        if (cfg.EnableMusicKits && lo.Music is { } mu) EconApplier.ApplyMusicKit(player, mu.KnifeTargetDefIndex);
        if (lo.Pin is { } pin) EconApplier.ApplyPin(player, pin.KnifeTargetDefIndex);
        if (cfg.EnableAgents && lo.Agent is { } ag)
        {
            var (ct, t) = ParseAgentModels(ag.Nametag);
            EconApplier.ApplyAgent(player, ct, t);
        }

        var weapons = player.PlayerPawn?.Value?.WeaponServices?.MyWeapons;
        if (weapons is null) return;
        foreach (var handle in weapons)
        {
            var weapon = handle.Value;
            if (weapon is not { IsValid: true }) continue;
            var defIndex = weapon.AttributeManager?.Item?.ItemDefinitionIndex ?? 0;
            var isKnife = (weapon.DesignerName ?? "").Contains("knife", StringComparison.OrdinalIgnoreCase)
                       || (weapon.DesignerName ?? "").Contains("bayonet", StringComparison.OrdinalIgnoreCase);
            var entry = lo.ForWeapon(defIndex, isKnife);
            var stickers = entry is not null ? StickersFor(lo, entry) : null;
            Plugin.Econ.ApplyWeapon(player, weapon, entry, stickers, cfg);
        }
    }

    private static IReadOnlyList<StickerAttachment>? StickersFor(PlayerLoadout lo, LoadoutItem entry)
        => lo.Stickers.TryGetValue(entry.WeaponDefIndex, out var s) && s.Count > 0 ? s : null;

    private static (string Ct, string T) ParseAgentModels(string? raw)
    {
        var s = (raw ?? "").Trim();
        var pipe = s.IndexOf('|');
        return pipe < 0 ? (s, s) : (s[..pipe].Trim(), s[(pipe + 1)..].Trim());
    }

    // ---------------- DB ----------------
    // 虚拟槽位: -1 手套, -2 探员(nametag=模型名), -3 音乐包, -4 徽章, 42=刀(目标刀在 knife_target_defindex)

    internal async Task<PlayerLoadout?> LoadFromDbAsync(string steamid64)
    {
        if (!Db.Ready) return null;
        try
        {
            var lo = new PlayerLoadout { LoadedAtUtc = DateTime.UtcNow, Bound = Plugin.Bind.IsBoundCached(steamid64) };

            string sql = Cfg.Skins.TeamSpecificLoadouts
                ? "SELECT weapon_defindex, knife_target_defindex, paintkit, paint_seed, paint_wear, nametag, stattrak, stattrak_count, keychain_id FROM cs2suite_loadout_teams WHERE steamid64=@s ORDER BY team ASC;"
                : "SELECT weapon_defindex, knife_target_defindex, paintkit, paint_seed, paint_wear, nametag, stattrak, stattrak_count, keychain_id FROM cs2suite_loadout_items WHERE steamid64=@s;";

            var rows = await Db.QueryAsync(sql, cmd => Db.P(cmd, "@s", steamid64));
            foreach (var row in rows)
            {
                var key = (int)Db.L(row, "weapon_defindex");
                lo.Items[key] = new LoadoutItem
                {
                    WeaponDefIndex = key,
                    KnifeTargetDefIndex = (int)Db.L(row, "knife_target_defindex"),
                    Paintkit = (int)Db.L(row, "paintkit"),
                    PaintSeed = (int)Db.L(row, "paint_seed"),
                    PaintWear = F(row, "paint_wear", 0.12f),
                    Nametag = Db.S(row, "nametag"),
                    StatTrak = Db.L(row, "stattrak") != 0,
                    StatTrakCount = (int)Db.L(row, "stattrak_count"),
                    KeychainId = (int)Db.L(row, "keychain_id"),
                };
            }

            if (Cfg.Skins.EnableStickers)
            {
                var srows = await Db.QueryAsync(
                    "SELECT weapon_defindex, slot, sticker_id, offset_x, offset_y, rotation, scale, wear FROM cs2suite_loadout_stickers WHERE steamid64=@s ORDER BY slot;",
                    cmd => Db.P(cmd, "@s", steamid64));
                foreach (var r in srows)
                {
                    var key = (int)Db.L(r, "weapon_defindex");
                    if (!lo.Stickers.TryGetValue(key, out var list)) lo.Stickers[key] = list = new();
                    list.Add(new StickerAttachment(
                        (int)Db.L(r, "slot"), (int)Db.L(r, "sticker_id"),
                        F(r, "offset_x"), F(r, "offset_y"), F(r, "rotation"), F(r, "scale", 1f), F(r, "wear", 0.12f)));
                }
            }
            return lo;
        }
        catch { return null; }
    }

    private static float F(Dictionary<string, object?> r, string key, float dflt = 0f)
    {
        if (!r.TryGetValue(key, out var v) || v is null or DBNull) return dflt;
        try { return Convert.ToSingle(v); } catch { return dflt; }
    }

    // ---------------- 命令 ----------------
    private void OnSkinsHelp(CCSPlayerController? player, CommandInfo info)
    {
        if (player is not { IsValid: true } || player.IsBot) { info.ReplyToCommand(Lang.T("cmd_console_only")); return; }
        var bound = Plugin.Bind.IsBoundCached(SteamId64(player));
        Tell(player, "sk_help_header");
        TellRaw(player, $"   {ChatColors.White}=> {ChatColors.Yellow}{Cfg.PublicWebUrl}");
        TellRaw(player, $"   {ChatColors.White}/cs2bind {ChatColors.Default}- {ChatColors.Grey}{Lang.T("sk_cmd_bind")}{ChatColors.Default}");
        TellRaw(player, $"   {ChatColors.White}/cs2skrefresh {ChatColors.Default}- {ChatColors.Grey}{Lang.T("sk_cmd_refresh")}{ChatColors.Default}");
        TellRaw(player, bound
            ? $"   {ChatColors.Green}OK {ChatColors.Default}{Lang.T("sk_help_bound", ("url", Cfg.PublicWebUrl))}"
            : $"   {ChatColors.Red}!! {ChatColors.Default}{Lang.T("sk_help_unbound")}");
    }

    private void OnRefresh(CCSPlayerController? player, CommandInfo info)
    {
        if (player is not { IsValid: true } || player.IsBot) { info.ReplyToCommand(Lang.T("cmd_console_only")); return; }
        var steam = SteamId64(player);
        var cd = Math.Max(5, Cfg.Skins.RefreshCooldownSeconds);
        if (_refreshCooldown.TryGetValue(steam, out var last) && (DateTime.UtcNow - last).TotalSeconds < cd)
        {
            Tell(player, "refresh_cooling", ("s", (int)Math.Ceiling(cd - (DateTime.UtcNow - last).TotalSeconds)));
            return;
        }
        _refreshCooldown[steam] = DateTime.UtcNow;
        _ = Task.Run(async () =>
        {
            var bound = await Plugin.Bind.CheckBoundAsync(steam);
            PlayerLoadout? lo = (bound || !Cfg.Skins.RequireBoundAccount) ? await LoadFromDbAsync(steam) : null;
            Server.NextFrame(() =>
            {
                if (!player.IsValid) return;
                if (lo is not null) { lo.Bound = bound; _cache[steam] = lo; Tell(player, "refresh_done", ("n", lo.Items.Count)); ApplyTo(player); }
                else if (!bound && Cfg.Skins.RequireBoundAccount) Tell(player, "need_bind_hint", ("url", Cfg.PublicWebUrl));
                else Tell(player, "refresh_empty", ("url", Cfg.PublicWebUrl));
            });
        });
    }
}
