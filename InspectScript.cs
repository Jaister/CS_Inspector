using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace SkinInspect;

/// <summary>
/// CounterStrikeSharp plugin that allows players to preview weapon skins, knives, and gloves
/// on a CS2 dedicated server via chat commands (<c>!g</c>, <c>!gen</c>, <c>!gl</c>).
/// Operates by manipulating <see cref="CEconItemView"/> attributes and invoking
/// <c>CAttributeList_SetOrAddAttributeValueByName</c> through resolved game signatures.
/// </summary>
public class SkinInspect : BasePlugin
{
    public override string ModuleName => "SkinInspect";
    public override string ModuleVersion => "1.6.0";
    public override string ModuleAuthor => "jalster";
    public override string ModuleDescription => "Apply skins via !g";

    /// <summary>Monotonically increasing counter used to generate unique <see cref="CEconItemView"/> item IDs.</summary>
    private static ulong NextItemId = 65578;

    /// <summary>
    /// Cached memory function pointer to <c>CAttributeList_SetOrAddAttributeValueByName</c>.
    /// <c>null</c> if the game signature could not be resolved at load time.
    /// </summary>
    private static readonly MemoryFunctionVoid<nint, string, float>? SetAttrByName = CreateSetAttrByName();

    /// <summary>
    /// Attempts to resolve the <c>CAttributeList_SetOrAddAttributeValueByName</c> signature
    /// from the <see cref="GameData"/> signature database.
    /// </summary>
    /// <returns>The resolved memory function, or <c>null</c> if the signature is unavailable.</returns>
    private static MemoryFunctionVoid<nint, string, float>? CreateSetAttrByName()
    {
        try
        {
            return new(GameData.GetSignature("CAttributeList_SetOrAddAttributeValueByName"));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Maps CS2 weapon definition indexes to their corresponding entity class names.</summary>
    private static readonly Dictionary<int, string> DefindexToWeapon = new()
    {
        { 1, "weapon_deagle" }, { 2, "weapon_elite" }, { 3, "weapon_fiveseven" },
        { 4, "weapon_glock" }, { 7, "weapon_ak47" }, { 8, "weapon_aug" },
        { 9, "weapon_awp" }, { 10, "weapon_famas" }, { 11, "weapon_g3sg1" },
        { 13, "weapon_galilar" }, { 14, "weapon_m249" }, { 16, "weapon_m4a1" },
        { 17, "weapon_mac10" }, { 19, "weapon_p90" }, { 23, "weapon_mp5sd" },
        { 24, "weapon_ump45" }, { 25, "weapon_xm1014" }, { 26, "weapon_bizon" },
        { 27, "weapon_mag7" }, { 28, "weapon_negev" }, { 29, "weapon_sawedoff" },
        { 30, "weapon_tec9" }, { 32, "weapon_hkp2000" }, { 33, "weapon_mp7" },
        { 34, "weapon_mp9" }, { 35, "weapon_nova" }, { 36, "weapon_p250" },
        { 38, "weapon_scar20" }, { 39, "weapon_sg556" }, { 40, "weapon_ssg08" },
        { 60, "weapon_m4a1_silencer" }, { 61, "weapon_usp_silencer" },
        { 63, "weapon_cz75a" }, { 64, "weapon_revolver" },
        { 500, "weapon_bayonet" }, { 505, "weapon_knife_flip" },
        { 506, "weapon_knife_gut" }, { 507, "weapon_knife_karambit" },
        { 508, "weapon_knife_m9_bayonet" }, { 509, "weapon_knife_tactical" },
        { 512, "weapon_knife_falchion" }, { 514, "weapon_knife_survival_bowie" },
        { 515, "weapon_knife_butterfly" }, { 516, "weapon_knife_push" },
        { 517, "weapon_knife_cord" }, { 518, "weapon_knife_canis" },
        { 519, "weapon_knife_ursus" }, { 520, "weapon_knife_gypsy_jackknife" },
        { 521, "weapon_knife_outdoor" }, { 522, "weapon_knife_stiletto" },
        { 523, "weapon_knife_widowmaker" }, { 525, "weapon_knife_skeleton" },
        { 526, "weapon_knife_kukri" },
    };

    /// <summary>Set of all valid knife definition indexes for O(1) lookup.</summary>
    private static readonly HashSet<int> KnifeDefindexes = new()
    {
        500, 503, 505, 506, 507, 508, 509, 512, 514, 515, 516,
        517, 518, 519, 520, 521, 522, 523, 525, 526
    };

    /// <summary>
    /// Valid CS2 glove definition indexes:
    /// 4725=Broken Fang, 5027=Bloodhound, 5030=Sport, 5031=Driver,
    /// 5032=Hand Wraps, 5033=Moto, 5034=Specialist, 5035=Hydra.
    /// </summary>
    private static readonly HashSet<int> GloveDefindexes = new()
    {
        4725,
        5027, 5030, 5031, 5032, 5033, 5034, 5035,
    };

    /// <summary>
    /// Plugin entry point. Registers chat commands and validates that the attribute
    /// memory signature is available for glove rendering.
    /// </summary>
    /// <param name="hotReload">Whether the plugin was hot-reloaded mid-session.</param>
    public override void Load(bool hotReload)
    {
        AddCommand("css_gen", "Apply skin or gloves", CommandGen);
        AddCommand("css_g", "Apply skin", CommandG);
        AddCommand("css_gl", "Apply gloves", CommandGl);
        if (SetAttrByName == null)
        {
            Logger.LogWarning("Signature CAttributeList_SetOrAddAttributeValueByName no encontrada. Los guantes pueden no renderizarse.");
        }
        Logger.LogInformation("SkinInspect loaded");
    }

    /// <summary>
    /// Assigns a new unique item ID to a <see cref="CEconItemView"/>, forcing the client
    /// to treat the entity as a freshly created item and re-evaluate its visual state.
    /// </summary>
    /// <param name="item">The econ item view to update.</param>
    private static void UpdateEconItemId(CEconItemView item)
    {
        var itemId = NextItemId++;
        TrySetMember(item, "ItemID", itemId);
        item.ItemIDLow = (uint)(itemId & 0xFFFFFFFF);
        item.ItemIDHigh = (uint)(itemId >> 32);
    }

    /// <summary>
    /// Sends a <c>ChangeSubclass</c> input to a weapon entity to update its subclass ID,
    /// enabling knife model swaps. Silently fails if the input is unsupported in the current build.
    /// </summary>
    /// <param name="weapon">The weapon entity to modify.</param>
    /// <param name="defindex">The target definition index for the subclass change.</param>
    private static void ChangeWeaponSubclass(CBasePlayerWeapon weapon, int defindex)
    {
        try
        {
            weapon.AcceptInput("ChangeSubclass", value: defindex.ToString());
        }
        catch
        {
            // Ignore if subclass input is unavailable in this server build.
        }
    }

    /// <summary>
    /// Determines whether a weapon entity is a knife or bayonet by inspecting its designer name.
    /// </summary>
    /// <param name="weapon">The weapon entity to check.</param>
    /// <returns><c>true</c> if the entity's designer name contains "knife" or "bayonet".</returns>
    private static bool IsKnifeEntity(CBasePlayerWeapon weapon)
    {
        var name = weapon.DesignerName ?? string.Empty;
        return name.Contains("knife") || name.Contains("bayonet");
    }

    /// <summary>
    /// Invokes <see cref="SetAttrByName"/> on a <c>CAttributeList</c> native handle to set the
    /// paint kit, pattern seed, and wear attributes. No-ops if the signature is unavailable.
    /// The seed is included in the attribute list to ensure correct pattern positioning when
    /// additional attributes (stickers, charms) are present, which causes CS2 to read all
    /// skin properties from the attribute list rather than fallback fields.
    /// </summary>
    /// <param name="handle">Native pointer to the <c>CAttributeList</c> instance.</param>
    /// <param name="paintindex">Paint kit definition index.</param>
    /// <param name="patternIndex">Pattern index (seed) that determines the skin pattern variation.</param>
    /// <param name="paintwear">Wear float (0.0 = factory new, 1.0 = battle-scarred).</param>
    private static void ApplyAttributes(nint handle, int paintindex, int patternIndex, float paintwear)
    {
        if (SetAttrByName == null) return;
        SetAttrByName.Invoke(handle, "set item texture prefab", paintindex);
        SetAttrByName.Invoke(handle, "set item texture seed", (float)patternIndex);
        SetAttrByName.Invoke(handle, "set item texture wear", paintwear);
    }

    /// <summary>
    /// Reinterprets an integer's raw bits as a float without numeric conversion.
    /// Required for uint32 attributes (sticker IDs, charm IDs) passed through the
    /// <c>SetAttrByName</c> float parameter: the engine reads the raw bits as uint32.
    /// </summary>
    private static float ViewAsFloat(int value) => BitConverter.Int32BitsToSingle(value);

    /// <summary>
    /// Applies sticker attributes to a <c>CAttributeList</c> handle for up to 5 slots (0-4).
    /// Uses default values: wear=0 (pristine), scale=1, rotation=0.
    /// </summary>
    /// <param name="handle">Native pointer to the <c>CAttributeList</c> instance.</param>
    /// <param name="stickerIds">Array of sticker definition indexes; 0 = empty slot.</param>
    private static void ApplyStickers(nint handle, int[]? stickerIds)
    {
        if (SetAttrByName == null || stickerIds == null) return;
        for (int i = 0; i < stickerIds.Length && i < 5; i++)
        {
            if (stickerIds[i] == 0) continue;
            SetAttrByName.Invoke(handle, $"sticker slot {i} id",       ViewAsFloat(stickerIds[i]));
            SetAttrByName.Invoke(handle, $"sticker slot {i} wear",     0f);
            SetAttrByName.Invoke(handle, $"sticker slot {i} scale",    1f);
            SetAttrByName.Invoke(handle, $"sticker slot {i} rotation", 0f);
        }
    }

    /// <summary>
    /// Applies charm (keychain) attributes to a <c>CAttributeList</c> handle.
    /// Uses default positional offsets (0, 0, 0).
    /// </summary>
    /// <param name="handle">Native pointer to the <c>CAttributeList</c> instance.</param>
    /// <param name="charmId">Keychain definition index.</param>
    /// <param name="charmSeed">Pattern seed for the keychain (default 0).</param>
    private static void ApplyCharm(nint handle, int charmId, int charmSeed = 0)
    {
        if (SetAttrByName == null || charmId <= 0) return;
        SetAttrByName.Invoke(handle, "keychain slot 0 id",       ViewAsFloat(charmId));
        SetAttrByName.Invoke(handle, "keychain slot 0 seed",     ViewAsFloat(charmSeed));
        SetAttrByName.Invoke(handle, "keychain slot 0 x offset", 0f);
        SetAttrByName.Invoke(handle, "keychain slot 0 y offset", 0f);
        SetAttrByName.Invoke(handle, "keychain slot 0 z offset", 0f);
    }

    /// <summary>
    /// Core skin application method. Sets the weapon's definition index (for knives), assigns a fresh
    /// econ item ID, configures fallback paint/seed/wear values, clears and re-applies both attribute
    /// lists, then notifies the network layer via <c>SetStateChanged</c>.
    /// </summary>
    /// <param name="weapon">The weapon entity to skin.</param>
    /// <param name="paintindex">Paint kit definition index.</param>
    /// <param name="patternIndex">Pattern index (seed) that determines the skin pattern variation.</param>
    /// <param name="paintwear">Wear float value.</param>
    /// <param name="isKnife">Whether this weapon is a knife requiring subclass and quality changes.</param>
    /// <param name="knifeDefindex">The target knife definition index (only used when <paramref name="isKnife"/> is <c>true</c>).</param>
    private void ApplySkinToWeapon(CBasePlayerWeapon weapon, int paintindex, int patternIndex, float paintwear, bool isKnife = false, int knifeDefindex = 0, int[]? stickerIds = null, int? charmId = null, int? charmSeed = null)
    {
        if (isKnife && knifeDefindex > 0)
        {
            ChangeWeaponSubclass(weapon, knifeDefindex);
            weapon.AttributeManager.Item.ItemDefinitionIndex = (ushort)knifeDefindex;
        }

        UpdateEconItemId(weapon.AttributeManager.Item);
        weapon.FallbackPaintKit = paintindex;
        weapon.FallbackSeed = patternIndex;
        weapon.FallbackWear = paintwear;
        // EntityQuality 3 = "star" quality used by knives; 0 = default for regular weapons
        weapon.AttributeManager.Item.EntityQuality = isKnife ? 3 : 0;

        weapon.AttributeManager.Item.NetworkedDynamicAttributes.Attributes.RemoveAll();
        ApplyAttributes(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle, paintindex, patternIndex, paintwear);
        weapon.AttributeManager.Item.AttributeList.Attributes.RemoveAll();
        ApplyAttributes(weapon.AttributeManager.Item.AttributeList.Handle, paintindex, patternIndex, paintwear);

        // Apply stickers to both attribute list handles (after paint, before SetStateChanged)
        ApplyStickers(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle, stickerIds);
        ApplyStickers(weapon.AttributeManager.Item.AttributeList.Handle, stickerIds);

        // Apply charm to both attribute list handles
        if (charmId.HasValue && charmId.Value > 0)
        {
            ApplyCharm(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle, charmId.Value, charmSeed ?? 0);
            ApplyCharm(weapon.AttributeManager.Item.AttributeList.Handle, charmId.Value, charmSeed ?? 0);
        }

        // Sync fallback skin properties to the client (CSS does NOT auto-mark these as dirty)
        Utilities.SetStateChanged(weapon, "CEconEntity", "m_nFallbackPaintKit");
        Utilities.SetStateChanged(weapon, "CEconEntity", "m_nFallbackSeed");
        Utilities.SetStateChanged(weapon, "CEconEntity", "m_flFallbackWear");
        // Sync attribute lists, subclass ID (knife model), and glow type (force client visual refresh)
        Utilities.SetStateChanged(weapon, "CEconEntity", "m_AttributeManager");
        Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_iSubclassID");
        Utilities.SetStateChanged(weapon, "CBaseEntity", "m_iGlowType");
    }

    /// <summary>
    /// Applies a glove skin in two phases: an immediate model swap to force the client to
    /// re-evaluate the glove model, followed by an 80ms delayed timer that sets the definition
    /// index, attributes, and bodygroup to avoid race conditions with the engine.
    /// </summary>
    /// <param name="player">The player controller receiving the gloves.</param>
    /// <param name="defindex">Glove definition index (e.g., 5027 for Bloodhound).</param>
    /// <param name="paintindex">Paint kit definition index.</param>
    /// <param name="patternIndex">Pattern index (seed) that determines the skin pattern variation.</param>
    /// <param name="paintwear">Wear float value (clamped to 0.0001–1.0).</param>
    private void ApplySkinToGloves(CCSPlayerController player, int defindex, int paintindex, int patternIndex, float paintwear)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid) return;

        // Phase 1: Swap model to a dummy and back to force the client renderer to invalidate the glove model cache
        var model = pawn.CBodyComponent?.SceneNode?.GetSkeletonInstance()?.ModelState.ModelName ?? string.Empty;
        if (!string.IsNullOrEmpty(model))
        {
            pawn.SetModel("characters/models/tm_jumpsuit/tm_jumpsuit_varianta.vmdl");
            pawn.SetModel(model);
        }

        CEconItemView item = pawn.EconGloves;
        item.NetworkedDynamicAttributes.Attributes.RemoveAll();
        item.AttributeList.Attributes.RemoveAll();

        // Phase 2 (80ms timer): Apply defindex + attributes + bodygroup.
        // Delay matches WeaponPaints timing pattern to avoid race conditions with the engine.
        AddTimer(0.08f, () =>
        {
            if (!player.IsValid || !player.PawnIsAlive) return;
            var pawn2 = player.PlayerPawn.Value;
            if (pawn2 == null || !pawn2.IsValid) return;

            CEconItemView item2 = pawn2.EconGloves;
            item2.ItemDefinitionIndex = (ushort)defindex;
            UpdateEconItemId(item2);

            item2.NetworkedDynamicAttributes.Attributes.RemoveAll();
            ApplyAttributes(item2.NetworkedDynamicAttributes.Handle, paintindex, patternIndex, paintwear);

            item2.AttributeList.Attributes.RemoveAll();
            ApplyAttributes(item2.AttributeList.Handle, paintindex, patternIndex, paintwear);

            /* TODO: If charms/keychains become applicable to gloves in a future CS2 update,
             * apply charm attributes here using the same SetAttrByName pattern
             * (see the detailed TODO block in ApplySkinToWeapon), before setting Initialized = true. */

            item2.Initialized = true;
            // Hide the default glove model so the custom-skinned glove renders instead
            pawn2.AcceptInput("SetBodygroup", value: "default_gloves,1");

            Utilities.SetStateChanged(pawn2, "CCSPlayerPawn", "m_EconGloves");
            Utilities.SetStateChanged(pawn2, "CBaseModelEntity", "m_nBodyGroupChoices");
            Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInventoryServices");

            Logger.LogInformation("Gloves applied def={Def} paint={Paint} pattern={Pattern} wear={Wear}",
                defindex, paintindex, patternIndex, paintwear);
        });
    }

