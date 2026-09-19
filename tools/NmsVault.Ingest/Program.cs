using System.CommandLine;
using NmsVault.Core;
using NmsVault.Core.Adapters;
using NmsVault.Core.Derived;
using NmsVault.Core.Detection;
using NmsVault.Json;

namespace NmsVault.Ingest;

/// <summary>
/// Command-line tool for building the gallery: adding items, checking what is already there,
/// and extracting the technology lookup from NMSE's resources.
/// </summary>
public static class Program
{
    private const string DefaultGalleryRoot = "src/NmsVault.Web/wwwroot/gallery";

    /// <summary>Entry point.</summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>Zero on success.</returns>
    public static int Main(string[] args)
    {
        var gallery = new Option<DirectoryInfo>("--gallery")
        {
            Description = "The gallery folder to work in.",
            DefaultValueFactory = _ => new DirectoryInfo(DefaultGalleryRoot),
        };

        var root = new RootCommand("Builds and checks the NMS-Vault gallery.")
        {
            AddCommand(gallery),
            InspectCommand(),
            ValidateCommand(gallery),
            ReindexCommand(gallery),
            ExtractTechCommand(gallery),
        };

        return root.Parse(args).Invoke();
    }

    // --- add ----------------------------------------------------------

    private static Command AddCommand(Option<DirectoryInfo> gallery)
    {
        var file = new Option<FileInfo>("--file") { Description = "The editor export to add.", Required = true };
        var name = new Option<string?>("--name") { Description = "Display name. Defaults to the file name." };
        var id = new Option<string?>("--id") { Description = "Permalink slug. Defaults to a slug of the name." };
        var description = new Option<string?>("--description") { Description = "Free text shown on the item." };
        var tags = new Option<string?>("--tags") { Description = "Comma-separated filter tags." };
        var altNames = new Option<string?>("--alt-names") { Description = "Comma-separated alternative names, for search." };
        var images = new Option<string?>("--images") { Description = "Comma-separated image paths, best first." };
        var author = new Option<string?>("--author") { Description = "Contributor credit." };
        var gameVersion = new Option<string?>("--game-version") { Description = "Game version captured from, e.g. 7.03." };
        var force = new Option<bool>("--force") { Description = "Replace an item that already exists." };

        var command = new Command("add", "Detect, convert to the vault format, store, and rebuild the index.")
        { file, name, id, description, tags, altNames, images, author, gameVersion, force, gallery };

        command.SetAction(result => Add(
            result.GetValue(file)!,
            new GalleryStore(result.GetValue(gallery)!.FullName),
            result.GetValue(name),
            result.GetValue(id),
            result.GetValue(description),
            Split(result.GetValue(tags)),
            Split(result.GetValue(altNames)),
            Split(result.GetValue(images)),
            result.GetValue(author),
            result.GetValue(gameVersion),
            result.GetValue(force)));

        return command;
    }

    private static int Add(
        FileInfo source, GalleryStore store, string? name, string? id, string? description,
        IReadOnlyList<string> tags, IReadOnlyList<string> altNames, IReadOnlyList<string> images,
        string? author, string? gameVersion, bool force)
    {
        var mapper = JsonNameMapper.LoadEmbedded();
        byte[] bytes = File.ReadAllBytes(source.FullName);

        // Reported before anything is stored, so a wrong detection is visible rather than
        // baked into an item nobody looks at again.
        var detected = FormatDetector.Detect(bytes, mapper, source.Name);
        Console.WriteLine($"  detected: {detected.Format} / {detected.Kind} / {detected.Keys} keys ({detected.Certainty})");
        Console.WriteLine($"    reason: {detected.Reason}");

        string displayName = name ?? Path.GetFileNameWithoutExtension(source.Name);
        string slug = id ?? Slug.From(displayName);

        if (store.Exists(slug) && !force)
        {
            Console.Error.WriteLine($"error: '{slug}' already exists. Pass --force to replace it, or --id to store alongside.");
            return 1;
        }

        var meta = new VaultMetadata
        {
            Id = slug,
            DisplayName = displayName,
            Description = description ?? "",
            AlternativeNames = altNames,
            Tags = tags,
            Author = author,
            DateAdded = DateTimeOffset.UtcNow,
            GameVersion = gameVersion,
            Images = [],
        };

        var item = new VaultImporter(mapper).Import(bytes, meta, source.Name);

        if (images.Count > 0)
        {
            var paths = images.Select((path, i) => store.AddImage(path, slug, i)).ToList();
            item = VaultItem.Create(item.Kind, item.Payload, meta with { Images = paths },
                item.CharacterCustomisationData, item.UsesLegacyColours,
                item.ShipBase, item.AccessorySlots);
        }

        store.Write(item);
        Console.WriteLine($"  stored: {store.PathFor(slug)}");
        ReportLosses(item, mapper);
        Console.WriteLine($"  index rebuilt: {store.RebuildIndex()} item(s)");
        return 0;
    }

