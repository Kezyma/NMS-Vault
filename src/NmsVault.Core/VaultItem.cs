using System.Text;
using NmsVault.Json;

namespace NmsVault.Core;

/// <summary>
/// One gallery item: NMSE's export format plus a <c>Vault</c> metadata block.
/// <para>
/// A vault document <em>is</em> an NMSE export with one extra key, which is what makes
/// converting to NMSE a projection rather than a translation - drop <c>Vault</c> and the
/// rest is already the right shape. This class is a typed view over that document, not a
/// separate model, so the file on disk stays the source of truth.
/// </para>
/// <para>
/// NMSE only wraps starships; it exports multitools, companions and frigates as bare
/// game objects. The vault generalises the starship wrapper to every kind, because the
/// bare kinds otherwise have nowhere to put metadata or sidecars.
/// </para>
/// </summary>
public sealed class VaultItem
{
    /// <summary>The key the metadata block lives under.</summary>
    public const string VaultKey = "Vault";

    /// <summary>The key a companion's unhatched form lives under.</summary>
    public const string EggKey = "Egg";

    private readonly JsonObject _root;

    private VaultItem(JsonObject root, EntityKind kind, VaultMetadata meta)
    {
        _root = root;
        Kind = kind;
        Meta = meta;
    }

    /// <summary>What kind of item this is.</summary>
    public EntityKind Kind { get; }

    /// <summary>The gallery's own fields.</summary>
    public VaultMetadata Meta { get; }

    /// <summary>
    /// The entity's game object - a <c>ShipOwnership</c> entry, a multitool, a pet, a
    /// frigate. Deobfuscated keys.
    /// </summary>
    public JsonObject Payload => _root.GetObject(Kind.PayloadKey())
        ?? throw new InvalidOperationException($"Vault document has no '{Kind.PayloadKey()}'.");

    // --- Sidecars: data the game stores OUTSIDE the entity object -----

    /// <summary>
    /// The ship's <c>CharacterCustomisationData</c> entry, or null. Carries the colour and
    /// part selections; Kaii and NomNom both derive their <c>Colours</c> array from it.
    /// </summary>
    public JsonObject? CharacterCustomisationData => _root.GetObject("CharacterCustomisationData");

    /// <summary>
    /// Whether the ship uses legacy (pre-Orbital) colour rendering, or <c>null</c> when the
    /// source document did not say.
    /// <para>
    /// Null is meaningful and is not the same as false. The game stores this in
    /// <c>PlayerStateData.ShipUsesLegacyColours</c>, an array parallel to
    /// <c>ShipOwnership</c> - outside the ship object entirely - which is why exports
    /// dropped it for so long. An importer that sees null must leave the destination
    /// slot's existing flag alone rather than assuming a default.
    /// </para>
    /// </summary>
    public bool? UsesLegacyColours => _root.Get("UsesLegacyColours") is bool b ? b : null;

    /// <summary>
    /// A corvette's <c>PersistentPlayerBases</c> entry, or null for ordinary ships.
    /// Corvettes are built out of base parts, so the ship object alone is not the whole ship.
    /// </summary>
    public JsonObject? ShipBase => _root.GetObject("Base");

    /// <summary>
    /// A companion's accessory customisation as the innermost flat slot array, or null.
    /// <para>
    /// Canonical form is the flat array because the three formats nest it three different
    /// ways: the save stores <c>{ Data: [...] }</c>, NMSE's <c>.nmspet</c> stores the bare
    /// array under <c>PetAccessoryCustomisation</c>, and NomNom stores the wrapper object
    /// under <c>Data.AccessoryCustomisation</c>. Storing the innermost value lets each
    /// adapter re-wrap rather than unwrap.
    /// </para>
    /// </summary>
    public JsonArray? AccessorySlots => _root.GetArray("PetAccessoryCustomisation");

    /// <summary>
    /// The same creature as it was before it hatched, or null where only one form was captured.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A companion can be exported twice: as the egg and as what hatched out of it. The two are
    /// the same animal - creature, species and genus seeds, traits, moves and origin all match -
    /// so they are one item in the gallery with two payloads rather than two entries for one
    /// creature.
    /// </para>
    /// <para>
    /// They are not interchangeable downloads, though, which is why both are kept. An egg
    /// carries no accessories, and on the pair checked it held a real <c>BoneScaleSeed</c> where
    /// the hatched form held a placeholder. Whoever is importing wants one or the other
    /// depending on whether they intend to hatch it themselves.
    /// </para>
    /// </remarks>
    public JsonObject? EggPayload => _root.GetObject(EggKey);

    /// <summary>Whether this item also holds the creature's unhatched form.</summary>
    public bool HasEgg => EggPayload is not null;

