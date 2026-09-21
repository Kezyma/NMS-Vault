using System.Text;
using NmsVault.Json;

namespace NmsVault.Core.Adapters;

/// <summary>
/// Produces goatfungus NMSSaveEditor export files.
/// <para>
/// The simplest adapter: a bare game object with human-readable keys and no wrapper. It is
/// also the lossiest — goatfungus carries no customisation, no legacy-colour flag, no
/// corvette base and no accessories. See <c>docs/format-coverage.md</c>.
/// </para>
/// </summary>
public sealed class GoatfungusExportAdapter : IExportAdapter
{
    /// <inheritdoc />
    public EditorId Editor => EditorId.Goatfungus;

    /// <inheritdoc />
    public string DisplayName => "goatfungus NMSSaveEditor";

    /// <inheritdoc />
    public string HomepageUrl => "https://github.com/goatfungus/NMSSaveEditor";

    /// <summary>
    /// True, on the evidence of five files the real editor wrote.
    /// </summary>
    /// <remarks>
    /// <c>tests/fixtures/goatfungus</c> holds genuine NMSSaveEditor exports - three ships and
    /// two multitools. Each one is read by this project and written back out byte for byte,
    /// which pins the key order, the number formatting and the absence of whitespace to what
    /// the editor itself produces.
    /// <para>
    /// What that does not show is the editor accepting a file, since it cannot open a 7.03
    /// save to be given one. It shows that a file this adapter writes is indistinguishable
    /// from one the editor wrote, which is as far as the evidence goes.
    /// </para>
    /// </remarks>
    public bool IsVerified => true;

    /// <inheritdoc />
    public OneOf<string, Unsupported> Extension(EntityKind kind) => kind switch
    {
        EntityKind.Starship => ".sh0",
        EntityKind.Multitool => ".wp0",
        EntityKind.Companion => ".pet",
        EntityKind.Frigate => new Unsupported(
            "goatfungus NMSSaveEditor has no frigate format."),
        EntityKind.Freighter => new Unsupported(
            "goatfungus NMSSaveEditor has no freighter format."),
        _ => new Unsupported($"Unknown entity kind {kind}."),
    };

    /// <inheritdoc />
    public IReadOnlyList<string> LossesFor(VaultItem item)
    {
        var losses = new List<string>();

        if (item.Kind == EntityKind.Starship)
        {
            if (item.UsesLegacyColours is not null)
                losses.Add("This format does not track the use of legacy colours, so the ship may appear "
                    + "differently in-game if it is restored with this editor.");

            if (item.CharacterCustomisationData is not null
                && !CustomisationHelpers.IsDefault(item.CharacterCustomisationData))
                losses.Add("This format stores no customisation at all, so the ship will arrive in its "
                    + "default colours, with its default parts and textures.");

            if (item.ShipBase is not null)
                losses.Add("This format does not store the parts a corvette is built from, so the ship "
                    + "will arrive as an empty hull.");
        }

        if (item.Kind == EntityKind.Companion && item.AccessorySlots is { Length: > 0 })
            losses.Add("This format does not store companion accessories, so anything the creature "
                + "is wearing will be missing.");

        return losses;
    }

    /// <inheritdoc />
    public ExportResult Export(VaultItem item, ExportOptions options = default)
    {
        var extension = Extension(item.Kind);
        if (!extension.HasValue)
            throw new NotSupportedException(extension.Alternative.Reason);

        // The whole format: the bare entity object, deobfuscated, on one line. libNOM's
        // implementation is literally `return Data["Ship"].Serialize().GetBytes()`, and
        // Newtonsoft's Serialize() defaults to Formatting.None - which is what the real
        // exports in tests/fixtures/goatfungus look like. NMSE indents its own files; this
        // editor does not, and matching it is the point.
        //
        // Deliberately serialised through this project's writer rather than a general JSON
        // library, because goatfungus's Java parser only accepts \u escapes <= 255 - so
        // non-ASCII has to go out as raw UTF-8 bytes, which is exactly what NMSE's
        // serialiser does and why it was written that way.
        byte[] content = Encoding.Latin1.GetBytes(item.Payload.ToCompactString());

        string fileName = NmseExportAdapter.SanitiseFileName(item.Meta.DisplayName) + extension.Value;
        return new ExportResult(fileName, content);
    }
}

/// <summary>
/// Shared helpers for reading a <c>CharacterCustomisationData</c> entry.
/// </summary>
public static class CustomisationHelpers
{
    /// <summary>
    /// Whether a CCD entry is the blank default - all collections empty, palette and preset
    /// "^". Ported from NMSE's <c>StarshipLogic.IsCcdDefault</c>.
    /// </summary>
    public static bool IsDefault(JsonObject ccd)
    {
        // No try/catch. GetString, GetObject and GetArray all answer null rather than throwing
        // for names like these, so the catch was unreachable - and it returned true, meaning
        // "nothing here", which is the direction that makes LossesFor report no loss at all.
        string preset = ccd.GetString("SelectedPreset") ?? "";
        if (preset is not ("^" or "")) return false;

        var custom = ccd.GetObject("CustomData");
        if (custom is null) return true;

        string palette = custom.GetString("PaletteID") ?? "";
        if (palette is not ("^" or "")) return false;

        foreach (var name in (string[])["DescriptorGroups", "Colours", "TextureOptions", "BoneScales"])
            if (custom.GetArray(name) is { Length: > 0 }) return false;

        return true;
    }

    /// <summary>
    /// What a format that carries only <c>CustomData.Colours</c> drops from a ship's
    /// customisation, as sentences for the reader.
    /// </summary>
    /// <remarks>
    /// One copy, called by both the Kaii and the NomNom adapter. They each had their own and
    /// both were incomplete in the same way: they tested three of the five things
    /// <see cref="IsDefault"/> considers, so a ship whose only customisation was a preset or a
    /// set of bone scales reported <em>no losses at all</em> - the dialog said nothing would be
    /// lost, and it was.
    /// </remarks>
    /// <param name="ccd">The customisation block, or null.</param>
    /// <returns>A sentence per thing that will not survive. Empty when nothing is lost.</returns>
    public static IReadOnlyList<string> Losses(JsonObject? ccd)
    {
        if (ccd is null || IsDefault(ccd)) return [];

        var losses = new List<string>();
        var custom = ccd.GetObject("CustomData");

        if (custom?.GetArray("DescriptorGroups") is { Length: > 0 })
            losses.Add("This format does not store custom parts, so the ship will arrive as the "
                + "base model in the right colours.");

        if (custom?.GetArray("TextureOptions") is { Length: > 0 })
            losses.Add("This format does not store the chosen texture, so the ship's finish will "
                + "differ in-game.");

        if (custom?.GetString("PaletteID") is { } p && p is not ("^" or ""))
            losses.Add($"This format does not store the colour palette ({p}), so the ship may "
                + "appear in different colours.");

        if (ccd.GetString("SelectedPreset") is { } preset && preset is not ("^" or ""))
            losses.Add($"This format does not store the chosen preset ({preset}), so the ship will "
                + "arrive without it.");

        if (custom?.GetArray("BoneScales") is { Length: > 0 })
            losses.Add("This format does not store the adjusted proportions, so the ship will "
                + "arrive at its default shape.");

        return losses;
    }

    /// <summary>
    /// The colours array from a CCD entry, or null. This is all Kaii and NomNom carry of a
    /// ship's customisation - the parts, textures and palette have nowhere to go.
    /// </summary>
    public static JsonArray? Colours(JsonObject? ccd)
        => ccd?.GetObject("CustomData")?.GetArray("Colours");
}