    // --- inspect ------------------------------------------------------

    private static Command InspectCommand()
    {
        var file = new Option<FileInfo>("--file") { Description = "The file to identify.", Required = true };
        var command = new Command("inspect", "Report what a file is and what each editor would lose. Changes nothing.") { file };

        command.SetAction(result => Inspect(result.GetValue(file)!));
        return command;
    }

    private static int Inspect(FileInfo source)
    {
        var mapper = JsonNameMapper.LoadEmbedded();
        byte[] bytes = File.ReadAllBytes(source.FullName);

        var detected = FormatDetector.Detect(bytes, mapper, source.Name);
        Console.WriteLine(source.Name);
        Console.WriteLine($"  format    {detected.Format}");
        Console.WriteLine($"  kind      {detected.Kind}");
        Console.WriteLine($"  keys      {detected.Keys}");
        Console.WriteLine($"  certainty {detected.Certainty}");
        Console.WriteLine($"  reason    {detected.Reason}");

        if (detected.Format == SourceFormat.Unknown) return 1;

        var item = new VaultImporter(mapper).Import(bytes,
            new VaultMetadata { Id = "inspect", DisplayName = "inspect" }, source.Name);

        var facts = ItemFacts.For(item);
        Console.WriteLine($"  type      {facts.Type}{(facts.IsModifiedResource ? " (modified)" : "")}");
        Console.WriteLine($"  class     {facts.Class ?? "none"}");
        Console.WriteLine($"  legacy    {DescribeLegacyColours(item)}");
        Console.WriteLine($"  custom    {DescribeCustomisation(item)}");
        Console.WriteLine($"  base      {(item.ShipBase is null ? "none" : "present")}");
        foreach (var stat in facts.Stats) Console.WriteLine($"  {stat.Label,-16}{stat.Value:0.##}");
        Console.WriteLine($"  tech      {facts.InstalledTech.Count} installed");

        ReportLosses(item, mapper);
        return 0;
    }

    // --- validate -----------------------------------------------------

    private static Command ValidateCommand(Option<DirectoryInfo> gallery)
    {
        var command = new Command("validate", "Check every stored item, and that every adapter can export it.") { gallery };
        command.SetAction(result => Validate(new GalleryStore(result.GetValue(gallery)!.FullName)));
        return command;
    }

    private static int Validate(GalleryStore store)
    {
        var mapper = JsonNameMapper.LoadEmbedded();
        var adapters = Adapters(mapper);
        var tech = LoadTechIndex(store);

        int checkedCount = 0, failed = 0;
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (path, item) in store.ReadAll())
        {
            checkedCount++;
            var problems = new List<string>();

            if (item.Meta.Id.Length == 0) problems.Add("no Id");
            else if (!seenIds.Add(item.Meta.Id)) problems.Add($"duplicate Id '{item.Meta.Id}'");
            else if (item.Meta.Id != Path.GetFileNameWithoutExtension(path))
                problems.Add($"Id '{item.Meta.Id}' does not match its filename");

            if (item.Meta.DisplayName.Length == 0) problems.Add("no DisplayName");
            if (!item.Kind.IsAvailable()) problems.Add($"{item.Kind} is not offered yet");

            // A stale mapping table leaves keys unmappable, which silently produces
            // half-obfuscated output for NMS Companion and NomNom.
            int unmapped = KeyObfuscator.CountUnmapped(item.Payload, mapper);
            if (unmapped > 0) problems.Add($"{unmapped} payload key(s) missing from the mapping table");

            var facts = ItemFacts.For(item);
            if (facts.Type is "Unknown") problems.Add("type did not resolve");

            // Every installed technology must be nameable, or the info panel shows raw ids.
            if (tech.Count > 0)
            {
                var unknown = facts.InstalledTech.Where(id => tech.Find(id) is null).ToList();
                if (unknown.Count > 0)
                    problems.Add($"{unknown.Count} technology id(s) not in tech.json: {string.Join(", ", unknown.Take(4))}");
            }

            foreach (var adapter in adapters)
            {
                if (!adapter.Extension(item.Kind).HasValue) continue;
                try
                {
                    if (adapter.Export(item).Content.Length == 0)
                        problems.Add($"{adapter.Editor} produced an empty file");
                }
                catch (Exception ex)
                {
                    problems.Add($"{adapter.Editor} failed: {ex.Message}");
                }
            }

            if (problems.Count > 0)
            {
                failed++;
                Console.Error.WriteLine($"FAIL {Path.GetFileName(path)}");
                foreach (var problem in problems) Console.Error.WriteLine($"       {problem}");
            }
        }

