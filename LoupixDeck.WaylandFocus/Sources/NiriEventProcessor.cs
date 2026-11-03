using System.Text.Json;

namespace LoupixDeck.WaylandFocus.Sources;

/// <summary>
/// State machine over niri's event stream: tracks the window list from the
/// atomic state updates and detects focused-window changes. Pure logic, no
/// IO, so it is directly unit-testable.
/// </summary>
public sealed class NiriEventProcessor
{
    private readonly Dictionary<long, WindowInfo> _windows = new();
    private long? _focusedWindowId;
    private FocusInfo? _lastFocus;

    /// <summary>
    /// Processes one JSON line from `niri msg --json event-stream`.
    /// Returns true when the focus changed; <paramref name="focus"/> then
    /// holds the new state (null = no focused toplevel).
    /// </summary>
    public bool Process(string jsonLine, out FocusInfo? focus)
    {
        focus = null;
        using JsonDocument document = JsonDocument.Parse(jsonLine);
        JsonElement eventElement = document.RootElement;

        // The stream opens with the reply to the EventStream request
        // ({"Ok":"Handled"} or {"Err":...}); the events after it are bare,
        // e.g. {"WindowFocusChanged":{"id":12}}.
        if (eventElement.ValueKind != JsonValueKind.Object
            || eventElement.TryGetProperty("Ok", out _)
            || eventElement.TryGetProperty("Err", out _))
            return false;

        if (eventElement.TryGetProperty("WindowsChanged", out JsonElement all))
        {
            _windows.Clear();
            foreach (JsonElement window in all.GetProperty("windows").EnumerateArray())
                Upsert(window);
        }
        else if (eventElement.TryGetProperty("WindowOpenedOrChanged", out JsonElement opened))
        {
            Upsert(opened.GetProperty("window"));
        }
        else if (eventElement.TryGetProperty("WindowClosed", out JsonElement closed))
        {
            long id = closed.GetProperty("id").GetInt64();
            _windows.Remove(id);
            if (_focusedWindowId == id)
                _focusedWindowId = null;
        }
        else if (eventElement.TryGetProperty("WindowFocusChanged", out JsonElement focusChanged))
        {
            // "id" is null when no window is focused; TryGetInt64 throws on a Null token.
            JsonElement idElement = focusChanged.GetProperty("id");
            _focusedWindowId = idElement.ValueKind == JsonValueKind.Number ? idElement.GetInt64() : null;
        }

        return TakeFocus(out focus);
    }

    /// <summary>The focus as of the events processed so far (for seeding an initial emission).</summary>
    public FocusInfo? CurrentFocus() =>
        _focusedWindowId is { } id && _windows.TryGetValue(id, out WindowInfo? window)
            ? new FocusInfo(window.AppId, window.Title, window.Pid)
            : null;

    private void Upsert(JsonElement window)
    {
        long id = window.GetProperty("id").GetInt64();
        JsonElement pidElement = window.GetProperty("pid");
        WindowInfo info = new(
            window.GetProperty("title").GetString(),
            window.GetProperty("app_id").GetString(),
            pidElement.ValueKind == JsonValueKind.Number ? pidElement.GetInt32() : null);
        _windows[id] = info;

        // WindowsOpenedOrChanged marks the focused window ("if the window is
        // focused, all other windows are no longer focused").
        if (window.TryGetProperty("is_focused", out JsonElement focused) && focused.GetBoolean())
            _focusedWindowId = id;
    }

    private bool TakeFocus(out FocusInfo? focus)
    {
        focus = null;
        if (_focusedWindowId is { } id && _windows.TryGetValue(id, out WindowInfo? window))
        {
            FocusInfo current = new(window.AppId, window.Title, window.Pid);
            if (current == _lastFocus)
                return false;
            focus = current;
            _lastFocus = current;
            return true;
        }

        if (_lastFocus is null)
            return false;
        _lastFocus = null;
        return true;
    }

    private sealed record WindowInfo(string? Title, string? AppId, int? Pid);
}