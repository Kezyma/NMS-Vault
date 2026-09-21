using NmsVault.Core;
using NmsVault.Core.Derived;

namespace NmsVault.Web.Services;

/// <summary>How a list can be ordered.</summary>
/// <param name="Key">Storage key.</param>
/// <param name="Label">How it reads in the dropdown, and in the table heading it sorts.</param>
/// <param name="Descending">
/// The direction to start in. True where larger is better, as it is for every stat and for
/// class; false where the order is a reading order, as it is for a name.
/// </param>
/// <param name="Value">Reads the sortable number, where the column holds one.</param>
/// <param name="Text">Reads the sortable text, where it holds words instead.</param>
public readonly record struct SortOption(
    string Key,
    string Label,
    bool Descending,
    Func<GalleryRow, double>? Value = null,
    Func<GalleryRow, string>? Text = null);

/// <summary>
/// The filter headings and sort orders each page offers.
/// </summary>
/// <remarks>
/// Adding a filter is one entry here; the filter bar and the dropdown build themselves from
/// these tables. What each page offers differs because the underlying data does: companions
/// have no inventory class and no base stats, so a class filter and a stat sort would be
/// empty controls rather than useful ones.
/// </remarks>
public static class GalleryFacets
{
    /// <summary>The headings a page offers.</summary>
    /// <param name="page">Which page.</param>
    /// <param name="tech">
    /// The technology lookup, used to give the technology heading real names and icons. Pass
    /// <see cref="TechIndex.Empty"/> where it has not loaded; the heading then shows raw ids,
    /// which is worse but not broken.
    /// </param>
    /// <returns>Its headings, in the order they should appear.</returns>
    public static IReadOnlyList<Facet<GalleryRow>> For(string page, TechIndex? tech = null)
    {
        var common = new List<Facet<GalleryRow>>
        {
            Facet<GalleryRow>.One("type", TypeLabel(page), r => r.Type),
            Facet<GalleryRow>.Many("tags", "Tags", r => r.Tags),
        };

        if (page is "Stable")
        {
            // Affinity wears the game's own glyphs, the same way class does for a ship: it is
            // what a creature is judged on, and eight of them group a stable properly where
            // the type does not - every creature has a species of its own.
            common.Insert(1, Facet<GalleryRow>.Labelled("affinity", "Affinity", r =>
                r.Affinity is { } affinity
                    ? [new FacetValue(affinity.Id, affinity.Name,
                        affinity.Icon is { Length: > 0 } icon ? $"gallery/img/affinity/{icon}" : null)]
                    : []));

            // The game's own word for the world - Verdant, Airless - rather than the payload's
            // raw biome, so the heading offers what the table and the item view show.
            common.Insert(2, Facet<GalleryRow>.One("climate", "Climate", r => r.Nature?.Climate ?? r.Biome));

            // The rest of what a creature is, each already resolved into the word it displays.
            common.Insert(3, Facet<GalleryRow>.One("rarity", "Rarity", r => r.Nature?.Rarity));
            common.Insert(4, Facet<GalleryRow>.One("movement", "Movement", r => r.Nature?.Movement));
            common.Insert(5, Facet<GalleryRow>.One("egg", "Egg", r => r.Nature?.Egg));

            // A yes-or-no reads as a heading with two entries rather than as a switch, which
            // is what the others here are and what keeps the bar looking like one thing.
            common.Insert(6, Facet<GalleryRow>.One("predator", "Predator",
                r => r.Nature is { } nature ? (nature.IsPredator ? "Yes" : "No") : null));
        }

        // Coarser than the type and, for ships, the only thing that does group - a unique hull
        // takes its own name as its type, so seventeen ships have eleven types between them.
        if (page is "Shipyard")
            common.Insert(1, Facet<GalleryRow>.One("family", "Hull", r => Hull(r.Family)));

        if (page is "Shipyard" or "Armoury")
        {
            // The class heading wears the game's own marks, which is how anyone reads a class.
            common.Insert(1, Facet<GalleryRow>.Labelled("class", "Class", r =>
                r.Class is { Length: > 0 } cls
                    ? [new FacetValue(cls, cls, $"gallery/img/class/{cls.ToLowerInvariant()}.webp")]
                    : []));

            // Named technologies only - see GalleryRow.NamedTech for why the rolled upgrade
            // modules are left out.
            common.Add(Facet<GalleryRow>.Labelled("tech", "Technology installed",
                r => r.NamedTech.Select(id => Technology(id, tech))));
        }

        return common;
    }

    /// <summary>
    /// One installed technology as something a reader can recognise. "^UP_PULSE4" is not a
    /// thing anyone is looking for; "Instability Drive" with its own icon is.
    /// </summary>
    private static FacetValue Technology(string id, TechIndex? tech)
    {
        var entry = tech?.Find(id);
        if (entry is null) return new FacetValue(id);

        return new FacetValue(
            id,
            entry.Name,
            entry.Icon is { Length: > 0 } icon ? $"gallery/img/tech/{icon}" : null);
    }

