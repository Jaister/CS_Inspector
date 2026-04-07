using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace SkinInspect;

public class SkinInspect : BasePlugin
{
    public override string ModuleName => "SkinInspect";
    public override string ModuleVersion => "1.5.0";
    public override string ModuleAuthor => "jalster";
    public override string ModuleDescription => "Apply skins via !g";
    private static ulong NextItemId = 65578;
    private static readonly MemoryFunctionVoid<nint, string, float>? SetAttrByName = CreateSetAttrByName();

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

    private static readonly HashSet<int> KnifeDefindexes = new()
    {
        500, 503, 505, 506, 507, 508, 509, 512, 514, 515, 516,
        517, 518, 519, 520, 521, 522, 523, 525, 526
    };

    // Valid CS2 glove defindexes (sourced from WeaponPaints):
    // 4725=Broken Fang, 5027=Bloodhound, 5030=Sport, 5031=Driver,
    // 5032=Hand Wraps, 5033=Moto, 5034=Specialist, 5035=Hydra
    private static readonly HashSet<int> GloveDefindexes = new()
    {
        4725,
        5027, 5030, 5031, 5032, 5033, 5034, 5035,
    };

    public override void Load(bool hotReload)
    {
        AddCommand("css_g", "Apply skin", CommandG);
        AddCommand("css_gl", "Apply gloves", CommandGl);
        if (SetAttrByName == null)
        {
            Logger.LogWarning("Signature CAttributeList_SetOrAddAttributeValueByName no encontrada. Los guantes pueden no renderizarse.");
        }
        Logger.LogInformation("SkinInspect loaded");
    }

    private static void UpdateEconItemId(CEconItemView item)
    {
        var itemId = NextItemId++;
        TrySetMember(item, "ItemID", itemId);
        item.ItemIDLow = (uint)(itemId & 0xFFFFFFFF);
        item.ItemIDHigh = (uint)(itemId >> 32);
    }

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

    private static bool IsKnifeEntity(CBasePlayerWeapon weapon)
    {
        var name = weapon.DesignerName ?? string.Empty;
        return name.Contains("knife") || name.Contains("bayonet");
    }

    private static void ApplyAttributes(nint handle, int paintindex, int paintseed, float paintwear)
    {
        if (SetAttrByName == null) return;
        SetAttrByName.Invoke(handle, "set item texture prefab", paintindex);
        SetAttrByName.Invoke(handle, "set item texture seed", paintseed);
        SetAttrByName.Invoke(handle, "set item texture wear", paintwear);
    }

    private void ApplySkinToWeapon(CBasePlayerWeapon weapon, int paintindex, int paintseed, float paintwear, bool isKnife = false, int knifeDefindex = 0)
    {
        if (isKnife && knifeDefindex > 0)
        {
            ChangeWeaponSubclass(weapon, knifeDefindex);
            weapon.AttributeManager.Item.ItemDefinitionIndex = (ushort)knifeDefindex;
        }

        UpdateEconItemId(weapon.AttributeManager.Item);
        weapon.FallbackPaintKit = paintindex;
        weapon.FallbackSeed = paintseed;
        weapon.FallbackWear = paintwear;
        weapon.AttributeManager.Item.EntityQuality = isKnife ? 3 : 0;
        weapon.AttributeManager.Item.NetworkedDynamicAttributes.Attributes.RemoveAll();
        ApplyAttributes(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle, paintindex, paintseed, paintwear);
        weapon.AttributeManager.Item.AttributeList.Attributes.RemoveAll();
        ApplyAttributes(weapon.AttributeManager.Item.AttributeList.Handle, paintindex, paintseed, paintwear);
        Utilities.SetStateChanged(weapon, "CEconEntity", "m_AttributeManager");
        Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_iSubclassID");
        Utilities.SetStateChanged(weapon, "CBaseEntity", "m_iGlowType");
    }

