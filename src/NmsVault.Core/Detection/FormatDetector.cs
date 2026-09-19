using System.Text;
using NmsVault.Core.Adapters;
using NmsVault.Json;

namespace NmsVault.Core.Detection;

/// <summary>Which editor produced a file.</summary>
public enum SourceFormat
{
    /// <summary>Not recognised.</summary>
    Unknown,

    /// <summary>NMSE. Wrapped for starships, bare otherwise, readable keys.</summary>
    Nmse,

    /// <summary>goatfungus NMSSaveEditor. Bare object, readable keys.</summary>
    Goatfungus,

    /// <summary>
    /// NMSE or goatfungus - the two formats are byte-compatible for this kind and the file
    /// carries nothing that distinguishes them. Harmless: both ingest identically.
    /// </summary>
    NmseOrGoatfungus,

    /// <summary>NMS Companion (Dr. Kaii). Readable envelope, obfuscated payload, FileVersion 1.</summary>
    Companion,

    /// <summary>NomNom. Data envelope, obfuscated payload, FileVersion 2.</summary>
    NomNom,

    /// <summary>NMS Model IO Tool ZIP, containing so.json / ccd.json / objects.json.</summary>
    ModelIoToolZip,
}

/// <summary>How sure the detector is.</summary>
public enum Certainty
{
    /// <summary>A structural marker unique to that format was found.</summary>
    Certain,

    /// <summary>The shape fits and nothing contradicts it.</summary>
    Likely,

    /// <summary>Inferred from the extension or a weak shape signal.</summary>
    Guess,
}

/// <summary>Whether a document's payload keys are the game's obfuscated form.</summary>
public enum KeySpace
{
    /// <summary>Human-readable names throughout.</summary>
    Readable,

    /// <summary>Obfuscated three-character keys.</summary>
    Obfuscated,

    /// <summary>
    /// Both, which should not happen. Usually means the writer used a mapping table older
    /// than the data, leaving newer keys readable inside an otherwise obfuscated document.
    /// </summary>
    Mixed,
}

/// <param name="Format">The editor that produced it.</param>
/// <param name="Kind">What the file contains, if determinable.</param>
/// <param name="Keys">The payload's key space.</param>
/// <param name="Certainty">How sure this is.</param>
/// <param name="Reason">What the decision was based on, for logs and error messages.</param>
public readonly record struct DetectionResult(
    SourceFormat Format,
    EntityKind? Kind,
    KeySpace Keys,
    Certainty Certainty,
    string Reason)
{
    /// <summary>A result meaning "no idea".</summary>
    public static DetectionResult Unrecognised(string reason)
        => new(SourceFormat.Unknown, null, KeySpace.Readable, Certainty.Guess, reason);
}

/// <summary>
/// Works out which editor produced a file, and what is in it.
/// </summary>
/// <remarks>
/// <para>
/// Content first, extension only as a tiebreaker. Extensions collide badly: NMS Companion
/// and NomNom share <c>.shp</c>, <c>.mlt</c>, <c>.cmp</c>, <c>.flt</c> and <c>.frt</c>,
/// and NMSE's <c>.nmsship</c> collides with the NMS Model IO Tool's ZIP of the same name.
/// </para>
/// <para>
/// Only the NMSE branches are verified against real files. The others are derived from
/// libNOM's writers - see <c>docs/format-coverage.md</c>.
/// </para>
/// </remarks>
public static class FormatDetector
{
    private static readonly byte[] ZipMagic = [0x50, 0x4B, 0x03, 0x04];

