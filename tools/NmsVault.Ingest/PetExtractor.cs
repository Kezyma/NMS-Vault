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
        var species = ReadSpecies(jsonDir, log);
        var (moves, names, unresolved) = Moves(jsonDir, globals, lang, affinities);

        var root = new JsonObject();
        root.Set("SchemaVersion", 2);
        root.Set("Affinities", affinities.Written);
        root.Set("BiomeAffinities", Map(globals.BiomeAffinities));
        root.Set("ForcedAffinities", Map(species.Forced));
        root.Set("Climates", Climates(lang));
        root.Set("Matchups", Matchups());
        root.Set("Traits", Traits(lang));
        root.Set("BattleStats", BattleStats(lang));
        root.Set("Species", species.Facts);
        root.Set("Moves", moves);

        string path = Path.Combine(galleryRoot, "pets.json");
        Directory.CreateDirectory(galleryRoot);
        File.WriteAllBytes(path, Encoding.Latin1.GetBytes(root.ToExportString()));

        log($"  written: {path} ({new FileInfo(path).Length / 1024.0:0.#} KB)");

        var (icons, missing) = iconSource is { Length: > 0 }
            ? WriteIcons(iconSource, galleryRoot, log)
            : (0, 0);

        return new PetExtractionResult(
            affinities.Written.Length, moves.Length, species.Forced.Count, names, unresolved, icons, missing);
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
    /// The move glyphs are the eight in <c>MOVES/</c>, which are stats rather than effects -
    /// accuracy, cooldown, health, attack, defence, power, speed, stealth. The game draws a
    /// move button with two of them: the creature's affinity and the stat the move touches.
    /// </para>
    /// <para>
    /// <c>MOVES/BUTTONBG/</c> holds the same eight plus seven affinities, all as white
    /// silhouettes, and is deliberately not used. It has no Barren glyph, and white on white
    /// does not read on this site; the coloured affinity set below covers all eight and is
    /// already what the affinity badge draws.
    /// </para>
    /// <para>
    /// The affinities come from the STATS.PLANET family rather than BUFF.AFFINITY, which is
    /// the other complete set. BUFF draws all eight as the same green shield, distinguished
    /// only by the glyph inside, and green means "buffed into this affinity" rather than "is
    /// this affinity". STATS gives each one its own colour - tropical teal, frost blue, fire
    /// red, radioactive yellow, anomalous purple - which reads at chip size, and it is round,
    /// which suits a chip. DEBUFF.AFFINITY is the same set in red and means the opposite.
    /// </para>
    /// </remarks>
    private static readonly (string Source, string Folder, string Name)[] Icons =
    [
        ("STATS.PLANET.LUSH",        "affinity", "lush"),
        ("STATS.PLANET.COLD",        "affinity", "cold"),
        ("STATS.PLANET.FIRE",        "affinity", "fire"),
        ("STATS.PLANET.TOXIC",       "affinity", "toxic"),
        ("STATS.PLANET.BARREN",      "affinity", "barren"),
        ("STATS.PLANET.RADIOACTIVE", "affinity", "radioactive"),
        ("STATS.PLANET.WEIRD",       "affinity", "weird"),
        ("STATS.PLANET.MECH",        "affinity", "mech"),

        ("MOVE.ACCURACY",            "move",     "accuracy"),
        ("MOVE.COOLDOWN",            "move",     "cooldown"),
        ("MOVE.HEALTH",              "move",     "health"),
        ("MOVE.PET.ATTACK",          "move",     "attack"),
        ("MOVE.PET.DEFENCE",         "move",     "defence"),
        ("MOVE.POWER",               "move",     "power"),
        ("MOVE.SPEED",               "move",     "speed"),
        ("MOVE.STEALTH",             "move",     "stealth"),
    ];

    private static (int Written, int Missing) WriteIcons(string source, string galleryRoot, Action<string> log)
    {
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"No icon folder at '{source}'.");

        // Found by name rather than by path, so it does not matter whether the extraction kept
        // the game's folder structure or flattened it. A name can appear twice - every glyph in
        // MOVES/ has a same-named silhouette in MOVES/BUTTONBG/ - so the shallowest wins, which
        // is the coloured one, and a flattened extraction has no duplicates to choose between.
        var found = Directory
            .EnumerateFiles(source, "*", SearchOption.AllDirectories)
            .Where(f => Path.GetExtension(f) is ".png" or ".PNG" or ".webp" or ".WEBP")
            .GroupBy(f => Path.GetFileNameWithoutExtension(f)!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(f => f.Count(c => c is '/' or '\\')).ThenBy(f => f, StringComparer.Ordinal).First(),
                StringComparer.OrdinalIgnoreCase);

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

    /// <summary>What <c>Creature Species.json</c> yielded.</summary>
    /// <param name="Forced">Species id to the affinity it always fights with.</param>
    /// <param name="Facts">Species id to the scale, habit and rarity the gallery shows.</param>
    private readonly record struct SpeciesTable(Dictionary<string, string> Forced, JsonObject Facts);

    private static SpeciesTable ReadSpecies(string jsonDir, Action<string> log)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(jsonDir, "Creature Species.json")));
        var forced = new Dictionary<string, string>(StringComparer.Ordinal);
        var facts = new JsonObject();

        foreach (var species in doc.RootElement.EnumerateArray())
        {
            if (Text(species, "Id") is not { Length: > 0 } id) continue;

            // Only the species that override. Normal is the default and means "use the biome",
            // so recording it would be recording nothing, eighty times over.
            if (Text(species, "PetBattlerForcedAffinity") is { Length: > 0 } affinity and not "Normal")
                forced[id] = affinity;

            var entry = new JsonObject();

            if (species.TryGetProperty("MinScale", out var min)) entry.Set("MinScale", min.GetDouble());
            if (species.TryGetProperty("MaxScale", out var max)) entry.Set("MaxScale", max.GetDouble());
            if (Text(species, "MoveArea") is { Length: > 0 } area) entry.Set("MoveArea", area);
            if (Text(species, "Rarity") is { Length: > 0 } rarity) entry.Set("Rarity", rarity);

            // Only when false. Nearly every species can fight, so the interesting case is the
            // one that cannot, and writing the flag out eighty-six times to say "as expected"
            // is eighty-six lines of nothing.
            if (species.TryGetProperty("CanBeUsedInPetBattler", out var battler)
                && battler.ValueKind == JsonValueKind.False)
            {
                entry.Set("NoBattle", true);
            }

            // Fiends read their personality in their own words - Feral rather than Adventurous.
            if (id.Contains("FIEND", StringComparison.OrdinalIgnoreCase)) entry.Set("TraitSet", "Fiend");

            if (entry.Names().Count > 0) facts.Set(id, entry);
        }

        log($"  {forced.Count} species with a forced affinity, {facts.Names().Count} with facts");
        return new SpeciesTable(forced, facts);
    }

    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// The climate a creature came from, in the game's words rather than the payload's.
    /// </summary>
    /// <remarks>
    /// The payload stores a biome id and the gallery printed it raw, which is wrong twice over:
    /// the game calls a Lush world Verdant and a Dead one Airless, and the label above it is
    /// "Native Climate". The ids on the left are the ones <c>PetBiomeAffinities</c> uses, so
    /// everything a real creature can carry is covered; a biome not listed falls back to its
    /// raw id, which is what every one of them showed before.
    /// </remarks>
    private static readonly (string Biome, string LocId)[] Climate =
    [
        ("Lush",        "UI_PET_CLIMATE_LUSH"),
        ("Toxic",       "UI_PET_CLIMATE_TOXIC"),
        ("Scorched",    "UI_PET_CLIMATE_HOT"),
        ("Radioactive", "UI_PET_CLIMATE_RADIO"),
        ("Frozen",      "UI_PET_CLIMATE_FROZEN"),
        ("Barren",      "UI_PET_CLIMATE_DUST"),
        ("Dead",        "UI_PET_CLIMATE_DEAD"),
        ("Weird",       "UI_PET_CLIMATE_WEIRD"),
        ("Swamp",       "UI_PET_CLIMATE_SWAMP"),
        ("Lava",        "UI_PET_CLIMATE_LAVA"),
        ("Waterworld",  "UI_PET_CLIMATE_WATER"),
        ("GasGiant",    "UI_PET_CLIMATE_GAS"),
    ];

    private static JsonObject Climates(Dictionary<string, string> lang)
    {
        var written = new JsonObject();

        foreach (var (biome, locId) in Climate)
            if (lang.GetValueOrDefault(locId) is { Length: > 0 } name)
                written.Set(biome, name);

        return written;
    }

    /// <summary>
    /// Which affinity beats which, as affinity ids.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not from the game's files - nothing in them states it. This is NMSE's table
    /// (<c>Data/CompanionDatabase.cs</c>), rewritten from its display names into the ids used
    /// here. It is self-consistent: every pairing appears twice, once as a weakness and once as
    /// the matching strength, which is what the test asserts.
    /// </para>
    /// <para>
    /// Worth carrying because it is the thing a reader actually wants from an affinity.
    /// "Tropical" on its own says nothing; "weak to Toxic and Mechanical" says what it is for.
    /// </para>
    /// </remarks>
    private static readonly (string Affinity, string[] Weak, string[] Strong)[] Matchup =
    [
        ("Lush",        ["Toxic", "Mech"],         ["Barren", "Weird"]),
        ("Cold",        ["Radioactive", "Fire"],   ["Toxic", "Mech"]),
        ("Fire",        ["Barren", "Radioactive"], ["Cold", "Weird"]),
        ("Toxic",       ["Barren", "Cold"],        ["Lush", "Radioactive"]),
        ("Barren",      ["Lush", "Mech"],          ["Toxic", "Fire"]),
        ("Radioactive", ["Toxic", "Weird"],        ["Fire", "Cold"]),
        ("Weird",       ["Fire", "Lush"],          ["Radioactive", "Mech"]),
        ("Mech",        ["Cold", "Weird"],         ["Barren", "Lush"]),
    ];

    private static JsonObject Matchups()
    {
        var written = new JsonObject();

        foreach (var (affinity, weak, strong) in Matchup)
        {
            var entry = new JsonObject();
            entry.Set("Weak", Strings(weak));
            entry.Set("Strong", Strings(strong));
            written.Set(affinity, entry);
        }

        return written;
    }

    /// <summary>
    /// The three personality axes, each as a positive and a negative pole.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A creature stores three numbers between -1 and 1 and the game shows each as a word. There
    /// are six words to choose from, so each slot is an axis with a name at each end and the
    /// sign picks the end - which the data bears out: across every real creature to hand, all
    /// three slots take both signs.
    /// </para>
    /// <para>
    /// <b>Which name belongs to which slot is inferred</b>, because the mapping lives in the
    /// game's code rather than its files - the language file is the only thing that mentions the
    /// class words at all. Two things pin it down. NMSE names the slots Helpfulness, Aggression
    /// and Independence in that order, which fixes the positive poles; the three remaining words
    /// then pair off as opposites with only one sensible assignment. The Diplodocus agrees on
    /// the sign: its middle slot is -1, and a docile herbivore reading as utterly gentle is right
    /// where utterly aggressive would not be.
    /// </para>
    /// <para>
    /// The percentage is the magnitude and the class is an even quarter of it, which is inference
    /// too - no threshold appears anywhere in the unpacked data. If the game disagrees, this
    /// table and <see cref="Bands"/> are the only things that need to change.
    /// </para>
    /// </remarks>
    private static readonly (string Positive, string PositiveStub, string Negative, string NegativeStub)[] Axes =
    [
        ("Helpfulness",  "HELPFUL",     "Playfulness", "PLAYFUL"),
        ("Aggression",   "AGGRESSIVE",  "Gentleness",  "GENTLE"),
        ("Independence", "INDEPENDENT", "Devotion",    "DEVOTED"),
    ];

    /// <summary>Fiends use six different words for the same six poles.</summary>
    private static readonly (string Stub, string FiendStub)[] FiendStubs =
    [
        ("HELPFUL",     "FIEND_HEL"), ("PLAYFUL", "FIEND_PLA"),
        ("AGGRESSIVE",  "FIEND_AGG"), ("GENTLE",  "FIEND_GEN"),
        ("INDEPENDENT", "FIEND_IND"), ("DEVOTED", "FIEND_DEV"),
    ];

    /// <summary>The lowest percentage each class covers, strongest first.</summary>
    private static readonly (string Class, double From)[] Bands =
        [("S", 75), ("A", 50), ("B", 25), ("C", 0)];

    private static JsonObject Traits(Dictionary<string, string> lang)
    {
        var fiend = FiendStubs.ToDictionary(x => x.Stub, x => x.FiendStub, StringComparer.Ordinal);

        var written = new JsonObject();
        written.Set("Default", Axis(lang, stub => stub));
        written.Set("Fiend", Axis(lang, stub => fiend.GetValueOrDefault(stub, stub)));
        written.Set("Bands", Bands.Aggregate(new JsonArray(), (a, b) => { a.Add(Band(b)); return a; }));
        written.Set("Rating", lang.GetValueOrDefault("UI_PET_TRAIT_RATING", "%NUM%% (%CLASS%)"));
        return written;
    }

    private static JsonObject Band((string Class, double From) band)
    {
        var entry = new JsonObject();
        entry.Set("Class", band.Class);
        entry.Set("From", band.From);
        return entry;
    }

    private static JsonArray Axis(Dictionary<string, string> lang, Func<string, string> stub)
    {
        var written = new JsonArray();

        foreach (var (positive, positiveStub, negative, negativeStub) in Axes)
        {
            var entry = new JsonObject();
            entry.Set("Positive", Pole(lang, positive, stub(positiveStub)));
            entry.Set("Negative", Pole(lang, negative, stub(negativeStub)));
            written.Add(entry);
        }

        return written;
    }

    private static JsonObject Pole(Dictionary<string, string> lang, string name, string stub)
    {
        var entry = new JsonObject();
        entry.Set("Name", name);

        var classes = new JsonObject();

        foreach (var (cls, _) in Bands)
            if (lang.GetValueOrDefault($"UI_PET_{stub}_CLASS_{cls}") is { Length: > 0 } word)
                classes.Set(cls, word);

        entry.Set("Classes", classes);
        return entry;
    }

    /// <summary>
    /// The three battle stats, in the order the payload stores them.
    /// </summary>
    /// <remarks>
    /// <c>PetBattlerCoreStatClassOverrides</c> and <c>PetBattlerTreatsEaten</c> are both indexed
    /// health, agility, combat (NMSE <c>CompanionPanel.cs:1697-1700</c>), which is not the order
    /// the game's own headers read in.
    /// </remarks>
    private static readonly (string LocId, string Fallback)[] Stats =
    [
        ("UI_PB_STAT_HEADER_HEALTH", "Health"),
        ("UI_PB_STAT_HEADER_SPEED",  "Agility"),
        ("UI_PB_STAT_HEADER_BUDGET", "Combat Effectiveness"),
    ];

    private static JsonArray BattleStats(Dictionary<string, string> lang)
    {
        var written = new JsonArray();

        foreach (var (locId, fallback) in Stats)
            written.Add(lang.GetValueOrDefault(locId) is { Length: > 0 } name ? name : fallback);

        return written;
    }

    private static JsonArray Strings(IReadOnlyList<string> values)
    {
        var written = new JsonArray();
        foreach (string value in values) written.Add(value);
        return written;
    }

    /// <summary>
    /// The stat glyph each move is drawn with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The game gives a move button two icons: the affinity it is typed to, and the stat it
    /// touches. This is the second of those, and it cannot come from <c>IconStyle</c> - 55 of
    /// the 61 moves say "Attack" there, including both heals. Nor from <c>Phases[].Effect</c>
    /// alone, which lumps every buff together whatever it buffs.
    /// </para>
    /// <para>
    /// The move id does say it, so this is written out in full: eight stats over sixty-one ids,
    /// with plain attacks left to the default. <b>Inferred</b>, in that the game states the
    /// pairing nowhere - but the ids are explicit enough (BUFF_ACCURACY, SELF_SHIELD,
    /// SELF_RESET_CD) that there is little room to be wrong.
    /// </para>
    /// </remarks>
    private static readonly Dictionary<string, string> MoveStat = new(StringComparer.Ordinal)
    {
        // Damage, raised or lowered.
        ["BUFF_DAMAGE"] = "power",     ["DEBUFF_DAMAGE"] = "power",  ["BM_DEBUFF_DAM"] = "power",
        ["DEBUFF_DAM_ACC"] = "power",  ["ENRAGE_DAMAGE"] = "power",  ["SACRIFICE_DAM"] = "power",
        ["BUFF_CRIT"] = "power",       ["ENRAGE_CRIT"] = "power",    ["CHARGEUP_AFF"] = "power",
        ["CHARGEUP_BUFF"] = "power",

        // Whether it lands.
        ["BUFF_ACCURACY"] = "accuracy", ["DEBUFF_ACCURACY"] = "accuracy", ["BM_DEBUFF_ACC"] = "accuracy",

        // Going first, and going twice.
        ["BUFF_SPEED"] = "speed", ["SPEED_UP"] = "speed",
        ["AFF_SPEED_ME"] = "speed", ["AFF_SPEED_THEM"] = "speed",

        // Not being hit at all.
        ["BUFF_DODGE"] = "stealth", ["BURROW"] = "stealth",

        // Being hit for less: shields, reflects, absorbs, and the two that trade defence away.
        ["SELF_SHIELD"] = "defence",  ["FULL_SHIELD"] = "defence",  ["SELF_REFLECT"] = "defence",
        ["FULL_REFLECT"] = "defence", ["SELF_ABSORB"] = "defence",  ["FULL_ABSORB"] = "defence",
        ["DEBUFF_DEF"] = "defence",   ["SACRIFICE_DEF"] = "defence", ["DONT_TOUCH"] = "defence",

        // Health back, or a turn back.
        ["SELF_HEAL"] = "health", ["SELF_HOT"] = "health", ["DELAY_HEAL"] = "health",
        ["REVIVE"] = "health",    ["SELF_DISPEL"] = "health",
        ["SELF_RESET_CD"] = "cooldown", ["STUN"] = "cooldown", ["DELAY_AFF"] = "cooldown",
    };

    /// <summary>The glyph for a move: the stat it touches, or a plain attack.</summary>
    private static string IconFor(string moveId)
        => (MoveStat.GetValueOrDefault(moveId) ?? "attack") + ".webp";

    /// <summary>
    /// The moves that are typed to something other than the creature that knows them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Most moves take the creature's own affinity, which is why their names change with it -
    /// the same STUN is Snaring Roots on a tropical creature and Icy Chains on a frost one.
    /// These are the exceptions: eight plain attacks fixed to one affinity each, and two that
    /// are typed to nothing at all.
    /// </para>
    /// <para>
    /// Written out rather than read off the id suffix, which looks like it would work and does
    /// not: SELF_HOT is a heal over time, not a fire move.
    /// </para>
    /// </remarks>
    private static readonly Dictionary<string, string> MoveAffinity = new(StringComparer.Ordinal)
    {
        ["ATTACK_HOT"] = "Fire",         ["ATTACK_COLD"] = "Cold",
        ["ATTACK_LUSH"] = "Lush",        ["ATTACK_DUST"] = "Barren",
        ["ATTACK_TOX"] = "Toxic",        ["ATTACK_RAD"] = "Radioactive",
        ["ATTACK_WEIRD"] = "Weird",
        ["ATTACK_NORM"] = "",            ["ATTACK_DOT_NORM"] = "",
    };

    /// <summary>
    /// Which affinity glyph a move is drawn with: an id, empty for none, or <c>Self</c> for
    /// whichever affinity the creature has.
    /// </summary>
    private static string AffinityFor(string moveId)
        => MoveAffinity.GetValueOrDefault(moveId, "Self");

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
            string target = move.TryGetProperty("Target", out var t) ? t.GetString() ?? "" : "";

            var entry = new JsonObject();
            entry.Set("Id", id);
            entry.Set("Icon", IconFor(id));

            // The affinity half of the button. Empty means the move is typed to nothing, so
            // only the stat glyph is drawn.
            if (AffinityFor(id) is { Length: > 0 } typed) entry.Set("Affinity", typed);

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