    private void ApplySkinToGloves(CCSPlayerController player, int defindex, int paintindex, int paintseed, float paintwear)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid) return;

        // Phase 1 (immediate): swap model to force visual refresh, then clear old attributes.
        var model = pawn.CBodyComponent?.SceneNode?.GetSkeletonInstance()?.ModelState.ModelName ?? string.Empty;
        if (!string.IsNullOrEmpty(model))
        {
            pawn.SetModel("characters/models/tm_jumpsuit/tm_jumpsuit_varianta.vmdl");
            pawn.SetModel(model);
        }

        CEconItemView item = pawn.EconGloves;
        item.NetworkedDynamicAttributes.Attributes.RemoveAll();
        item.AttributeList.Attributes.RemoveAll();

        // Phase 2 (80ms timer): apply defindex + attributes + bodygroup.
        // Matches WeaponPaints timing pattern to avoid race conditions.
        AddTimer(0.08f, () =>
        {
            if (!player.IsValid || !player.PawnIsAlive) return;
            var pawn2 = player.PlayerPawn.Value;
            if (pawn2 == null || !pawn2.IsValid) return;

            CEconItemView item2 = pawn2.EconGloves;
            item2.ItemDefinitionIndex = (ushort)defindex;
            UpdateEconItemId(item2);

            item2.NetworkedDynamicAttributes.Attributes.RemoveAll();
            ApplyAttributes(item2.NetworkedDynamicAttributes.Handle, paintindex, paintseed, paintwear);

            item2.AttributeList.Attributes.RemoveAll();
            ApplyAttributes(item2.AttributeList.Handle, paintindex, paintseed, paintwear);

            item2.Initialized = true;
            pawn2.AcceptInput("SetBodygroup", value: "default_gloves,1");

            Utilities.SetStateChanged(pawn2, "CCSPlayerPawn", "m_EconGloves");
            Utilities.SetStateChanged(pawn2, "CBaseModelEntity", "m_nBodyGroupChoices");
            Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInventoryServices");

            Logger.LogInformation("Gloves applied def={Def} paint={Paint} seed={Seed} wear={Wear}",
                defindex, paintindex, paintseed, paintwear);
        });
    }

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

    private void CommandG(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !player.IsValid || player.IsBot) return;

        if (info.ArgCount < 5)
        {
            player.PrintToChat(" \x01[Skins] \x07Uso: !g <defindex> <paintindex> <paintseed> <wear>");
            return;
        }

        if (!int.TryParse(info.GetArg(1), out int defindex) ||
            !int.TryParse(info.GetArg(2), out int paintindex) ||
            !int.TryParse(info.GetArg(3), out int paintseed) ||
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

        var pawn = player.PlayerPawn.Value;
        if (pawn == null) return;

        bool isKnife = KnifeDefindexes.Contains(defindex);

        if (isKnife)
        {
            // Borrar cuchillo actual
            if (pawn.WeaponServices?.MyWeapons != null)
            {
                foreach (var wh in pawn.WeaponServices.MyWeapons)
                {
                    var w = wh.Value;
                    if (w == null || !w.IsValid) continue;
                    var idx = w.AttributeManager.Item.ItemDefinitionIndex;
                    if (idx == 42 || idx == 59 || KnifeDefindexes.Contains(idx))
                    {
                        w.Remove();
                    }
                }
            }
        }
        else
        {
            // Borrar arma del mismo tipo si existe
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

        // Dar el arma nueva
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
                        ApplySkinToWeapon(weapon, paintindex, paintseed, paintwear, true, defindex);
                        appliedKnife = true;
                        break;
                    }
                }
                else
                {
                    if (weapon.AttributeManager.Item.ItemDefinitionIndex != (ushort)defindex) continue;
                    ApplySkinToWeapon(weapon, paintindex, paintseed, paintwear);
                }
            }

            if (isKnife && !appliedKnife && firstKnife != null)
            {
                ApplySkinToWeapon(firstKnife, paintindex, paintseed, paintwear, true, defindex);
            }

            if (isKnife)
            {
                Server.NextFrame(() =>
                {
                    var pawn3 = player.PlayerPawn.Value;
                    if (pawn3?.WeaponServices?.MyWeapons == null) return;

                    foreach (var wh3 in pawn3.WeaponServices.MyWeapons)
                    {
                        var weapon3 = wh3.Value;
                        if (weapon3 == null || !weapon3.IsValid) continue;
                        if (!IsKnifeEntity(weapon3) && !KnifeDefindexes.Contains(weapon3.AttributeManager.Item.ItemDefinitionIndex)) continue;
                        ApplySkinToWeapon(weapon3, paintindex, paintseed, paintwear, true, defindex);
                        player.ExecuteClientCommand("slot3");
                        break;
                    }
                });
            }

            player.PrintToChat($" \x01[Skins] \x04{weaponName} paint={paintindex} seed={paintseed} wear={paintwear:F4}");
        });
    }

    private void CommandGl(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !player.IsValid || player.IsBot) return;

        if (info.ArgCount < 5)
        {
            player.PrintToChat(" \x01[Skins] \x07Uso: !gl <defindex> <paintindex> <paintseed> <wear>");
            return;
        }

        if (!int.TryParse(info.GetArg(1), out int defindex) ||
            !int.TryParse(info.GetArg(2), out int paintindex) ||
            !int.TryParse(info.GetArg(3), out int paintseed) ||
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

        Logger.LogInformation("Command !gl def={Def} paint={Paint} seed={Seed} wear={Wear}",
            defindex, paintindex, paintseed, paintwear);

        ApplySkinToGloves(player, defindex, paintindex, paintseed,
            Math.Clamp(paintwear, 0.0001f, 1.0f));

        player.PrintToChat($" \x01[Skins] \x04Guantes {defindex} paint={paintindex} seed={paintseed} wear={paintwear:F4}");
    }
}