using System.Text;
using NmsVault.Core.Adapters;
using NmsVault.Json;

namespace NmsVault.Core.Detection;

/// <summary>Raised when a file cannot be turned into a gallery item.</summary>
public sealed class ImportException(string message) : Exception(message);

/// <summary>
/// Turns any recognised editor export into a <see cref="VaultItem"/>, using
/// <see cref="FormatDetector"/> to work out what it is first.
/// </summary>
/// <remarks>
/// Only the NMSE paths are verified against real files. The others are implemented from the
/// shapes documented in <c>docs/format-coverage.md</c> and have not been exercised against
/// output from the editors themselves, because none of them supports 7.03 Cosmos.
/// </remarks>
public sealed class VaultImporter(JsonNameMapper mapper)
{
    private readonly JsonNameMapper _mapper = mapper;

    /// <summary>
    /// Reads a file into a gallery item.
    /// </summary>
    /// <param name="bytes">The file's bytes.</param>
    /// <param name="meta">Gallery metadata to attach.</param>
    /// <param name="fileName">Original file name, used as a detection hint.</param>
    /// <exception cref="ImportException">If the file is not a recognised export.</exception>
    public VaultItem Import(ReadOnlySpan<byte> bytes, VaultMetadata meta, string? fileName = null)
    {
        var detected = FormatDetector.Detect(bytes, _mapper, fileName);

        if (detected.Format == SourceFormat.Unknown)
            throw new ImportException($"Could not identify this file. {detected.Reason}");

        if (detected.Kind is null)
            throw new ImportException($"Identified as {detected.Format} but could not tell what it contains. {detected.Reason}");

        // A mixed key space means the writer used a mapping table older than the data, so
        // some keys are obfuscated and some are not. Storing that would propagate the
        // corruption into every format we later convert to, so refuse it here.
        if (detected.Keys == KeySpace.Mixed)
            throw new ImportException(
                "This file has a mixed key space - some keys obfuscated, some readable. That " +
                "usually means the editor that wrote it used an out-of-date key mapping. " +
                "Re-export it with a current version.");

        return detected.Format switch
        {
            SourceFormat.Nmse or SourceFormat.Goatfungus or SourceFormat.NmseOrGoatfungus
                => ImportReadable(bytes, meta, detected, fileName),

            SourceFormat.Companion => ImportCompanionFormat(bytes, meta, detected),
            SourceFormat.NomNom => ImportNomNom(bytes, meta, detected),

            SourceFormat.ModelIoToolZip => throw new ImportException(
                "NMS Model IO Tool ZIPs are not supported yet. Import the ship through NMSE " +
                "and export it as .nmsship first."),

            _ => throw new ImportException($"No importer for {detected.Format}."),
        };
    }

    /// <summary>
    /// NMSE and goatfungus both write readable keys, and for multitools their formats are
    /// identical, so one path handles both. NMSE's extra sidecars are picked up when present
    /// and simply absent otherwise.
    /// </summary>
    private static VaultItem ImportReadable(
        ReadOnlySpan<byte> bytes, VaultMetadata meta, DetectionResult detected, string? fileName)
        => NmseImporter.Read(bytes, meta,
            fileName is null ? null : Path.GetExtension(fileName));

    private VaultItem ImportCompanionFormat(
        ReadOnlySpan<byte> bytes, VaultMetadata meta, DetectionResult detected)
    {
        var root = Parse(bytes);
        var kind = detected.Kind!.Value;

        JsonObject payload;
        bool? legacyColours = null;
        JsonObject? ccd = null;
        JsonArray? accessories = null;

        if (kind == EntityKind.Starship)
        {
            // { "Ship": { "@Cs": <ship>, "4hl": <flag> }, "Colours": [...] }
            var ship = root.GetObject("Ship")
                ?? throw new ImportException("NMS Companion file has no Ship object.");

            payload = Deobfuscate(ship.GetObject("@Cs")
                ?? throw new ImportException("NMS Companion Ship has no @Cs payload."));

            legacyColours = ship.Get("4hl") is bool b ? b : null;
            ccd = RebuildCustomisation(root.GetArray("Colours"));
        }
        else
        {
            string key = kind == EntityKind.Multitool ? "MultiTool"
                : kind == EntityKind.Companion ? "Companion"
                : kind.PayloadKey();

            payload = Deobfuscate(root.GetObject(key)
                ?? throw new ImportException($"NMS Companion file has no {key} object."));

            if (kind == EntityKind.Companion)
                accessories = UnwrapAccessories(root.GetObject("Accessories"));
        }

        return VaultItem.Create(kind, payload, EnrichFrom(meta, kind, root.GetString("Description")),
            characterCustomisationData: ccd, usesLegacyColours: legacyColours,
            accessorySlots: accessories);
    }

