using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace CS2Suite.Core;

/// <summary>所有子模块的公共基类:日志、DB、本地化、聊天输出、权限。</summary>
public abstract class ModuleBase
{
    protected readonly CS2SuitePlugin Plugin;
    protected Db Db => Plugin.Db;
    protected CS2SuiteConfig Cfg => Plugin.Config;
    protected Lang Lang => Plugin.L;

    public bool IsActive { get; private set; }

    protected ModuleBase(CS2SuitePlugin plugin) { Plugin = plugin; }

    public virtual void Initialize() { }
    public virtual void Unload() { }

    public void SetActive(bool active)
    {
        if (active == IsActive) { if (active) OnReactivated(); return; }
        IsActive = active;
        if (active) OnActivated(); else OnDeactivated();
    }

    protected virtual void OnActivated() { }
    protected virtual void OnDeactivated() { }
    protected virtual void OnReactivated() { }

    // ---------- 输出辅助 ----------
    protected void Tell(CCSPlayerController player, string key, params (string, object)[] vars) =>
        player.PrintToChat(" " + ChatColors.Green + Prefix() + " " + Lang.T(key, vars));

    protected void TellRaw(CCSPlayerController player, string text) =>
        player.PrintToChat(" " + text);

    protected void Broadcast(string key, params (string, object)[] vars) =>
        Server.PrintToChatAll(" " + ChatColors.Green + Prefix() + " " + Lang.T(key, vars));

    protected static string Prefix() => ChatColors.Green.ToString();

    /// <summary>清洗玩家输入文本(长度+控制符)。</summary>
    protected static string Safe(string? input, int maxLen = 32)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        var s = new string(input.Where(ch => ch >= ' ' && ch != '').ToArray());
        return s.Length <= maxLen ? s.Trim() : s[..maxLen];
    }

    protected static string SteamId64(CCSPlayerController p) => p.SteamID.ToString();

    protected bool IsAdmin(CCSPlayerController? player)
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

    /// <summary>转发到插件的计时器(BasePlugin 提供),模块内可直接使用。</summary>
    protected Timer AddTimer(float delay, Action callback, CounterStrikeSharp.API.Modules.Timers.TimerFlags? flags = null)
        => Plugin.AddTimer(delay, callback, flags);

    /// <summary>设置服务器 convar(控制台命令方式;mp_* 等非 cheat 保护变量可稳定生效)。</summary>
    protected static void Cvar(string name, string value) => Server.ExecuteCommand($"{name} {value}");
}
