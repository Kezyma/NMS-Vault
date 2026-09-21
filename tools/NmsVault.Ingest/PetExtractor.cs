using System.Text;
using System.Text.Json;
using NmsVault.Json;

namespace NmsVault.Ingest;

/// <summary>What a pet extraction run produced.</summary>
/// <param name="Affinities">How many affinities were written.</param>
/// <param name="Moves">How many battle moves were written.</param>
/// <param name="Species">How many species carry a forced affinity.</param>
/// <param name="Names">How many move names resolved.</param>
/// <param name="Unresolved">Move-and-affinity pairs the language file had no name for.</param>
/// <param name="Icons">How many glyphs were written.</param>
/// <param name="IconsMissing">Glyphs the gallery wants that the source folder did not hold.</param>
public readonly record struct PetExtractionResult(
    int Affinities, int Moves, int Species, int Names, int Unresolved, int Icons, int IconsMissing);

/// <summary>
/// Builds the gallery's companion lookup from an NMSE resources folder.
/// </summary>
/// <remarks>
/// <para>
/// A creature's battle moves are stored as ids - <c>^ATTACK_AFF</c> - and the gallery showed
/// them exactly like that. The name depends on the creature's affinity as well as on the move:
/// the same ATTACK is "Lash" on a tropical creature and "Freeze" on a frost one, so there is no
/// single name to print without knowing both.
/// </para>
/// <para>
/// Every table here is the game's own rather than inferred. <c>Game Table Globals.json</c>
/// states the biome-to-affinity map, the affinity display names, and the prefix each affinity
/// uses in the move-name keys - which is worth having, because that last one does not match the
/// display names: Toxic is TOX, Radioactive is RAD and Weird is ODD.
/// </para>
/// <para>
/// Run when the game updates, not on every ingest, and commit the output - the same arrangement
/// <see cref="TechExtractor"/> uses, and for the same reason: no runner has a game install.
/// </para>
/// </remarks>
public static class PetExtractor
{
    /// <summary>The language file read for display names. English only; the gallery is English.</summary>
    private const string Language = "en-GB.json";

    /// <summary>
    /// Extracts the companion lookup into the gallery.
    /// </summary>
    /// <param name="nmseResources">Path to NMSE's <c>Resources</c> folder.</param>
    /// <param name="galleryRoot">The gallery folder to write into.</param>
    /// <param name="iconSource">
    /// A folder of PNGs extracted from the game, or null to write the tables only.
    /// </param>
    /// <param name="log">Called with progress lines.</param>
    /// <returns>What was produced.</returns>
    public static PetExtractionResult Extract(
        string nmseResources, string galleryRoot, string? iconSource, Action<string> log)
    {
        string jsonDir = Path.Combine(nmseResources, "json");

        if (!Directory.Exists(jsonDir))
            throw new DirectoryNotFoundException(
                $"No json folder under '{nmseResources}'. Point --nmse at NMSE's Resources directory.");

        var globals = ReadGlobals(jsonDir);
        var lang = ReadLanguage(jsonDir, log);

        var affinities = Affinities(globals, lang);
        var species = ForcedAffinities(jsonDir, log);
        var (moves, names, unresolved) = Moves(jsonDir, globals, lang, affinities);

        var root = new JsonObject();
        root.Set("SchemaVersion", 1);
        root.Set("Affinities", affinities.Written);
        root.Set("BiomeAffinities", Map(globals.BiomeAffinities));
        root.Set("ForcedAffinities", Map(species));
        root.Set("Moves", moves);

        string path = Path.Combine(galleryRoot, "pets.json");
        Directory.CreateDirectory(galleryRoot);
        File.WriteAllBytes(path, Encoding.Latin1.GetBytes(root.ToExportString()));

        log($"  written: {path} ({new FileInfo(path).Length / 1024.0:0.#} KB)");

        var (icons, missing) = iconSource is { Length: > 0 }
            ? WriteIcons(iconSource, galleryRoot, log)
            : (0, 0);

        return new PetExtractionResult(
            affinities.Written.Length, moves.Length, species.Count, names, unresolved, icons, missing);
    }

