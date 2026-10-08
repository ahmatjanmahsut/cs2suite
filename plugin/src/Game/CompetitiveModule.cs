using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using CS2Suite.Core;

namespace CS2Suite.Game;

/// 满十竞技 — 整合自 MatchZy 比赛流程精简版:
/// !queue 排队 → 满 10 人自动 ready 确认(Elo 蛇形分队)→ 刀局选边 →
/// MR12(先 13 胜)/加时 → 结算入库 + Elo → 回到等待。非参赛玩家自动旁观。
/// /queue /unqueue /ready /notready /match /choose /elo /matchcancel
public sealed class CompetitiveModule : ModuleBase
{
    private enum Phase { Idle, Ready, Knife, KnifePick, Live }
    private Phase _phase = Phase.Idle;

    private sealed class Queued
    {
        public string Steam = "";
        public CCSPlayerController? Controller;
        public bool Ready;
        public DateTime Since = DateTime.UtcNow;
        public int Elo;
    }

    private readonly List<Queued> _queue = new();
    private readonly Dictionary<string, int> _eloCache = new();
    private readonly Dictionary<string, (int K, int D, int A, int HS)> _liveStats = new();

    private Dictionary<string, int> _teamOf = new();
    private int _winTeamQueue = 1;
    private int _knifeTQueue = 1;
    private bool _team1IsT;


    /// <summary>★ 每回合 round_prestart 实测蓝队(队列1队)当前是否在 T 侧,
    /// 半场/加时的引擎换边全部自动兼容,不再依赖手工维护的取反标志。</summary>
    private bool _blueIsTNow;

    /// <summary>刀局/选边/正赛阶段视为"比赛进行中"(菜单据此禁用非外观项)。</summary>
    internal bool IsMatchInProgress => _phase is Phase.Knife or Phase.KnifePick or Phase.Live;

    /// <summary>供菜单显示的当前阶段文本。</summary>
    internal string PhaseText => _phase switch
    {
        Phase.Idle => "等待排队",
        Phase.Ready => "确认准备中",
        Phase.Knife => "刀局",
        Phase.KnifePick => "胜方选边",
        Phase.Live => $"正赛 {_score1}:{_score2}",
        _ => "-",
    };

    internal int QueueCount => _queue.Count;
    internal int ReadyCount => _queue.Count(q => q.Ready);

    internal void OnRoundPrestart()
    {
        if (_phase is not (Phase.Knife or Phase.KnifePick or Phase.Live)) return;
        int blue = 0, blueT = 0;
        foreach (var p in Utilities.GetPlayers())
        {
            if (p is not { IsValid: true } || p.IsBot) continue;
            if (!_teamOf.TryGetValue(p.SteamID.ToString(), out var q)) continue;
            if (q == 1) { blue++; if (p.TeamNum == 2) blueT++; }
        }
        if (blue > 0) _blueIsTNow = blueT * 2 >= blue;   // 多数决:蓝队过半在 T → 当前蓝队为 T 侧
    }

    /// <summary>刀局回合:强制只发刀(剥掉手枪/枪,与 MatchZy 同款做法)。</summary>
    internal void OnPlayerSpawn(CCSPlayerController player)
    {
        if (!IsActive || _phase != Phase.Knife || player.IsBot) return;
        AddTimer(0.15f, () =>
        {
            if (!player.IsValid || !player.PawnIsAlive) return;
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
        });
    }

    private int _score1, _score2, _roundsPlayed;
    private bool _halftimeDone;
    private uint _matchRowId;
    private DateTime _readyDeadline = DateTime.MinValue;
    private DateTime _pickDeadline = DateTime.MinValue;
    private DateTime _knifeStarted;
    private string _knifeWinner = "";

    public CompetitiveModule(CS2SuitePlugin p) : base(p) { }

    public override void Initialize()
    {
        Plugin.AddCommand("queue", "CS2Suite: join the 10-man queue", OnQueue);
        Plugin.AddCommand("unqueue", "CS2Suite: leave the queue", OnUnqueue);
        Plugin.AddCommand("ready", "CS2Suite: mark ready", (pl, info) => SetReady(pl, info, true));
        Plugin.AddCommand("notready", "CS2Suite: mark not ready", (pl, info) => SetReady(pl, info, false));
        Plugin.AddCommand("match", "CS2Suite: match/queue status", OnMatchInfo);
        Plugin.AddCommand("choose", "CS2Suite: knife winner choose /choose ct|t", OnChoose);
        Plugin.AddCommand("elo", "CS2Suite: my Elo rating", OnElo);
        Plugin.AddCommand("matchcancel", "CS2Suite: admin cancel current match", OnCancel);
        AddTimer(5f, Tick, TimerFlags.REPEAT);
    }

