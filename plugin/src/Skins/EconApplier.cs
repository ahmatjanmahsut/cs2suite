using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using CS2Suite.Core;
using Microsoft.Extensions.Logging;

namespace CS2Suite.Skins;

/// <summary>一件武器上的贴纸(槽位 0-4)。</summary>
public readonly record struct StickerAttachment(int Slot, int StickerId, float OffsetX, float OffsetY, float Rotation, float Scale, float Wear);

/// <summary>
/// 经济属性注入引擎 —— 整合自:
///   * WeaponPaints (Nereziel/cs2-WeaponPaints, ★415):fallback 字段 + 属性双写(AttributeList/NetworkedDynamicAttributes)、
///     刀型 ChangeSubclass、手套 lastinv 刷新、探员模型、音乐包、徽章。
///   * AstraSkins (Ayrton09/AstraSkins):MemoryFunctionVoid<nint,string,float> 封装、kill eater 位重解释、
///     贴纸 offset/rotation/scale/wear 属性名、gamedata 签名。
/// 依赖 gamedata/cs2suite.json(需部署到 addons/counterstrikesharp/gamedata/)。
/// </summary>
public sealed class EconApplier
{
    private const string SignatureKey = "CS2Suite_CAttributeList_SetOrAddAttributeValueByName";
    private readonly ILogger _log;
    private readonly MemoryFunctionVoid<nint, string, float>? _setAttr;

    /// <summary>物品 id 自增器(重设外观后必须换 ItemID,客户端才会重建模型)。</summary>
    private ulong _nextItemId = 1000000UL;
    private int _fadeSeed = 1;

    public bool Available => _setAttr is not null;

    public EconApplier(ILogger log)
    {
        _log = log;
        try
        {
            var sig = GameData.GetSignature(SignatureKey);
            if (string.IsNullOrWhiteSpace(sig))
            {
                _log.LogError($"[CS2Suite] gamedata 签名缺失({SignatureKey})。请把 plugin/gamedata/cs2suite.json 复制到 addons/counterstrikesharp/gamedata/。");
                return;
            }
            _setAttr = new MemoryFunctionVoid<nint, string, float>(sig);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[CS2Suite] gamedata 签名加载失败,换肤不可用。");
        }
    }

    private static float AsFloat(int unsignedBits) => BitConverter.Int32BitsToSingle(unsignedBits);
    private static float AsFloat(uint unsignedBits) => BitConverter.Int32BitsToSingle((int)unsignedBits);

    private void ApplyToBothLists(CEconItemView item, Action<nint> apply)
    {
        item.AttributeList.Attributes.RemoveAll();
        item.NetworkedDynamicAttributes.Attributes.RemoveAll();
        apply(item.AttributeList.Handle);
        apply(item.NetworkedDynamicAttributes.Handle);
    }