    /// <summary>
    /// The glyphs the gallery draws, and the game file each comes from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The game keeps them in <c>NMSARC.TexUI.pak</c> under
    /// <c>TEXTURES/UI/FRONTEND/ICONS/PETS/</c>, as BC7 DDS. SkiaSharp cannot decode BC7, and
    /// the archive is Hello Games' own HGPA format rather than PSARC, so neither half of
    /// getting them out can happen in here. Extract and convert to PNG first - the PCBANKS
    /// Explorer that ships with AMUMSS reads the archive, and texconv or any image editor
    /// handles the texture - then point --icons at the folder.
    /// </para>
    /// <para>
    /// Written as an explicit table rather than derived from the file names, because two of the
    /// four move glyphs are not named after the style they serve: the Heal style is drawn by
    /// MOVE.HEALTH, and the Attack style by MOVE.PET.ATTACK.
    /// </para>
    /// </remarks>
    private static readonly (string Source, string Folder, string Name)[] Icons =
    [
        ("BUFF.AFFINITY.LUSH",        "affinity", "lush"),
        ("BUFF.AFFINITY.COLD",        "affinity", "cold"),
        ("BUFF.AFFINITY.FIRE",        "affinity", "fire"),
        ("BUFF.AFFINITY.TOXIC",       "affinity", "toxic"),
        ("BUFF.AFFINITY.BARREN",      "affinity", "barren"),
        ("BUFF.AFFINITY.RADIOACTIVE", "affinity", "radioactive"),
        ("BUFF.AFFINITY.WEIRD",       "affinity", "weird"),
        ("BUFF.AFFINITY.MECH",        "affinity", "mech"),

        ("MOVE.PET.ATTACK",           "move",     "attack"),
        ("MOVE.COOLDOWN",             "move",     "cooldown"),
        ("MOVE.HEALTH",               "move",     "heal"),
        ("MOVE.POWER",                "move",     "power"),
    ];

    private static (int Written, int Missing) WriteIcons(string source, string galleryRoot, Action<string> log)
    {
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"No icon folder at '{source}'.");

        // Found by name rather than by path, so it does not matter whether the extraction kept
        // the game's folder structure or flattened it.
        var found = Directory
            .EnumerateFiles(source, "*", SearchOption.AllDirectories)
            .Where(f => Path.GetExtension(f) is ".png" or ".PNG" or ".webp" or ".WEBP")
            .ToDictionary(f => Path.GetFileNameWithoutExtension(f), StringComparer.OrdinalIgnoreCase);

        int written = 0, missing = 0;

        foreach (var (name, folder, target) in Icons)
        {
            if (!found.TryGetValue(name, out string? file))
            {
                log($"  glyph {name} not found");
                missing++;
                continue;
            }

            string outDir = Path.Combine(galleryRoot, "img", folder);
            Directory.CreateDirectory(outDir);

            // Trimmed, like the class badges: these are drawn inside a square frame with a
            // wide transparent margin, and at chip size that margin is most of the glyph.
            TechExtractor.Downscale(file, Path.Combine(outDir, target + ".webp"), trim: true);
            written++;
        }