    /// <summary>
    /// This item as its egg, ready to hand to an export adapter.
    /// </summary>
    /// <remarks>
    /// The egg becomes the payload and the accessories go, because an egg wears nothing - which
    /// is not a detail the adapters need to know about: each one reads <see cref="Payload"/> and
    /// <see cref="AccessorySlots"/> and produces the right file without a variant of its own.
    /// The name is suffixed so downloading both does not put two files of the same name in
    /// someone's downloads folder.
    /// </remarks>
    /// <returns>An item whose payload is the egg.</returns>
    /// <exception cref="InvalidOperationException">If this item has no egg.</exception>
    public VaultItem AsEgg()
    {
        if (EggPayload is not { } egg)
            throw new InvalidOperationException($"'{Meta.Id}' has no egg to export.");

        return Create(
            Kind,
            egg.DeepClone(),
            Meta with { DisplayName = $"{Meta.DisplayName} (Egg)" });
    }

    // --- Reading and writing ------------------------------------------

    /// <summary>
    /// Parses a vault document. The <c>Vault</c> block supplies the kind; if it is absent
    /// the kind is inferred from which payload key is present, so a plain NMSE export can
    /// be read as a vault item with empty metadata.
    /// </summary>
    /// <param name="bytes">The document bytes.</param>
    /// <param name="id">Id to use when the document carries no metadata.</param>
    public static VaultItem FromBytes(ReadOnlySpan<byte> bytes, string id = "")
        => FromJson(JsonObject.FromBytes(bytes), id);

    /// <inheritdoc cref="FromBytes(ReadOnlySpan{byte}, string)"/>
    public static VaultItem FromJson(JsonObject root, string id = "")
    {
        var vaultBlock = root.GetObject(VaultKey);
        var meta = vaultBlock is not null
            ? VaultMetadata.FromJson(vaultBlock)
            : new VaultMetadata { Id = id, DisplayName = "" };

        EntityKind kind = vaultBlock is not null
            && Enum.TryParse<EntityKind>(vaultBlock.GetString("Kind"), out var parsed)
                ? parsed
                : InferKind(root);

        return new VaultItem(root, kind, meta);
    }

    /// <summary>
    /// Builds a vault item from an entity payload and its sidecars.
    /// </summary>
    public static VaultItem Create(
        EntityKind kind,
        JsonObject payload,
        VaultMetadata meta,
        JsonObject? characterCustomisationData = null,
        bool? usesLegacyColours = null,
        JsonObject? shipBase = null,
        JsonArray? accessorySlots = null)
    {
        var root = new JsonObject();
        root.Set(kind.PayloadKey(), payload);

        // Key order mirrors NMSE's own wrapper so a vault document and an NMSE export
        // of the same item differ only by the trailing Vault block.
        if (shipBase is not null) root.Set("Base", shipBase);
        if (characterCustomisationData is not null)
            root.Set("CharacterCustomisationData", characterCustomisationData);
        if (accessorySlots is not null) root.Set("PetAccessoryCustomisation", accessorySlots);
        if (usesLegacyColours is not null) root.Set("UsesLegacyColours", usesLegacyColours.Value);

        // Kind is written into the Vault block so the document is self-describing. Without
        // it, reading back relies on inferring the kind from which payload key is present,
        // which works but silently becomes wrong the moment two kinds share a key name.
        var vaultBlock = meta.ToJson();
        vaultBlock.Set("Kind", kind.ToString());
        root.Set(VaultKey, vaultBlock);

        return new VaultItem(root, kind, meta);
    }

    /// <summary>
    /// The same item with a companion's unhatched form attached.
    /// </summary>
    /// <remarks>
    /// Written after the <c>Vault</c> block rather than before it. Both are the gallery's
    /// additions to what the game wrote, so they belong together on the end - and an importer
    /// reading one of these documents takes the payload by name and never sees either.
    /// </remarks>
    /// <param name="eggPayload">The egg's creature object.</param>
    /// <returns>A new item carrying the egg.</returns>
    public VaultItem WithEgg(JsonObject eggPayload)
    {
        var root = _root.DeepClone();
        root.Set(EggKey, eggPayload.DeepClone());
        return new VaultItem(root, Kind, Meta);
    }

    /// <summary>The whole document, including the <c>Vault</c> block.</summary>
    public JsonObject ToJson() => _root;

    /// <summary>The whole document as bytes, ready to store in the gallery.</summary>
    public byte[] ToBytes() => Encoding.Latin1.GetBytes(_root.ToExportString());

    /// <summary>
    /// The document with the <c>Vault</c> block removed - i.e. exactly what NMSE would
    /// have exported for a starship. The other kinds need further unwrapping, which the
    /// NMSE adapter handles.
    /// </summary>
    public JsonObject WithoutMetadata()
    {
        var copy = _root.DeepClone();
        copy.Remove(VaultKey);

        // The egg goes with it. It is the gallery's own addition too, and an NMSE export that
        // carried a second creature stapled to it would not be an NMSE export.
        copy.Remove(EggKey);
        return copy;
    }

    private static EntityKind InferKind(JsonObject root)
    {
        foreach (EntityKind kind in Enum.GetValues<EntityKind>())
            if (root.Contains(kind.PayloadKey()))
                return kind;

        throw new InvalidOperationException(
            "Document has no recognised payload key (Ship, Multitool, Pet, Frigate, Freighter).");
    }
}