    /// <summary>
    /// Identifies a file.
    /// </summary>
    /// <param name="bytes">The file's bytes.</param>
    /// <param name="mapper">Mapping table, used to recognise obfuscated keys.</param>
    /// <param name="fileName">
    /// Original name or extension, used only where content genuinely cannot decide.
    /// </param>
    public static DetectionResult Detect(ReadOnlySpan<byte> bytes, JsonNameMapper mapper, string? fileName = null)
    {
        // 1. Magic bytes. A PK header means this is the Model IO Tool's ZIP, not the NMSE
        //    .nmsship JSON it shares an extension with.
        if (bytes.Length >= 4 && bytes[..4].SequenceEqual(ZipMagic))
            return new DetectionResult(SourceFormat.ModelIoToolZip, EntityKind.Starship,
                KeySpace.Readable, Certainty.Certain,
                "ZIP magic bytes: NMS Model IO Tool package.");

        // 2. Parse with no mapper, so raw keys survive for inspection. Latin-1 because every
        //    byte maps to one char and the parser validates UTF-8 runs itself; decoding as
        //    UTF-8 here would destroy any genuine binary payload.
        JsonObject root;
        try
        {
            root = JsonParser.ParseObject(Encoding.Latin1.GetString(bytes));
        }
        catch (JsonException ex)
        {
            return DetectionResult.Unrecognised($"Not valid JSON: {ex.Message}");
        }

        string? extension = ExtensionOf(fileName);

        // 3. Wrapped third-party formats, discriminated on FileVersion as a parsed number.
        //    libNOM's own check is a substring test for "\"FileVersion\":1", which breaks on
        //    pretty-printed input - deliberately not copied.
        int? fileVersion = root.Get("FileVersion") as int?;
        bool hasData = root.Contains("Data");

        if (fileVersion == 2 && hasData)
            return DetectWrapped(root, mapper, SourceFormat.NomNom, "Data",
                "FileVersion 2 with a Data envelope: NomNom.");

        if (fileVersion == 1 && !hasData)
            return DetectWrapped(root, mapper, SourceFormat.Companion, null,
                "FileVersion 1 with root-level entity key: NMS Companion.");

        if (fileVersion is not null)
            return DetectionResult.Unrecognised(
                $"FileVersion {fileVersion} with{(hasData ? "" : "out")} a Data envelope " +
                "matches no known format.");

        // 4. NMSE's starship wrapper. UsesLegacyColours - with the s - is NMSE's spelling;
        //    NomNom writes UseLegacyColours, so this is a reliable fingerprint.
        if (root.Contains("Ship"))
        {
            bool nmseMarker = root.Contains("UsesLegacyColours")
                || root.Contains("CharacterCustomisationData")
                || root.Contains("Base");

            return new DetectionResult(SourceFormat.Nmse, EntityKind.Starship,
                KeySpaceOf(root.GetObject("Ship"), mapper),
                nmseMarker ? Certainty.Certain : Certainty.Likely,
                nmseMarker
                    ? "Ship wrapper with NMSE sidecar keys."
                    : "Ship wrapper with no sidecars; NMSE by shape.");
        }

        // 5. Bare objects. Everything here has readable keys, so the question is only what
        //    it contains and which of the two bare-format editors wrote it.
        return DetectBare(root, mapper, extension);
    }

    private static DetectionResult DetectWrapped(
        JsonObject root, JsonNameMapper mapper, SourceFormat format, string? envelopeKey, string reason)
    {
        var payloadHost = envelopeKey is null ? root : root.GetObject(envelopeKey);
        if (payloadHost is null)
            return DetectionResult.Unrecognised($"{format}: envelope '{envelopeKey}' is not an object.");

        EntityKind? kind = KindFromEnvelope(payloadHost, format);
        if (kind is null)
            return DetectionResult.Unrecognised(
                $"{format}: no recognised entity key among [{string.Join(", ", payloadHost.Names())}].");

        var payload = payloadHost.GetObject(EnvelopeKeyFor(kind.Value, format));

        // Kaii nests the ship one deeper, under the hardcoded obfuscated @Cs.
        if (format == SourceFormat.Companion && kind == EntityKind.Starship)
            payload = payload?.GetObject("@Cs") ?? payload;

        return new DetectionResult(format, kind, KeySpaceOf(payload, mapper), Certainty.Certain, reason);
    }