    /// <summary>对一把武器应用皮肤/贴纸/挂饰/改名/StatTrak。knifeTarget>0 时先切刀型。</summary>
    public bool ApplyWeapon(CCSPlayerController player, CBasePlayerWeapon weapon, LoadoutItem? entry,
                            IReadOnlyList<StickerAttachment>? stickers, SkinsConfig cfg, bool logFail = false)
    {
        if (!Available || player is not { IsValid: true } || weapon?.IsValid != true) return false;
        var item = weapon.AttributeManager.Item;
        if (item.Handle == IntPtr.Zero) return false;

        try
        {
            var isKnife = (weapon.DesignerName ?? "").Contains("knife", StringComparison.OrdinalIgnoreCase)
                       || (weapon.DesignerName ?? "").Contains("bayonet", StringComparison.OrdinalIgnoreCase);

            // —— 目标物品身份(索引/品质/ID)——
            if (isKnife && entry is { KnifeTargetDefIndex: > 0 })
            {
                if (item.ItemDefinitionIndex != (ushort)entry.KnifeTargetDefIndex)
                    weapon.AcceptInput("ChangeSubclass", value: entry.KnifeTargetDefIndex.ToString());
                item.ItemDefinitionIndex = (ushort)entry.KnifeTargetDefIndex;
                // 刀/手套为"罕见"品质才有检视模型正确性;StatTrak 刀为品质 9
                item.EntityQuality = cfg.EnableStatTrak && entry.StatTrak ? 9 : 3;
            }
            else if (entry is not null && cfg.EnableStatTrak && entry.StatTrak)
            {
                item.EntityQuality = 9; // StatTrak
                weapon.AcceptInput("SetBodygroup", value: "stattrak,1");
            }
            else
            {
                item.EntityQuality = 0;
            }

            item.AccountID = (uint)player.SteamID;
            NextItemId(item);

            if (entry is null || entry.Paintkit <= 0)
            {
                // 无皮肤:清空贴图属性回退默认外观;
                // 改名 / StatTrak / 挂饰 / 贴纸在默认皮肤上依然生效(AstraSkins 同款行为,v1.2)
                ApplyToBothLists(item, _ => { });
                if (entry is null)
                {
                    // 解绑剥离路径:名字牌一并清除(apply extras 提前 return 时不会清)
                    item.CustomName = "";
                    item.CustomNameOverride = "";
                }
                ApplyExtras(item, weapon, entry, stickers, cfg);
                if (entry is not null)
                    Utilities.SetStateChanged(weapon, "CEconEntity", "m_pAttributeManager");
                return true;
            }

            var seed = entry.Paintkit == 10000 ? (_fadeSeed = _fadeSeed % 1000 + 1) : entry.PaintSeed;

            // —— fallback 三件套 + 属性双写(WeaponPaints 做法,兼容检视/掉落)——
            weapon.FallbackPaintKit = entry.Paintkit;
            weapon.FallbackSeed = seed;
            weapon.FallbackWear = Math.Clamp(entry.PaintWear, 0f, 1f);

            ApplyToBothLists(item, h =>
            {
                _setAttr!.Invoke(h, "set item texture prefab", entry.Paintkit);
                _setAttr.Invoke(h, "set item texture seed", seed);
                _setAttr.Invoke(h, "set item texture wear", Math.Clamp(entry.PaintWear, 0f, 1f));
            });

            // 传统模型体(旧皮肤模型 vs 新模型)
            weapon.AcceptInput("SetBodygroup", value: $"body,{(entry.LegacyModel ? 1 : 0)}");

            ApplyExtras(item, weapon, entry, stickers, cfg);
            Utilities.SetStateChanged(weapon, "CEconEntity", "m_pAttributeManager");
            return true;
        }
        catch (Exception ex)
        {
            if (logFail) _log.LogWarning(ex, "[CS2Suite] ApplyWeapon 失败: " + weapon.DesignerName);
            return false;
        }
    }

    /// <summary>StatTrak / 贴纸 / 挂饰 / 改名 —— 有无皮肤都应用(v1.2)。</summary>
    private void ApplyExtras(CEconItemView item, CBasePlayerWeapon weapon, LoadoutItem? entry,
                             IReadOnlyList<StickerAttachment>? stickers, SkinsConfig cfg)
    {
        if (entry is null) return;

        if (cfg.EnableStatTrak && entry.StatTrak)
        {
            weapon.AcceptInput("SetBodygroup", value: "stattrak,1");
            ApplyToBothLists(item, h =>
            {
                _setAttr!.Invoke(h, "kill eater", AsFloat((uint)Math.Max(0, entry.StatTrakCount)));
                _setAttr.Invoke(h, "kill eater score type", 0);
            });
        }

        if (cfg.EnableStickers && stickers is { Count: > 0 })
        {
            foreach (var st in stickers.Take(Math.Clamp(cfg.MaxStickersPerWeapon, 0, 5)))
            {
                if (st.Slot < 0 || st.Slot > 4 || st.StickerId <= 0) continue;
                WriteSticker(item, st);
            }
        }

        if (cfg.EnableKeychains && entry.KeychainId > 0)
            ApplyToBothLists(item, h =>
            {
                _setAttr!.Invoke(h, "keychain slot 0 id", AsFloat(entry.KeychainId));
                _setAttr.Invoke(h, "keychain slot 0 seed", AsFloat(entry.PaintSeed));
            });

        if (cfg.EnableNametags && !string.IsNullOrWhiteSpace(entry.Nametag))
        {
            item.CustomName = entry.Nametag;
            item.CustomNameOverride = entry.Nametag;
        }
        else if (string.IsNullOrWhiteSpace(entry.Nametag))
        {
            item.CustomName = "";
            item.CustomNameOverride = "";
        }
    }

