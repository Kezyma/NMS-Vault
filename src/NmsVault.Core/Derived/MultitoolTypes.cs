using NmsVault.Json;

namespace NmsVault.Core.Derived;

/// <summary>
/// Resolves a multitool's display type.
/// </summary>
/// <remarks>
/// <para>
/// Most types are decided by the model file alone, but several share
/// <c>MULTITOOL.SCENE.MBIN</c> - pistols, rifles, alien and pristine tools all use it - so those
/// have to be told apart by something else. There is no type field in the save.
/// </para>
/// <para>
/// A shared-model tool is identified by <b>which stat range it rolled within</b> - see
/// <see cref="MultitoolStatRanges"/>. Each type has fixed bounds per class, and they separate
/// cleanly: pistols roll no damage, rifles roll no mining, and experimental tools scan far
/// higher than either.
/// </para>
/// <para>
/// The stats are the whole answer for a shared-model tool; nothing else is consulted. A tool
/// whose stats match no range at all is reported as Unknown rather than guessed at.
/// </para>
/// <para>
/// Names follow NomNom's own vocabulary, where Pistol and Rifle are distinct types and there is
/// no "Standard".
/// </para>
/// </remarks>
public static class MultitoolTypes
{
    private const string SharedModel = "MODELS/COMMON/WEAPONS/MULTITOOL/MULTITOOL.SCENE.MBIN";
    private const string Prefix = "MODELS/COMMON/WEAPONS/MULTITOOL/";

    /// <summary>Types decided by their model file, longest-specific first.</summary>
    private static readonly (string File, string Type)[] ByModel =
    [
        (Prefix + "SENTINELMULTITOOLB.SCENE.MBIN", "Sentinel B"),
        (Prefix + "SENTINELMULTITOOL.SCENE.MBIN", "Sentinel"),
        (Prefix + "ROYALMULTITOOL.SCENE.MBIN", "Royal"),
        (Prefix + "SWITCHMULTITOOL.SCENE.MBIN", "Switch"),
        (Prefix + "STAFFMULTITOOLATLAS.SCENE.MBIN", "Voltaic Staff"),
        (Prefix + "STAFFMULTITOOLRUIN.SCENE.MBIN", "Staff Ruin"),
        (Prefix + "STAFFMULTITOOLBONE.SCENE.MBIN", "Staff Bone"),
        (Prefix + "STAFFNPCMULTITOOL.SCENE.MBIN", "Staff NPC"),
        (Prefix + "STAFFMULTITOOL.SCENE.MBIN", "Staff"),
        (Prefix + "ATLASMULTITOOL.SCENE.MBIN", "Atlantid"),
        (Prefix + "SWARMMULTITOOL.SCENE.MBIN", "Direwasp Disintegrator"),
        (Prefix + "RETROMULTITOOL.SCENE.MBIN", "Starbound"),
    ];

    /// <summary>Every type detection can produce, for building a filter list.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        .. ByModel.Select(m => m.Type).Concat(MultitoolStatRanges.SharedModelTypes)
                  .Distinct().Order(StringComparer.Ordinal)
    ];

    /// <summary>Resolves a multitool's type from its object.</summary>
    public static string FromMultitool(JsonObject multitool)
    {
        string filename = multitool.GetObject("Resource")?.GetString("Filename") ?? "";

        foreach (var (file, type) in ByModel)
            if (string.Equals(file, filename, StringComparison.OrdinalIgnoreCase))
                return type;

        if (!string.Equals(filename, SharedModel, StringComparison.OrdinalIgnoreCase))
        {
            // An unknown model. Fall back to the substring family NMSE uses for archived
            // weapons, so a modded path still lands somewhere sensible.
            foreach (var (file, type) in ByModel)
                if (filename.Contains(Path.GetFileNameWithoutExtension(file).Replace(".SCENE", ""),
                        StringComparison.OrdinalIgnoreCase))
                    return type;

            return "Unknown";
        }

        return FromSharedModel(multitool);
    }

    /// <summary>
    /// Tells apart the tools that share the standard model, by the stat range they rolled
    /// within - which is the only thing in the file that distinguishes them.
    /// </summary>
    private static string FromSharedModel(JsonObject multitool)
    {
        var store = multitool.GetObject("Store");

        return MultitoolStatRanges.Match(
                store?.GetObject("Class")?.GetString("InventoryClass"),
                ItemStats.Read(store, "^WEAPON_DAMAGE"),
                ItemStats.Read(store, "^WEAPON_MINING"),
                ItemStats.Read(store, "^WEAPON_SCAN"))
            ?? "Unknown";
    }
}
