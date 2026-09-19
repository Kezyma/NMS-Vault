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
/// <b>The body comes from <c>IsLarge</c>:</b> a pistol is the small one, a rifle the large.
/// NMSE instead runs a heuristic over the base stats, and that answers "Rifle" when every stat
/// is zero - which is exactly the state of a freshly found tool, so every starter pistol came
/// out a rifle. The stat ranges still earn their place distinguishing alien and pristine tools,
/// which share a body with the ordinary ones, but only where there are stats to read.
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
        .. ByModel.Select(m => m.Type).Concat(["Pistol", "Rifle", "Alien", "Pristine"])
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

            return filename.Length == 0 ? "Unknown" : Body(multitool);
        }

        return FromSharedModel(multitool);
    }

    /// <summary>
    /// The body a shared-model tool has. <c>IsLarge</c> is the only thing in the file that
    /// says, and it is present whether or not the tool has any stats yet.
    /// </summary>
    private static string Body(JsonObject multitool)
        => multitool.Get("IsLarge") is true ? "Rifle" : "Pistol";

    /// <summary>
    /// Tells apart the tools that share the standard model. The body is decided by
    /// <c>IsLarge</c>; the stat ranges, which come from NMSE, only distinguish the alien and
    /// pristine variants, and only where there are stats to read.
    /// </summary>
    private static string FromSharedModel(JsonObject multitool)
    {
        string body = Body(multitool);

        var store = multitool.GetObject("Store");
        string? cls = store?.GetObject("Class")?.GetString("InventoryClass");

        // C=0, B=1, A=2, S=3 - NMSE's own ordering, lowest to highest.
        int classIndex = cls switch { "C" => 0, "B" => 1, "A" => 2, "S" => 3, _ => -1 };

        double damage = ItemStats.Read(store, "^WEAPON_DAMAGE");
        double mining = ItemStats.Read(store, "^WEAPON_MINING");
        double scan = ItemStats.Read(store, "^WEAPON_SCAN");

        // A tool that has not been upgraded yet has nothing to distinguish it beyond its body,
        // and guessing past that is how the starter pistols became rifles.
        if (classIndex < 0 || (damage == 0.0 && mining == 0.0 && scan == 0.0)) return body;

        if (mining > 0.0)
            return classIndex <= 1
                ? scan < 40.0 ? "Alien" : "Pristine"
                : scan >= 80.0 ? "Pristine" : "Alien";

        // Damage and scan but no mining at all: NMSE reads a C-class scan above 5 as alien.
        if (classIndex == 0 && damage > 0.0 && scan > 5.0) return "Alien";

        return body;
    }
}
