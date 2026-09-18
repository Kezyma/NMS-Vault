using System.Text;
using NmsVault.Json;

namespace NmsVault.Core.Adapters;

/// <summary>
/// Produces NMSE export files.
/// <para>
/// This is the cheapest adapter, because the vault format is NMSE's format plus a
/// <c>Vault</c> block: for starships the conversion is "drop the metadata". The other
/// kinds need unwrapping, because NMSE exports them bare rather than wrapped.
/// </para>
/// </summary>
public sealed class NmseExportAdapter : IExportAdapter
{
    /// <inheritdoc />
    public EditorId Editor => EditorId.Nmse;

    /// <inheritdoc />
    public string DisplayName => "NMSE (No Man's Save Editor)";

    /// <inheritdoc />
    public string HomepageUrl => "https://github.com/vectorcmdr/NMSE";

    /// <summary>
    /// True. NMSE supports the current game version, and this adapter's output is tested
    /// byte-for-byte against real NMSE exports.
    /// </summary>
    public bool IsVerified => true;

    /// <inheritdoc />
    public OneOf<string, Unsupported> Extension(EntityKind kind) => kind switch
    {
        EntityKind.Starship => ".nmsship",
        EntityKind.Multitool => ".nmstool",
        EntityKind.Companion => ".nmspet",
        EntityKind.Frigate => ".nmsfrig",
        EntityKind.Freighter => new Unsupported(
            "NMSE has no whole-freighter-ship format - .nmsfreight is the base interior, " +
            "and .nmsfc/.nmsft are bare cargo and tech inventories."),
        _ => new Unsupported($"Unknown entity kind {kind}."),
    };

    /// <inheritdoc />
    public ExportResult Export(VaultItem item)
    {
        var extension = Extension(item.Kind);
        if (!extension.HasValue)
            throw new NotSupportedException(extension.Alternative.Reason);

        JsonObject document = item.Kind switch
        {
            // The starship wrapper is the vault document minus its metadata: NMSE writes
            // { Ship, Base?, CharacterCustomisationData?, UsesLegacyColours } and so do we.
            EntityKind.Starship => item.WithoutMetadata(),

            // NMSE exports a bare Pet object with the accessory slots spliced in flat
            // under PetAccessoryCustomisation - note the save nests them as { Data: [...] }
            // and NomNom nests them differently again.
            EntityKind.Companion => BuildCompanion(item),

            // Multitools and frigates export as the bare game object, no wrapper at all.
            _ => item.Payload.DeepClone(),
        };

        // NMSE writes formatted JSON with human-readable keys. The export string holds
        // non-ASCII as UTF-8 bytes in Latin-1 chars, so Latin1 is the encoding that puts
        // well-formed UTF-8 on disk. See the note on NmseByteCompatibility below.
        byte[] content = Encoding.Latin1.GetBytes(document.ToExportString());

        string fileName = SanitiseFileName(item.Meta.DisplayName) + extension.Value;
        return new ExportResult(fileName, content);
    }

    private static JsonObject BuildCompanion(VaultItem item)
    {
        var pet = item.Payload.DeepClone();
        if (item.AccessorySlots is { } slots)
            pet.Set("PetAccessoryCustomisation", slots.DeepClone());
        return pet;
    }

    /// <summary>
    /// Strips characters that are invalid in a filename on any common platform. Not using
    /// Path.GetInvalidFileNameChars: it returns the host's set, which under WebAssembly is
    /// the small Unix set, so a name that downloads fine from the browser could be illegal
    /// on the Windows machine receiving it.
    /// </summary>
    internal static string SanitiseFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "item";

        const string invalid = "<>:\"/\\|?*";
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
            sb.Append(invalid.Contains(c) || char.IsControl(c) ? '_' : c);

        // Trailing dots and spaces are legal to create but awkward on Windows.
        return sb.ToString().TrimEnd('.', ' ') is { Length: > 0 } trimmed ? trimmed : "item";
    }
}
