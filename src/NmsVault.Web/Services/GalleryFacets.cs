using NmsVault.Core;
using NmsVault.Core.Derived;

namespace NmsVault.Web.Services;

/// <summary>How a list can be ordered.</summary>
/// <param name="Key">Storage key.</param>
/// <param name="Label">How it reads in the dropdown.</param>
/// <param name="Descending">Whether larger is better, as it is for every stat.</param>
/// <param name="Value">Reads the sortable number, or null to sort by name.</param>
public readonly record struct SortOption(
    string Key,
    string Label,
    bool Descending,
    Func<GalleryRow, double>? Value = null);

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

        if (page is "Shipyard" or "Armoury")
        {
            // The class heading wears the game's own marks, which is how anyone reads a class.
            common.Insert(1, Facet<GalleryRow>.Labelled("class", "Class", r =>
                r.Class is { Length: > 0 } cls
                    ? [new FacetValue(cls, cls, $"gallery/img/class/{cls.ToLowerInvariant()}.webp")]
                    : []));

            common.Add(Facet<GalleryRow>.Labelled("tech", "Technology installed",
                r => r.Tech.Select(id => Technology(id, tech))));
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
    public static IReadOnlyList<SortOption> SortsFor(string page)
    {
        var sorts = new List<SortOption> { new("name", "Name", Descending: false) };

        // The stat labels come from the data rather than being written out again here, so a
        // page cannot offer a sort on a stat its items do not carry.
        foreach (var (key, label) in StatLabels(page))
            sorts.Add(new SortOption(key, label, Descending: true, r => Stat(r, label)));

        return sorts;
    }

    /// <summary>Applies a sort to a list of rows.</summary>
    /// <param name="rows">The rows.</param>
    /// <param name="sort">The chosen order.</param>
    /// <returns>A new, ordered list.</returns>
    public static IReadOnlyList<GalleryRow> Sort(IEnumerable<GalleryRow> rows, SortOption sort)
    {
        if (sort.Value is null)
            return [.. rows.OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase)];

        // Name breaks ties, so two ships with the same damage keep a stable, readable order
        // rather than whatever the source happened to be in.
        return
        [
            .. sort.Descending
                ? rows.OrderByDescending(sort.Value).ThenBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase)
                : rows.OrderBy(sort.Value).ThenBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase)
        ];
    }

    private static string TypeLabel(string page) => page switch
    {
        "Armoury" => "Tool type",
        "Stable" => "Creature type",
        _ => "Ship type",
    };

    private static (string Key, string Label)[] StatLabels(string page) => page switch
    {
        "Shipyard" => [("damage", "Damage"), ("shield", "Shield"),
                       ("hyperdrive", "Hyperdrive"), ("manoeuvrability", "Manoeuvrability")],
        "Armoury" => [("damage", "Damage"), ("mining", "Mining"), ("scan", "Scan")],
        _ => [],
    };

    private static double Stat(GalleryRow row, string label)
    {
        foreach (var stat in row.Stats)
            if (string.Equals(stat.Key, label, StringComparison.Ordinal)) return stat.Value;
        return 0;
    }
}
