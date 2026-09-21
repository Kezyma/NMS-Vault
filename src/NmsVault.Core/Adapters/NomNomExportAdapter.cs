using System.Text;
using NmsVault.Json;

namespace NmsVault.Core.Adapters;

/// <summary>
/// Produces NomNom export files — the format libNOM calls "Standard".
/// </summary>
/// <remarks>
/// <para>
/// Structure taken from libNOM.collect's <c>CollectionItem.ExportStandard()</c>: a readable
/// envelope of <c>Data</c>, <c>DateCreated</c>, <c>Description</c>, <c>FileVersion</c>,
/// <c>Preview</c>-<c>Preview6</c> and <c>Starred</c>, wrapping an obfuscated payload whose
/// own sub-keys stay readable.
/// </para>
/// <para>
/// This is the richest of the third-party formats: alone among them it carries a corvette's
/// base parts. It still cannot carry a ship's custom parts or textures.
/// </para>
/// <para>
/// <b>Unverified.</b> NomNom has not been updated for 7.03 Cosmos and cannot load a current
/// save, so nothing here has been confirmed by importing the output.
/// </para>
/// </remarks>
public sealed class NomNomExportAdapter : IExportAdapter
{
    private readonly JsonNameMapper _mapper;
    private readonly TimeProvider _time;

    /// <param name="mapper">
    /// The key mapping table. Must be current for the game version the items were captured
    /// from: unmapped keys pass through readable, producing a half-obfuscated file.
    /// </param>
    /// <param name="time">
    /// Clock for <c>DateCreated</c>. Injected so golden-file tests are not time-dependent.
    /// </param>
    public NomNomExportAdapter(JsonNameMapper mapper, TimeProvider? time = null)
    {
        _mapper = mapper;
        _time = time ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public EditorId Editor => EditorId.NomNom;

    /// <inheritdoc />
    public string DisplayName => "NomNom";

    /// <inheritdoc />
    public string HomepageUrl => "https://github.com/zencq/NomNom";

    /// <summary>False — see the remarks on this class.</summary>
    public bool IsVerified => false;

    /// <summary>The number of image slots: <c>Preview</c> plus 2-6.</summary>
    private const int PreviewSlots = 6;

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

        // Colours only, like Kaii - CustomisationHelpers.Losses is the one list of what that
        // leaves behind, shared so the two cannot drift apart again.
        if (item.Kind == EntityKind.Starship)
            losses.AddRange(CustomisationHelpers.Losses(item.CharacterCustomisationData));

        return losses;
    }

    /// <inheritdoc />
    public ExportResult Export(VaultItem item, ExportOptions options = default)
    {
        var extension = Extension(item.Kind);
        if (!extension.HasValue)
            throw new NotSupportedException(extension.Alternative.Reason);

        // The Data sub-keys are libNOM's own dictionary names and stay readable; only the
        // values beneath them are obfuscated.
        var data = new JsonObject();

        switch (item.Kind)
        {
            case EntityKind.Starship:
                data.Set("Ship", KeyObfuscator.Obfuscate(item.Payload, _mapper));
                // Note the spelling: UseLegacyColours here, UsesLegacyColours in NMSE.
                data.Set("UseLegacyColours", item.UsesLegacyColours ?? false);
                var colours = CustomisationHelpers.Colours(item.CharacterCustomisationData);
                data.Set("Colours", colours is null ? null : KeyObfuscator.Obfuscate(colours, _mapper));
                // Alone among the third-party formats, this one keeps a corvette's base.
                data.Set("PersistentPlayerBases", item.ShipBase is { } shipBase
                    ? KeyObfuscator.Obfuscate(shipBase, _mapper)
                    : null);
                break;

            case EntityKind.Multitool:
                data.Set("Multitool", KeyObfuscator.Obfuscate(item.Payload, _mapper));
                // Type is editor metadata rather than a game field - no NMSE fixture has
                // one - so it is derived from the model path and size.
                data.Set("Type", MultitoolType(item.Payload));
                break;

            case EntityKind.Companion:
                data.Set("Pet", KeyObfuscator.Obfuscate(item.Payload, _mapper));
                // A sibling wrapper object, where Kaii puts it at root and the vault holds
                // the flat array.
                data.Set("AccessoryCustomisation", item.AccessorySlots is { } slots
                    ? WrapAccessories(slots, _mapper)
                    : null);
                break;

            default:
                data.Set(item.Kind.PayloadKey(), KeyObfuscator.Obfuscate(item.Payload, _mapper));
                break;
        }

        var root = new JsonObject();
        root.Set("Data", data);
        root.Set("DateCreated", (item.Meta.DateAdded ?? _time.GetUtcNow()).UtcDateTime.ToString("O"));
        root.Set("Description", item.Meta.Description);
        root.Set("FileVersion", 2);

        // Preview, Preview2 .. Preview6 - no "Preview1". Absent slots written as nulls.
        var images = options.Images ?? [];
        for (int slot = 0; slot < PreviewSlots; slot++)
        {
            string key = slot == 0 ? "Preview" : $"Preview{slot + 1}";
            root.Set(key, slot < images.Count ? Convert.ToBase64String(images[slot]) : null);
        }

        root.Set("Starred", false);

        byte[] content = Encoding.Latin1.GetBytes(root.ToExportString());
        string fileName = NmseExportAdapter.SanitiseFileName(item.Meta.DisplayName) + extension.Value;
        return new ExportResult(fileName, content);
    }

    /// <summary>
    /// The multitool type NomNom records. Not a game field - no export carries one - so it is
    /// derived, and then translated into NomNom's own vocabulary.
    /// </summary>
    /// <remarks>
    /// The derivation is shared with the gallery so the two cannot disagree about what a tool
    /// is, but the words are not: NomNom's enum draws distinctions the gallery spells
    /// differently - what is shown as "Voltaic Staff" is <c>StaffAtlas</c> there - and writing
    /// the display name into the file would produce a value it cannot read back.
    /// </remarks>
    internal static string MultitoolType(JsonObject multitool)
        => Derived.MultitoolTypes.FromMultitool(multitool) switch
        {
            "Pistol" => "Pistol",
            "Rifle" => "Rifle",
            "Switch" => "RifleSwitch",
            // NomNom's enum has no Experimental; Pristine is the value it uses for the
            // high-scanning shared-model tool.
            "Experimental" => "Pristine",
            "Alien" => "Alien",
            "Royal" => "Royal",
            "Sentinel" or "Sentinel B" => "Robot",
            "Atlantid" => "Atlas",
            "Staff" or "Staff NPC" => "Staff",
            "Voltaic Staff" => "StaffAtlas",
            "Staff Ruin" => "StaffRuin",
            "Staff Bone" => "StaffBone",
            // Bodies NomNom has no value for. NMSE files these under the family they behave
            // as, which is the nearest honest answer available.
            "Direwasp Disintegrator" => "Rifle",
            "Starbound" => "Pistol",
            _ => "Unknown",
        };

    private static JsonObject WrapAccessories(JsonArray slots, JsonNameMapper mapper)
    {
        var wrapper = new JsonObject();
        wrapper.Set("Data", KeyObfuscator.Obfuscate(slots, mapper));
        return wrapper;
    }
}