    private VaultItem ImportNomNom(ReadOnlySpan<byte> bytes, VaultMetadata meta, DetectionResult detected)
    {
        var root = Parse(bytes);
        var kind = detected.Kind!.Value;

        var data = root.GetObject("Data")
            ?? throw new ImportException("NomNom file has no Data envelope.");

        string key = kind == EntityKind.Companion ? "Pet" : kind.PayloadKey();
        var payload = Deobfuscate(data.GetObject(key)
            ?? throw new ImportException($"NomNom Data has no {key} object."));

        bool? legacyColours = null;
        JsonObject? ccd = null;
        JsonObject? shipBase = null;
        JsonArray? accessories = null;

        if (kind == EntityKind.Starship)
        {
            // Note the spelling: UseLegacyColours here, UsesLegacyColours in NMSE.
            legacyColours = data.Get("UseLegacyColours") is bool b ? b : null;
            ccd = RebuildCustomisation(data.GetArray("Colours"));
            shipBase = data.GetObject("PersistentPlayerBases") is { } b2 ? Deobfuscate(b2) : null;
        }
        else if (kind == EntityKind.Companion)
        {
            accessories = UnwrapAccessories(data.GetObject("AccessoryCustomisation"));
        }

        return VaultItem.Create(kind, payload, EnrichFrom(meta, kind, root.GetString("Description")),
            characterCustomisationData: ccd, usesLegacyColours: legacyColours,
            shipBase: shipBase, accessorySlots: accessories);
    }

    /// <summary>
    /// Rebuilds a <c>CharacterCustomisationData</c> entry around a bare colours array.
    /// <para>
    /// This is a lossy inverse and cannot be otherwise: NMS Companion and NomNom carry only
    /// <c>CustomData.Colours</c>, so the parts, textures, palette and preset are simply not
    /// in the file. The rebuilt entry has empty collections and default markers, which is an
    /// honest representation of what is known - not a claim the ship had no parts.
    /// </para>
    /// </summary>
    private JsonObject? RebuildCustomisation(JsonArray? colours)
    {
        if (colours is null || colours.Length == 0) return null;

        var customData = new JsonObject();
        customData.Set("DescriptorGroups", new JsonArray());
        customData.Set("PaletteID", "^");
        // Deobfuscate, not just clone: each colour entry is an object with its own keys
        // (Palette, ColourAlt and so on), which are obfuscated in the source file.
        customData.Set("Colours", KeyObfuscator.Deobfuscate(colours, _mapper));
        customData.Set("TextureOptions", new JsonArray());
        customData.Set("BoneScales", new JsonArray());
        customData.Set("Scale", 1.0);

        var ccd = new JsonObject();
        ccd.Set("SelectedPreset", "^");
        ccd.Set("CustomData", customData);
        return ccd;
    }

    /// <summary>
    /// Both third-party formats store accessories as <c>{ Data: [...] }</c>; the vault holds
    /// the innermost flat array so each adapter re-wraps rather than unwraps.
    /// </summary>
    private JsonArray? UnwrapAccessories(JsonObject? wrapper)
    {
        var inner = wrapper?.GetArray("Data") ?? wrapper?.GetArray(_mapper.ToKey("Data"));
        return inner is null ? null : KeyObfuscator.Deobfuscate(inner, _mapper);
    }

    private JsonObject Deobfuscate(JsonObject obj) => KeyObfuscator.Deobfuscate(obj, _mapper);

    private static JsonObject Parse(ReadOnlySpan<byte> bytes)
        => JsonParser.ParseObject(Encoding.Latin1.GetString(bytes));

    /// <summary>
    /// Takes the source file's description when the caller did not supply one, so metadata
    /// the other editors carry is not thrown away on import.
    /// </summary>
    private static VaultMetadata EnrichFrom(VaultMetadata meta, EntityKind kind, string? description)
        => string.IsNullOrWhiteSpace(meta.Description) && !string.IsNullOrWhiteSpace(description)
            ? meta with { Description = description }
            : meta;
}