    private void WriteSticker(CEconItemView item, in StickerAttachment st)
    {
        foreach (var h in new[] { item.AttributeList.Handle, item.NetworkedDynamicAttributes.Handle })
        {
            _setAttr!.Invoke(h, $"sticker slot {st.Slot} id", AsFloat(st.StickerId));
            _setAttr.Invoke(h, $"sticker slot {st.Slot} schema", 0);
            _setAttr.Invoke(h, $"sticker slot {st.Slot} offset x", st.OffsetX);
            _setAttr.Invoke(h, $"sticker slot {st.Slot} offset y", st.OffsetY);
            _setAttr.Invoke(h, $"sticker slot {st.Slot} rotation", st.Rotation);
            _setAttr.Invoke(h, $"sticker slot {st.Slot} scale", st.Scale);
            _setAttr.Invoke(h, $"sticker slot {st.Slot} wear", st.Wear);
        }
    }

    /// <summary>手套:写 pawn 的 EconGloves 并强制第三人称模型刷新(WeaponPaints lastinv 技巧)。</summary>
    public void ApplyGloves(CCSPlayerController player, LoadoutItem? glove)
    {
        if (!Available || player?.PlayerPawn?.Value is not CCSPlayerPawn pawn || !pawn.IsValid) return;
        try
        {
            var item = pawn.EconGloves;
            if (item.Handle == IntPtr.Zero) return;
            item.AccountID = (uint)player.SteamID;
            NextItemId(item);

            if (glove is null || glove.KnifeTargetDefIndex <= 0)
            {
                // 剥离:清属性;手套模型索引由引擎在下次重生时还原为阵营默认
                ApplyToBothLists(item, _ => { });
                player.ExecuteClientCommand("lastinv");
                return;
            }

            item.ItemDefinitionIndex = (ushort)glove.KnifeTargetDefIndex;
            item.EntityQuality = 3;
            item.Initialized = true;

            var paint = glove.Paintkit > 0 ? glove.Paintkit : 10001; // 无皮肤也要有默认布料
            ApplyToBothLists(item, h =>
            {
                _setAttr!.Invoke(h, "set item texture prefab", paint);
                _setAttr.Invoke(h, "set item texture seed", glove.PaintSeed);
                _setAttr.Invoke(h, "set item texture wear", Math.Clamp(glove.PaintWear <= 0 ? 0.12f : glove.PaintWear, 0f, 1f));
            });

            player.ExecuteClientCommand("lastinv");
            AddTimerRefresh(player, pawn);
        }
        catch (Exception ex) { _log.LogDebug(ex, "[CS2Suite] ApplyGloves"); }
    }

    private void AddTimerRefresh(CCSPlayerController player, CCSPlayerPawn pawn)
    {
        CS2SuitePlugin.Instance?.AddTimer(0.2f, () =>
        {
            if (player.IsValid && pawn.IsValid)
                pawn.AcceptInput("SetBodygroup", value: "first_or_third_person,1");
        });
        pawn.AcceptInput("SetBodygroup", value: "first_or_third_person,0");
    }

    /// <summary>探员模型(按当前队选 T/CT 模型名)。</summary>
    public static void ApplyAgent(CCSPlayerController player, string ctModel, string tModel)
    {
        if (player.PlayerPawn?.Value is not CCSPlayerPawn pawn || !pawn.IsValid) return;
        var model = player.Team == CsTeam.CounterTerrorist ? ctModel : tModel;
        if (string.IsNullOrWhiteSpace(model)) return;
        Server.NextFrame(() =>
        {
            try { if (pawn.IsValid) pawn.SetModel($"agents/models/{model}.vmdl"); }
            catch { /* 未下载该探员模型(社区服常见),静默忽略 */ }
        });
    }

    public static void ApplyMusicKit(CCSPlayerController player, int musicId)
    {
        if (musicId <= 0 || player.IsBot) return;
        try
        {
            player.MusicKitID = musicId;
            Utilities.SetStateChanged(player, "CCSPlayerController", "m_iMusicKitID");
            if (player.InventoryServices != null)
            {
                player.InventoryServices.MusicID = (ushort)musicId;
                Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInventoryServices");
            }
        }
        catch { /* 部分音乐包为付费内容,服务器可能无权限 */ }
    }

    public static void ApplyPin(CCSPlayerController player, int medalRank)
    {
        if (player.InventoryServices is null) return;
        try
        {
            player.InventoryServices.Rank[5] = (MedalRank_t)Math.Clamp(medalRank, 0, 16);
            Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInventoryServices");
        }
        catch { }
    }

    private void NextItemId(CEconItemView item)
    {
        var id = _nextItemId++;
        item.ItemID = id;
        item.ItemIDLow = (uint)(id & 0xFFFFFFFF);
        item.ItemIDHigh = (uint)(id >> 32);
    }
}
