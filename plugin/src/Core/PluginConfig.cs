using CounterStrikeSharp.API.Core;
using System.Text.Json.Serialization;

namespace CS2Suite.Core;

/// <summary>config/plugins/CS2Suite/CS2Suite.json — 用户在此文件提供 MySQL 等全部配置。</summary>
public sealed class CS2SuiteConfig : BasePluginConfig
{
    [JsonPropertyName("ServerId")] public string ServerId { get; set; } = "cs2suite-1";
    [JsonPropertyName("Language")] public string Language { get; set; } = "zh";
    [JsonPropertyName("PublicWebUrl")] public string PublicWebUrl { get; set; } = "http://127.0.0.1:8788";
    [JsonPropertyName("ChatPrefix")] public string ChatPrefix { get; set; } = "[CS2Suite]";

    [JsonPropertyName("Database")] public DatabaseConfig Database { get; set; } = new();
    [JsonPropertyName("Modes")] public ModesConfig Modes { get; set; } = new();
    [JsonPropertyName("Practice")] public PracticeConfig Practice { get; set; } = new();
    [JsonPropertyName("Deathmatch")] public DeathmatchConfig Deathmatch { get; set; } = new();
    [JsonPropertyName("Competitive")] public CompetitiveConfig Competitive { get; set; } = new();
    [JsonPropertyName("Skins")] public SkinsConfig Skins { get; set; } = new();
}

public sealed class DatabaseConfig
{
    [JsonPropertyName("Host")] public string Host { get; set; } = "127.0.0.1";
    [JsonPropertyName("Port")] public int Port { get; set; } = 3306;
    [JsonPropertyName("User")] public string User { get; set; } = "cs2suite";
    [JsonPropertyName("Password")] public string Password { get; set; } = "";
    [JsonPropertyName("Database")] public string Database { get; set; } = "cs2suite";
    [JsonPropertyName("AutoCreateTables")] public bool AutoCreateTables { get; set; } = true;
    /// <summary>绑定码有效分钟数(/cs2bind 生成)。</summary>
    [JsonPropertyName("BindCodeTtlMinutes")] public int BindCodeTtlMinutes { get; set; } = 15;
}

public sealed class ModesConfig
{
    /// <summary>服务器启动默认玩法:none | practice | deathmatch | competitive</summary>
    [JsonPropertyName("DefaultMode")] public string DefaultMode { get; set; } = "deathmatch";
    /// <summary>拥有 @css/kick 及以上权限者可用 /cs2mode 切换玩法。</summary>
    [JsonPropertyName("AllowAdminModeSwitch")] public bool AllowAdminModeSwitch { get; set; } = true;
    /// <summary>玩家可用 /practice 或 /dm 自助切换(冷却)。</summary>
    [JsonPropertyName("AllowPlayerModeVote")] public bool AllowPlayerModeVote { get; set; } = false;
}

