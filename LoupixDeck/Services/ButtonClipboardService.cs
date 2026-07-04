using LoupixDeck.Models;

namespace LoupixDeck.Services;

/// <summary>
/// The kind of element a clipboard snapshot came from. Paste is only allowed onto the same
/// kind (issue #166 compatibility rules) — the payloads are structurally different and cross
/// pasting would misplace or drop content.
/// </summary>
public enum ButtonKind
{
    Touch,
    Simple,
    Rotary,
    SideDisplay
}

/// <summary>
/// In-app clipboard for copy/paste of buttons and side displays (issue #166). Holds a JSON
/// <b>snapshot</b> of the copied element (independent of the source object, so it survives page /
/// device switches and later source edits), plus the source kind for compatibility checks.
/// Registered as a root singleton and forwarded into every device provider, so copy/paste works
/// across pages and devices. The serialize/apply logic lives in <see cref="ButtonSnapshot"/>.
/// </summary>
public interface IButtonClipboardService
{
    bool HasData { get; }
    ButtonKind? CurrentKind { get; }

    /// <summary>Raised when the clipboard content changes (set/clear).</summary>
    event Action Changed;

    /// <summary>Take a deep snapshot of <paramref name="source"/> as the given kind.</summary>
    void Copy(LoupedeckButton source, ButtonKind kind);

    /// <summary>True when a snapshot exists and it can be pasted onto a target of this kind.</summary>
    bool CanPasteInto(ButtonKind targetKind);

    /// <summary>Apply the current snapshot onto <paramref name="target"/> in place. Caller must
    /// have checked <see cref="CanPasteInto"/>.</summary>
    void PasteInto(LoupedeckButton target);

    void Clear();

    /// <summary>True when a button/side-display currently holds no user configuration.</summary>
    bool IsEmpty(LoupedeckButton button);
}

public sealed class ButtonClipboardService : IButtonClipboardService
{
    private ButtonKind _kind;
    private string _json;

    public bool HasData => _json != null;
    public ButtonKind? CurrentKind => _json != null ? _kind : null;

    public event Action Changed;

    public void Copy(LoupedeckButton source, ButtonKind kind)
    {
        if (source == null) return;
        _kind = kind;
        _json = ButtonSnapshot.Capture(source);
        Changed?.Invoke();
    }

    public bool CanPasteInto(ButtonKind targetKind) => _json != null && _kind == targetKind;

    public void PasteInto(LoupedeckButton target)
    {
        if (_json == null) return;
        ButtonSnapshot.Apply(_json, target);
    }

    public void Clear()
    {
        if (_json == null) return;
        _json = null;
        Changed?.Invoke();
    }

    public bool IsEmpty(LoupedeckButton button) => ButtonSnapshot.IsEmpty(button);
}
