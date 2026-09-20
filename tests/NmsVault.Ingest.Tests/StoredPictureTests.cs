using NmsVault.Ingest;
using SkiaSharp;

namespace NmsVault.Ingest.Tests;

/// <summary>
/// Tests for the URLs a stored picture is published under.
/// </summary>
/// <remarks>
/// A stored picture is named after the item it belongs to, so replacing one reuses the name
/// it already had. Without a fingerprint the replacement is served from the same URL as the
/// picture it replaced, and every browser and CDN that already holds the old one goes on
/// showing it - a failure that looks exactly like the import not having run.
/// </remarks>
public class StoredPictureTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("nmsvault-pictures").FullName;

    /// <summary>Removes the temporary folder.</summary>
    public void Dispose()
    {
        Directory.Delete(_folder, recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Writes a plain single-colour PNG to import.</summary>
    private string Picture(string name, SKColor colour, int width = 40, int height = 30)
    {
        string path = Path.Combine(_folder, name);

        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap)) canvas.Clear(colour);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.Create(path);
        data.SaveTo(file);

        return path;
    }

    private GalleryStore Store() => new(Path.Combine(_folder, "gallery"));

    [Fact]
    public void PictureUrlCarriesAFingerprint()
    {
        string url = Store().AddImage(Picture("a.png", SKColors.Red), "iron-vulture", 0);

        Assert.StartsWith("img/iron-vulture.webp?v=", url, StringComparison.Ordinal);
        Assert.Equal(8, url["img/iron-vulture.webp?v=".Length..].Length);
    }

    [Fact]
    public void ReplacingAPictureChangesItsUrl()
    {
        var store = Store();

        string before = store.AddImage(Picture("red.png", SKColors.Red), "iron-vulture", 0);
        string after = store.AddImage(Picture("blue.png", SKColors.Blue), "iron-vulture", 0);

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void ReimportingTheSamePictureKeepsItsUrl()
    {
        var store = Store();
        string source = Picture("same.png", SKColors.Green);

        // The whole point of the fingerprint is that it moves only when the picture does.
        // A URL that changed on every run would defeat caching rather than correct it.
        Assert.Equal(
            store.AddImage(source, "iron-vulture", 0),
            store.AddImage(source, "iron-vulture", 0));
    }

    [Fact]
    public void LaterPicturesAreNumberedFromTwo()
    {
        var store = Store();

        Assert.StartsWith("img/iron-vulture.webp?", store.AddImage(Picture("a.png", SKColors.Red), "iron-vulture", 0), StringComparison.Ordinal);
        Assert.StartsWith("img/iron-vulture-2.webp?", store.AddImage(Picture("b.png", SKColors.Blue), "iron-vulture", 1), StringComparison.Ordinal);
        Assert.StartsWith("img/iron-vulture-3.webp?", store.AddImage(Picture("c.png", SKColors.Green), "iron-vulture", 2), StringComparison.Ordinal);
    }

    [Fact]
    public void TwoItemsDoNotShareAPicture()
    {
        var store = Store();
        string source = Picture("shared.png", SKColors.Red);

        // Same bytes, so the same fingerprint - but the file is named for the item, and one
        // item's picture must never resolve to another's.
        string vulture = store.AddImage(source, "iron-vulture", 0);
        string wraith = store.AddImage(source, "the-wraith", 0);

        Assert.NotEqual(vulture, wraith);
        Assert.True(File.Exists(Path.Combine(_folder, "gallery", "img", "iron-vulture.webp")));
        Assert.True(File.Exists(Path.Combine(_folder, "gallery", "img", "the-wraith.webp")));
    }

    [Fact]
    public void PicturesBesideAnExportStopAtAGap()
    {
        string export = Path.Combine(_folder, "[EXP-13-R] Iron Vulture.nmsship");
        File.WriteAllText(export, "{}");

        Picture("[EXP-13-R] Iron Vulture.png", SKColors.Red);
        Picture("[EXP-13-R] Iron Vulture-2.png", SKColors.Blue);
        Picture("[EXP-13-R] Iron Vulture-4.png", SKColors.Green);   // no -3, so unreachable

        Assert.Equal(2, GalleryStore.PicturesBeside(export).Count);
    }
}
