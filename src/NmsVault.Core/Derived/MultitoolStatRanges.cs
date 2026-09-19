namespace NmsVault.Core.Derived;

/// <summary>The base stat range one multitool type rolls within at one class.</summary>
/// <param name="Type">The display type.</param>
/// <param name="Class">Inventory class - C, B, A or S.</param>
/// <param name="DamageMin">Lowest damage.</param>
/// <param name="DamageMax">Highest damage.</param>
/// <param name="MiningMin">Lowest mining.</param>
/// <param name="MiningMax">Highest mining.</param>
/// <param name="ScanMin">Lowest scanning.</param>
/// <param name="ScanMax">Highest scanning.</param>
public readonly record struct MultitoolStatRange(
    string Type,
    string Class,
    double DamageMin,
    double DamageMax,
    double MiningMin,
    double MiningMax,
    double ScanMin,
    double ScanMax)
{
    /// <summary>Whether a set of stats falls inside this range.</summary>
    /// <param name="damage">Weapon damage.</param>
    /// <param name="mining">Mining.</param>
    /// <param name="scan">Scanning.</param>
    /// <returns>True when all three are within bounds.</returns>
    public bool Contains(double damage, double mining, double scan)
        => damage >= DamageMin && damage <= DamageMax
        && mining >= MiningMin && mining <= MiningMax
        && scan >= ScanMin && scan <= ScanMax;
}

/// <summary>
/// The base stat ranges each multitool type rolls within, per class.
/// </summary>
/// <remarks>
/// <para>
/// This is what actually identifies a tool that shares the standard model. Most types are
/// decided by their model file, but pistols, rifles, experimental and alien tools all use
/// <c>MULTITOOL.SCENE.MBIN</c>, and there is no type field in the save - the stats are the only
/// thing that separates them.
/// </para>
/// <para>
/// Two pairs are indistinguishable by stats alone. <b>Experimental and Royal share a range
/// exactly</b>, and <b>Sentinel and Staff differ only in S-class scanning</b> (50 against 55).
/// In both cases the other member has its own model file, so the ambiguity never has to be
/// resolved from stats: only the shared-model types are matched against this table.
/// </para>
/// <para>
/// Note that all-zero stats fall inside <b>Rifle C</b> exactly - damage 0 to 5, mining 0, scan
/// 0 to 5 - and inside no other range. Scripted tools such as the one from the crashed ship
/// carry no rolled bonuses at all, so they resolve as C-class rifles by these ranges.
/// </para>
/// </remarks>
public static class MultitoolStatRanges
{
    /// <summary>
    /// Every range, per type and class. Pistols roll no damage and rifles no mining, which is
    /// why those columns are zero to zero rather than absent.
    /// </summary>
    public static readonly MultitoolStatRange[] All =
    [
        new("Pistol", "C", 0, 0, 5, 10, 10, 20),
        new("Pistol", "B", 0, 0, 10, 15, 25, 30),
        new("Pistol", "A", 0, 0, 15, 20, 35, 40),
        new("Pistol", "S", 0, 0, 20, 35, 45, 50),

        new("Rifle", "C", 0, 5, 0, 0, 0, 5),
        new("Rifle", "B", 5, 10, 0, 0, 5, 10),
        new("Rifle", "A", 10, 15, 0, 0, 10, 15),
        new("Rifle", "S", 15, 20, 0, 0, 15, 20),

        new("Experimental", "C", 0, 5, 5, 10, 40, 50),
        new("Experimental", "B", 5, 10, 10, 20, 60, 70),
        new("Experimental", "A", 10, 15, 20, 25, 80, 90),
        new("Experimental", "S", 15, 25, 25, 30, 100, 100),

        new("Alien", "C", 10, 15, 0, 5, 20, 25),
        new("Alien", "B", 15, 20, 5, 10, 30, 35),
        new("Alien", "A", 20, 25, 10, 15, 40, 45),
        new("Alien", "S", 25, 35, 15, 20, 50, 60),

        // Identical to Experimental. Royal has its own model file, which is what tells them
        // apart - these rows are here so the table is the whole truth rather than a subset.
        new("Royal", "C", 0, 5, 5, 10, 40, 50),
        new("Royal", "B", 5, 10, 10, 20, 60, 70),
        new("Royal", "A", 10, 15, 20, 25, 80, 90),
        new("Royal", "S", 15, 25, 25, 30, 100, 100),

        new("Sentinel", "C", 10, 20, 0, 5, 20, 25),
        new("Sentinel", "B", 15, 25, 5, 10, 30, 35),
        new("Sentinel", "A", 20, 30, 5, 10, 35, 45),
        new("Sentinel", "S", 25, 50, 10, 15, 40, 50),

        new("Atlantid", "C", 5, 10, 0, 15, 20, 25),
        new("Atlantid", "B", 5, 10, 15, 25, 30, 35),
        new("Atlantid", "A", 10, 15, 20, 30, 35, 45),
        new("Atlantid", "S", 10, 20, 35, 50, 40, 50),

        // Sentinel's ranges but for S-class scanning, which reaches 55 rather than 50.
        new("Staff", "C", 10, 20, 0, 5, 20, 25),
        new("Staff", "B", 15, 25, 5, 10, 30, 35),
        new("Staff", "A", 20, 30, 5, 10, 35, 45),
        new("Staff", "S", 25, 50, 10, 15, 40, 55),
    ];

    /// <summary>
    /// The types that share <c>MULTITOOL.SCENE.MBIN</c> and therefore have to be told apart by
    /// their stats. Everything else is identified by its own model file.
    /// </summary>
    public static readonly string[] SharedModelTypes = ["Pistol", "Rifle", "Experimental", "Alien"];

    /// <summary>
    /// The shared-model type whose range these stats fall inside, or null when none does.
    /// </summary>
    /// <param name="cls">Inventory class - C, B, A or S.</param>
    /// <param name="damage">Weapon damage.</param>
    /// <param name="mining">Mining.</param>
    /// <param name="scan">Scanning.</param>
    /// <returns>The matching type, or null when the stats match nothing.</returns>
    public static string? Match(string? cls, double damage, double mining, double scan)
    {
        if (cls is null) return null;

        string? found = null;

        foreach (var range in All)
        {
            if (!SharedModelTypes.Contains(range.Type, StringComparer.Ordinal)) continue;
            if (!string.Equals(range.Class, cls, StringComparison.OrdinalIgnoreCase)) continue;
            if (!range.Contains(damage, mining, scan)) continue;

            // Two shared-model types claiming the same stats would make the answer depend on
            // table order, which is not an answer. Say nothing rather than guess.
            if (found is not null && found != range.Type) return null;
            found = range.Type;
        }

        return found;
    }
}