    protected override void OnActivated()
    {
        if (!Cfg.Competitive.Enabled) return;
        WarmEloCache();
        ApplyIdleConvars();
        Broadcast("comp_ready_hint");
    }
    protected override void OnReactivated() { if (Cfg.Competitive.Enabled) ApplyIdleConvars(); }
    protected override void OnDeactivated() { if (_phase != Phase.Idle) Cancel("mode-switch"); }

    private void ApplyIdleConvars()
    {
        Cvar("game_type", "0");
        Cvar("game_mode", "1");
        Cvar("mp_respawn_on_death_ct", "0");
        Cvar("mp_respawn_on_death_t", "0");
        Cvar("mp_ignore_round_win_conditions", "1");
        Cvar("mp_freezetime", "0");
        Cvar("mp_timelimit", "0");
        Cvar("mp_maxrounds", "0");
        Cvar("mp_warmup_pausetimer", "1");
        Cvar("mp_warmuptime", "9999");
        Cvar("mp_friendlyfire", "0");
        Cvar("mp_autoteambalance", "0");
        Cvar("mp_limitteams", "10");
        Cvar("mp_allowspectators", "1");
        Cvar("mp_spectators_max", "32");
        Cvar("mp_defaultteam", "0");
        Cvar("mp_disable_autokick", "1");
        Cvar("sv_cheats", "0");
    }

