using System.Text;
using NmsVault.Json;

namespace NmsVault.Json.Tests;

/// <summary>
/// Drives the ported JSON engine over real NMSE exports rather than synthetic input.
/// A byte-identical round trip over production data is the strongest evidence the port
/// preserved NMSE's serialisation quirks - int-vs-float, RawDouble text, key order and
/// indentation all have to match exactly for these to pass.
/// </summary>
public class FixtureRoundTripTests
{
    private static string FixtureRoot =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "nmse");

    public static TheoryData<string> AllFixtures()
    {
        var data = new TheoryData<string>();
        foreach (var path in Directory.EnumerateFiles(FixtureRoot, "*", SearchOption.AllDirectories))
            data.Add(Path.GetRelativePath(FixtureRoot, path));
        return data;
    }

    private static byte[] ReadFixture(string relativePath)
        => File.ReadAllBytes(Path.Combine(FixtureRoot, relativePath));

    [Fact]
    public void CorpusIsPresent()
    {
        // Guards against the fixtures silently not being copied to the output directory,
        // which would make every Theory below vacuously pass with zero cases.
        Assert.True(Directory.Exists(FixtureRoot), $"Fixture root missing: {FixtureRoot}");
        Assert.Equal(17, Directory.GetFiles(Path.Combine(FixtureRoot, "starships")).Length);
        Assert.Equal(12, Directory.GetFiles(Path.Combine(FixtureRoot, "multitools")).Length);
        Assert.Single(Directory.GetFiles(Path.Combine(FixtureRoot, "companions")));
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void EveryFixtureRoundTripsByteIdentically_ExceptLineEndings(string relativePath)
    {
        // NMSE wrote these on Windows through Environment.NewLine, so they are CRLF.
        // This port hardcodes LF so the same item serialises identically everywhere,
        // which means line endings are a known, deliberate divergence. Normalising the
        // ORIGINAL (never the output) keeps this test honest: everything else - key
        // order, indentation, int-vs-float, RawDouble text - still has to match byte
        // for byte, and any real fidelity loss still fails here.
        byte[] original = NormaliseLineEndings(ReadFixture(relativePath));

        var parsed = JsonObject.FromBytes(original);
        byte[] rewritten = Encoding.Latin1.GetBytes(parsed.ToExportString());

        Assert.Equal(original, rewritten);
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void LineEndingsAreTheOnlyDivergenceFromNmse(string relativePath)
    {
        // Pins the claim the test above rests on. If the port ever normalises anything
        // else, the two counts stop matching and this fails with a concrete number.
        byte[] original = ReadFixture(relativePath);
        byte[] rewritten = Encoding.Latin1.GetBytes(
            JsonObject.FromBytes(original).ToExportString());

        Assert.Equal(CountCrLf(original), CountLf(rewritten));
        Assert.Equal(0, CountCrLf(rewritten));
        // Same content length once the extra CR bytes are accounted for.
        Assert.Equal(original.Length - CountCrLf(original), rewritten.Length);
    }

    private static byte[] NormaliseLineEndings(byte[] bytes)
    {
        var result = new List<byte>(bytes.Length);
        for (int i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] == (byte)'\r' && i + 1 < bytes.Length && bytes[i + 1] == (byte)'\n')
                continue;
            result.Add(bytes[i]);
        }
        return [.. result];
    }

    private static int CountCrLf(byte[] bytes)
    {
        int count = 0;
        for (int i = 0; i + 1 < bytes.Length; i++)
            if (bytes[i] == (byte)'\r' && bytes[i + 1] == (byte)'\n') count++;
        return count;
    }

    private static int CountLf(byte[] bytes)
    {
        int count = 0;
        foreach (byte b in bytes)
            if (b == (byte)'\n') count++;
        return count;
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void EveryFixtureIsStableAcrossTwoRoundTrips(string relativePath)
    {
        // Catches anything that normalises on the first pass and then holds steady -
        // that would slip past the byte-identity test if the corpus were ever regenerated.
        var once = JsonObject.FromBytes(ReadFixture(relativePath)).ToExportString();
        var twice = JsonObject.FromBytes(Encoding.Latin1.GetBytes(once)).ToExportString();

        Assert.Equal(once, twice);
    }

    // --- The sidecar this whole project exists to preserve -------------

    [Theory]
    [InlineData("Horizon Omega", true, false)]
    [InlineData("Alpha Vector", true, false)]
    public void MatchedPairs_DifferOnlyInTheLegacyColourFlag(
        string shipName, bool originalExpected, bool newExpected)
    {
        // These pairs are the same model and class, captured with the flag set both ways.
        // If a conversion ever loses UsesLegacyColours, these two stop being distinguishable.
        var original = LoadShip($"[PRE-PC] {shipName} (Original).nmsship",
                                $"[PRE-PS] {shipName} (Original).nmsship");
        var modern = LoadShip($"[PRE-PC] {shipName} (New).nmsship",
                              $"[PRE-PS] {shipName} (New).nmsship");

        Assert.Equal(originalExpected, original.GetBool("UsesLegacyColours"));
        Assert.Equal(newExpected, modern.GetBool("UsesLegacyColours"));

        // Same underlying model - so the flag really is the distinguishing field.
        Assert.Equal(ResourceFilename(original), ResourceFilename(modern));
    }

    [Fact]
    public void EveryShipFixtureCarriesTheWrapperKeys()
    {
        foreach (var path in Directory.EnumerateFiles(Path.Combine(FixtureRoot, "starships")))
        {
            var obj = JsonObject.FromBytes(File.ReadAllBytes(path));
            string name = Path.GetFileName(path);

            Assert.True(obj.Contains("Ship"), $"{name} has no Ship");
            Assert.True(obj.Contains("UsesLegacyColours"), $"{name} has no UsesLegacyColours");
            Assert.True(obj.Get("UsesLegacyColours") is bool, $"{name} UsesLegacyColours is not a bool");
        }
    }

    [Fact]
    public void EveryMultitoolFixtureCarriesItsFlagInline()
    {
        // Multitools store UseLegacyColours (no trailing s) on the object itself, which is
        // why multitool export never lost it the way ship export did.
        foreach (var path in Directory.EnumerateFiles(Path.Combine(FixtureRoot, "multitools")))
        {
            var obj = JsonObject.FromBytes(File.ReadAllBytes(path));
            string name = Path.GetFileName(path);

            Assert.True(obj.Contains("UseLegacyColours"), $"{name} has no UseLegacyColours");
            Assert.False(obj.Contains("UsesLegacyColours"), $"{name} has the ship spelling");
            Assert.True(obj.Contains("Store"), $"{name} has no Store");
        }
    }

    // --- Fidelity spot-checks on real data ----------------------------

    [Fact]
    public void RealFixtures_PreserveWholeNumberFloats()
    {
        // NMS distinguishes 1 from 1.0. If the parser collapsed them, the round-trip test
        // would fail - but this asserts the distinction exists in the corpus at all, so
        // that test is actually exercising the behaviour rather than passing vacuously.
        string text = Encoding.Latin1.GetString(
            ReadFixture(Path.Combine("starships", "[START] Rasamama S36.nmsship")));

        Assert.Matches(@":\s*-?\d+\.0\b", text);
    }

    private static JsonObject LoadShip(params string[] candidateNames)
    {
        foreach (var name in candidateNames)
        {
            string path = Path.Combine(FixtureRoot, "starships", name);
            if (File.Exists(path))
                return JsonObject.FromBytes(File.ReadAllBytes(path));
        }
        throw new FileNotFoundException($"None of: {string.Join(", ", candidateNames)}");
    }

    private static string ResourceFilename(JsonObject shipWrapper)
        => shipWrapper.GetObject("Ship")?.GetObject("Resource")?.GetString("Filename") ?? "";
}