public sealed class PracticeConfig
{
    [JsonPropertyName("Enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("InfiniteAmmo")] public bool InfiniteAmmo { get; set; } = true;
    [JsonPropertyName("NoFreezetime")] public bool NoFreezetime { get; set; } = true;
    [JsonPropertyName("InstantRespawn")] public bool InstantRespawn { get; set; } = true;
    [JsonPropertyName("EnableGod")] public bool EnableGod { get; set; } = true;
    [JsonPropertyName("SaveTeleportPositions")] public bool SaveTeleportPositions { get; set; } = true;
    [JsonPropertyName("MaxPositionsPerMap")] public int MaxPositionsPerMap { get; set; } = 50;
}

public sealed class DeathmatchConfig
{
    [JsonPropertyName("Enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("SpawnProtectionSeconds")] public float SpawnProtectionSeconds { get; set; } = 3f;
    [JsonPropertyName("RespawnDelaySeconds")] public float RespawnDelaySeconds { get; set; } = 0f;
    [JsonPropertyName("PointsKill")] public int PointsKill { get; set; } = 1;
    [JsonPropertyName("PointsHeadshotBonus")] public int PointsHeadshotBonus { get; set; } = 1;
    [JsonPropertyName("KillstreakAnnounceAt")] public int[] KillstreakAnnounceAt { get; set; } = [5, 10, 15, 20];
    [JsonPropertyName("AllowWeaponSelection")] public bool AllowWeaponSelection { get; set; } = true;
    [JsonPropertyName("RefillAmmoOnRound")] public bool RefillAmmoOnRound { get; set; } = true;
    [JsonPropertyName("GiveHelmet")] public bool GiveHelmet { get; set; } = true;
    [JsonPropertyName("LeaderboardRows")] public int LeaderboardRows { get; set; } = 10;
}

public sealed class CompetitiveConfig
{
    [JsonPropertyName("Enabled")] public bool Enabled { get; set; } = true;
    /// <summary>"满十" — 队列达到该人数自动开排。</summary>
    [JsonPropertyName("PlayersRequired")] public int PlayersRequired { get; set; } = 10;
    [JsonPropertyName("ReadyTimeoutSeconds")] public int ReadyTimeoutSeconds { get; set; } = 60;
    [JsonPropertyName("KnifeRound")] public bool KnifeRound { get; set; } = true;
    [JsonPropertyName("MaxRounds")] public int MaxRounds { get; set; } = 24;
    [JsonPropertyName("HalftimeRounds")] public int HalftimeRounds { get; set; } = 12;
    [JsonPropertyName("RoundTimeSeconds")] public int RoundTimeSeconds { get; set; } = 115;
    [JsonPropertyName("OvertimeMaxRounds")] public int OvertimeMaxRounds { get; set; } = 6;
    [JsonPropertyName("EloKFactor")] public int EloKFactor { get; set; } = 32;
    [JsonPropertyName("EloStartingValue")] public int EloStartingValue { get; set; } = 1200;
    [JsonPropertyName("MapPool")] public string[] MapPool { get; set; } = ["de_dust2", "de_mirage", "de_inferno", "de_nuke", "de_overpass", "de_ancient", "de_anubis", "de_vertigo"];
    [JsonPropertyName("RecordDemo")] public bool RecordDemo { get; set; } = false;
}

public sealed class SkinsConfig
{
    [JsonPropertyName("Enabled")] public bool Enabled { get; set; } = true;
    /// <summary>必须先在 Web 端注册账号并用 /cs2bind 绑定后才能使用自定义外观(需求核心开关,勿随意关闭)。</summary>
    [JsonPropertyName("RequireBoundAccount")] public bool RequireBoundAccount { get; set; } = true;
    [JsonPropertyName("RefreshCooldownSeconds")] public int RefreshCooldownSeconds { get; set; } = 30;
    /// <summary>Web 端保存外观后,游戏内自动同步的轮询秒数(cs2suite_events 事件队列)。</summary>
    [JsonPropertyName("SyncSeconds")] public int SyncSeconds { get; set; } = 2;
    [JsonPropertyName("EnableStickers")] public bool EnableStickers { get; set; } = true;
    [JsonPropertyName("MaxStickersPerWeapon")] public int MaxStickersPerWeapon { get; set; } = 5;
    [JsonPropertyName("EnableKeychains")] public bool EnableKeychains { get; set; } = true;
    [JsonPropertyName("EnableNametags")] public bool EnableNametags { get; set; } = true;
    [JsonPropertyName("EnableStatTrak")] public bool EnableStatTrak { get; set; } = true;
    [JsonPropertyName("EnableGloves")] public bool EnableGloves { get; set; } = true;
    [JsonPropertyName("EnableAgents")] public bool EnableAgents { get; set; } = true;
    [JsonPropertyName("EnableMusicKits")] public bool EnableMusicKits { get; set; } = true;
    [JsonPropertyName("TeamSpecificLoadouts")] public bool TeamSpecificLoadouts { get; set; } = false;
}
