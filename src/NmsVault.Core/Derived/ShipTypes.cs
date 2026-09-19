using NmsVault.Json;

namespace NmsVault.Core.Derived;

/// <summary>
/// How a ship's model resource maps to a display type, and what family that type belongs to.
/// </summary>
/// <param name="Type">Display name, e.g. "Fighter", "Living Ship", "Golden Vector".</param>
/// <param name="Family">
/// The game's own ownership category, which governs what technology will fit.
/// </param>
/// <param name="IsModified">
/// True when the resource path did not match a known ship exactly and was resolved by keyword.
/// NMSE surfaces this as "(Modified)" - it usually means a hand-edited or modded resource path.
/// </param>
public readonly record struct ShipType(string Type, ShipFamily Family, bool IsModified)
{
    /// <summary>The display type with NMSE's "(Modified)" suffix where that applies.</summary>
    public string Display => IsModified ? $"{Type} (Modified)" : Type;
}

/// <summary>The ownership families the game distinguishes. Determines which tech fits.</summary>
public enum ShipFamily
{
    /// <summary>An ordinary starship: fighter, hauler, explorer, shuttle, exotic, solar.</summary>
    Ship,

    /// <summary>A living ship. Takes organic technology only.</summary>
    AlienShip,

    /// <summary>A sentinel interceptor. Takes its own technology set.</summary>
    RobotShip,

    /// <summary>A corvette, which is built from base parts rather than being a single model.</summary>
    Corvette,
}

/// <summary>
/// Resolves a starship's display type from its <c>Resource.Filename</c>.
/// </summary>
/// <remarks>
/// <para>
/// Ported from NMSE's <c>StarshipLogic.ShipInfo</c> and <c>GetShipInfo</c>. Resolution is
/// three-tier: exact path match, then keyword substring, then Unknown.
/// </para>
/// <para>
/// The table is an <b>ordered array</b>, not a dictionary, and that is deliberate. The keyword
/// pass returns the first entry whose keyword appears anywhere in the path, so order decides
/// the answer - <c>FIGHTER</c> is tested before <c>FIGHTERCLASSICGOLD</c>, and a near-miss
/// path containing both resolves to Fighter. NMSE iterates a <c>Dictionary</c>'s values and
/// relies on insertion order, which .NET does not actually guarantee; an array makes the same
/// behaviour deterministic.
/// </para>
/// </remarks>
public static class ShipTypes
{
    private readonly record struct Entry(string Path, string Type, string Keyword, ShipFamily Family);

