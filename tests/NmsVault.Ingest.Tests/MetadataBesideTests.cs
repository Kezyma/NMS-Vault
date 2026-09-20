using System.Text;
using NmsVault.Core;
using NmsVault.Ingest;

namespace NmsVault.Ingest.Tests;

/// <summary>
/// Tests for the metadata written by hand beside an export.
/// </summary>
/// <remarks>
/// This is the one part of the tool a person types into rather than the tool generating, so
/// the rules about what a missing key means, what an empty one means, and what an absent file
/// means all have to hold - each of them silently does the wrong thing otherwise, and the
/// wrong thing is somebody's description quietly disappearing.
/// </remarks>
public class MetadataBesideTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("nmsvault-meta").FullName;

    /// <summary>Removes the temporary folder.</summary>
    public void Dispose()
    {
        Directory.Delete(_folder, recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Writes an export and, optionally, a metadata file beside it.
    /// </summary>
    /// <remarks>
    /// With a byte order mark by default, because a file typed on Windows arrives with one as
    /// often as not and that is the case worth exercising.
    /// </remarks>
    private string Export(string? metadata, string name = "[EXP-13-R] Iron Vulture.nmsship", bool mark = true)
    {
        string export = Path.Combine(_folder, name);
        File.WriteAllText(export, "{}");

        if (metadata is not null)
        {
            File.WriteAllText(Path.ChangeExtension(export, ".json"), metadata,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: mark));
        }

        return export;
    }

    private static VaultMetadata Stored => new()
    {
        Id = "iron-vulture",
        DisplayName = "Iron Vulture",
        Summary = "A hauler.",
        Description = "Set earlier.",
        Tags = ["hauler"],
        AlternativeNames = ["Vulture"],
        Author = "Someone",
        GameVersion = "7.03",
    };

    [Fact]
    public void NoFileLeavesEverythingAlone()
    {
        // Unlike pictures, where the files beside the export are the whole truth and removing
        // one removes it. A metadata file says what to set; it does not claim to be the only
        // thing that ever set it, so deleting it does not wipe what update put there.
        var read = GalleryStore.ApplyMetadataBeside(Stored, Export(metadata: null));

        Assert.Equal(Stored.Summary, read.Summary);
        Assert.Equal(Stored.Description, read.Description);
        Assert.Equal(Stored.Tags, read.Tags);
        Assert.Equal(Stored.Author, read.Author);
    }

    [Fact]
    public void OnlyTheKeysPresentAreApplied()
    {
        var read = GalleryStore.ApplyMetadataBeside(Stored, Export("""
            { "Summary": "A hauler carrying full custom parts." }
            """));

        Assert.Equal("A hauler carrying full custom parts.", read.Summary);

        // Everything the file did not mention is as it was.
        Assert.Equal("Set earlier.", read.Description);
        Assert.Equal(["hauler"], read.Tags);
        Assert.Equal("Someone", read.Author);
        Assert.Equal("Iron Vulture", read.DisplayName);
    }

    [Fact]
    public void AnEmptyValueClearsTheField()
    {
        // How something set earlier is removed. A key that is present and empty is an
        // instruction; a key that is absent is silence.
        var read = GalleryStore.ApplyMetadataBeside(Stored, Export("""
            { "Description": "", "Tags": [], "Author": "" }
            """));

        Assert.Equal("", read.Description);
        Assert.Empty(read.Tags);
        Assert.Null(read.Author);

        Assert.Equal("A hauler.", read.Summary);
    }

    [Fact]
    public void AByteOrderMarkIsNotMistakenForContent()
    {
        // Notepad and Visual Studio both write one; the parser reads bytes as Latin-1, so
        // three of them turn into three characters in front of the opening brace.
        foreach (bool mark in (bool[])[true, false])
        {
            var read = GalleryStore.ApplyMetadataBeside(Stored,
                Export("""{ "Summary": "Either way." }""", $"ship-{mark}.nmsship", mark));

            Assert.Equal("Either way.", read.Summary);
        }
    }

    [Fact]
    public void ParagraphsSurviveTheJson()
    {
        var read = GalleryStore.ApplyMetadataBeside(Stored, Export("""
            { "Description": "First.\n\nSecond.\n\nThird." }
            """));

        Assert.Equal(3, read.Description.Split("\n\n").Length);
        Assert.StartsWith("First.", read.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void AnIdIsSluggedRatherThanTakenAsWritten()
    {
        // The id is a URL, and a file filled in by hand is exactly where "Iron Vulture" gets
        // typed into one.
        var read = GalleryStore.ApplyMetadataBeside(Stored, Export("""
            { "Id": "Iron Vulture (Redux)" }
            """));

        Assert.Equal("iron-vulture-redux", read.Id);
    }

    [Fact]
    public void ABlockCopiedOutOfAStoredItemWorksToo()
    {
        // A stored item wraps its metadata under Vault. Someone looking for an example will
        // open one and copy what is in it, so both shapes are accepted.
        var read = GalleryStore.ApplyMetadataBeside(Stored, Export("""
            { "Vault": { "Summary": "From a stored item.", "Tags": ["copied"] } }
            """));

        Assert.Equal("From a stored item.", read.Summary);
        Assert.Equal(["copied"], read.Tags);
    }

    [Fact]
    public void TheTemplatesOwnCommentaryIsIgnored()
    {
        // The template carries its instructions in $comment and $fields. Filling it in and
        // leaving those there must not put them in the gallery.
        var read = GalleryStore.ApplyMetadataBeside(Stored, Export("""
            {
              "$comment": ["Copy this next to the export it describes."],
              "Summary": "Real.",
              "$fields": { "Summary": ["One line, shown on the card."] }
            }
            """));

        Assert.Equal("Real.", read.Summary);
        Assert.DoesNotContain("template", read.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheTemplateShippedWithTheProjectReadsAsAllEmpty()
    {
        // Filling in nothing must do nothing, so the template can be copied beside every ship
        // and completed one at a time.
        string template = Path.Combine(AppContext.BaseDirectory, "docs", "item-template.json");
        Assert.True(File.Exists(template), $"template missing: {template}");

        var read = GalleryStore.ApplyMetadataBeside(Stored, Export(File.ReadAllText(template)));

        // Its empty strings and arrays do clear those fields - which is the documented
        // behaviour - but nothing in it is mistaken for a value.
        Assert.Equal("", read.Summary);
        Assert.Equal("", read.Description);
        Assert.Empty(read.Tags);
        Assert.Empty(read.AlternativeNames);
        Assert.Null(read.Author);
        Assert.Null(read.GameVersion);

        // Id and DisplayName are left alone by an empty value rather than blanked, because an
        // item without either is not addressable.
        Assert.Equal("iron-vulture", read.Id);
        Assert.Equal("Iron Vulture", read.DisplayName);
    }

    [Fact]
    public void UnreadableJsonIsReportedAgainstItsFileRatherThanThrowingSomethingOpaque()
    {
        var export = Export("{ this is not json");

        var thrown = Assert.Throws<InvalidDataException>(
            () => GalleryStore.ApplyMetadataBeside(Stored, export));

        Assert.Contains("Iron Vulture.json", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMistypedNameIsSimplyNotFound()
    {
        // The reason reimport calls out a metadata file that sits beside no export. Nothing
        // here throws or warns on its own - the file is looked for under the export's name and
        // is not there, so the export is read with whatever it had. A page of fields filled in
        // under the wrong name would otherwise vanish without a word.
        string export = Path.Combine(_folder, "[EXP-13-R] Iron Vulture.nmsship");
        File.WriteAllText(export, "{}");
        File.WriteAllText(Path.Combine(_folder, "Iron Vulture.json"), """{ "Summary": "Missed." }""");

        var read = GalleryStore.ApplyMetadataBeside(Stored, export);

        Assert.Equal("A hauler.", read.Summary);
    }

    [Fact]
    public void PicturesAndMetadataAreFoundBySeparateRules()
    {
        // A .json beside an export is metadata, never a picture; the picture extensions do not
        // overlap with it. Worth asserting because both look for the same stem.
        string export = Export("""{ "Summary": "x" }""");

        Assert.Empty(GalleryStore.PicturesBeside(export));
    }
}
