using System.Diagnostics;
using LoupixDeck.Models.Macros;
using LoupixDeck.PluginSdk;
using LoupixDeck.Services.Mouse;

namespace LoupixDeck.Services.Macros;

/// <summary>
/// Executes the steps of a user-defined macro sequentially. Each step is wrapped in
/// its own try/catch so a faulty step (unknown key, failing command) does not abort
/// the rest of the macro.
/// </summary>
public class MacroRunner : IDisposable
{
    private readonly IUInputKeyboard _keyboard;
    private readonly IVirtualMouse _mouse;
    private readonly ICommandService _commandService;
    private readonly IMacroStopCoordinator _stopCoordinator;

    public MacroRunner(IUInputKeyboard keyboard, IVirtualMouse mouse, ICommandService commandService,
        IMacroStopCoordinator stopCoordinator)
    {
        _keyboard = keyboard;
        _mouse = mouse;
        _commandService = commandService;
        _stopCoordinator = stopCoordinator;

        // Join the app-global registry so the global stop hotkey can reach this runner.
        _stopCoordinator.Register(this);
    }

    public void Dispose()
    {
        _stopCoordinator.Unregister(this);
    }

    // Cancellation tokens of every in-flight Run, so CancelAll() can stop them all.
    private readonly object _runLock = new();
    private readonly List<CancellationTokenSource> _activeRuns = [];

    /// <summary>True while at least one macro is currently executing.</summary>
    public bool IsRunning
    {
        get
        {
            lock (_runLock)
                return _activeRuns.Count > 0;
        }
    }

    /// <summary>Cancels every running macro (the Stop command / hotkey entry point).</summary>
    public void CancelAll()
    {
        lock (_runLock)
        {
            foreach (var cts in _activeRuns)
            {
                try { cts.Cancel(); }
                catch (ObjectDisposedException) { /* run already finished */ }
            }
        }
    }

    // Hard cap on total executed steps so a runaway/huge repeat count can never hang forever.
    private const int MaxExecutedSteps = 100_000;

    // One active Repeat block. Mutable so the interpreter can decrement Remaining in place.
    private sealed class LoopFrame
    {
        public int StartIndex;
        public int Remaining;
        public int LoopDelayMs;
    }

    public async Task Run(Macro macro, CancellationToken cancellationToken = default)
    {
        if (macro == null)
        {
            Console.Error.WriteLine("[MacroRunner] Macro not found.");
            return;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_runLock)
            _activeRuns.Add(cts);

        try
        {
            await RunSteps(macro, cts.Token);
        }
        finally
        {
            lock (_runLock)
                _activeRuns.Remove(cts);
        }
    }

    private async Task RunSteps(Macro macro, CancellationToken token)
    {
        // Snapshot the steps so concurrent edits in the editor can't shift indices mid-run.
        var steps = macro.Steps.ToList();
        var frames = new Stack<LoopFrame>();
        var executed = 0;

        for (var i = 0; i < steps.Count; i++)
        {
            if (token.IsCancellationRequested)
                break;

            if (++executed > MaxExecutedSteps)
            {
                Console.Error.WriteLine(
                    $"[MacroRunner] Macro '{macro.Name}' exceeded {MaxExecutedSteps} steps — aborting (possible runaway repeat).");
                break;
            }

            var step = steps[i];
            if (step == null) continue;

            switch (step)
            {
                case RepeatStartStep repeatStart:
                    frames.Push(new LoopFrame
                    {
                        StartIndex = i,
                        Remaining = Math.Max(1, repeatStart.Count),
                        LoopDelayMs = repeatStart.LoopDelayMilliseconds
                    });
                    break;

                case RepeatEndStep:
                    // Unmatched end → ignore. Otherwise loop back until the frame is spent.
                    if (frames.Count > 0)
                    {
                        var frame = frames.Peek();
                        frame.Remaining--;
                        if (frame.Remaining > 0)
                        {
                            if (frame.LoopDelayMs > 0)
                                await Delay(frame.LoopDelayMs, token);
                            i = frame.StartIndex; // for-loop's i++ resumes just after the start marker
                        }
                        else
                        {
                            frames.Pop();
                        }
                    }
                    break;

                default:
                    try
                    {
                        await ExecuteStep(step, token);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine(
                            $"[MacroRunner] Step '{step.TypeText}' in macro '{macro.Name}' failed: {ex.Message}");
                    }
                    break;
            }
        }
    }

    private async Task ExecuteStep(MacroStep step, CancellationToken token)
    {
        switch (step)
        {
            case TextStep text:
                if (!string.IsNullOrEmpty(text.Text))
                    _keyboard.SendText(text.Text);
                break;

            case KeyCombinationStep combo:
                var keys = (combo.Keys ?? string.Empty)
                    .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (keys.Length > 0)
                    _keyboard.SendKeyCombination(keys);
                break;

            case DelayStep delay:
                await Delay(delay.Milliseconds, token);
                break;

            case KeyDownStep keyDown:
                if (!string.IsNullOrWhiteSpace(keyDown.Key))
                    _keyboard.KeyDown(keyDown.Key);
                break;

            case KeyUpStep keyUp:
                if (!string.IsNullOrWhiteSpace(keyUp.Key))
                    _keyboard.KeyUp(keyUp.Key);
                break;

            case MouseStep mouse:
                ExecuteMouseStep(mouse);
                break;

            case CommandStep command:
                if (!string.IsNullOrWhiteSpace(command.CommandString))
                    await _commandService.ExecuteCommand(command.CommandString, ButtonTargets.None);
                break;
        }
    }

    private void ExecuteMouseStep(MouseStep step)
    {
        switch (step.Action)
        {
            case MouseStepAction.Click:
                _mouse.Click(step.Button);
                break;
            case MouseStepAction.Down:
                _mouse.ButtonDown(step.Button);
                break;
            case MouseStepAction.Up:
                _mouse.ButtonUp(step.Button);
                break;
            case MouseStepAction.MoveRelative:
                _mouse.MoveRelative(step.X, step.Y);
                break;
            case MouseStepAction.MoveAbsolute:
                _mouse.MoveAbsolute(step.X, step.Y);
                break;
            case MouseStepAction.Scroll:
                _mouse.Scroll(step.Amount);
                break;
        }
    }

    // Coarse Task.Delay for the bulk, short spin for the tail — Task.Delay alone has
    // ~15 ms granularity on Windows (same pattern as DeviceControlCommands.WaitUntilMs).
    private static async Task Delay(int milliseconds, CancellationToken token)
    {
        if (milliseconds <= 0)
            return;

        var sw = Stopwatch.StartNew();
        while (true)
        {
            if (token.IsCancellationRequested) return;
            var remain = milliseconds - sw.Elapsed.TotalMilliseconds;
            if (remain <= 0) return;
            if (remain > 3) await Task.Delay(1);
            else Thread.SpinWait(200);
        }
    }
}