    /// <summary>
    /// Reflection-based utility that sets a property or field on an object by name.
    /// Used to work around non-public setters in CounterStrikeSharp schema-generated classes.
    /// </summary>
    /// <typeparam name="T">The type of the value to set.</typeparam>
    /// <param name="target">The object instance to modify.</param>
    /// <param name="memberName">The name of the property or field to set.</param>
    /// <param name="value">The value to assign.</param>
    /// <returns><c>true</c> if the member was found and set; <c>false</c> otherwise.</returns>
    private static bool TrySetMember<T>(object target, string memberName, T value)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        var type = target.GetType();
        var prop = type.GetProperty(memberName, flags);
        if (prop != null && prop.CanWrite)
        {
            prop.SetValue(target, value);
            return true;
        }

        var field = type.GetField(memberName, flags);
        if (field != null)
        {
            field.SetValue(target, value);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Parses optional sticker and charm arguments from a command.
    /// Sticker IDs are positional (up to 5, use 0 for empty slots).
    /// Charm is specified with <c>--charm &lt;id&gt; [seed]</c>.
    /// </summary>
    /// <param name="info">Command arguments.</param>
    /// <param name="startArgIndex">Index of the first optional argument (after the 4 required ones).</param>
    /// <param name="stickerIds">Parsed sticker IDs array, or null if none provided.</param>
    /// <param name="charmId">Parsed charm definition index, or null.</param>
    /// <param name="charmSeed">Parsed charm seed, or null.</param>
    private static void ParseStickerAndCharmArgs(CommandInfo info, int startArgIndex, out int[]? stickerIds, out int? charmId, out int? charmSeed)
    {
        stickerIds = null;
        charmId = null;
        charmSeed = null;

        var stickers = new List<int>();
        for (int i = startArgIndex; i < info.ArgCount; i++)
        {
            string arg = info.GetArg(i);
            if (arg == "--charm")
            {
                if (i + 1 < info.ArgCount && int.TryParse(info.GetArg(i + 1), out int cId))
                {
                    charmId = cId;
                    if (i + 2 < info.ArgCount && int.TryParse(info.GetArg(i + 2), out int cSeed))
                        charmSeed = cSeed;
                }
                break;
            }

            if (stickers.Count < 5 && int.TryParse(arg, out int stickerId))
                stickers.Add(stickerId);
        }

        if (stickers.Count > 0)
            stickerIds = stickers.ToArray();
    }

    /// <summary>
    /// Orchestrates the full weapon or knife application flow: removes any existing instance of
    /// the target weapon (or all knives for knife swaps), gives a new item to the player, then
    /// applies skin attributes on the next server frame. For knives, a second <c>Server.NextFrame</c>
    /// pass ensures the subclass change has fully propagated before re-applying the skin.
    /// </summary>
    /// <param name="player">The player controller receiving the weapon.</param>
    /// <param name="defindex">Weapon definition index.</param>
    /// <param name="weaponName">Entity class name (e.g., <c>"weapon_ak47"</c>).</param>
    /// <param name="paintindex">Paint kit definition index.</param>
    /// <param name="patternIndex">Pattern index (seed) that determines the skin pattern variation.</param>
    /// <param name="paintwear">Wear float value.</param>
    private void ApplyWeaponOrKnife(CCSPlayerController player, int defindex, string weaponName, int paintindex, int patternIndex, float paintwear, int[]? stickerIds = null, int? charmId = null, int? charmSeed = null)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn == null) return;