    private static DetectionResult DetectBare(JsonObject root, JsonNameMapper mapper, string? extension)
    {
        var keys = KeySpaceOf(root, mapper);

        // A companion carrying accessories flat is NMSE specifically: goatfungus does not
        // store accessories at all.
        if (root.GetArray("PetAccessoryCustomisation") is not null)
            return new DetectionResult(SourceFormat.Nmse, EntityKind.Companion, keys, Certainty.Certain,
                "Bare pet with a flat PetAccessoryCustomisation array: NMSE .nmspet.");

        // A PersistentPlayerBases entry, which NMSE exports for bases and freighter interiors.
        if (root.Contains("Objects") && root.Contains("BaseType"))
        {
            string baseType = root.GetObject("BaseType")?.GetString("PersistentBaseTypes") ?? "";
            return DetectionResult.Unrecognised(
                $"A PersistentPlayerBases entry ({baseType}). Bases are not gallery items; " +
                "freighter interiors are explicitly out of scope.");
        }

        EntityKind? kind = BareKind(root, extension);
        if (kind is null)
            return DetectionResult.Unrecognised(
                "A bare object that matches no known entity shape. Pass the original file " +
                "name so the extension can be used as a hint.");

        // For multitools the two bare formats are byte-identical, so the file cannot say
        // which wrote it. Name it from the extension if we have one, otherwise say both.
        var format = extension switch
        {
            ".nmstool" or ".nmspet" or ".nmsfrig" => SourceFormat.Nmse,
            ".wp0" or ".pet" or ".sh0" => SourceFormat.Goatfungus,
            _ => SourceFormat.NmseOrGoatfungus,
        };

        var certainty = extension is null ? Certainty.Guess : Certainty.Likely;

        return new DetectionResult(format, kind, keys, certainty,
            $"Bare {kind} object with readable keys" +
            (extension is null ? ", kind from shape." : $", format from the {extension} extension."));
    }

    /// <summary>
    /// The entity kind a bare object represents. Shape first so a renamed file still works,
    /// extension as the tiebreaker.
    /// </summary>
    private static EntityKind? BareKind(JsonObject root, string? extension)
    {
        // Shape signals, taken from the real fixtures.
        if (root.Contains("Store") && root.Contains("SecondaryMode")) return EntityKind.Multitool;
        if (root.Contains("CreatureID")) return EntityKind.Companion;
        if (root.Contains("TraitIDs") || root.Contains("FrigateClass")) return EntityKind.Frigate;

        // A bare ship: has the three ship inventories rather than a multitool's single Store.
        if (root.Contains("Resource") && root.Contains("Inventory_TechOnly")
            && root.Contains("Inventory_Cargo"))
            return EntityKind.Starship;

        return extension switch
        {
            ".nmstool" or ".wp0" => EntityKind.Multitool,
            ".nmspet" or ".pet" => EntityKind.Companion,
            ".nmsfrig" => EntityKind.Frigate,
            ".sh0" => EntityKind.Starship,
            _ => null,
        };
    }

    private static EntityKind? KindFromEnvelope(JsonObject host, SourceFormat format)
    {
        foreach (EntityKind kind in Enum.GetValues<EntityKind>())
            if (host.Contains(EnvelopeKeyFor(kind, format)))
                return kind;
        return null;
    }

    /// <summary>
    /// The key each format uses per kind. Note NMS Companion spells it <c>MultiTool</c> and
    /// NomNom spells it <c>Multitool</c>.
    /// </summary>
    private static string EnvelopeKeyFor(EntityKind kind, SourceFormat format) => (kind, format) switch
    {
        (EntityKind.Multitool, SourceFormat.Companion) => "MultiTool",
        (EntityKind.Multitool, _) => "Multitool",
        (EntityKind.Companion, SourceFormat.Companion) => "Companion",
        (EntityKind.Companion, _) => "Pet",
        _ => kind.PayloadKey(),
    };

    /// <summary>
    /// Whether an object's keys are obfuscated, readable, or - worryingly - both.
    /// <para>
    /// Mixed is the signature of a writer using a stale mapping table. Detecting it at
    /// ingestion is the difference between rejecting a bad file and storing one.
    /// </para>
    /// </summary>
    private static KeySpace KeySpaceOf(JsonObject? obj, JsonNameMapper mapper)
    {
        if (obj is null || obj.Length == 0) return KeySpace.Readable;

        int obfuscated = 0, readable = 0;
        foreach (var name in obj.Names())
        {
            if (mapper.IsObfuscatedKey(name)) obfuscated++;
            else if (mapper.ToKey(name) != name) readable++;  // a known readable game key
        }

        if (obfuscated > 0 && readable > 0) return KeySpace.Mixed;
        return obfuscated > 0 ? KeySpace.Obfuscated : KeySpace.Readable;
    }

    private static string? ExtensionOf(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        string ext = Path.GetExtension(fileName);
        return ext.Length == 0 ? null : ext.ToLowerInvariant();
    }
}
