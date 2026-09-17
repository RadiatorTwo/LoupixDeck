using System.Runtime.InteropServices;
using LoupixDeck.Localization;
using LoupixDeck.Services.PluginStore;

namespace LoupixDeck.Services.Diagnostics.Linux;

/// <summary>The outcome of one interactive test, ready to be shown as it is.</summary>
/// <param name="Success">True when the tested path worked end to end.</param>
/// <param name="Message">Localized one-line result.</param>
/// <param name="Detail">Raw technical output, or null.</param>
public sealed record InteractiveTestResult(bool Success, string Message, string Detail = null);

/// <summary>
/// The tests of issue #258 phase 5. They are the only part of Device Doctor that acts rather
/// than observes, so none of them runs as part of a normal run: the page asks first, and the
/// user starts each one explicitly.
/// </summary>
public interface IInteractiveDiagnosticTests
{
    /// <summary>
    /// Creates a virtual keyboard, presses F24 once and destroys the device again. F24 is used
    /// because nothing binds it, so the key press cannot trigger anything.
    /// </summary>
    Task<InteractiveTestResult> SendTestKeyAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Waits for the user to press a key and reports the first event that arrives. This is the
    /// only way to prove recording works: playback working says nothing about it, because the
    /// two use different device nodes and different permissions.
    /// </summary>
    Task<InteractiveTestResult> AwaitKeyEventAsync(TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>
    /// Loads the plugin catalog from the network. This is the only check that leaves the
    /// machine, which is why it is a test the user starts and never part of a run.
    /// </summary>
    Task<InteractiveTestResult> CheckPluginStoreAsync(CancellationToken cancellationToken);
}

/// <inheritdoc cref="IInteractiveDiagnosticTests"/>
public sealed class InteractiveDiagnosticTests(IPluginStoreService store) : IInteractiveDiagnosticTests
{
    private const string TestDeviceName = "LoupixDeck Diagnostics Test";

    public Task<InteractiveTestResult> SendTestKeyAsync(CancellationToken cancellationToken)
        => Task.Run(() => SendTestKey(), cancellationToken);

    public Task<InteractiveTestResult> AwaitKeyEventAsync(TimeSpan timeout,
        CancellationToken cancellationToken)
        => Task.Run(() => AwaitKeyEvent(timeout, cancellationToken), cancellationToken);

    public async Task<InteractiveTestResult> CheckPluginStoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            PluginStoreResult result = await store.GetItemsAsync(true, cancellationToken);

            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                return new InteractiveTestResult(false, Loc.Tr("Diagnostics_StoreTestFailed"), result.Error);
            }

            return result.IsFromCache
                ? new InteractiveTestResult(false, Loc.Tr("Diagnostics_StoreTestFromCache"))
                : new InteractiveTestResult(true,
                    Loc.Tr("Diagnostics_StoreTestOkFmt", result.Items.Count));
        }
        catch (Exception ex)
        {
            return new InteractiveTestResult(false, Loc.Tr("Diagnostics_StoreTestFailed"),
                $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static InteractiveTestResult SendTestKey()
    {
        int fileDescriptor = LinuxInputInterop.open(LinuxInputInterop.UinputPath,
            LinuxInputInterop.O_WRONLY | LinuxInputInterop.O_NONBLOCK);

        if (fileDescriptor < 0)
        {
            int errno = Marshal.GetLastPInvokeError();

            return new InteractiveTestResult(false, Loc.Tr("Diagnostics_TestKeyNoAccessFmt", errno),
                $"open(\"{LinuxInputInterop.UinputPath}\", O_WRONLY|O_NONBLOCK) = -1 (errno {errno})");
        }

        bool created = false;

        try
        {
            if ((LinuxInputInterop.ioctl(fileDescriptor, LinuxInputInterop.UI_SET_EVBIT,
                    LinuxInputInterop.EV_KEY) < 0) ||
                (LinuxInputInterop.ioctl(fileDescriptor, LinuxInputInterop.UI_SET_KEYBIT,
                    LinuxInputInterop.KEY_F24) < 0) ||
                !LinuxInputInterop.WriteUserDev(fileDescriptor, TestDeviceName) ||
                (LinuxInputInterop.ioctl(fileDescriptor, LinuxInputInterop.UI_DEV_CREATE, 0) < 0))
            {
                return Failed(Loc.Tr("Diagnostics_TestKeyCreateFailed"));
            }

            created = true;

            // The kernel needs a moment to hand the new device to the compositor; without it the
            // events are written into a device nobody is listening to yet.
            Thread.Sleep(200);

            if (!LinuxInputInterop.WriteKey(fileDescriptor, LinuxInputInterop.KEY_F24, true) ||
                !LinuxInputInterop.WriteKey(fileDescriptor, LinuxInputInterop.KEY_F24, false))
            {
                return Failed(Loc.Tr("Diagnostics_TestKeyWriteFailed"));
            }

            return new InteractiveTestResult(true, Loc.Tr("Diagnostics_TestKeySent"));
        }
        finally
        {
            if (created)
            {
                LinuxInputInterop.ioctl(fileDescriptor, LinuxInputInterop.UI_DEV_DESTROY, 0);
            }

            LinuxInputInterop.close(fileDescriptor);
        }
    }

    private static InteractiveTestResult AwaitKeyEvent(TimeSpan timeout, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> nodes = LinuxEventDeviceFacts.KeyboardNodes();

        if (nodes.Count == 0)
        {
            return new InteractiveTestResult(false, Loc.Tr("Diagnostics_TestRecordNoNode"));
        }

        List<int> descriptors = [];

        try
        {
            foreach (string node in nodes)
            {
                int descriptor = LinuxInputInterop.open(node,
                    LinuxInputInterop.O_RDONLY | LinuxInputInterop.O_NONBLOCK);

                if (descriptor >= 0)
                {
                    descriptors.Add(descriptor);
                }
            }

            if (descriptors.Count == 0)
            {
                return new InteractiveTestResult(false, Loc.Tr("Diagnostics_TestRecordNoAccess"));
            }

            DateTime deadline = DateTime.UtcNow + timeout;

            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                foreach (int descriptor in descriptors)
                {
                    if (LinuxInputInterop.TryReadKeyEvent(descriptor, out ushort code, out int value))
                    {
                        // A release is reported as well: what matters is that an event arrived.
                        return new InteractiveTestResult(true,
                            Loc.Tr("Diagnostics_TestRecordReceivedFmt", code),
                            $"EV_KEY code={code} value={value}");
                    }
                }

                Thread.Sleep(20);
            }

            return new InteractiveTestResult(false, Loc.Tr("Diagnostics_TestRecordTimedOut"));
        }
        finally
        {
            foreach (int descriptor in descriptors)
            {
                LinuxInputInterop.close(descriptor);
            }
        }
    }

    private static InteractiveTestResult Failed(string message)
    {
        int errno = Marshal.GetLastPInvokeError();

        return new InteractiveTestResult(false, message, $"errno {errno}");
    }
}