        if (tech.Count == 0)
            Console.WriteLine("  note: no tech.json, so installed technology was not checked. Run extract-tech.");

        Console.WriteLine($"{checkedCount - failed}/{checkedCount} item(s) valid");
        return failed == 0 ? 0 : 1;
    }

    // --- reindex ------------------------------------------------------

    private static Command ReindexCommand(Option<DirectoryInfo> gallery)
    {
        var command = new Command("reindex", "Rebuild index.json from the item files.") { gallery };
        command.SetAction(result =>
        {
            var store = new GalleryStore(result.GetValue(gallery)!.FullName);
            Console.WriteLine($"index rebuilt: {store.RebuildIndex()} item(s)");
            return 0;
        });
        return command;
    }

    // --- extract-tech -------------------------------------------------

    private static Command ExtractTechCommand(Option<DirectoryInfo> gallery)
    {
        var nmse = new Option<DirectoryInfo>("--nmse")
        {
            Description = "Path to NMSE's Resources folder.",
            Required = true,
        };

        var command = new Command("extract-tech",
            "Build the technology lookup and icons from NMSE's resources. Run when the game updates.")
        { nmse, gallery };

        command.SetAction(result =>
        {
            var target = result.GetValue(gallery)!.FullName;
            Console.WriteLine($"extracting technology into {target}");

            var outcome = TechExtractor.Extract(
                result.GetValue(nmse)!.FullName, target, line => Console.WriteLine(line));

            Console.WriteLine($"  {outcome.Technologies} technologies, {outcome.IconsWritten} icons " +
                              $"({outcome.Bytes / 1024.0 / 1024.0:0.0} MB)");
            if (outcome.IconsMissing > 0)
                Console.WriteLine($"  {outcome.IconsMissing} icon(s) named but not found in the source");
            return 0;
        });

        return command;
    }

    // --- shared -------------------------------------------------------

    private static TechIndex LoadTechIndex(GalleryStore store)
    {
        string path = Path.Combine(Path.GetDirectoryName(store.IndexPath)!, "tech.json");
        return File.Exists(path) ? TechIndex.FromBytes(File.ReadAllBytes(path)) : TechIndex.Empty;
    }

    private static IReadOnlyList<IExportAdapter> Adapters(JsonNameMapper mapper) =>
    [
        new NmseExportAdapter(),
        new GoatfungusExportAdapter(),
        new CompanionExportAdapter(mapper),
        new NomNomExportAdapter(mapper),
    ];

    private static void ReportLosses(VaultItem item, JsonNameMapper mapper)
    {
        foreach (var adapter in Adapters(mapper))
        {
            var extension = adapter.Extension(item.Kind);
            if (!extension.HasValue)
            {
                Console.WriteLine($"  {adapter.Editor,-10} unavailable - {extension.Alternative.Reason}");
                continue;
            }

            var losses = adapter.LossesFor(item);
            string suffix = adapter.IsVerified ? "" : " [unverified]";
            if (losses.Count == 0)
                Console.WriteLine($"  {adapter.Editor,-10} {extension.Value} - lossless{suffix}");
            else
                foreach (var loss in losses)
                    Console.WriteLine($"  {adapter.Editor,-10} {extension.Value} - loses {loss}{suffix}");
        }
    }

    /// <summary>
    /// Ships keep the legacy-colour flag in a parallel array outside the entity, so it is a
    /// vault sidecar. Multitools keep theirs inline as <c>UseLegacyColours</c> - note the
    /// missing s - so reporting only the sidecar would wrongly say "not stated" for one.
    /// </summary>
    private static string DescribeLegacyColours(VaultItem item)
    {
        if (item.UsesLegacyColours is { } sidecar) return $"{sidecar} (sidecar)";
        if (item.Payload.Get("UseLegacyColours") is bool inline) return $"{inline} (inline)";
        return "not stated";
    }

    private static string DescribeCustomisation(VaultItem item)
    {
        if (item.CharacterCustomisationData is { } ccd)
            return CustomisationHelpers.IsDefault(ccd) ? "present but default (sidecar)" : "present (sidecar)";

        if (item.Payload.GetObject("CustomisationData") is { } inline)
            return CustomisationHelpers.IsDefault(inline) ? "present but default (inline)" : "present (inline)";

        return "none";
    }

    private static IReadOnlyList<string> Split(string? value)
        => value is null ? [] : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
