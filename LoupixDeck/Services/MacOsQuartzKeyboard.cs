using System.Runtime.InteropServices;
using LoupixDeck.Utils;

namespace LoupixDeck.Services;

/// <summary>
/// Keyboard injection on macOS through Quartz Event Services: key codes from
/// <see cref="KeyNames"/> are posted as CGEvents at the HID tap, so they reach every
/// application the same way a real keyboard does.
/// </summary>
/// <remarks>
/// <para>
/// Two things differ from the Linux and Windows backends and are worth knowing before
/// changing anything here.
/// </para>
/// <para>
/// <b>Permission.</b> Posting events requires the Accessibility grant. Without it macOS
/// discards every event silently — no error, no exception, simply nothing happens. That is
/// why <see cref="Connected"/> reflects <c>AXIsProcessTrusted</c> and the failure is logged
/// once: a silent no-op is otherwise indistinguishable from a broken key map. The grant is
/// tied to the binary's identity, so an unsigned build run straight from <c>bin/</c> has to
/// be re-granted whenever that path changes — see the packaging item in PLAN.md.
/// </para>
/// <para>
/// <b>Modifier flags.</b> Posting the modifier key's own event is not enough on macOS. Many
/// applications read the flags carried on the event rather than tracking modifier presses,
/// so a combination has to set the accumulated mask on every event it posts, which is what
/// <see cref="SendKeyCombination"/> does.
/// </para>
/// </remarks>
public sealed partial class MacOsQuartzKeyboard : IUInputKeyboard
{
    private const string ApplicationServices =
        "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

    private const string CoreFoundation =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    // kCGHIDEventTap — the lowest tap, so events are seen by everything above it.
    private const uint HidEventTap = 0;

    // CGEventFlags. Only the four that a combination can carry.
    private const ulong FlagShift = 0x00020000;
    private const ulong FlagControl = 0x00040000;
    private const ulong FlagAlternate = 0x00080000;
    private const ulong FlagCommand = 0x00100000;


    private bool _warnedUntrusted;

    public MacOsQuartzKeyboard()
    {
        // Ask once at startup. macOS shows its own dialog with a button that opens the right
        // settings pane, which is the only discoverable way for the user to find this — a key
        // that silently does nothing gives them nothing to act on.
        Connected = RequestTrust();

        if (!Connected)
        {
            Console.WriteLine(
                "[Keyboard] Accessibility permission is not granted, so key output will do nothing. " +
                "Grant it under System Settings > Privacy & Security > Accessibility, then restart.");
        }
    }

    /// <summary>
    /// Checks the Accessibility grant, asking macOS to show its permission dialog when it is
    /// missing.
    /// </summary>
    /// <remarks>
    /// The option dictionary has to be built exactly as CoreFoundation expects or the check
    /// segfaults rather than returning: the key must be the framework's own
    /// <c>kAXTrustedCheckOptionPrompt</c> instance, because a dictionary built with the standard
    /// callbacks compares keys by <c>CFEqual</c> and identity, and the dictionary must carry
    /// <c>kCFTypeDictionaryKeyCallBacks</c>/<c>kCFTypeDictionaryValueCallBacks</c> so its contents
    /// are retained — passing null callbacks crashes inside the framework. Any symbol that cannot
    /// be resolved falls back to the silent check, so this can never be worse than not prompting.
    /// </remarks>
    private static bool RequestTrust()
    {
        IntPtr options = IntPtr.Zero;

        try
        {
            IntPtr coreFoundation = NativeLibrary.Load(CoreFoundation);
            IntPtr applicationServices = NativeLibrary.Load(ApplicationServices);

            // Pointer-valued symbols are dereferenced; the callback structs are passed by address.
            if (!TryReadPointer(coreFoundation, "kCFBooleanTrue", out IntPtr trueValue) ||
                !TryReadPointer(applicationServices, "kAXTrustedCheckOptionPrompt", out IntPtr promptKey) ||
                !NativeLibrary.TryGetExport(coreFoundation, "kCFTypeDictionaryKeyCallBacks", out IntPtr keyCallBacks) ||
                !NativeLibrary.TryGetExport(coreFoundation, "kCFTypeDictionaryValueCallBacks", out IntPtr valueCallBacks))
            {
                return AXIsProcessTrusted();
            }

            options = CFDictionaryCreate(IntPtr.Zero, [promptKey], [trueValue], 1, keyCallBacks, valueCallBacks);

            return options != IntPtr.Zero ? AXIsProcessTrustedWithOptions(options) : AXIsProcessTrusted();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException
                                       or BadImageFormatException)
        {
            return AXIsProcessTrusted();
        }
        finally
        {
            if (options != IntPtr.Zero)
                CFRelease(options);
        }
    }

    /// <summary>
    /// Reads a pointer-valued exported symbol (a CFStringRef or CFBooleanRef singleton). These are
    /// data, not functions, so they cannot be imported like the calls below.
    /// </summary>
    private static bool TryReadPointer(IntPtr module, string symbol, out IntPtr value)
    {
        if (NativeLibrary.TryGetExport(module, symbol, out IntPtr address) && address != IntPtr.Zero)
        {
            value = Marshal.ReadIntPtr(address);
            return value != IntPtr.Zero;
        }

        value = IntPtr.Zero;
        return false;
    }

    public bool Connected { get; set; }

    public void SendKey(int keyCode)
    {
        if (!Ready()) return;
        if (keyCode is < 0 or > ushort.MaxValue) return;

        PostKey((ushort)keyCode, true, 0);
        PostKey((ushort)keyCode, false, 0);
    }

