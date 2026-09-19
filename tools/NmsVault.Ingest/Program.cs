using NmsVault.Core;
using NmsVault.Core.Adapters;
using NmsVault.Core.Detection;
using NmsVault.Json;

namespace NmsVault.Ingest;

/// <summary>
/// Command-line tool for adding items to the gallery and checking what is already there.
/// </summary>
/// <remarks>
/// Hand-rolled argument parsing rather than a library, to keep the no-dependencies property
/// that makes the WebAssembly payload small. There are four verbs; a parser library would
/// outweigh them.
/// </remarks>
public static class Program
{
    private const string DefaultGalleryRoot = "src/NmsVault.Web/wwwroot/gallery";

    /// <summary>Entry point.</summary>
    public static int Main(string[] args)
    {
        try
        {
            return (args.FirstOrDefault() ?? "help") switch
            {
                "add" => Add(Args.Parse(args.Skip(1))),
                "inspect" => Inspect(Args.Parse(args.Skip(1))),
                "validate" => Validate(Args.Parse(args.Skip(1))),
                "reindex" => Reindex(Args.Parse(args.Skip(1))),
                _ => Help(),
            };
        }
        catch (Exception ex) when (ex is ImportException or InvalidDataException or ArgumentException)
        {
            // Expected failure modes get a clean message; anything else keeps its stack trace.
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    // --- add ----------------------------------------------------------

    private static int Add(Args args)
    {
        string source = args.Require("file");
        var store = new GalleryStore(args.Get("gallery") ?? DefaultGalleryRoot);
        var mapper = JsonNameMapper.LoadEmbedded();

        byte[] bytes = File.ReadAllBytes(source);

        // Report what was detected before doing anything, so a wrong guess is visible rather
        // than silently baked into a stored item.
        var detected = FormatDetector.Detect(bytes, mapper, source);
        Console.WriteLine($"  detected: {detected.Format} / {detected.Kind} / {detected.Keys} keys ({detected.Certainty})");
        Console.WriteLine($"    reason: {detected.Reason}");

        string displayName = args.Get("name") ?? Path.GetFileNameWithoutExtension(source);
        string id = args.Get("id") ?? Slug.From(displayName);

        if (store.Exists(id) && !args.Has("force"))
            throw new ArgumentException($"'{id}' already exists. Pass --force to replace it, or --id to store alongside.");

        var images = args.GetList("images");
        var meta = new VaultMetadata
        {
            Id = id,
            DisplayName = displayName,
            Description = args.Get("description") ?? "",
            AlternativeNames = args.GetList("alt-names"),
            Tags = args.GetList("tags"),
            Author = args.Get("author"),
            DateAdded = DateTimeOffset.UtcNow,
            GameVersion = args.Get("game-version"),
            // Placeholder: replaced below once the files are copied and their final
            // gallery-relative paths are known.
            Images = [],
        };

        var item = new VaultImporter(mapper).Import(bytes, meta, source);

        if (images.Count > 0)
        {
            var paths = images.Select((path, i) => store.AddImage(path, id, i)).ToList();
            item = VaultItem.FromJson(item.ToJson(), id);
            item = Rebuild(item, meta with { Images = paths });
        }

        store.Write(item);
        int total = store.RebuildIndex();

        Console.WriteLine($"  stored: {store.PathFor(id)}");
        ReportLosses(item, mapper);
        Console.WriteLine($"  index rebuilt: {total} item(s)");
        return 0;
    }

    /// <summary>
    /// Rebuilds an item with different metadata. Needed because images are copied after the
    /// item is imported - only then are their final paths known.
    /// </summary>
    private static VaultItem Rebuild(VaultItem item, VaultMetadata meta)
        => VaultItem.Create(item.Kind, item.Payload, meta,
            item.CharacterCustomisationData, item.UsesLegacyColours,
            item.ShipBase, item.AccessorySlots);

    // --- inspect ------------------------------------------------------

    private static int Inspect(Args args)
    {
        string source = args.Require("file");
        var mapper = JsonNameMapper.LoadEmbedded();
        byte[] bytes = File.ReadAllBytes(source);

        var detected = FormatDetector.Detect(bytes, mapper, source);
        Console.WriteLine($"{Path.GetFileName(source)}");
        Console.WriteLine($"  format    {detected.Format}");
        Console.WriteLine($"  kind      {detected.Kind}");
        Console.WriteLine($"  keys      {detected.Keys}");
        Console.WriteLine($"  certainty {detected.Certainty}");
        Console.WriteLine($"  reason    {detected.Reason}");

        if (detected.Format == SourceFormat.Unknown) return 1;

        var item = new VaultImporter(mapper).Import(bytes,
            new VaultMetadata { Id = "inspect", DisplayName = "inspect" }, source);

        Console.WriteLine($"  payload   {item.Payload.Length} keys");
        Console.WriteLine($"  legacy    {DescribeLegacyColours(item)}");
        Console.WriteLine($"  custom    {DescribeCustomisation(item)}");
        Console.WriteLine($"  base      {(item.ShipBase is null ? "none" : "present")}");
        ReportLosses(item, mapper);
        return 0;
    }

    // --- validate -----------------------------------------------------

    private static int Validate(Args args)
    {
        var store = new GalleryStore(args.Get("gallery") ?? DefaultGalleryRoot);
        var mapper = JsonNameMapper.LoadEmbedded();
        var adapters = Adapters(mapper);

        int checked_ = 0, failed = 0;
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (path, item) in store.ReadAll())
        {
            checked_++;
            string name = Path.GetFileName(path);
            var problems = new List<string>();

            if (item.Meta.Id.Length == 0) problems.Add("no Id");
            else if (!seenIds.Add(item.Meta.Id)) problems.Add($"duplicate Id '{item.Meta.Id}'");
            else if (item.Meta.Id != Path.GetFileNameWithoutExtension(path))
                problems.Add($"Id '{item.Meta.Id}' does not match its filename");

            if (item.Meta.DisplayName.Length == 0) problems.Add("no DisplayName");
            if (!item.Kind.IsAvailable()) problems.Add($"{item.Kind} is not offered yet");

            // A stale mapping table would leave keys unmappable, which silently produces
            // half-obfuscated output for NMS Companion and NomNom.
            int unmapped = KeyObfuscator.CountUnmapped(item.Payload, mapper);
            if (unmapped > 0) problems.Add($"{unmapped} payload key(s) missing from the mapping table");

            // Every adapter that claims to support this kind must actually produce a file.
            foreach (var adapter in adapters)
            {
                if (!adapter.Extension(item.Kind).HasValue) continue;
                try
                {
                    var result = adapter.Export(item);
                    if (result.Content.Length == 0) problems.Add($"{adapter.Editor} produced an empty file");
                }
                catch (Exception ex)
                {
                    problems.Add($"{adapter.Editor} failed: {ex.Message}");
                }
            }

            if (problems.Count > 0)
            {
                failed++;
                Console.Error.WriteLine($"FAIL {name}");
                foreach (var p in problems) Console.Error.WriteLine($"       {p}");
            }
        }

        Console.WriteLine($"{checked_ - failed}/{checked_} item(s) valid");
        return failed == 0 ? 0 : 1;
    }

    // --- reindex ------------------------------------------------------

    private static int Reindex(Args args)
    {
        var store = new GalleryStore(args.Get("gallery") ?? DefaultGalleryRoot);
        Console.WriteLine($"index rebuilt: {store.RebuildIndex()} item(s)");
        return 0;
    }

    // --- shared -------------------------------------------------------

    private static IReadOnlyList<IExportAdapter> Adapters(JsonNameMapper mapper) =>
    [
        new NmseExportAdapter(),
        new GoatfungusExportAdapter(),
        new CompanionExportAdapter(mapper),
        new NomNomExportAdapter(mapper),
    ];

    /// <summary>
    /// Ships keep the legacy-colour flag in a parallel array outside the entity, so it is a
    /// vault sidecar. Multitools keep theirs inline on the object as <c>UseLegacyColours</c>
    /// - note the missing s - so reporting only the sidecar would wrongly say "not stated"
    /// for a multitool that has one.
    /// </summary>
    private static string DescribeLegacyColours(VaultItem item)
    {
        if (item.UsesLegacyColours is { } sidecar) return $"{sidecar} (sidecar)";
        if (item.Payload.Get("UseLegacyColours") is bool inline) return $"{inline} (inline)";
        return "not stated";
    }

    /// <summary>
    /// Same split: a ship's customisation is a sidecar entry, a multitool's is inline.
    /// </summary>
    private static string DescribeCustomisation(VaultItem item)
    {
        if (item.CharacterCustomisationData is { } ccd)
            return CustomisationHelpers.IsDefault(ccd) ? "present but default (sidecar)" : "present (sidecar)";

        if (item.Payload.GetObject("CustomisationData") is { } inline)
            return CustomisationHelpers.IsDefault(inline) ? "present but default (inline)" : "present (inline)";

        return "none";
    }

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

    private static int Help()
    {
        Console.WriteLine("""
            NMS-Vault ingest

              add       --file <path> [--name <s>] [--id <s>] [--description <s>]
                        [--tags a,b] [--alt-names a,b] [--images a.png,b.png]
                        [--author <s>] [--game-version <s>] [--force] [--gallery <dir>]
                        Detect, convert to the vault format, store, rebuild the index.

              inspect   --file <path>
                        Report what a file is and what each editor would lose. Changes nothing.

              validate  [--gallery <dir>]
                        Check every stored item and that every adapter can export it.

              reindex   [--gallery <dir>]
                        Rebuild index.json from the item files.

            Gallery defaults to src/NmsVault.Web/wwwroot/gallery.
            """);
        return 0;
    }
}
