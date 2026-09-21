using NmsVault.Core.Derived;
using NmsVault.Json;

namespace NmsVault.Web.Services;

/// <summary>
/// Loads the companion lookup, once, so a creature's affinity and its battle moves can be
/// shown by name rather than by id.
/// </summary>
/// <remarks>
/// Missing data is not an error, the same as with the technology lookup. A gallery published
/// before <c>extract-pets</c> has run shows the raw move ids, which is worse but not broken -
/// so a failed fetch leaves an empty index rather than taking the page down.
/// </remarks>
public sealed class PetData(HttpClient http)
{
    private readonly HttpClient _http = http;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private PetIndex? _index;

    /// <summary>The companion lookup, loaded on first call and cached after.</summary>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The index, empty when the data is not published.</returns>
    public async Task<PetIndex> IndexAsync(CancellationToken cancellationToken = default)
    {
        if (_index is not null) return _index;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_index is not null) return _index;

            try
            {
                byte[] bytes = await _http.GetByteArrayAsync("gallery/pets.json", cancellationToken)
                    .ConfigureAwait(false);

                return _index = PetIndex.FromBytes(bytes);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException)
            {
                return _index = PetIndex.Empty;
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
