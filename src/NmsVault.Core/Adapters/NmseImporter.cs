using NmsVault.Json;

namespace NmsVault.Core.Adapters;

/// <summary>
/// Reads NMSE export files into vault items - the inverse of <see cref="NmseExportAdapter"/>.
/// <para>
/// This is where the vault's wrapping actually earns its keep. NMSE only wraps starships;
/// it exports multitools, companions and frigates as bare game objects, so a bare file
/// carries no marker saying what it is. The kind has to be recovered from shape, with the
/// file extension as a hint.
/// </para>
/// </summary>
public static class NmseImporter
{
    /// <summary>
    /// Reads an NMSE export.
    /// </summary>
    /// <param name="bytes">The file bytes.</param>
    /// <param name="meta">Gallery metadata to attach.</param>
    /// <param name="extensionHint">
    /// The source file's extension (e.g. <c>.nmstool</c>), used to disambiguate bare
    /// objects. Optional, but without it a bare export has only its shape to go on.
    /// </param>
    /// <param name="kind">
    /// What the document holds, where the caller already knows. The detector works this out
    /// from the whole file and is better informed than an extension, so when it has an
    /// answer that answer is used and nothing is inferred.
    /// </param>
    public static VaultItem Read(
        ReadOnlySpan<byte> bytes, VaultMetadata meta, string? extensionHint = null, EntityKind? kind = null)
        => Read(JsonObject.FromBytes(bytes), meta, extensionHint, kind);

    /// <inheritdoc cref="Read(ReadOnlySpan{byte}, VaultMetadata, string?, EntityKind?)"/>
    public static VaultItem Read(
        JsonObject document, VaultMetadata meta, string? extensionHint = null, EntityKind? kind = null)
    {
        // A starship export is the only wrapped NMSE format, and it is self-identifying.
        if (document.Contains("Ship"))
            return ReadStarship(document, meta);

        EntityKind resolved = kind ?? InferBareKind(document, extensionHint);

        return resolved switch
        {
            EntityKind.Companion => ReadCompanion(document, meta),
            _ => VaultItem.Create(resolved, StripKnownSidecars(document, resolved), meta),
        };
    }

    private static VaultItem ReadStarship(JsonObject document, VaultMetadata meta)
    {
        var ship = document.GetObject("Ship")
            ?? throw new InvalidOperationException("Ship key present but not an object.");

        return VaultItem.Create(
            EntityKind.Starship,
            ship.DeepClone(),
            meta,
            characterCustomisationData: document.GetObject("CharacterCustomisationData")?.DeepClone(),
            // Absent means "this file does not say", not false. Files exported before the
            // flag was added omit it, and an importer must leave the destination alone
            // rather than assuming a default.
            usesLegacyColours: document.Get("UsesLegacyColours") is bool b ? b : null,
            shipBase: document.GetObject("Base")?.DeepClone());
    }

    private static VaultItem ReadCompanion(JsonObject document, VaultMetadata meta)
    {
        // NMSE splices the accessory slots into the pet object flat. Lift them back out so
        // the payload is the pet alone and the slots are a first-class sidecar.
        var pet = document.DeepClone();
        var slots = pet.GetArray("PetAccessoryCustomisation");
        pet.Remove("PetAccessoryCustomisation");

        return VaultItem.Create(EntityKind.Companion, pet, meta, accessorySlots: slots);
    }

    private static JsonObject StripKnownSidecars(JsonObject document, EntityKind kind)
    {
        var payload = document.DeepClone();

        // Defensive: if a sidecar key ever leaks into a bare payload, drop it rather than
        // carrying it into the vault - and from there back into somebody's real save,
        // since ToKey passes unknown names through unchanged.
        if (kind != EntityKind.Starship)
        {
            payload.Remove("UsesLegacyColours");
            payload.Remove("CharacterCustomisationData");
        }
        payload.Remove(VaultItem.VaultKey);
        return payload;
    }

    /// <summary>
    /// Works out what a bare export is, from an extension or failing that from its shape.
    /// </summary>
    /// <remarks>
    /// Both editors that write readable keys come through here, so both sets of extensions
    /// are listed. goatfungus writes a bare object for every kind including starships, which
    /// is why a starship has a shape rule as well - NMSE's own starship export is wrapped
    /// and never reaches this method.
    /// </remarks>
    internal static EntityKind InferBareKind(JsonObject document, string? extensionHint)
    {
        switch (extensionHint?.ToLowerInvariant())
        {
            case ".nmstool" or ".wp0": return EntityKind.Multitool;
            case ".nmspet" or ".pet": return EntityKind.Companion;
            case ".nmsfrig": return EntityKind.Frigate;
            case ".sh0": return EntityKind.Starship;
        }

        // Shape fallback. These discriminators come from the real fixtures:
        // a multitool has Store plus the mode fields; a pet has creature data.
        if (document.Contains("Store") && document.Contains("SecondaryMode"))
            return EntityKind.Multitool;

        if (document.Contains("CreatureID") || document.Contains("PetAccessoryCustomisation"))
            return EntityKind.Companion;

        if (document.Contains("TraitIDs") || document.Contains("FrigateClass"))
            return EntityKind.Frigate;

        // A ship is the only thing carrying a technology-only inventory beside a resource.
        if (document.Contains("Resource") && document.Contains("Inventory_TechOnly"))
            return EntityKind.Starship;

        throw new InvalidOperationException(
            "Could not determine what this export contains. Pass the original file " +
            "extension as a hint, or check the file is an entity export at all.");
    }
}
