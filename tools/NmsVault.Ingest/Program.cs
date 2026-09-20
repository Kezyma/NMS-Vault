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
            UpdateCommand(gallery),
            ReimportCommand(gallery),
            ConvertCommand(),
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
        var summary = new Option<string?>("--summary") { Description = "One line, shown on the card." };
        var description = new Option<string?>("--description") { Description = "Full text, shown when the item is opened." };
        var tags = new Option<string?>("--tags") { Description = "Comma-separated filter tags." };
        var altNames = new Option<string?>("--alt-names") { Description = "Comma-separated alternative names, for search." };
        var images = new Option<string?>("--images") { Description = "Comma-separated image paths. Defaults to the pictures stored beside the export." };
        var author = new Option<string?>("--author") { Description = "Contributor credit." };
        var gameVersion = new Option<string?>("--game-version") { Description = "Game version captured from, e.g. 7.03." };
        var force = new Option<bool>("--force") { Description = "Replace an item that already exists." };

        var command = new Command("add", "Detect, convert to the vault format, store, and rebuild the index.")
        { file, name, id, summary, description, tags, altNames, images, author, gameVersion, force, gallery };

        command.SetAction(result => Add(
            result.GetValue(file)!,
            new GalleryStore(result.GetValue(gallery)!.FullName),
            result.GetValue(name),
            result.GetValue(id),
            result.GetValue(summary),
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
        FileInfo source, GalleryStore store, string? name, string? id, string? summary, string? description,
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

        // What is written beside the export, then what was typed - a flag is a deliberate
        // override of a file that is otherwise the item's own record of itself.
        var beside = GalleryStore.ApplyMetadataBeside(
            new VaultMetadata { Id = "", DisplayName = "" }, source.FullName);

        string displayName = name
            ?? (beside.DisplayName is { Length: > 0 } written ? written : Path.GetFileNameWithoutExtension(source.Name));

        string slug = id ?? (beside.Id is { Length: > 0 } chosen ? chosen : Slug.From(displayName));

        if (store.Exists(slug) && !force)
        {
            Console.Error.WriteLine($"error: '{slug}' already exists. Pass --force to replace it, or --id to store alongside.");
            return 1;
        }

        var meta = beside with
        {
            Id = slug,
            DisplayName = displayName,
            Summary = summary ?? beside.Summary,
            Description = description ?? beside.Description,
            AlternativeNames = altNames.Count > 0 ? altNames : beside.AlternativeNames,
            Tags = tags.Count > 0 ? tags : beside.Tags,
            Author = author ?? beside.Author,
            DateAdded = DateTimeOffset.UtcNow,
            GameVersion = gameVersion ?? beside.GameVersion,
            Source = source.Name,
            Images = [],
        };

        var item = new VaultImporter(mapper).Import(bytes, meta, source.Name);

        // Beside the export unless told otherwise: a capture is kept next to the backup it is
        // of, under the same name, so an item's pictures arrive with it.
        if (images.Count == 0) images = GalleryStore.PicturesBeside(source.FullName);

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

    // --- update -------------------------------------------------------

    private static Command UpdateCommand(Option<DirectoryInfo> gallery)
    {
        var id = new Option<string>("--id") { Description = "The item to change.", Required = true };
        var name = new Option<string?>("--name") { Description = "New display name." };
        var summary = new Option<string?>("--summary") { Description = "One line, shown on the card." };
        var description = new Option<string?>("--description") { Description = "Full text, shown when the item is opened." };
        var tags = new Option<string?>("--tags") { Description = "Comma-separated filter tags, replacing the current ones." };
        var altNames = new Option<string?>("--alt-names") { Description = "Comma-separated alternative names, replacing the current ones." };
        var images = new Option<string?>("--images") { Description = "Comma-separated image paths, best first, replacing the current ones." };
        var author = new Option<string?>("--author") { Description = "Contributor credit." };
        var gameVersion = new Option<string?>("--game-version") { Description = "Game version captured from, e.g. 7.03." };

        var command = new Command("update", "Change an existing item's metadata. The payload is left alone.")
        { id, name, summary, description, tags, altNames, images, author, gameVersion, gallery };

        command.SetAction(result => Update(
            new GalleryStore(result.GetValue(gallery)!.FullName),
            result.GetValue(id)!,
            result.GetValue(name),
            result.GetValue(summary),
            result.GetValue(description),
            result.GetValue(tags),
            result.GetValue(altNames),
            result.GetValue(images),
            result.GetValue(author),
            result.GetValue(gameVersion)));

        return command;
    }

    /// <summary>
    /// Rewrites one item's metadata in place.
    /// </summary>
    /// <remarks>
    /// Only what is passed changes; everything else is left as it was. That is the whole
    /// point of the verb - re-adding the export to change a caption would mean supplying
    /// every other field again and getting a new DateAdded for the trouble.
    /// </remarks>
    private static int Update(
        GalleryStore store, string id, string? name, string? summary, string? description,
        string? tags, string? altNames, string? images, string? author, string? gameVersion)
    {
        if (!store.Exists(id))
        {
            Console.Error.WriteLine($"error: no item with id '{id}'.");
            return 1;
        }

        var item = VaultItem.FromBytes(File.ReadAllBytes(store.PathFor(id)), id);
        var meta = item.Meta;

        if (name is not null) meta = meta with { DisplayName = name };
        if (summary is not null) meta = meta with { Summary = summary };
        if (description is not null) meta = meta with { Description = description };
        if (tags is not null) meta = meta with { Tags = Split(tags) };
        if (altNames is not null) meta = meta with { AlternativeNames = Split(altNames) };
        if (author is not null) meta = meta with { Author = author };
        if (gameVersion is not null) meta = meta with { GameVersion = gameVersion };

        if (images is not null)
            meta = meta with { Images = [.. Split(images).Select((path, i) => store.AddImage(path, id, i))] };

        store.Write(VaultItem.Create(item.Kind, item.Payload, meta,
            item.CharacterCustomisationData, item.UsesLegacyColours,
            item.ShipBase, item.AccessorySlots));

        Console.WriteLine($"  updated: {store.PathFor(id)}");
        Console.WriteLine($"  index rebuilt: {store.RebuildIndex()} item(s)");
        return 0;
    }

    // --- reimport -----------------------------------------------------

    private static Command ReimportCommand(Option<DirectoryInfo> gallery)
    {
        var file = new Option<FileInfo?>("--file") { Description = "One corrected export to read again." };
        var id = new Option<string?>("--id") { Description = "The item it belongs to. Defaults to the one recording this file as its source." };
        var from = new Option<DirectoryInfo?>("--from") { Description = "A folder of exports: every item whose source is in it is read again." };

        var command = new Command("reimport",
            "Read an export again into an item that already exists, keeping its gallery metadata.")
        { file, id, from, gallery };

        command.SetAction(result => Reimport(
            new GalleryStore(result.GetValue(gallery)!.FullName),
            result.GetValue(file),
            result.GetValue(id),
            result.GetValue(from)));

        return command;
    }

    /// <summary>
    /// Replaces stored payloads from their source exports, leaving the gallery metadata alone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The verb exists because the two halves of an item have different lifetimes. The payload
    /// comes from a backup and gets corrected when the backup does; the display name, summary
    /// and tags are work done here and must survive that. Adding the file again with --force
    /// would replace both.
    /// </para>
    /// <para>
    /// Pictures side with the payload rather than with the metadata, because they live beside
    /// the export and are re-read from there. An item whose export has no picture beside it
    /// ends up with none - which is how a picture is removed, and why one set by hand with
    /// <c>update --images</c> does not survive a re-import.
    /// </para>
    /// </remarks>
    private static int Reimport(GalleryStore store, FileInfo? file, string? id, DirectoryInfo? from)
    {
        if (file is null && from is null)
        {
            Console.Error.WriteLine("error: pass --file for one export, or --from for a folder of them.");
            return 1;
        }

        var mapper = JsonNameMapper.LoadEmbedded();
        var stored = store.ReadAll().ToList();

        var jobs = new List<(string Id, FileInfo File)>();

        if (file is not null)
        {
            string? target = id ?? stored.FirstOrDefault(s => IsSource(s.Item, file.Name)).Item?.Meta.Id;

            if (target is null)
            {
                Console.Error.WriteLine($"error: no item records '{file.Name}' as its source. Pass --id to say which one.");
                return 1;
            }

            jobs.Add((target, file));
        }

        if (from is not null)
        {
            // Matched on the recorded source rather than on the file name as a slug: two
            // exports can slug to the same id - there are two ships called Rasamama S36 -
            // and only the stored item knows which of them it was read from.
            var byName = from.EnumerateFiles("*", SearchOption.AllDirectories)
                .ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var (_, item) in stored)
            {
                if (item.Meta.Source is not { } source)
                {
                    Console.WriteLine($"  skipped {item.Meta.Id}: records no source file");
                    continue;
                }

                if (byName.TryGetValue(source, out var found)) jobs.Add((item.Meta.Id, found));
                else Console.WriteLine($"  skipped {item.Meta.Id}: '{source}' is not in that folder");
            }

            ReportOrphanedMetadata(byName.Values);
        }

        int changed = 0, failed = 0;

        foreach (var (target, export) in jobs)
        {
            if (!store.Exists(target))
            {
                Console.Error.WriteLine($"  {target}: no such item");
                failed++;
                continue;
            }

            var before = VaultItem.FromBytes(File.ReadAllBytes(store.PathFor(target)), target);
            byte[] bytes = File.ReadAllBytes(export.FullName);

            var pictures = GalleryStore.PicturesBeside(export.FullName);
            var meta = before.Meta with
            {
                Source = export.Name,
                Images = [.. pictures.Select((path, i) => store.AddImage(path, target, i))],
            };

            // Read again, so editing the file beside a ship and re-importing applies it.
            try
            {
                meta = GalleryStore.ApplyMetadataBeside(meta, export.FullName) with { Id = target };
            }
            catch (InvalidDataException ex)
            {
                Console.Error.WriteLine($"  {target}: {ex.Message}");
                failed++;
                continue;
            }

            VaultItem after;
            try
            {
                after = new VaultImporter(mapper).Import(bytes, meta, export.Name);
            }
            catch (Exception ex) when (ex is ImportException or InvalidDataException)
            {
                Console.Error.WriteLine($"  {target}: {ex.Message}");
                failed++;
                continue;
            }

            // A corrected backup is still the same thing. A different kind means the wrong
            // file, and writing it would quietly move the item to another page.
            if (after.Kind != before.Kind)
            {
                Console.Error.WriteLine($"  {target}: '{export.Name}' is a {after.Kind}, not a {before.Kind}");
                failed++;
                continue;
            }

            byte[] wrote = after.ToBytes();
            bool differs = !before.ToBytes().AsSpan().SequenceEqual(wrote);

            if (differs)
            {
                store.Write(after);
                changed++;
                Console.WriteLine($"  {target}: updated from {export.Name}{Describe(before, after)}");
            }
        }

        Console.WriteLine($"  {jobs.Count} read, {changed} changed, {failed} failed");
        if (changed > 0) Console.WriteLine($"  index rebuilt: {store.RebuildIndex()} item(s)");

        return failed > 0 ? 1 : 0;
    }

    /// <summary>
    /// Names any metadata file that is not beside an export.
    /// </summary>
    /// <remarks>
    /// A file whose name does not match an export is read by nothing, and says so to nobody -
    /// which is the whole failure: someone fills in a page of fields, mistypes the name by a
    /// bracket, re-imports, and sees a run that reports no errors and changes nothing.
    /// </remarks>
    private static void ReportOrphanedMetadata(IEnumerable<FileInfo> files)
    {
        var all = files.ToList();

        // Beside means a file of the same name with some other extension - the export itself.
        var stems = all
            .Where(f => !f.Extension.Equals(GalleryStore.MetadataExtension, StringComparison.OrdinalIgnoreCase))
            .Select(f => Path.Combine(f.DirectoryName ?? "", Path.GetFileNameWithoutExtension(f.Name)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var orphan in all
            .Where(f => f.Extension.Equals(GalleryStore.MetadataExtension, StringComparison.OrdinalIgnoreCase))
            .Where(f => !stems.Contains(Path.Combine(f.DirectoryName ?? "", Path.GetFileNameWithoutExtension(f.Name))))
            .OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            Console.WriteLine($"  note: '{orphan.Name}' sits beside no export, so nothing reads it");
        }
    }

    private static bool IsSource(VaultItem item, string fileName)
        => string.Equals(item.Meta.Source, fileName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Names the differences worth reading in a one-line report.</summary>
    private static string Describe(VaultItem before, VaultItem after)
    {
        var notes = new List<string>();

        if (before.UsesLegacyColours != after.UsesLegacyColours)
            notes.Add($"legacy colours {Say(before.UsesLegacyColours)} -> {Say(after.UsesLegacyColours)}");

        if (DescribeCustomisation(before) != DescribeCustomisation(after))
            notes.Add($"customisation {DescribeCustomisation(before)} -> {DescribeCustomisation(after)}");

        if (before.Meta.Images.Count != after.Meta.Images.Count)
            notes.Add($"pictures {before.Meta.Images.Count} -> {after.Meta.Images.Count}");

        if (Describe(before.Meta) != Describe(after.Meta)) notes.Add("metadata");

        var wasTech = ItemFacts.For(before).InstalledTech.Count;
        var nowTech = ItemFacts.For(after).InstalledTech.Count;
        if (wasTech != nowTech) notes.Add($"technology {wasTech} -> {nowTech}");

        return notes.Count == 0 ? "" : " (" + string.Join("; ", notes) + ")";
    }

    private static string Say(bool? value) => value is null ? "not stated" : value.Value ? "true" : "false";

    /// <summary>
    /// The metadata a person wrote, as one string, so a change to any of it can be noticed.
    /// Leaves out what the tool sets itself, which changes on every run and means nothing.
    /// </summary>
    private static string Describe(VaultMetadata meta) => string.Join('',
        meta.DisplayName, meta.Summary, meta.Description, meta.Author ?? "", meta.GameVersion ?? "",
        string.Join(',', meta.AlternativeNames), string.Join(',', meta.Tags));

    // --- convert ------------------------------------------------------

    private static Command ConvertCommand()
    {
        var file = new Option<FileInfo>("--file") { Description = "The export to read.", Required = true };
        var to = new Option<EditorId>("--to") { Description = "Which editor to write for.", Required = true };
        var output = new Option<FileInfo?>("--out") { Description = "Where to write it. Defaults to beside the source." };
        var name = new Option<string?>("--name") { Description = "Name to write inside formats that carry one." };

        var command = new Command("convert",
            "Read one export and write it out in another editor's format. Touches no gallery.")
        { file, to, output, name };

        command.SetAction(result => Convert(
            result.GetValue(file)!, result.GetValue(to), result.GetValue(output), result.GetValue(name)));

        return command;
    }

    /// <summary>
    /// Converts a single file, without storing anything.
    /// </summary>
    /// <remarks>
    /// The gallery is the reason this project exists, but the conversion underneath it is
    /// useful on its own - somebody with an old backup and a current editor wants one file
    /// turned into another, not a gallery.
    /// </remarks>
    private static int Convert(FileInfo source, EditorId to, FileInfo? output, string? name)
    {
        var mapper = JsonNameMapper.LoadEmbedded();
        byte[] bytes = File.ReadAllBytes(source.FullName);

        var detected = FormatDetector.Detect(bytes, mapper, source.Name);
        Console.WriteLine($"  read:    {detected.Format} / {detected.Kind} / {detected.Keys} keys ({detected.Certainty})");
        Console.WriteLine($"    reason: {detected.Reason}");

        if (detected.Format == SourceFormat.Unknown)
        {
            Console.Error.WriteLine("error: that file is not a format this recognises.");
            return 1;
        }

        VaultItem item;
        try
        {
            item = new VaultImporter(mapper).Import(bytes, new VaultMetadata
            {
                Id = "convert",
                DisplayName = name ?? Path.GetFileNameWithoutExtension(source.Name),
            }, source.Name);
        }
        catch (Exception ex) when (ex is ImportException or InvalidDataException or InvalidOperationException)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }

        var adapter = Adapters(mapper).First(a => a.Editor == to);
        var extension = adapter.Extension(item.Kind);

        if (!extension.HasValue)
        {
            Console.Error.WriteLine($"error: {adapter.DisplayName} {extension.Alternative.Reason}");
            return 1;
        }

        // Said before the file is written, not after: the point of saying it is to give
        // somebody the chance to not write it.
        foreach (string loss in adapter.LossesFor(item))
            Console.WriteLine($"  loses:   {loss}");

        var written = adapter.Export(item);
        string target = output?.FullName
            ?? Path.Combine(source.DirectoryName ?? ".", Path.GetFileNameWithoutExtension(source.Name) + extension.Value);

        File.WriteAllBytes(target, written.Content);

        Console.WriteLine($"  written: {target} ({written.Content.Length:n0} bytes, {adapter.DisplayName})");
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

            Console.WriteLine($"  {outcome.Technologies} technologies, {outcome.IconsWritten} icons, " +
                              $"{outcome.ClassIcons} class badges ({outcome.Bytes / 1024.0 / 1024.0:0.0} MB)");
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
