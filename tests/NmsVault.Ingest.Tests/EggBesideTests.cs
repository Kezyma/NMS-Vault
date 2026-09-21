using NmsVault.Ingest;

namespace NmsVault.Ingest.Tests;

/// <summary>
/// How an egg is paired with the creature it hatches into.
/// </summary>
/// <remarks>
/// A convention rather than anything in the files: an egg export and a hatched one have the
/// same keys and the same seeds, so only the names say which is which.
/// </remarks>
public class EggBesideTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("nmsvault-eggs").FullName;

    private string Write(string name)
    {
        string path = Path.Combine(_folder, name);
        File.WriteAllText(path, "{}");
        return path;
    }

    [Theory]
    [InlineData("[EXP-23-R] Diplodocus_egg.nmspet", true)]
    [InlineData("[EXP-23-R] Diplodocus_EGG.nmspet", true)]
    [InlineData("[EXP-23-R] Diplodocus.nmspet", false)]
    // A creature whose name merely ends in those letters is not an egg.
    [InlineData("Golden Egg.nmspet", false)]
    public void AnEggIsRecognisedByItsName(string name, bool expected)
        => Assert.Equal(expected, GalleryStore.IsEgg(name));

    [Fact]
    public void AnEggIsFoundBesideTheCreatureItHatchesInto()
    {
        string pet = Write("[EXP-23-R] Diplodocus.nmspet");
        string egg = Write("[EXP-23-R] Diplodocus_egg.nmspet");

        Assert.Equal(egg, GalleryStore.EggBeside(pet));
    }

    [Fact]
    public void ACreatureCapturedOnItsOwnHasNoEgg()
        => Assert.Null(GalleryStore.EggBeside(Write("[EXP-23-R] Diplodocus.nmspet")));

    [Fact]
    public void AnEggHasNoEggOfItsOwn()
    {
        // Otherwise it would go looking for X_egg_egg, and a folder of pairs would build twice.
        Write("[EXP-23-R] Diplodocus.nmspet");

        Assert.Null(GalleryStore.EggBeside(Write("[EXP-23-R] Diplodocus_egg.nmspet")));
    }

    [Fact]
    public void AnEggNamesTheCreatureItIsMissing()
    {
        // What the build reports when an egg was exported without its hatchling, so the
        // message says which file to go and find.
        string egg = Path.Combine(_folder, "[EXP-23-R] Diplodocus_egg.nmspet");

        Assert.Equal(Path.Combine(_folder, "[EXP-23-R] Diplodocus.nmspet"), GalleryStore.Hatched(egg));
    }

    [Fact]
    public void PairingIsByExtensionAsWellAsName()
    {
        // A .nmspet egg does not belong to a .pet export that happens to share a stem.
        Write("[EXP-23-R] Diplodocus.pet");
        string egg = Write("[EXP-23-R] Diplodocus_egg.nmspet");

        Assert.Null(GalleryStore.EggBeside(Path.Combine(_folder, "[EXP-23-R] Diplodocus.pet")));
        Assert.Equal(Path.Combine(_folder, "[EXP-23-R] Diplodocus.nmspet"), GalleryStore.Hatched(egg));
    }

    public void Dispose()
    {
        Directory.Delete(_folder, recursive: true);
        GC.SuppressFinalize(this);
    }
}