        bool isKnife = KnifeDefindexes.Contains(defindex);

        if (isKnife)
        {
            // Remove all existing knives (defindex 42=CT default, 59=T default, plus custom knife defindexes)
            if (pawn.WeaponServices?.MyWeapons != null)
            {
                foreach (var wh in pawn.WeaponServices.MyWeapons)
                {
                    var w = wh.Value;
                    if (w == null || !w.IsValid) continue;
                    var idx = w.AttributeManager.Item.ItemDefinitionIndex;
                    if (idx == 42 || idx == 59 || KnifeDefindexes.Contains(idx))
                        w.Remove();
                }
            }
        }
        else
        {
            if (pawn.WeaponServices?.MyWeapons != null)
            {
                foreach (var wh in pawn.WeaponServices.MyWeapons)
                {
                    var existing = wh.Value;
                    if (existing == null || !existing.IsValid) continue;
                    if (existing.AttributeManager.Item.ItemDefinitionIndex == (ushort)defindex)
                    {
                        existing.Remove();
                        break;
                    }
                }
            }
        }

        player.GiveNamedItem(isKnife ? "weapon_knife" : weaponName);
        if (isKnife)
        {
            Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInventoryServices");
            player.ExecuteClientCommand("slot3");
        }

        Server.NextFrame(() =>
        {
            var pawn2 = player.PlayerPawn.Value;
            if (pawn2?.WeaponServices?.MyWeapons == null) return;

            // Track first knife found as fallback if the exact defindex isn't matched yet
            CBasePlayerWeapon? firstKnife = null;
            bool appliedKnife = false;

            foreach (var wh in pawn2.WeaponServices.MyWeapons)
            {
                var weapon = wh.Value;
                if (weapon == null || !weapon.IsValid) continue;

                if (isKnife)
                {
                    if (!IsKnifeEntity(weapon) && !KnifeDefindexes.Contains(weapon.AttributeManager.Item.ItemDefinitionIndex)) continue;
                    firstKnife ??= weapon;
                    if (weapon.AttributeManager.Item.ItemDefinitionIndex == (ushort)defindex)
                    {
                        ApplySkinToWeapon(weapon, paintindex, patternIndex, paintwear, true, defindex, stickerIds, charmId, charmSeed);
                        appliedKnife = true;
                        break;
                    }
                }
                else
                {
                    if (weapon.AttributeManager.Item.ItemDefinitionIndex != (ushort)defindex) continue;
                    ApplySkinToWeapon(weapon, paintindex, patternIndex, paintwear, stickerIds: stickerIds, charmId: charmId, charmSeed: charmSeed);
                }
            }

            // Fallback: if the exact defindex wasn't found, apply to the first available knife entity
            if (isKnife && !appliedKnife && firstKnife != null)
                ApplySkinToWeapon(firstKnife, paintindex, patternIndex, paintwear, true, defindex, stickerIds, charmId, charmSeed);

            if (isKnife)
            {
                // Second NextFrame pass: knife subclass changes need an extra frame to propagate
                // before skin attributes are guaranteed to stick on the new model
                Server.NextFrame(() =>
                {
                    var pawn3 = player.PlayerPawn.Value;
                    if (pawn3?.WeaponServices?.MyWeapons == null) return;
                    foreach (var wh3 in pawn3.WeaponServices.MyWeapons)
                    {
                        var weapon3 = wh3.Value;
                        if (weapon3 == null || !weapon3.IsValid) continue;
                        if (!IsKnifeEntity(weapon3) && !KnifeDefindexes.Contains(weapon3.AttributeManager.Item.ItemDefinitionIndex)) continue;
                        ApplySkinToWeapon(weapon3, paintindex, patternIndex, paintwear, true, defindex, stickerIds, charmId, charmSeed);
                        player.ExecuteClientCommand("slot3");
                        break;
                    }
                });
            }

            var msg = $" \x01[Skins] \x04{weaponName} paint={paintindex} pattern={patternIndex} wear={paintwear:F4}";
            if (stickerIds != null)
                msg += $" stickers=[{string.Join(",", stickerIds)}]";
            if (charmId.HasValue)
                msg += $" charm={charmId.Value}" + (charmSeed.HasValue ? $" seed={charmSeed.Value}" : "");
            player.PrintToChat(msg);
        });
    }

    /// <summary>
    /// Handles the <c>!g</c> command for weapon-only skin application.
    /// Parses four required arguments (defindex, paintindex, patternIndex, wear) and delegates to
    /// <see cref="ApplyWeaponOrKnife"/>. Does not support glove defindexes.
    /// </summary>
    /// <param name="player">The player who issued the command.</param>
    /// <param name="info">Command arguments and metadata.</param>
    private void CommandG(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !player.IsValid || player.IsBot) return;

        if (info.ArgCount < 5)
        {
            player.PrintToChat(" \x01[Skins] \x07Uso: !g <defindex> <paint> <seed> <wear> [sticker0..4] [--charm <id> [seed]]");
            return;
        }

        if (!int.TryParse(info.GetArg(1), out int defindex) ||
            !int.TryParse(info.GetArg(2), out int paintindex) ||
            !int.TryParse(info.GetArg(3), out int patternIndex) ||
            !float.TryParse(info.GetArg(4), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float paintwear))
        {
            player.PrintToChat(" \x01[Skins] \x07Parámetros inválidos.");
            return;
        }

        if (!DefindexToWeapon.TryGetValue(defindex, out string? weaponName))
        {
            player.PrintToChat($" \x01[Skins] \x07Defindex {defindex} no reconocido.");
            return;
        }

        ParseStickerAndCharmArgs(info, 5, out int[]? stickerIds, out int? charmId, out int? charmSeed);
        ApplyWeaponOrKnife(player, defindex, weaponName, paintindex, patternIndex, paintwear, stickerIds, charmId, charmSeed);
    }

    /// <summary>
    /// Handles the <c>!gen</c> unified command that routes to either glove or weapon/knife
    /// application based on the provided defindex.
    /// </summary>
    /// <param name="player">The player who issued the command.</param>
    /// <param name="info">Command arguments and metadata.</param>
    private void CommandGen(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !player.IsValid || player.IsBot) return;

        if (info.ArgCount < 5)
        {
            player.PrintToChat(" \x01[Skins] \x07Uso: !gen <defindex> <paint> <seed> <wear> [sticker0..4] [--charm <id> [seed]]");
            return;
        }

        if (!int.TryParse(info.GetArg(1), out int defindex) ||
            !int.TryParse(info.GetArg(2), out int paintindex) ||
            !int.TryParse(info.GetArg(3), out int patternIndex) ||
            !float.TryParse(info.GetArg(4), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float paintwear))
        {
            player.PrintToChat(" \x01[Skins] \x07Parámetros inválidos.");
            return;
        }

        if (GloveDefindexes.Contains(defindex))
        {
            if (!player.PawnIsAlive)
            {
                player.PrintToChat(" \x01[Skins] \x07Debes estar vivo para aplicar guantes.");
                return;
            }
            ApplySkinToGloves(player, defindex, paintindex, patternIndex, Math.Clamp(paintwear, 0.0001f, 1.0f));
            player.PrintToChat($" \x01[Skins] \x04Guantes {defindex} paint={paintindex} pattern={patternIndex} wear={paintwear:F4}");
            return;
        }

        if (!DefindexToWeapon.TryGetValue(defindex, out string? weaponName))
        {
            player.PrintToChat($" \x01[Skins] \x07Defindex {defindex} no reconocido.");
            return;
        }

        ParseStickerAndCharmArgs(info, 5, out int[]? stickerIds, out int? charmId, out int? charmSeed);
        ApplyWeaponOrKnife(player, defindex, weaponName, paintindex, patternIndex, paintwear, stickerIds, charmId, charmSeed);
    }

    /// <summary>
    /// Handles the <c>!gl</c> command for glove-only skin application.
    /// Validates that the defindex corresponds to a known glove type, clamps the wear value,
    /// and routes to <see cref="ApplySkinToGloves"/>.
    /// </summary>
    /// <param name="player">The player who issued the command.</param>
    /// <param name="info">Command arguments and metadata.</param>
    private void CommandGl(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !player.IsValid || player.IsBot) return;

        if (info.ArgCount < 5)
        {
            player.PrintToChat(" \x01[Skins] \x07Uso: !gl <defindex> <paintindex> <patternIndex> <wear>");
            return;
        }

        if (!int.TryParse(info.GetArg(1), out int defindex) ||
            !int.TryParse(info.GetArg(2), out int paintindex) ||
            !int.TryParse(info.GetArg(3), out int patternIndex) ||
            !float.TryParse(info.GetArg(4), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float paintwear))
        {
            player.PrintToChat(" \x01[Skins] \x07Parámetros inválidos.");
            return;
        }

        if (!GloveDefindexes.Contains(defindex))
        {
            player.PrintToChat($" \x01[Skins] \x07Defindex {defindex} no es un guante válido. Válidos: 4725,5027,5030-5035");
            return;
        }

        if (!player.PawnIsAlive)
        {
            player.PrintToChat(" \x01[Skins] \x07Debes estar vivo para aplicar guantes.");
            return;
        }

        Logger.LogInformation("Command !gl def={Def} paint={Paint} pattern={Pattern} wear={Wear}",
            defindex, paintindex, patternIndex, paintwear);

        ApplySkinToGloves(player, defindex, paintindex, patternIndex,
            Math.Clamp(paintwear, 0.0001f, 1.0f));

        player.PrintToChat($" \x01[Skins] \x04Guantes {defindex} paint={paintindex} pattern={patternIndex} wear={paintwear:F4}");
    }
}
