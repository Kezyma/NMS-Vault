using System.Text;
using NmsVault.Json;

namespace NmsVault.Core.Adapters;

/// <summary>
/// Produces NMS Companion (Dr. Kaii) export files — the format libNOM calls "Kaii".
/// </summary>
/// <remarks>
/// <para>
/// Structure taken from libNOM.collect's <c>ExportKaii()</c>: readable envelope keys around
/// an obfuscated payload, with the ship nested under hardcoded obfuscated keys.
/// </para>
/// <para>
/// <b>Unverified.</b> NMS Companion has not been updated for 7.03 Cosmos and cannot load a
/// current save, so nothing here has been confirmed by importing the output. The shapes come
/// from libNOM's writers, which is second-hand. The obfuscation, at least, uses a current
/// 7.03 table rather than libNOM's own, which maps for NMS 5.5.
/// </para>
/// </remarks>
public sealed class CompanionExportAdapter : IExportAdapter
{
    private readonly JsonNameMapper _mapper;

    /// <param name="mapper">
    /// The key mapping table. Must be current for the game version the items were captured
    /// from: unmapped keys pass through readable, producing a half-obfuscated file rather
    /// than an error.
    /// </param>
    public CompanionExportAdapter(JsonNameMapper mapper) => _mapper = mapper;

    /// <inheritdoc />
    public EditorId Editor => EditorId.Companion;

    /// <inheritdoc />
    public string DisplayName => "NMS Companion";

    /// <inheritdoc />
    public string HomepageUrl => "https://www.nexusmods.com/nomanssky/mods/1879";

    /// <summary>False — see the remarks on this class.</summary>
    public bool IsVerified => false;

    /// <summary>The number of image slots this format has: <c>Thumbnail</c> plus 2-6.</summary>
    private const int ThumbnailSlots = 6;

    /// <inheritdoc />
    public OneOf<string, Unsupported> Extension(EntityKind kind) => kind switch
    {
        EntityKind.Starship => ".shp",
        EntityKind.Multitool => ".mlt",
        EntityKind.Companion => ".cmp",
        EntityKind.Frigate => ".flt",
        EntityKind.Freighter => ".frt",
        _ => new Unsupported($"Unknown entity kind {kind}."),
    };

    /// <inheritdoc />
    public IReadOnlyList<string> LossesFor(VaultItem item)
    {
        var losses = new List<string>();

        // This format carries CustomData.Colours only - verified from libNOM's own JSONPath,
        // which terminates at .Colours. Everything else in the block has nowhere to go, and
        // CustomisationHelpers.Losses is the one list of what that means.
        if (item.Kind == EntityKind.Starship)
            losses.AddRange(CustomisationHelpers.Losses(item.CharacterCustomisationData));

        if (item.Kind == EntityKind.Starship && item.ShipBase is not null)
            losses.Add("This format does not store the parts a corvette is built from, so the ship "
                + "will arrive as an empty hull.");

        if (item.Kind == EntityKind.Freighter)
            losses.Add("This format does not store a freighter's cargo inventory or its colours, so "
                + "both will be lost.");

        return losses;
    }

    /// <inheritdoc />
    public ExportResult Export(VaultItem item, ExportOptions options = default)
    {
        var extension = Extension(item.Kind);
        if (!extension.HasValue)
            throw new NotSupportedException(extension.Alternative.Reason);

        var root = new JsonObject();

        if (item.Kind == EntityKind.Starship)
        {
            // libNOM nests the ship under hardcoded obfuscated keys:
            //   { "Ship": { "@Cs": <ship>, "4hl": <legacy colours> }, ... }
            // @Cs is ShipOwnership and 4hl is ShipUsesLegacyColours. They are written
            // literally there, so they are written literally here rather than being looked
            // up - if the table ever disagreed, matching the real file matters more.
            var ship = new JsonObject();
            ship.Set("@Cs", KeyObfuscator.Obfuscate(item.Payload, _mapper));
            ship.Set("4hl", item.UsesLegacyColours ?? false);
            root.Set("Ship", ship);

            var colours = CustomisationHelpers.Colours(item.CharacterCustomisationData);
            root.Set("Colours", colours is null ? null : KeyObfuscator.Obfuscate(colours, _mapper));
        }
        else
        {
            root.Set(EnvelopeKey(item.Kind), KeyObfuscator.Obfuscate(item.Payload, _mapper));

            if (item.Kind == EntityKind.Companion)
            {
                // Kaii stores the accessory wrapper object, not the flat array the vault
                // holds, so re-wrap: { Data: [slot, slot, slot] }.
                root.Set("Accessories", item.AccessorySlots is { } slots
                    ? WrapAccessories(slots, _mapper)
                    : null);
            }
        }

        root.Set("Description", item.Meta.Description);
        root.Set("FileVersion", 1);

        // Thumbnail, Thumbnail2 .. Thumbnail6 - note there is no "Thumbnail1".
        // Absent slots are written as explicit nulls, matching libNOM.
        var images = options.Images ?? [];
        for (int slot = 0; slot < ThumbnailSlots; slot++)
        {
            string key = slot == 0 ? "Thumbnail" : $"Thumbnail{slot + 1}";
            root.Set(key, slot < images.Count ? Convert.ToBase64String(images[slot]) : null);
        }

        byte[] content = Encoding.Latin1.GetBytes(root.ToExportString());
        string fileName = NmseExportAdapter.SanitiseFileName(item.Meta.DisplayName) + extension.Value;
        return new ExportResult(fileName, content);
    }

    /// <summary>
    /// The root key this format uses per kind. Note the capital T in <c>MultiTool</c> -
    /// NomNom spells the same thing <c>Multitool</c>, and getting it wrong produces a file
    /// the editor cannot read.
    /// </summary>
    private static string EnvelopeKey(EntityKind kind) => kind switch
    {
        EntityKind.Multitool => "MultiTool",
        EntityKind.Companion => "Companion",
        EntityKind.Frigate => "Frigate",
        EntityKind.Freighter => "Freighter",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>
    /// Wraps the accessory slots the way this editor expects them.
    /// </summary>
    /// <remarks>
    /// Obfuscated, like every other value in the document. Writing them readable produced
    /// exactly the half-obfuscated file KeyObfuscator's own docs describe as the failure mode -
    /// and our importer tolerates it, because ToName passes readable names straight through, so
    /// a round trip through this project would never have shown it.
    /// </remarks>
    private static JsonObject WrapAccessories(JsonArray slots, JsonNameMapper mapper)
    {
        var wrapper = new JsonObject();
        wrapper.Set("Data", KeyObfuscator.Obfuscate(slots, mapper));
        return wrapper;
    }
}
