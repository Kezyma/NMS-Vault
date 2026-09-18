namespace NmsVault.Core.Adapters;

/// <summary>The save editors the gallery can produce files for.</summary>
public enum EditorId
{
    /// <summary>NMSE (No Man's Save Editor).</summary>
    Nmse,

    /// <summary>goatfungus NMSSaveEditor.</summary>
    Goatfungus,

    /// <summary>NMS Companion, by Dr. Kaii. Called "Kaii" in libNOM's source.</summary>
    Companion,

    /// <summary>NomNom, by zencq. Called "Standard" in libNOM's source.</summary>
    NomNom,
}

/// <summary>A file produced for one editor.</summary>
/// <param name="FileName">Suggested download name, including extension.</param>
/// <param name="Content">The file bytes.</param>
public readonly record struct ExportResult(string FileName, byte[] Content);

/// <summary>
/// Why an editor cannot produce a file for an item, so the UI can say so rather than
/// silently omitting a button or producing something broken.
/// </summary>
/// <param name="Reason">One sentence, shown to the user.</param>
public readonly record struct Unsupported(string Reason);

/// <summary>
/// Converts a <see cref="VaultItem"/> into one editor's export format.
/// </summary>
public interface IExportAdapter
{
    /// <summary>Which editor this produces files for.</summary>
    EditorId Editor { get; }

    /// <summary>The editor's name, as its authors write it.</summary>
    string DisplayName { get; }

    /// <summary>Where to send someone who wants the editor.</summary>
    string HomepageUrl { get; }

    /// <summary>
    /// Whether output from this adapter has been confirmed against the real editor.
    /// <para>
    /// False for goatfungus, NMS Companion and NomNom: as of 7.03 Cosmos none of them can
    /// load a current save, so their formats are implemented from documented shapes but
    /// have never been round-tripped through the editor itself. The UI should say so
    /// rather than implying a guarantee it cannot make.
    /// </para>
    /// </summary>
    bool IsVerified { get; }

    /// <summary>
    /// The file extension for this item, or an <see cref="Unsupported"/> explanation when
    /// this editor has no format for that kind - goatfungus has no frigate format, for one.
    /// </summary>
    OneOf<string, Unsupported> Extension(EntityKind kind);

    /// <summary>Produces the export file.</summary>
    /// <exception cref="NotSupportedException">
    /// If <see cref="Extension"/> reported the kind unsupported.
    /// </exception>
    ExportResult Export(VaultItem item);
}

/// <summary>
/// A minimal two-case union, so <see cref="IExportAdapter.Extension"/> can return either a
/// value or a reason without an exception or a nullable-plus-out-string dance.
/// </summary>
public readonly struct OneOf<TValue, TAlternative>
{
    private readonly TValue? _value;
    private readonly TAlternative? _alternative;

    private OneOf(TValue? value, TAlternative? alternative, bool hasValue)
    {
        _value = value;
        _alternative = alternative;
        HasValue = hasValue;
    }

    /// <summary>True when this holds a <typeparamref name="TValue"/>.</summary>
    public bool HasValue { get; }

    /// <summary>The value. Throws when this holds the alternative.</summary>
    public TValue Value => HasValue
        ? _value!
        : throw new InvalidOperationException("This OneOf holds the alternative.");

    /// <summary>The alternative. Throws when this holds a value.</summary>
    public TAlternative Alternative => !HasValue
        ? _alternative!
        : throw new InvalidOperationException("This OneOf holds a value.");

    /// <summary>Wraps a value.</summary>
    public static implicit operator OneOf<TValue, TAlternative>(TValue value) => new(value, default, true);

    /// <summary>Wraps the alternative.</summary>
    public static implicit operator OneOf<TValue, TAlternative>(TAlternative alternative) => new(default, alternative, false);
}