        log($"  {written} glyph(s) written, {missing} missing");
        return (written, missing);
    }

    private readonly record struct Globals(
        Dictionary<string, string> BiomeAffinities,
        Dictionary<string, string> AffinityLoc,
        Dictionary<string, string> AffinityStub,
        Dictionary<string, string> TargetLoc);

    private static Globals ReadGlobals(string jsonDir)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(jsonDir, "Game Table Globals.json")));

        // The file is a one-element array wrapping the object.
        var root = doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement[0]
            : doc.RootElement;

        return new Globals(
            Strings(root, "PetBiomeAffinities"),
            Strings(root, "PetAffinityLoc"),
            Strings(root, "PetAffinityLocStub"),
            Strings(root, "PetTargetLoc"));
    }

    private static Dictionary<string, string> Strings(JsonElement root, string name)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        if (!root.TryGetProperty(name, out var table)) return map;

        foreach (var entry in table.EnumerateObject())
            if (entry.Value.ValueKind == JsonValueKind.String)
                map[entry.Name] = entry.Value.GetString() ?? "";

        return map;
    }

    private static Dictionary<string, string> ReadLanguage(string jsonDir, Action<string> log)
    {
        string path = Path.Combine(jsonDir, "lang", Language);

        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"No {Language} under the lang folder. The display names live there.", path);

        // Eight megabytes, read once. This is a local step, not something a runner does.
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var entry in doc.RootElement.EnumerateObject())
            if (entry.Value.ValueKind == JsonValueKind.String)
                map[entry.Name] = entry.Value.GetString() ?? "";

        log($"  read {map.Count:n0} strings from {Language}");
        return map;
    }

    private readonly record struct AffinityTable(JsonArray Written, Dictionary<string, string> Stubs);

    private static AffinityTable Affinities(Globals globals, Dictionary<string, string> lang)
    {
        var written = new JsonArray();
        var stubs = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (id, locId) in globals.AffinityLoc)
        {
            string stub = globals.AffinityStub.GetValueOrDefault(id, id.ToUpperInvariant());
            stubs[id] = stub;

            var entry = new JsonObject();
            entry.Set("Id", id);
            entry.Set("Name", lang.GetValueOrDefault(locId, id));
            entry.Set("Stub", stub);

            // Normal means "no affinity of its own". The game draws no glyph for it, and no
            // real creature carries it either: a species whose forced affinity is Normal falls
            // back to its biome, and every biome maps to one of the other eight.
            if (!id.Equals("Normal", StringComparison.Ordinal))
                entry.Set("Icon", id.ToLowerInvariant() + ".webp");

            written.Add(entry);
        }

        return new AffinityTable(written, stubs);
    }

    private static Dictionary<string, string> ForcedAffinities(string jsonDir, Action<string> log)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(jsonDir, "Creature Species.json")));
        var forced = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var species in doc.RootElement.EnumerateArray())
        {
            if (!species.TryGetProperty("Id", out var id)) continue;
            if (!species.TryGetProperty("PetBattlerForcedAffinity", out var affinity)) continue;

            string value = affinity.GetString() ?? "Normal";

            // Only the species that override. Normal is the default and means "use the biome",
            // so recording it would be recording nothing, eighty times over.
            if (value is "Normal" or "") continue;

            forced[id.GetString() ?? ""] = value;
        }

        log($"  {forced.Count} species with a forced affinity");
        return forced;
    }

    /// <summary>The file an icon style is extracted to. Four styles, four glyphs.</summary>
    private static string IconFor(string style) => style.ToLowerInvariant() + ".webp";

    private static (JsonArray Moves, int Names, int Unresolved) Moves(
        string jsonDir, Globals globals, Dictionary<string, string> lang, AffinityTable affinities)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(jsonDir, "Pet Battle Moves.json")));

        var written = new JsonArray();
        int resolved = 0, unresolved = 0;

        foreach (var move in doc.RootElement.EnumerateArray())
        {
            string id = move.GetProperty("Id").GetString() ?? "";
            string stub = move.TryGetProperty("NameStub", out var s) ? s.GetString() ?? "" : "";
            string style = move.TryGetProperty("IconStyle", out var i) ? i.GetString() ?? "" : "";
            string target = move.TryGetProperty("Target", out var t) ? t.GetString() ?? "" : "";

            var entry = new JsonObject();
            entry.Set("Id", id);
            entry.Set("Icon", IconFor(style));

            if (globals.TargetLoc.GetValueOrDefault(target) is { Length: > 0 } targetLoc
                && lang.GetValueOrDefault(targetLoc) is { Length: > 0 } targetName)
            {
                entry.Set("Target", targetName);
            }

            // The game's own one-line summary of what the move does. It is called
            // DebugDescription in the source file, but it is plain English and it is the only
            // per-move prose there is: the localised strings are effect templates full of
            // placeholders only the battle itself can fill in.
            if (move.TryGetProperty("DebugDescription", out var d) && d.GetString() is { Length: > 0 } text)
                entry.Set("Description", text);

            var names = new JsonObject();

            foreach (var (affinity, affinityStub) in affinities.Stubs)
            {
                // Variant 1 of three. The variants are flavour rather than tiers - nothing in
                // the move id says which to use - so the first is taken as the canonical name.
                string key = $"UI_PB_MOVE_{affinityStub}_{stub}1";

                if (lang.GetValueOrDefault(key) is { Length: > 0 } name)
                {
                    names.Set(affinity, name);
                    resolved++;
                }
                else
                {
                    unresolved++;
                }
            }

            entry.Set("Names", names);
            written.Add(entry);
        }

        return (written, resolved, unresolved);
    }

    private static JsonObject Map(Dictionary<string, string> source)
    {
        var obj = new JsonObject();
        foreach (var (key, value) in source) obj.Set(key, value);
        return obj;
    }
}
