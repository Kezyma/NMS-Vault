namespace NmsVault.Core;

/// <summary>
/// Turns a display name into a URL-safe permalink slug.
/// </summary>
/// <remarks>
/// An item's id is its permalink and must stay stable once published, so this is
/// deliberately simple and total: lowercase ASCII alphanumerics, everything else collapsed
/// to single dashes. Anything cleverer (transliteration, stop-word removal) would risk
/// changing its mind between versions and breaking links.
/// </remarks>
public static class Slug
{
    /// <summary>Folds a display name into a slug.</summary>
    /// <param name="name">The display name.</param>
    /// <returns>A URL-safe slug, or "item" when nothing usable survives folding.</returns>
    public static string From(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length);
        bool lastWasDash = false;

        foreach (char c in name.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                sb.Append(c);
                lastWasDash = false;
            }
            else if (!lastWasDash && sb.Length > 0)
            {
                sb.Append('-');
                lastWasDash = true;
            }
        }

        return sb.ToString().Trim('-') is { Length: > 0 } slug ? slug : "item";
    }
}