    // Order matters - see the class remarks. Keep it identical to NMSE's table.
    private static readonly Entry[] Table =
    [
        new("MODELS/COMMON/SPACECRAFT/DROPSHIPS/DROPSHIP_PROC.SCENE.MBIN", "Hauler", "DROPSHIP", ShipFamily.Ship),
        new("MODELS/COMMON/SPACECRAFT/SCIENTIFIC/SCIENTIFIC_PROC.SCENE.MBIN", "Explorer", "SCIENTIFIC", ShipFamily.Ship),
        new("MODELS/COMMON/SPACECRAFT/SHUTTLE/SHUTTLE_PROC.SCENE.MBIN", "Shuttle", "SHUTTLE", ShipFamily.Ship),
        new("MODELS/COMMON/SPACECRAFT/FIGHTERS/FIGHTER_PROC.SCENE.MBIN", "Fighter", "FIGHTER", ShipFamily.Ship),
        new("MODELS/COMMON/SPACECRAFT/S-CLASS/S-CLASS_PROC.SCENE.MBIN", "Exotic", "EXOTIC", ShipFamily.Ship),
        new("MODELS/COMMON/SPACECRAFT/S-CLASS/BIOPARTS/BIOSHIP_PROC.SCENE.MBIN", "Living Ship", "BIOSHIP", ShipFamily.AlienShip),
        new("MODELS/COMMON/SPACECRAFT/SAILSHIP/SAILSHIP_PROC.SCENE.MBIN", "Solar", "SAILSHIP", ShipFamily.Ship),
        new("MODELS/COMMON/SPACECRAFT/FIGHTERS/VRSPEEDER.SCENE.MBIN", "Utopia Speeder", "VRSPEEDER", ShipFamily.Ship),
        new("MODELS/COMMON/SPACECRAFT/FIGHTERS/FIGHTERCLASSICGOLD.SCENE.MBIN", "Golden Vector", "FIGHTERCLASSICGOLD", ShipFamily.Ship),
        new("MODELS/COMMON/SPACECRAFT/FIGHTERS/FIGHTERSPECIALSWITCH.SCENE.MBIN", "Horizon Vector NX (Switch)", "FIGHTERSPECIALSWITCH", ShipFamily.Ship),
        new("MODELS/COMMON/SPACECRAFT/SENTINELSHIP/SENTINELSHIP_PROC.SCENE.MBIN", "Sentinel", "SENTINEL", ShipFamily.RobotShip),
        // The keyword carries ".SCENE" so it cannot also match WRACERSE below.
        new("MODELS/COMMON/SPACECRAFT/FIGHTERS/WRACER.SCENE.MBIN", "Starborn Runner", "WRACER.SCENE", ShipFamily.Ship),
        new("MODELS/COMMON/SPACECRAFT/FIGHTERS/WRACERSE.SCENE.MBIN", "Starborn Phoenix", "WRACERSE", ShipFamily.Ship),
        new("MODELS/COMMON/SPACECRAFT/BIGGS/BIGGS.SCENE.MBIN", "Corvette", "BIGGS", ShipFamily.Corvette),
        new("MODELS/COMMON/SPACECRAFT/FIGHTERS/SPOOKSHIP.SCENE.MBIN", "Boundary Herald", "SPOOKSHIP", ShipFamily.Ship),
        new("MODELS/COMMON/SPACECRAFT/S-CLASS/BIOPARTS/BIOFIGHTER.SCENE.MBIN", "The Wraith", "BIOFIGHTER", ShipFamily.AlienShip),
        new("MODELS/COMMON/SPACECRAFT/FIGHTERS/RASAMAMAGOLD.SCENE.MBIN", "Golden Rasamama S36", "RASAMAMAGOLD", ShipFamily.Ship),
        new("MODELS/COMMON/SPACECRAFT/FIGHTERS/VINTAGEINTERCEPTOR.SCENE.MBIN", "Vintage Interceptor", "VINTAGEINTERCEPTOR", ShipFamily.RobotShip),
    ];

    /// <summary>Every distinct display type, for building a filter list.</summary>
    public static IReadOnlyList<string> All { get; } =
        [.. Table.Select(e => e.Type).Distinct().Order(StringComparer.Ordinal)];

    /// <summary>Resolves a ship's type from its resource filename.</summary>
    public static ShipType FromFilename(string? filename)
    {
        if (string.IsNullOrEmpty(filename))
            return new ShipType("Unknown", ShipFamily.Ship, IsModified: false);

        foreach (var entry in Table)
            if (string.Equals(entry.Path, filename, StringComparison.OrdinalIgnoreCase))
                return new ShipType(entry.Type, entry.Family, IsModified: false);

        foreach (var entry in Table)
            if (filename.Contains(entry.Keyword, StringComparison.OrdinalIgnoreCase))
                return new ShipType(entry.Type, entry.Family, IsModified: true);

        return new ShipType("Unknown", ShipFamily.Ship, IsModified: true);
    }

    /// <summary>Resolves a ship's type from its game object.</summary>
    public static ShipType FromShip(JsonObject ship)
        => FromFilename(ship.GetObject("Resource")?.GetString("Filename"));

    /// <summary>
    /// Whether a resource path is a corvette. Corvettes are built from base parts, so they
    /// carry a <c>PersistentPlayerBases</c> entry that the ship object alone does not describe.
    /// </summary>
    public static bool IsCorvette(string? filename)
        => filename?.Contains("BIGGS", StringComparison.OrdinalIgnoreCase) == true;
}
