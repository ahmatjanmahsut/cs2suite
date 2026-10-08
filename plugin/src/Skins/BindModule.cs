using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CS2Suite.Core;
using System.Text;

namespace CS2Suite.Skins;

/// 绑定码模块 — 需求核心流程:
///   1. 用户在 Web 端注册账号;
///   2. 服务器内 /cs2bind 生成 6 位一次性绑定码(cs2suite_bind_codes,限时);
///   3. Web 端登录输入绑定码 → SteamID 与账号关联;
///   4. 插件 30s 轮询刷新绑定状态;已绑定才允许应用自定义外观(门控在 SkinsModule)。
public sealed class BindModule : ModuleBase
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private static readonly Random Rng = new();

    internal readonly Dictionary<string, (bool Bound, DateTime At)> BindCache = new();

    public BindModule(CS2SuitePlugin p) : base(p) { }

    public override void Initialize()
    {
        Plugin.AddCommand("cs2bind", "CS2Suite: print a bind code for the web account", OnBind);
        Plugin.AddCommand("cs2whoami", "CS2Suite: show SteamID & bind status", OnWhoami);
        Plugin.AddCommand("cs2unbind", "CS2Suite: unbind this SteamID (requires confirm)", OnUnbind);
        Plugin.AddCommand("cs2unbind_confirm", "CS2Suite: confirm unbind", (pl, info) =>
        {
            if (pl is { IsValid: true }) ConfirmUnbind(pl);
        });
    }

    private void OnBind(CCSPlayerController? player, CommandInfo info)
    {
        if (player is not { IsValid: true } || player.IsBot)
        {
            info.ReplyToCommand(Lang.T("cmd_console_only"));
            return;
        }
        if (!Db.Ready) { Tell(player, "db_down"); return; }

        var steam = SteamId64(player);
        if (IsBoundCached(steam))
        {
            Tell(player, "bind_already", ("url", Cfg.PublicWebUrl));
            return;
        }

        // ★ 游戏线程内先取名字快照(实体句柄不允许跨线程访问)
        var name = Safe(player.PlayerName, 64);
        _ = Task.Run(async () =>
        {
            string? code = null;
            for (var attempt = 0; attempt < 8 && code is null; attempt++)
            {
                var candidate = RandomCode();
                var clash = await Db.QueryAsync(
                    "SELECT used_by_account FROM cs2suite_bind_codes WHERE code=@c AND used_by_account IS NULL LIMIT 1;",
                    cmd => Db.P(cmd, "@c", candidate));
                if (clash.Count == 0) code = candidate;
            }
            if (code is null) return;

            var ttl = Math.Clamp(Cfg.Database.BindCodeTtlMinutes, 1, 240);
            await Db.ExecuteAsync(
                "INSERT INTO cs2suite_bind_codes (code, steamid64, persona, expires_at) VALUES (@c,@s,@p, DATE_ADD(NOW(), INTERVAL @m MINUTE)) " +
                "ON DUPLICATE KEY UPDATE steamid64=@s, persona=@p, issued_at=NOW(), expires_at=DATE_ADD(NOW(), INTERVAL @m MINUTE), used_by_account=NULL, used_at=NULL;",
                cmd =>
                {
                    Db.P(cmd, "@c", code!);
                    Db.P(cmd, "@s", steam);
                    Db.P(cmd, "@p", name);
                    Db.P(cmd, "@m", ttl);
                });

            Server.NextFrame(() =>
            {
                if (player.IsValid)
                    Tell(player, "bind_issued",
                        ("code", ChatColors.Yellow + code + ChatColors.Default),
                        ("url", Cfg.PublicWebUrl),
                        ("min", ttl));
            });
        });
    }

    private void OnWhoami(CCSPlayerController? player, CommandInfo info)
    {
        if (player is not { IsValid: true } || player.IsBot) { info.ReplyToCommand(Lang.T("cmd_console_only")); return; }
        var steam = SteamId64(player);
        Tell(player, IsBoundCached(steam) ? "whoami_bound" : "whoami_unbound",
            ("steamid", ChatColors.Yellow + steam + ChatColors.Default));
    }

    private void OnUnbind(CCSPlayerController? player, CommandInfo info)
    {
        if (player is not { IsValid: true } || player.IsBot) { info.ReplyToCommand(Lang.T("cmd_console_only")); return; }
        if (!Db.Ready) { Tell(player, "db_down"); return; }
        var steam = SteamId64(player);
        if (!IsBoundCached(steam)) { Tell(player, "unbind_none"); return; }
        PendingUnbind.Add(steam);
        AddTimer(60f, () => PendingUnbind.Remove(steam));
        Tell(player, "unbind_confirm_hint");
    }

    internal void ConfirmUnbind(CCSPlayerController player)
    {
        var steam = SteamId64(player);
        if (!PendingUnbind.Remove(steam)) return;
        _ = Task.Run(async () =>
        {
            await Db.ExecuteAsync("DELETE FROM cs2suite_account_steam WHERE steamid64=@s;", cmd => Db.P(cmd, "@s", steam));
            Invalidate(steam);
            Server.NextFrame(() => { if (player.IsValid) Tell(player, "unbind_done"); });
        });
    }

    private readonly HashSet<string> PendingUnbind = new();

    private static string RandomCode()
    {
        var sb = new StringBuilder(6);
        lock (Rng)
            for (var i = 0; i < 6; i++)
                sb.Append(Alphabet[Rng.Next(Alphabet.Length)]);
        return sb.ToString();
    }

    /// 纯缓存读取 — 缓存由 SkinsModule 每 30s 异步批量刷新,游戏帧内永不查库。
    internal bool IsBoundCached(string steam)
        => BindCache.TryGetValue(steam, out var hit) && (DateTime.UtcNow - hit.At).TotalMinutes < 5 && hit.Bound;

    public async Task<bool> CheckBoundAsync(string steamid64)
    {
        if (!Db.Ready) return false;
        var rows = await Db.QueryAsync(
            "SELECT account_id FROM cs2suite_account_steam WHERE steamid64=@s LIMIT 1;",
            cmd => Db.P(cmd, "@s", steamid64));
        var bound = rows.Count > 0;
        BindCache[steamid64] = (bound, DateTime.UtcNow);
        return bound;
    }

    /// 批量刷新在线玩家绑定状态(异步)。返回由未绑定变为绑定的集合。
    public async Task<HashSet<string>> RefreshCacheAsync(IEnumerable<CCSPlayerController> players)
    {
        var newly = new HashSet<string>();
        if (!Db.Ready) return newly;
        var ids = players.Where(p => p.IsValid && !p.IsBot).Select(SteamId64).Distinct().ToList();
        if (ids.Count == 0) return newly;
        var inList = string.Join(",", ids.Select(i => "'" + i.Replace("'", "") + "'"));
        var rows = await Db.QueryAsync(
            $"SELECT steamid64 FROM cs2suite_account_steam WHERE steamid64 IN ({inList});");
        var boundSet = rows.Select(r => Db.S(r, "steamid64") ?? "").ToHashSet();
        foreach (var id in ids)
        {
            var was = BindCache.TryGetValue(id, out var old) && old.Bound;
            var now = boundSet.Contains(id);
            BindCache[id] = (now, DateTime.UtcNow);
            if (!was && now) newly.Add(id);
        }
        return newly;
    }

    public void Invalidate(string steamid64) => BindCache.Remove(steamid64);
}
