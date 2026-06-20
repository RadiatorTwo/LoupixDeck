namespace LoupixDeck.PluginSdk;

/// <summary>
/// One slot inside a folder view. Tapping the slot either runs
/// <see cref="OnPress"/> or opens the nested <see cref="OpensFolder"/>.
/// </summary>
public sealed class FolderEntry
{
    /// <summary>Target grid slot (see <see cref="FolderLayout"/>).</summary>
    public int SlotIndex { get; init; }

    public string Text { get; init; } = string.Empty;

    /// <summary>Optional PNG-encoded icon bytes; null when the slot is text-only.</summary>
    public byte[]? Image { get; init; }

    public PluginColor BackColor { get; init; } = PluginColor.Black;
    public PluginColor TextColor { get; init; } = PluginColor.White;
    public int TextSize { get; init; } = 16;
    public bool Bold { get; init; }

    /// <summary>Action run when the slot is tapped (a leaf entry).</summary>
    public Func<Task>? OnPress { get; init; }

    /// <summary>Nested folder opened when the slot is tapped (a folder entry).</summary>
    public IFolderProvider? OpensFolder { get; init; }
}

/// <summary>Per-rotary-encoder behavior override while a folder is open.</summary>
public sealed class RotaryOverride
{
    public Func<Task>? OnLeft { get; init; }
    public Func<Task>? OnRight { get; init; }
    public Func<Task>? OnPress { get; init; }
}