    private void ApplyMatchConvars()
    {
        Cvar("mp_ignore_round_win_conditions", "0");
        Cvar("mp_freezetime", "20");
        Cvar("mp_maxmoney", "16000");
        Cvar("mp_startmoney", "800");
        Cvar("mp_buytime", "15");
        Cvar("mp_buy_anywhere", "0");
        Cvar("mp_friendlyfire", "1");
        Cvar("mp_roundtime_defuse", (Cfg.Competitive.RoundTimeSeconds / 60f).ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
        // ★ 比赛结束判定完全由本插件自计数(平局也强制结算记平),不让引擎走 OT/换图
        Cvar("mp_maxrounds", "0");
        Cvar("mp_overtime_enable", "0");
        Cvar("mp_match_end_restart", "1");
        Cvar("mp_match_end_changelevel", "0");
        Cvar("mp_halftime", "1");
        Cvar("mp_halftime_rounds", Cfg.Competitive.HalftimeRounds.ToString());
        Cvar("sv_cheats", "0");
        Cvar("mp_warmup_pausetimer", "0");
        Cvar("mp_warmup_end", "");
    }

    private void ApplyKnifeConvars()
    {
        Cvar("mp_ignore_round_win_conditions", "0");
        Cvar("mp_freezetime", "10");
        Cvar("mp_maxrounds", "1");
        Cvar("mp_maxmoney", "0");
        Cvar("mp_startmoney", "0");
        Cvar("mp_buytime", "0");
        Cvar("mp_friendlyfire", "0");
        Cvar("mp_overtime_enable", "0");
        Cvar("mp_halftime", "0");
        Cvar("mp_roundtime", "1");
        Cvar("mp_timelimit", "0");
        Cvar("mp_match_end_restart", "1");
        Cvar("mp_match_end_changelevel", "0");
        Cvar("sv_cheats", "0");
        Cvar("mp_warmup_pausetimer", "0");
        Cvar("mp_warmup_end", "");
    }

    /// <summary>菜单用:加入/退出队列。</summary>
    internal void MenuToggleQueue(CCSPlayerController player)
    {
        var steam = SteamId64(player);
        if (_queue.Any(q => q.Steam == steam))
        {
            if (_phase == Phase.Live && _teamOf.ContainsKey(steam)) { Tell(player, "match_live_leave_ban"); return; }
            _queue.RemoveAll(q => q.Steam == steam);
            LogQ("leave", steam, player.PlayerName);
            Tell(player, "queue_left");
            if (_phase == Phase.Ready && _queue.Count(q => q.Ready) < Cfg.Competitive.PlayersRequired) Cancel("queue-shrank");
        }
        else
        {
            if (_phase is Phase.Knife or Phase.KnifePick or Phase.Live) { Tell(player, "match_live"); return; }
            int cap = Math.Max(Cfg.Competitive.PlayersRequired, Cfg.Competitive.PlayersRequired * 2);
            if (_queue.Count >= cap) { Tell(player, "queue_full"); return; }
            _queue.Add(new Queued { Steam = steam, Controller = player, Since = DateTime.UtcNow, Elo = GetElo(steam) });
            LogQ("join", steam, player.PlayerName);
            Tell(player, "queue_joined", ("n", _queue.Count), ("need", Cfg.Competitive.PlayersRequired));
            if (_phase == Phase.Idle && _queue.Count >= Cfg.Competitive.PlayersRequired && CountPlayingHumans() >= 2)
                StartReadyPhase();
        }
    }

    /// <summary>菜单用:准备确认。</summary>
    internal void MenuSetReady(CCSPlayerController player, bool ready)
    {
        if (_phase != Phase.Ready) { Tell(player, "no_ready_phase"); return; }
        var q = _queue.FirstOrDefault(x => x.Steam == SteamId64(player));
        if (q is null) { Tell(player, "queue_not_in"); return; }
        q.Ready = ready;
        LogQ(ready ? "ready" : "notready", q.Steam, player.PlayerName);
        Broadcast("ready_count", ("n", _queue.Count(x => x.Ready)), ("need", Cfg.Competitive.PlayersRequired));
        if (_queue.Count(x => x.Ready) >= Cfg.Competitive.PlayersRequired) StartMatch();
    }

    internal bool InQueue(CCSPlayerController player) => _queue.Any(q => q.Steam == SteamId64(player));

    /// <summary>菜单用:我的 Elo(直接返回缓存值,避免异步菜单渲染)。</summary>
    internal int MyElo(CCSPlayerController player) => GetElo(SteamId64(player));

    private static readonly Random Rng = new();

    private void Tick()
    {
        if (!IsActive || !Cfg.Competitive.Enabled) return;

        // 清理排队中的失效玩家(换图/断线后的旧句柄)
        if (_phase is Phase.Idle or Phase.Ready)
        {
            if (_queue.RemoveAll(q => q.Controller?.IsValid != true) > 0
                && _phase == Phase.Ready && _queue.Count < Cfg.Competitive.PlayersRequired)
                Cancel("queue-shrank");
        }

        switch (_phase)
        {
            case Phase.Idle:
                if (_queue.Count >= Cfg.Competitive.PlayersRequired && CountPlayingHumans() >= 2)
                    StartReadyPhase();
                break;
            case Phase.Ready:
                if (DateTime.UtcNow >= _readyDeadline)
                {
                    if (_queue.Count(q => q.Ready) >= Cfg.Competitive.PlayersRequired) StartMatch();
                    else Cancel("ready-timeout");
                }
                break;
            case Phase.Knife:
                if ((DateTime.UtcNow - _knifeStarted).TotalSeconds > 90)
                {
                    _winTeamQueue = Rng.Next(2) + 1;
                    _knifeWinner = FirstSteamOfQueue(_winTeamQueue);
                    _phase = Phase.KnifePick;
                    _pickDeadline = DateTime.UtcNow.AddSeconds(20);
                    Broadcast("knife_draw_pick");
                }
                break;
            case Phase.KnifePick:
                if (DateTime.UtcNow >= _pickDeadline)
                {
                    SetSides(winCt: true);
                    BeginLive();
                }
                break;
        }
    }

    // ---------------- 队列 ----------------
    private void OnQueue(CCSPlayerController? player, CommandInfo info)
    {
        if (player is not { IsValid: true } || player.IsBot) { info.ReplyToCommand(Lang.T("cmd_console_only")); return; }
        if (!IsActive) { Tell(player, "comp_not_active"); return; }
        if (_phase is Phase.Knife or Phase.KnifePick or Phase.Live) { Tell(player, "match_live"); return; }
        var steam = SteamId64(player);
        if (_queue.Any(q => q.Steam == steam)) { Tell(player, "queue_dup"); return; }
        int cap = Math.Max(Cfg.Competitive.PlayersRequired, Cfg.Competitive.PlayersRequired * 2);
        if (_queue.Count >= cap) { Tell(player, "queue_full"); return; }

        _queue.Add(new Queued { Steam = steam, Controller = player, Since = DateTime.UtcNow, Elo = GetElo(steam) });
        LogQ("join", steam, player.PlayerName);
        Tell(player, "queue_joined", ("n", _queue.Count), ("need", Cfg.Competitive.PlayersRequired));
        if (_phase == Phase.Ready) Tell(player, "queue_ready_hint");
        if (_phase == Phase.Idle && _queue.Count >= Cfg.Competitive.PlayersRequired && CountPlayingHumans() >= 2)
            StartReadyPhase();
    }

    private void OnUnqueue(CCSPlayerController? player, CommandInfo info)
    {
        if (player is not { IsValid: true }) { info.ReplyToCommand(Lang.T("cmd_console_only")); return; }
        var steam = SteamId64(player);
        if (_phase == Phase.Live && _teamOf.ContainsKey(steam)) { Tell(player, "match_live_leave_ban"); return; }
        if (_queue.RemoveAll(q => q.Steam == steam) == 0) { Tell(player, "queue_not_in"); return; }
        LogQ("leave", steam, player.PlayerName);
        Tell(player, "queue_left");
        if (_phase == Phase.Ready && _queue.Count(q => q.Ready) < Cfg.Competitive.PlayersRequired)
            Cancel("queue-shrank");
    }

    private void SetReady(CCSPlayerController? player, CommandInfo info, bool ready)
    {
        if (player is not { IsValid: true }) { info.ReplyToCommand(Lang.T("cmd_console_only")); return; }
        if (_phase != Phase.Ready) { Tell(player, "no_ready_phase"); return; }
        var q = _queue.FirstOrDefault(x => x.Steam == SteamId64(player));
        if (q is null) { Tell(player, "queue_not_in"); return; }
        q.Ready = ready;
        LogQ(ready ? "ready" : "notready", q.Steam, player.PlayerName);
        Broadcast("ready_count", ("n", _queue.Count(x => x.Ready)), ("need", Cfg.Competitive.PlayersRequired));
        if (_queue.Count(x => x.Ready) >= Cfg.Competitive.PlayersRequired) StartMatch();
    }

    // ---------------- 开赛 ----------------
    private void StartReadyPhase()
    {
        _phase = Phase.Ready;
        foreach (var q in _queue) q.Ready = false;
        int sec = Math.Clamp(Cfg.Competitive.ReadyTimeoutSeconds, 15, 300);
        _readyDeadline = DateTime.UtcNow.AddSeconds(sec);
        Broadcast("ready_phase_started", ("n", _queue.Count), ("sec", sec));
    }

    private void StartMatch()
    {
        var take = _queue.Where(q => q.Ready && q.Controller?.IsValid == true)
                         .OrderByDescending(q => q.Elo)
                         .Take(Cfg.Competitive.PlayersRequired)
                         .ToList();
        if (take.Count < Cfg.Competitive.PlayersRequired) { Cancel("insufficient"); return; }
        // 通知被挤出名额的排队玩家(只取前 N,其余留队等下一场)
        var takeSet = take.Select(q => q.Steam).ToHashSet();
        foreach (var q in _queue.Where(q => q.Ready && !takeSet.Contains(q.Steam)))
            if (q.Controller?.IsValid == true) Tell(q.Controller!, "queue_not_picked");
        _teamOf = BalanceByElo(take);
        _queue.Clear();
        _queue.AddRange(take);
        foreach (var q in take) LogQ("start", q.Steam, q.Controller?.IsValid == true ? q.Controller!.PlayerName : "");

        SpectateOthers();

        if (Cfg.Competitive.KnifeRound)
        {
            _phase = Phase.Knife;
            _knifeStarted = DateTime.UtcNow;
            _knifeTQueue = 1;
            foreach (var q in take)
            {
                var c = q.Controller;
                if (c is not { IsValid: true }) continue;
                c.ChangeTeam(_teamOf[q.Steam] == 1 ? CsTeam.Terrorist : CsTeam.CounterTerrorist);
            }
            ApplyKnifeConvars();
            Broadcast("knife_started");
            Server.ExecuteCommand("mp_restartgame 1");
        }
        else
        {
            _winTeamQueue = 1;
            SetSides(winCt: true);
            BeginLive();
        }
    }

    private static Dictionary<string, int> BalanceByElo(List<Queued> players)
    {
        var sorted = players.OrderByDescending(p => p.Elo).ToList();
        var map = new Dictionary<string, int>();
        var team = 1;
        for (int i = 0; i < sorted.Count; i++)
        {
            map[sorted[i].Steam] = team;
            if ((i + 1) % 2 == 0) team = team == 1 ? 2 : 1;
        }
        return map;
    }

    private int CountPlayingHumans()
    {
        int n = 0;
        foreach (var p in Utilities.GetPlayers())
            if (p.IsValid && !p.IsBot && p.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist) n++;
        return n;
    }

    private void SpectateOthers()
    {
        foreach (var p in Utilities.GetPlayers())
        {
            if (!p.IsValid || p.IsBot) continue;
            if (!_teamOf.ContainsKey(p.SteamID.ToString()) && p.Team != CsTeam.Spectator)
            {
                p.ChangeTeam(CsTeam.Spectator);
                Tell(p, "moved_to_spec");
            }
        }
    }

    // ---------------- 刀局 / 选边 ----------------
    internal void OnRoundEnd(EventRoundEnd ev)
    {
        if (!IsActive || !Cfg.Competitive.Enabled) return;

        if (_phase == Phase.Knife)
        {
            if (ev.Winner is 2 or 3)
            {
                _winTeamQueue = ev.Winner == 2 ? _knifeTQueue : (_knifeTQueue == 1 ? 2 : 1);
                _knifeWinner = FirstSteamOfQueue(_winTeamQueue);
                _phase = Phase.KnifePick;
                _pickDeadline = DateTime.UtcNow.AddSeconds(20);
                Broadcast("knife_won", ("team", QueueTeamName(_winTeamQueue)));
                foreach (var q in _queue.Where(q => _teamOf.GetValueOrDefault(q.Steam) == _winTeamQueue))
                    if (q.Controller?.IsValid == true) Tell(q.Controller!, "knife_choose_hint");
            }
            return;
        }

        if (_phase != Phase.Live) return;

        // 自计数比分:Winner 2=T 3=CT,阵营归属用"本轮实测"的 _blueIsTNow(半场/加时换边自适应)
        if (ev.Winner is 2 or 3)
        {
            var winnerQueue = _blueIsTNow ? (ev.Winner == 2 ? 1 : 2) : (ev.Winner == 2 ? 2 : 1);
            if (winnerQueue == 1) _score1++; else _score2++;
            _roundsPlayed++;
        }

        if (!_halftimeDone && _roundsPlayed >= Cfg.Competitive.HalftimeRounds)
        {
            _halftimeDone = true;
            Broadcast("halftime_score", ("a", QueueTeamName(1)), ("aScore", _score1), ("b", QueueTeamName(2)), ("bScore", _score2));
        }

        var need = Cfg.Competitive.MaxRounds / 2 + 1; // 先 13 胜(MR12)
        if (_score1 >= need || _score2 >= need) { FinishMatch(); return; }
        // 平局保护:打满 MaxRounds 且同分(OT 已禁用)→ 记平局结算,防死锁
        if (_roundsPlayed >= Cfg.Competitive.MaxRounds && _score1 == _score2) FinishMatch();
    }

    private string FirstSteamOfQueue(int tq)
    {
        foreach (var kv in _teamOf) if (kv.Value == tq) return kv.Key;
        return "";
    }

    private void OnChoose(CCSPlayerController? player, CommandInfo info)
    {
        if (player is not { IsValid: true }) { info.ReplyToCommand(Lang.T("cmd_console_only")); return; }
        if (_phase != Phase.KnifePick) { Tell(player, "no_choose_phase"); return; }
        if (_teamOf.GetValueOrDefault(SteamId64(player)) != _winTeamQueue && !CS2SuitePlugin.IsPlayerAdmin(player))
        {
            Tell(player, "choose_not_winner");
            return;
        }
        var pick = (info.ArgCount > 1 ? info.ArgByIndex(1) : "ct").ToLowerInvariant();
        SetSides(winCt: !pick.StartsWith("t"));
        BeginLive();
    }

    /// 决定正赛初始边:winCt=胜方打 CT。_team1IsT 记录蓝队(队列1队)是否在 T 侧。
    private void SetSides(bool winCt)
    {
        var wq = _winTeamQueue;
        var lq = wq == 1 ? 2 : 1;
        var winTeamNum = winCt ? CsTeam.CounterTerrorist : CsTeam.Terrorist;
        var loseTeamNum = winCt ? CsTeam.Terrorist : CsTeam.CounterTerrorist;

        foreach (var q in _queue)
        {
            var c = q.Controller;
            if (c is not { IsValid: true }) continue;
            var my = _teamOf.GetValueOrDefault(q.Steam, 1);
            c.ChangeTeam(my == wq ? winTeamNum : loseTeamNum);
        }

        var tQueue = winCt ? lq : wq;   // 谁在 T
        _team1IsT = tQueue == 1;

        _halftimeDone = false;
    }

    private void BeginLive()
    {
        _phase = Phase.Live;
        _score1 = _score2 = _roundsPlayed = 0;
        _liveStats.Clear();
        foreach (var steam in _teamOf.Keys) _liveStats[steam] = (0, 0, 0, 0);

        OpenMatchRow();
        ApplyMatchConvars();
        if (Cfg.Competitive.RecordDemo)
            Cvar("tv_record", "cs2suite_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + "_" + Server.MapName);
        Broadcast("match_live", ("a", QueueTeamName(1)), ("b", QueueTeamName(2)));
        Server.ExecuteCommand("mp_restartgame 1");
    }

    private void OpenMatchRow()
    {
        _matchRowId = 0;
        if (!Db.Ready) return;
        var map = Server.MapName; // 游戏线程快照
        _ = Task.Run(async () =>
        {
            _matchRowId = (uint)await Db.InsertReturnIdAsync(
                "INSERT INTO cs2suite_matches (map_name, status) VALUES (@m,'live');",
                cmd => Db.P(cmd, "@m", map));
        });
    }

    // ---------------- 比赛统计 ----------------
    internal void OnPlayerDeath(CCSPlayerController victim, EventPlayerDeath ev)
    {
        if (!IsActive || _phase != Phase.Live) return;
        var vS = SteamId64(victim);
        if (_liveStats.TryGetValue(vS, out var v)) _liveStats[vS] = (v.K, v.D + 1, v.A, v.HS);
        var att = ev.Attacker;
        if (att is not { IsValid: true } || att.SteamID == victim.SteamID) return;
        var aS = att.SteamID.ToString();
        if (_liveStats.TryGetValue(aS, out var a))
            _liveStats[aS] = (a.K + 1, a.D, a.A, a.HS + (ev.Headshot ? 1 : 0));
    }

    internal void OnPlayerLeft(CCSPlayerController player)
    {
        var steam = SteamId64(player);
        _queue.RemoveAll(q => q.Steam == steam);
        if (_phase is Phase.Idle or Phase.Ready or Phase.Knife or Phase.KnifePick && _queue.Count < Cfg.Competitive.PlayersRequired)
            Cancel("player-left");
        else if (_phase == Phase.Live)
            Broadcast("match_player_left", ("name", Safe(player.PlayerName, 32)));
    }

    // ---------------- 结算 ----------------
    private void FinishMatch()
    {
        var w = _score1 == _score2 ? 0 : (_score1 > _score2 ? 1 : 2);
        Broadcast("match_finished", ("a", QueueTeamName(1)), ("aScore", _score1), ("b", QueueTeamName(2)), ("bScore", _score2));

        var deltas = new Dictionary<string, int>();
        if (w != 0)
        {
            int k = Cfg.Competitive.EloKFactor;
            var winners = _teamOf.Where(kv => kv.Value == w).Select(kv => kv.Key).ToList();
            var losers = _teamOf.Where(kv => kv.Value != w).Select(kv => kv.Key).ToList();
            if (winners.Count > 0 && losers.Count > 0)
            {
                var wAvg = winners.Sum(GetElo) / winners.Count;
                var lAvg = losers.Sum(GetElo) / losers.Count;
                var ew = 1.0 / (1.0 + Math.Pow(10, (lAvg - wAvg) / 400.0));
                foreach (var s in winners) deltas[s] = (int)Math.Round(k * (1 - ew));
                foreach (var s in losers) deltas[s] = -(int)Math.Round(k * ew);
            }
        }

        if (Db.Ready && _matchRowId != 0)
        {
            var matchId = _matchRowId;
            var s1 = _score1; var s2 = _score2; var knife = _knifeWinner;
            var stats = new Dictionary<string, (int, int, int, int)>(_liveStats);
            var teamOf = new Dictionary<string, int>(_teamOf);
            var dlt = new Dictionary<string, int>(deltas);
            _matchRowId = 0;
            // ★ 名字/阵营必须在游戏线程快照(Task 里再遍历 GetPlayers 属跨线程违规)
            var snapNames = new Dictionary<string, string>();
            var snapTeam = new Dictionary<string, int>();
            foreach (var p in Utilities.GetPlayers())
            {
                if (p is not { IsValid: true } || p.IsBot) continue;
                var st = p.SteamID.ToString();
                snapNames[st] = p.PlayerName;
                snapTeam[st] = p.TeamNum;
            }
            _ = Task.Run(async () =>
            {
                await Db.ExecuteAsync(
                    "UPDATE cs2suite_matches SET score1=@s1, score2=@s2, ended_at=NOW(), status='finished', knife_winner=@kw WHERE match_id=@id;",
                    cmd => { Db.P(cmd, "@s1", s1); Db.P(cmd, "@s2", s2); Db.P(cmd, "@kw", knife); Db.P(cmd, "@id", matchId); });
                foreach (var kv in stats)
                {
                    var d = dlt.GetValueOrDefault(kv.Key);
                    var win = d > 0;
                    await Db.ExecuteAsync(
                        "INSERT INTO cs2suite_match_players (match_id,steamid64,persona,team,slot_team,kills,deaths,assists,headshots,elo_delta) " +
                        "VALUES (@m,@s,@p,@t,@st,@k,@d,@a,@h,@e) ON DUPLICATE KEY UPDATE kills=@k,deaths=@d,assists=@a,headshots=@h,elo_delta=@e,team=@t;",
                        cmd =>
                        {
                            Db.P(cmd, "@m", matchId); Db.P(cmd, "@s", kv.Key); Db.P(cmd, "@p", snapNames.GetValueOrDefault(kv.Key, ""));
                            Db.P(cmd, "@t", snapTeam.GetValueOrDefault(kv.Key, 0)); Db.P(cmd, "@st", teamOf.GetValueOrDefault(kv.Key, 1));
                            Db.P(cmd, "@k", kv.Value.Item1); Db.P(cmd, "@d", kv.Value.Item2);
                            Db.P(cmd, "@a", kv.Value.Item3); Db.P(cmd, "@h", kv.Value.Item4);
                            Db.P(cmd, "@e", d);
                        });
                    if (d != 0) await WriteEloAsync(kv.Key, d, win);
                }
                if (s1 == s2)
                    foreach (var steam in stats.Keys)
                        await Db.ExecuteAsync(
                            "INSERT INTO cs2suite_elo (steamid64) VALUES (@s) ON DUPLICATE KEY UPDATE draws=draws+1;",
                            cmd => Db.P(cmd, "@s", steam));
            });
        }

        ResetToIdle();
    }

    private void ResetToIdle()
    {
        _phase = Phase.Idle;
        _queue.Clear();
        _teamOf.Clear();
        _liveStats.Clear();
        _knifeWinner = "";
        _score1 = _score2 = _roundsPlayed = 0;
        _matchRowId = 0;
        if (Cfg.Competitive.RecordDemo) Cvar("tv_stoprecord", "");
        foreach (var p in Utilities.GetPlayers())
            if (p.IsValid && !p.IsBot && p.Team != CsTeam.Spectator)
                p.ChangeTeam(CsTeam.Spectator);
        ApplyIdleConvars();
        Server.ExecuteCommand("mp_restartgame 1");
    }

    private void Cancel(string reason)
    {
        if (_phase == Phase.Idle) return;
        Broadcast("match_cancelled", ("why", reason));
        if (Db.Ready && _matchRowId != 0)
        {
            var id = _matchRowId;
            _ = Task.Run(() => Db.ExecuteAsync("UPDATE cs2suite_matches SET status='cancelled', ended_at=NOW() WHERE match_id=@id;",
                cmd => Db.P(cmd, "@id", id)));
        }
        ResetToIdle();
    }

    private void OnCancel(CCSPlayerController? player, CommandInfo info)
    {
        if (player is not { IsValid: true }) { info.ReplyToCommand(Lang.T("cmd_console_only")); return; }
        if (!CS2SuitePlugin.IsPlayerAdmin(player)) { Tell(player, "need_admin"); return; }
        if (_phase == Phase.Idle) { Tell(player, "no_active_match"); return; }
        Cancel("admin");
    }

    // ---------------- Elo / 查询 ----------------
    private int GetElo(string steam) => _eloCache.GetValueOrDefault(steam, Cfg.Competitive.EloStartingValue);

    private async Task WriteEloAsync(string steam, int delta, bool win)
    {
        var now = Math.Max(0, GetElo(steam) + delta);
        _eloCache[steam] = now;
        await Db.ExecuteAsync(
            "INSERT INTO cs2suite_elo (steamid64, elo, wins, losses) VALUES (@s,@e,@w,@l) " +
            "ON DUPLICATE KEY UPDATE elo=@e, wins=wins+@w, losses=losses+@l;",
            cmd => { Db.P(cmd, "@s", steam); Db.P(cmd, "@e", now); Db.P(cmd, "@w", win ? 1 : 0); Db.P(cmd, "@l", win ? 0 : 1); });
    }

    internal void WarmEloCache()
    {
        if (!Db.Ready) return;
        _ = Task.Run(async () =>
        {
            var rows = await Db.QueryAsync("SELECT steamid64, elo FROM cs2suite_elo;");
            foreach (var r in rows)
            {
                var s = Db.S(r, "steamid64");
                if (!string.IsNullOrEmpty(s)) _eloCache[s] = (int)Db.L(r, "elo");
            }
        });
    }

    private void OnMatchInfo(CCSPlayerController? player, CommandInfo info)
    {
        if (player is not { IsValid: true }) { info.ReplyToCommand(Lang.T("cmd_console_only")); return; }
        TellRaw(player, ChatColors.Green + "  CS2Suite | " + Lang.T("phase_" + _phase.ToString().ToLowerInvariant()));
        TellRaw(player, ChatColors.Grey + "  Queue: " + _queue.Count + "/" + Cfg.Competitive.PlayersRequired + "  Ready: " + _queue.Count(q => q.Ready) + ChatColors.Default);
        if (_phase == Phase.Live)
            TellRaw(player, ChatColors.Yellow + "  " + QueueTeamName(1) + " " + _score1 + " : " + _score2 + " " + QueueTeamName(2) + ChatColors.Default);
    }

    private void OnElo(CCSPlayerController? player, CommandInfo info)
    {
        if (player is not { IsValid: true } || player.IsBot) { info.ReplyToCommand(Lang.T("cmd_console_only")); return; }
        var steam = SteamId64(player);
        if (!Db.Ready) { TellRaw(player, ChatColors.Grey + "  Elo " + GetElo(steam) + " (" + Lang.T("db_down") + ")" + ChatColors.Default); return; }
        _ = Task.Run(async () =>
        {
            var rows = await Db.QueryAsync("SELECT elo,wins,losses,draws FROM cs2suite_elo WHERE steamid64=@s;", cmd => Db.P(cmd, "@s", steam));
            Server.NextFrame(() =>
            {
                if (!player.IsValid) return;
                if (rows.Count == 0) Tell(player, "elo_default", ("e", Cfg.Competitive.EloStartingValue));
                else
                {
                    var r = rows[0];
                    TellRaw(player, ChatColors.Green + "  Elo " + Db.L(r, "elo") + " | W" + Db.L(r, "wins") + " L" + Db.L(r, "losses") + " D" + Db.L(r, "draws") + ChatColors.Default);
                }
            });
        });
    }

    private static string QueueTeamName(int q) => q == 1 ? "Blue" : "Red";

    private void LogQ(string ev, string steam, string? name)
    {
        if (!Db.Ready) return;
        var map = Server.MapName;
        _ = Task.Run(() => Db.ExecuteAsync(
            "INSERT INTO cs2suite_queue_log (event, steamid64, persona, map_name) VALUES (@e,@s,@p,@m);",
            cmd => { Db.P(cmd, "@e", ev); Db.P(cmd, "@s", steam); Db.P(cmd, "@p", name ?? ""); Db.P(cmd, "@m", map); }));
    }
}