    /// <summary>The sort orders a page offers, best default first.</summary>
    /// <param name="page">Which page.</param>
    /// <returns>Its sort orders.</returns>
    /// <remarks>
    /// One list serves both views. The cards offer it as a dropdown and the table offers it
    /// as clickable headings, and they are the same orders because a reader who sorted by
    /// class in one view and switched to the other would otherwise find the list silently
    /// reordered - or worse, a dropdown showing an order the list is not in.
    /// <para>
    /// Every entry is matched to its table column by <see cref="SortOption.Label"/>, so a
    /// label here is the heading there and the two cannot drift apart.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<SortOption> SortsFor(string page)
    {
        var sorts = new List<SortOption>
        {
            new("name", "Name", Descending: false, Text: r => r.DisplayName),
            new("type", "Type", Descending: false, Text: r => r.Type),
        };

        // Class reads best from S downwards, which is neither alphabetical nor the order the
        // letters fall in - hence a rank rather than the letter itself.
        if (page is "Shipyard" or "Armoury")
            sorts.Add(new SortOption("class", "Class", Descending: true, r => ClassRank(r.Class)));

        // A creature's equivalent: the thing it is grouped by rather than ranked on, so it
        // sorts by name and gathers the tropical ones together.
        if (page is "Stable")
            sorts.Add(new SortOption("affinity", "Affinity", Descending: false,
                Text: r => r.Affinity?.Name ?? ""));

        // The stat labels come from the data rather than being written out again here, so a
        // page cannot offer a sort on a stat its items do not carry.
        foreach (var (key, label) in StatLabels(page))
            sorts.Add(new SortOption(key, label, Descending: true, r => Stat(r, label)));

        return sorts;
    }

    /// <summary>Where a class sits, best first. Anything unknown sorts below C.</summary>
    private static double ClassRank(string? cls) => cls?.ToUpperInvariant() switch
    {
        "S" => 4,
        "A" => 3,
        "B" => 2,
        "C" => 1,
        _ => 0,
    };

    /// <summary>Applies a sort to a list of rows.</summary>
    /// <param name="rows">The rows.</param>
    /// <param name="sort">The chosen order.</param>
    /// <returns>A new, ordered list.</returns>
    public static IReadOnlyList<GalleryRow> Sort(IEnumerable<GalleryRow> rows, SortOption sort)
    {
        // Name breaks ties in every case, so two ships with the same damage keep a stable,
        // readable order rather than whatever the source happened to be in. It also means the
        // name sort needs no tiebreak of its own.
        IOrderedEnumerable<GalleryRow> ordered = (sort.Value, sort.Text) switch
        {
            ({ } value, _) => sort.Descending
                ? rows.OrderByDescending(value)
                : rows.OrderBy(value),

            (_, { } text) => sort.Descending
                ? rows.OrderByDescending(text, StringComparer.OrdinalIgnoreCase)
                : rows.OrderBy(text, StringComparer.OrdinalIgnoreCase),

            _ => rows.OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase),
        };

        return [.. ordered.ThenBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// A ship family as something a reader recognises.
    /// </summary>
    /// <remarks>
    /// The enum names are the game's internals - an <c>AlienShip</c> is what everyone else
    /// calls a living ship, and a <c>RobotShip</c> is a sentinel interceptor. What the
    /// distinction is actually for is that each takes its own technology.
    /// </remarks>
    private static string? Hull(string family) => family switch
    {
        "Ship" => "Standard",
        "AlienShip" => "Living",
        "RobotShip" => "Sentinel",
        "Corvette" => "Corvette",
        _ => null,
    };

    private static string TypeLabel(string page) => page switch
    {
        "Armoury" => "Tool type",
        "Stable" => "Creature type",
        _ => "Ship type",
    };

    /// <summary>
    /// The stats a page can order by, in the order they appear on a card and as columns.
    /// </summary>
    /// <remarks>
    /// The derived slot counts are here alongside the game's own stats because a reader sorts
    /// by them the same way. Their labels come from <see cref="SlotCounts"/> rather than being
    /// written out again, so a heading, a card label and a sort cannot drift apart.
    /// </remarks>
    private static (string Key, string Label)[] StatLabels(string page) => page switch
    {
        "Shipyard" => [("damage", "Damage"), ("shield", "Shield"),
                       ("hyperdrive", "Hyperdrive"), ("manoeuvrability", "Manoeuvrability"),
                       ("techslots", SlotCounts.TechSlotsLabel),
                       ("storage", SlotCounts.StorageLabel),
                       ("techinstalled", SlotCounts.TechInstalledLabel)],

        "Armoury" => [("damage", "Damage"), ("mining", "Mining"), ("scan", "Scan"),
                      ("techslots", SlotCounts.TechSlotsLabel),
                      ("techinstalled", SlotCounts.TechInstalledLabel)],

        // Nothing. A creature has no number that ranks it against another creature: scale is
        // a size rather than a score, trust and the moods drift with play, and each of the
        // three traits names one end of an axis, so sorting on the stored value put the
        // gentlest creature and the fiercest at opposite ends of one scale. What is left to
        // order a stable by is its name, its species and its affinity, which are above.
        "Stable" => [],

        _ => [],
    };

    private static double Stat(GalleryRow row, string label)
    {
        foreach (var stat in row.Stats)
            if (string.Equals(stat.Key, label, StringComparison.Ordinal)) return stat.Value;
        return 0;
    }
}
