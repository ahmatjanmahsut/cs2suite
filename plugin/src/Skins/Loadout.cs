namespace CS2Suite.Skins;

/// <summary>虚拟槽位(存于 cs2suite_loadout_items.weapon_defindex,负数避免与真实 defindex 冲突)。</summary>
public static class Slots
{
    public const int Knife = 42;      // weapon_knife 基准行:KnifeTargetDefIndex = 目标刀型
    public const int Gloves = -1;
    public const int Agent = -2;      // Nametag = CT 模型名;扩展字段 AgentTModel
    public const int Music = -3;      // KnifeTargetDefIndex = music kit id
    public const int Pin = -4;        // KnifeTargetDefIndex = medal rank 1..16
}

/// <summary>一件物品的外观条目(行模型,直接映射数据库)。</summary>
public sealed class LoadoutItem
{
    public int WeaponDefIndex { get; set; }
    public int KnifeTargetDefIndex { get; set; }
    public int Paintkit { get; set; }
    public int PaintSeed { get; set; }
    public float PaintWear { get; set; } = 0.12f;
    public string? Nametag { get; set; }
    public bool StatTrak { get; set; }
    public int StatTrakCount { get; set; }
    public int KeychainId { get; set; }
    /// <summary>旧版(legacy)模型的皮肤,需 body group 切换。</summary>
    public bool LegacyModel { get; set; }
    /// <summary>探员行专用:CT 模型名存 Nametag,T 模型名存这里。</summary>
    public string? AgentTModel { get; set; }
}

/// <summary>一个玩家的完整外观缓存。</summary>
public sealed class PlayerLoadout
{
    /// <summary>key = 实际武器 defindex(普通枪)或 Slots 常量。</summary>
    public readonly Dictionary<int, LoadoutItem> Items = new();
    public readonly Dictionary<int, List<StickerAttachment>> Stickers = new();

    public bool Bound { get; set; }
    public DateTime LoadedAtUtc { get; set; }
    public DateTime LastRefreshUtc { get; set; } = DateTime.MinValue;

    public LoadoutItem? ForWeapon(int defIndex, bool isKnife)
        => isKnife
            ? (Items.TryGetValue(Slots.Knife, out var k) ? k : null)
            : (Items.TryGetValue(defIndex, out var w) ? w : null);

    public LoadoutItem? Gloves => Items.TryGetValue(Slots.Gloves, out var v) ? v : null;
    public LoadoutItem? Agent => Items.TryGetValue(Slots.Agent, out var v) ? v : null;
    public LoadoutItem? Music => Items.TryGetValue(Slots.Music, out var v) ? v : null;
    public LoadoutItem? Pin => Items.TryGetValue(Slots.Pin, out var v) ? v : null;
}