    /// <summary>
    /// Types text as Unicode rather than as key codes. A key code names a physical position,
    /// whose character depends on the active layout; attaching the string to the event instead
    /// produces exactly these characters on any layout.
    /// </summary>
    public void SendText(string text)
    {
        if (!Ready() || string.IsNullOrEmpty(text)) return;

        foreach (char character in text)
            TypeCharacter(character);
    }

    public void SendKeyCombination(IReadOnlyList<string> keyNames)
    {
        if (!Ready() || keyNames == null || keyNames.Count == 0) return;

        var codes = new List<ushort>(keyNames.Count);
        ulong flags = 0;

        foreach (string name in keyNames)
        {
            if (!TryResolve(name, out ushort code))
                continue;

            codes.Add(code);
            flags |= FlagFor(code);
        }

        if (codes.Count == 0)
            return;

        // Press in order, release in reverse — same contract as the other backends. Every
        // event carries the full mask so an application that reads flags sees the modifiers
        // even on the key that completes the chord.
        foreach (ushort code in codes)
            PostKey(code, true, flags);

        for (int i = codes.Count - 1; i >= 0; i--)
            PostKey(codes[i], false, flags);
    }

    public void KeyDown(string keyName)
    {
        if (!Ready()) return;
        if (!TryResolve(keyName, out ushort code)) return;

        PostKey(code, true, FlagFor(code));
    }

    public void KeyUp(string keyName)
    {
        if (!Ready()) return;
        if (!TryResolve(keyName, out ushort code)) return;

        PostKey(code, false, FlagFor(code));
    }

    public void Dispose()
    {
    }

    /// <summary>
    /// Resolves a name to a macOS key code. Names the position table does not carry fall back
    /// to being typed as characters, which covers the layout-specific punctuation and umlauts
    /// the other backends resolve against the active layout.
    /// </summary>
    private bool TryResolve(string name, out ushort code)
    {
        code = 0;

        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (KeyNames.TryGetMacOs(name, out int keyCode) && keyCode is >= 0 and <= ushort.MaxValue)
        {
            code = (ushort)keyCode;
            return true;
        }

        if (KeyNames.TryGetCharacter(name, out char character))
        {
            TypeCharacter(character);
            return false;
        }

        Console.WriteLine($"[Keyboard] '{name}' has no macOS equivalent and was skipped.");
        return false;
    }

    /// <summary>The modifier mask a key code contributes, or zero for an ordinary key.</summary>
    private static ulong FlagFor(ushort code) => code switch
    {
        56 or 60 => FlagShift,     // kVK_Shift, kVK_RightShift
        59 or 62 => FlagControl,   // kVK_Control, kVK_RightControl
        58 or 61 => FlagAlternate, // kVK_Option, kVK_RightOption
        55 => FlagCommand,         // kVK_Command
        _ => 0
    };

    private void TypeCharacter(char character)
    {
        // Key code 0 with a Unicode string attached: the code is ignored and the string is
        // delivered instead, so this works whatever the active layout.
        ushort[] buffer = [character];

        IntPtr down = CGEventCreateKeyboardEvent(IntPtr.Zero, 0, true);
        if (down != IntPtr.Zero)
        {
            CGEventKeyboardSetUnicodeString(down, buffer.Length, buffer);
            CGEventPost(HidEventTap, down);
            CFRelease(down);
        }

        IntPtr up = CGEventCreateKeyboardEvent(IntPtr.Zero, 0, false);
        if (up != IntPtr.Zero)
        {
            CGEventKeyboardSetUnicodeString(up, buffer.Length, buffer);
            CGEventPost(HidEventTap, up);
            CFRelease(up);
        }
    }

    private static void PostKey(ushort code, bool down, ulong flags)
    {
        IntPtr handle = CGEventCreateKeyboardEvent(IntPtr.Zero, code, down);
        if (handle == IntPtr.Zero)
            return;

        if (flags != 0)
            CGEventSetFlags(handle, flags);

        CGEventPost(HidEventTap, handle);
        CFRelease(handle);
    }

    /// <summary>
    /// Re-checks the Accessibility grant, so output starts working as soon as the user grants
    /// it without needing a restart. Logs the refusal once rather than on every key.
    /// </summary>
    private bool Ready()
    {
        if (Connected)
            return true;

        Connected = AXIsProcessTrusted();
        if (Connected)
        {
            Console.WriteLine("[Keyboard] Accessibility permission granted; key output is now active.");
            return true;
        }

        if (!_warnedUntrusted)
        {
            _warnedUntrusted = true;
            Console.WriteLine("[Keyboard] Key output skipped: no Accessibility permission.");
        }

        return false;
    }

    [LibraryImport(ApplicationServices)]
    private static partial IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort virtualKey,
        [MarshalAs(UnmanagedType.I1)] bool keyDown);

    [LibraryImport(ApplicationServices)]
    private static partial void CGEventPost(uint tap, IntPtr handle);

    [LibraryImport(ApplicationServices)]
    private static partial void CGEventSetFlags(IntPtr handle, ulong flags);

    [LibraryImport(ApplicationServices)]
    private static partial void CGEventKeyboardSetUnicodeString(IntPtr handle, long length,
        [In] ushort[] unicodeString);

    [LibraryImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool AXIsProcessTrusted();

    [LibraryImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool AXIsProcessTrustedWithOptions(IntPtr options);

    [LibraryImport(CoreFoundation)]
    private static partial IntPtr CFDictionaryCreate(IntPtr allocator, [In] IntPtr[] keys,
        [In] IntPtr[] values, long count, IntPtr keyCallBacks, IntPtr valueCallBacks);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRelease(IntPtr handle);
}
