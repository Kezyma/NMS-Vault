using NmsVault.Json;

namespace NmsVault.Core.Derived;

/// <summary>
/// Resolves a multitool's display type.
/// </summary>
/// <remarks>
/// <para>
/// Ported from NMSE's <c>MultitoolLogic</c>. Most types are decided by the model file alone,
/// but <b>five share one model</b> - Standard, Rifle, Alien, Pristine and Experimental all use
/// <c>MULTITOOL.SCENE.MBIN</c> - so those are separated by a heuristic over the tool's base
/// stats and class. There is nothing in the file that says which one it is.
/// </para>
/// <para>
/// Experimental is never produced by detection; NMSE offers it in its UI only.
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
        .. ByModel.Select(m => m.Type).Concat(["Standard", "Rifle", "Alien", "Pristine"])
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

            return filename.Length == 0 ? "Unknown" : "Standard";
        }

        return FromSharedModelStats(multitool);
    }

    /// <summary>
    /// Separates the five types that share a model, using the stat and class ranges NMSE
    /// documents. Ported from <c>MultitoolLogic.cs:186-231</c>.
    /// </summary>
    private static string FromSharedModelStats(JsonObject multitool)
    {
        var store = multitool.GetObject("Store");
        string? cls = store?.GetObject("Class")?.GetString("InventoryClass");

        // C=0, B=1, A=2, S=3. NMSE's own ordering, lowest to highest.
        int classIndex = cls switch { "C" => 0, "B" => 1, "A" => 2, "S" => 3, _ => -1 };
        if (classIndex < 0) return "Standard";

        double damage = ItemStats.Read(store, "^WEAPON_DAMAGE");
        double mining = ItemStats.Read(store, "^WEAPON_MINING");
        double scan = ItemStats.Read(store, "^WEAPON_SCAN");

        if (damage == 0.0)
        {
            if (classIndex != 0) return "Standard";
            if (mining == 0.0) return "Rifle";
            return scan > 20.0 ? "Pristine" : "Standard";
        }

        if (mining == 0.0)
            return classIndex == 0 && scan > 5.0 ? "Alien" : "Rifle";

        if (classIndex <= 1)
            return scan < 40.0 ? "Alien" : "Pristine";

        return scan >= 80.0 ? "Pristine" : "Alien";
    }
}
