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
    /// False. goatfungus has not been updated for 7.03 Cosmos and cannot load a current
    /// save, so this output has never been confirmed by importing it.
    /// </summary>
    public bool IsVerified => false;

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

        // The whole format: the bare entity object, deobfuscated. libNOM's implementation
        // is literally `return Data["Ship"].Serialize().GetBytes()`.
        //
        // Deliberately serialised through this project's writer rather than a general JSON
        // library, because goatfungus's Java parser only accepts \u escapes <= 255 - so
        // non-ASCII has to go out as raw UTF-8 bytes, which is exactly what NMSE's
        // serialiser does and why it was written that way.
        byte[] content = Encoding.Latin1.GetBytes(item.Payload.ToExportString());

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
        try
        {
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
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// The colours array from a CCD entry, or null. This is all Kaii and NomNom carry of a
    /// ship's customisation - the parts, textures and palette have nowhere to go.
    /// </summary>
    public static JsonArray? Colours(JsonObject? ccd)
        => ccd?.GetObject("CustomData")?.GetArray("Colours");
}
