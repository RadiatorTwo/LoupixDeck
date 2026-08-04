using LoupixDeck.Models;

namespace LoupixDeck.Services.Portable;

/// <summary>
/// Brings a freshly deserialized package payload into the shape this device expects, and performs
/// the post-load wiring the startup path does for the config but which never runs for an import.
/// </summary>
/// <remarks>
/// <para><b>Button counts.</b> <c>TouchButtonPage</c>/<c>RotaryButtonPage</c> have no parameterless
/// constructor, so Newtonsoft binds their <c>pageSize</c> to 0 and the button collections come
/// purely from the JSON — an imported page therefore carries the <i>source</i> device's button
/// count. Missing slots are padded; surplus buttons are deliberately kept rather than truncated,
/// so a page exported from a wider device and re-exported back arrives intact.</para>
/// <para><b>Active state.</b> A stateful button with <c>ResetOnRestart</c> does not serialize its
/// <c>ActiveStateId</c>, so after deserialization it sits on <c>Guid.Empty</c> and renders nothing
/// until normalized. <c>RewireLayerHandlers()</c> runs that normalization (and re-attaches the
/// layer handlers the polymorphic layer converter bypasses), mirroring what <c>DevicePostInit</c>
/// does at startup.</para>
/// </remarks>
public static class PortablePayloadNormalizer
{
    /// <summary>Normalizes every page of a profile.</summary>
    public static void Normalize(Profile profile, int touchButtonCount, int rotaryButtonCount, int sideRotaryButtonCount)
    {
        if (profile == null) return;

        foreach (Workspace workspace in profile.Workspaces ?? [])
            Normalize(workspace, touchButtonCount, rotaryButtonCount, sideRotaryButtonCount);

        // A home workspace that no longer resolves would leave the profile without an entry point.
        if (profile.Workspaces is { Count: > 0 } &&
            profile.Workspaces.All(w => w.Id != profile.HomeWorkspaceId))
        {
            profile.HomeWorkspaceId = profile.Workspaces[0].Id;
        }
    }

    /// <summary>Normalizes every page of a workspace and its page numbering.</summary>
    public static void Normalize(Workspace workspace, int touchButtonCount, int rotaryButtonCount, int sideRotaryButtonCount)
    {
        if (workspace == null) return;

        Renumber(workspace.TouchButtonPages);
        Renumber(workspace.RotaryButtonPages);
        Renumber(workspace.LeftRotaryButtonPages);
        Renumber(workspace.RightRotaryButtonPages);

        foreach (TouchButtonPage page in workspace.TouchButtonPages ?? [])
            Normalize(page, touchButtonCount);

        foreach (RotaryButtonPage page in workspace.RotaryButtonPages ?? [])
            Normalize(page, rotaryButtonCount, sideRotaryButtonCount);

        foreach (RotaryButtonPage page in workspace.LeftRotaryButtonPages ?? [])
            Normalize(page, rotaryButtonCount, sideRotaryButtonCount);

        foreach (RotaryButtonPage page in workspace.RightRotaryButtonPages ?? [])
            Normalize(page, rotaryButtonCount, sideRotaryButtonCount);

        int pageCount = workspace.TouchButtonPages?.Count ?? 0;
        workspace.StartupTouchPageIndex = pageCount > 0
            ? Math.Clamp(workspace.StartupTouchPageIndex, 0, pageCount - 1)
            : 0;
    }

    /// <summary>Pads a touch page to this device's key count and re-runs the post-load wiring.</summary>
    public static void Normalize(TouchButtonPage page, int touchButtonCount)
    {
        if (page?.TouchButtons == null) return;

        while (page.TouchButtons.Count < touchButtonCount)
            page.TouchButtons.Add(new TouchButton(page.TouchButtons.Count));

        for (int i = 0; i < page.TouchButtons.Count; i++)
        {
            TouchButton button = page.TouchButtons[i];
            if (button == null)
            {
                page.TouchButtons[i] = new TouchButton(i);
                continue;
            }

            button.Index = i;
            button.RewireLayerHandlers();
        }
    }

    /// <summary>Pads a rotary page to this device's dial count and wires its free-draw strip canvas.</summary>
    public static void Normalize(RotaryButtonPage page, int rotaryButtonCount, int sideRotaryButtonCount)
    {
        if (page == null) return;

        int expected = page.Side == RotarySide.Both ? rotaryButtonCount : sideRotaryButtonCount;

        if (page.RotaryButtons != null)
        {
            while (page.RotaryButtons.Count < expected)
                page.RotaryButtons.Add(new RotaryButton(page.RotaryButtons.Count, string.Empty, string.Empty));
        }

        // The free-draw strip is an ordinary touch button and needs the same post-load wiring.
        page.StripCanvas?.RewireLayerHandlers();
    }

    private static void Renumber<T>(IList<T> pages) where T : ButtonPageBase
    {
        if (pages == null) return;

        for (int i = 0; i < pages.Count; i++)
        {
            if (pages[i] != null)
                pages[i].Page = i + 1;
        }
    }
}
