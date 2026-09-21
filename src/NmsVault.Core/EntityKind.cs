namespace NmsVault.Core;

/// <summary>
/// The kinds of item the gallery holds. Each maps to one page and one family of
/// editor export formats.
/// </summary>
public enum EntityKind
{
    /// <summary>A starship, including corvettes. Shipyard.</summary>
    Starship,

    /// <summary>A multitool. Armoury.</summary>
    Multitool,

    /// <summary>A companion creature. Stable.</summary>
    Companion,

    /// <summary>A frigate. Shipyard.</summary>
    Frigate,

    /// <summary>
    /// A freighter - the ship with its tech and cargo, not the base interior.
    /// Deferred: NMSE has no whole-freighter-ship format yet, and NMS Companion's
    /// writer drops freighter cargo. Present so adding it later is additive.
    /// </summary>
    Freighter,
}

/// <summary>
/// Per-kind facts the vault format and the adapters both need.
/// </summary>
public static class EntityKinds
{
    /// <summary>
    /// The key the entity's game object sits under inside a vault document.
    /// <para>
    /// For starships this matches NMSE's own wrapper (<c>Ship</c>). NMSE exports the
    /// other kinds as bare objects with no wrapper at all, so those keys are the
    /// vault's own choice - chosen to match the name the game and the other editors
    /// already use, so the wrapper reads naturally.
    /// </para>
    /// </summary>
    public static string PayloadKey(this EntityKind kind) => kind switch
    {
        EntityKind.Starship => "Ship",
        EntityKind.Multitool => "Multitool",
        EntityKind.Companion => "Pet",
        EntityKind.Frigate => "Frigate",
        EntityKind.Freighter => "Freighter",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>The page this kind appears on.</summary>
    public static string Page(this EntityKind kind) => kind switch
    {
        EntityKind.Starship or EntityKind.Frigate or EntityKind.Freighter => "Shipyard",
        EntityKind.Multitool => "Armoury",
        EntityKind.Companion => "Stable",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>
    /// Whether this kind is offered by the gallery yet. Freighters are designed for but
    /// not shipped - see <see cref="EntityKind.Freighter"/>.
    /// </summary>
    public static bool IsAvailable(this EntityKind kind) => kind != EntityKind.Freighter;
}

/// <summary>
/// Which form of a companion a download carries.
/// </summary>
/// <remarks>
/// A creature can be captured twice, as the egg and as what hatched out of it, and the two are
/// not interchangeable: an egg carries no accessories and whoever is importing wants one or the
/// other depending on whether they mean to hatch it themselves. Names match the words on the
/// download menu so the two cannot drift apart.
/// </remarks>
public enum CompanionForm
{
    /// <summary>The hatched creature. What an item holds unless it says otherwise.</summary>
    Companion,

    /// <summary>The same creature before it hatched.</summary>
    Egg,
}
