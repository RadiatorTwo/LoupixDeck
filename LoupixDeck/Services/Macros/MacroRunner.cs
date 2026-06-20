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
    private readonly IMacroConditionEvaluator _conditionEvaluator;
    private readonly IMacroExecutionRegistry _executionRegistry;
    private readonly IMacroPromptService _promptService;

    public MacroRunner(IUInputKeyboard keyboard, IVirtualMouse mouse, ICommandService commandService,
        IMacroStopCoordinator stopCoordinator, IMacroConditionEvaluator conditionEvaluator,
        IMacroExecutionRegistry executionRegistry, IMacroPromptService promptService)
    {
        _keyboard = keyboard;
        _mouse = mouse;
        _commandService = commandService;
        _stopCoordinator = stopCoordinator;
        _conditionEvaluator = conditionEvaluator;
        _executionRegistry = executionRegistry;
        _promptService = promptService;

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

    // One frame on the unified control stack. Loops and conditionals share the stack so
    // arbitrary nesting (If inside Repeat and vice-versa) is correct by construction and
    // malformed markers degrade to no-ops instead of desyncing the interpreter.
    private enum FrameKind { Loop, IfTaken, IfSkipped }

    private sealed class ControlFrame
    {
        public FrameKind Kind;
        public int StartIndex;   // Loop: index of the RepeatStart marker.
        public int Remaining;    // Loop: iterations still to run.
        public int LoopDelayMs;  // Loop: pause between iterations.
        public bool Infinite;    // Loop: repeat until stopped.
    }

    public async Task Run(Macro macro, CancellationToken cancellationToken = default)
    {
        if (macro == null)
        {
            Console.Error.WriteLine("[MacroRunner] Macro not found.");
            return;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Enforce the macro's execution mode app-wide. RunOnce skips a re-trigger while busy;
        // RestartOnTrigger cancels the in-flight run; AllowParallel always admits.
        if (!_executionRegistry.TryBegin(macro, cts))
            return;

        lock (_runLock)
            _activeRuns.Add(cts);

        var context = new MacroContext();
        var failed = false;
        _executionRegistry.Report(macro.Name, MacroExecutionState.Running);
        try
        {
            failed = await RunSteps(macro, context, cts.Token);
        }
        catch (Exception ex)
        {
            failed = true;
            Console.Error.WriteLine($"[MacroRunner] Macro '{macro.Name}' failed: {ex.Message}");
        }
        finally
        {
            ReleaseHeldInput(context);

            var state = cts.IsCancellationRequested ? MacroExecutionState.Cancelled
                : failed ? MacroExecutionState.Failed
                : MacroExecutionState.Completed;
            _executionRegistry.Report(macro.Name, state);

            lock (_runLock)
                _activeRuns.Remove(cts);
            _executionRegistry.End(macro.Name, cts);
        }
    }

    /// <summary>
    /// Guaranteed cleanup: release every key / mouse button the macro left held, in reverse
    /// acquisition order, each isolated so one failure can't strand the rest. Runs on normal
    /// completion, exceptions, and cancellation alike.
    /// </summary>
    private void ReleaseHeldInput(MacroContext context)
    {
        foreach (var key in context.HeldKeys.Reverse())
        {
            try { _keyboard.KeyUp(key); }
            catch (Exception ex) { Console.Error.WriteLine($"[MacroRunner] Cleanup KeyUp('{key}') failed: {ex.Message}"); }
        }

        foreach (var button in context.HeldButtons.Reverse())
        {
            try { _mouse.ButtonUp(button); }
            catch (Exception ex) { Console.Error.WriteLine($"[MacroRunner] Cleanup ButtonUp({button}) failed: {ex.Message}"); }
        }
    }

    /// <summary>Runs the steps. Returns true if the macro aborted on a failed condition.</summary>
    private async Task<bool> RunSteps(Macro macro, MacroContext context, CancellationToken token)
    {
        // Snapshot the steps so concurrent edits in the editor can't shift indices mid-run.
        var steps = macro.Steps.ToList();
        var stack = new Stack<ControlFrame>();
        var executed = 0;

        var i = 0;
        while (i < steps.Count)
        {
            if (token.IsCancellationRequested)
                break;

            // The step cap guards against accidental runaway finite repeats. A deliberate
            // infinite loop is exempt — it runs until the user stops it (Stop command/hotkey).
            if (!stack.Any(f => f.Kind == FrameKind.Loop && f.Infinite) && ++executed > MaxExecutedSteps)
            {
                Console.Error.WriteLine(
                    $"[MacroRunner] Macro '{macro.Name}' exceeded {MaxExecutedSteps} steps — aborting (possible runaway repeat).");
                break;
            }

            var step = steps[i];
            if (step == null)
            {
                i++;
                continue;
            }

            switch (step)
            {
                case RepeatStartStep repeatStart:
                    stack.Push(new ControlFrame
                    {
                        Kind = FrameKind.Loop,
                        StartIndex = i,
                        Remaining = Math.Max(1, repeatStart.Count),
                        LoopDelayMs = repeatStart.LoopDelayMilliseconds,
                        Infinite = repeatStart.Infinite
                    });
                    i++;
                    break;

                case RepeatEndStep:
                    // Unmatched end (no loop on top) → ignore. Otherwise loop back until spent.
                    if (stack.Count > 0 && stack.Peek().Kind == FrameKind.Loop)
                    {
                        var frame = stack.Peek();

                        if (frame.Infinite)
                        {
                            if (frame.LoopDelayMs > 0)
                                await Delay(frame.LoopDelayMs, token);
                            else
                                // Throttle a no-delay infinite loop so it can't peg a core and
                                // stays responsive to cancellation (checked at the loop top).
                                await Task.Delay(1);
                            i = frame.StartIndex + 1;
                        }
                        else
                        {
                            frame.Remaining--;
                            if (frame.Remaining > 0)
                            {
                                if (frame.LoopDelayMs > 0)
                                    await Delay(frame.LoopDelayMs, token);
                                i = frame.StartIndex + 1;
                            }
                            else
                            {
                                stack.Pop();
                                i++;
                            }
                        }
                    }
                    else
                    {
                        i++;
                    }
                    break;

                case IfStep ifStep:
                    if (_conditionEvaluator.Evaluate(ifStep.Condition, context))
                    {
                        // Run the then-branch; the matching Else/EndIf will pop this frame.
                        stack.Push(new ControlFrame { Kind = FrameKind.IfTaken });
                        i++;
                    }
                    else
                    {
                        i = SkipToElseOrEndIf(steps, i, out var landedOnElse);
                        if (landedOnElse)
                        {
                            // Enter the false-branch (step after the Else marker).
                            stack.Push(new ControlFrame { Kind = FrameKind.IfSkipped });
                            i++;
                        }
                        // else: i is already just past the EndIf, nothing to push.
                    }
                    break;

                case ElseStep:
                    // Reached by falling through the end of a taken then-branch → skip the
                    // false-branch. An orphan Else (no open If) is a no-op.
                    if (stack.Count > 0 && stack.Peek().Kind == FrameKind.IfTaken)
                    {
                        stack.Pop();
                        i = SkipPastMatchingEndIf(steps, i);
                    }
                    else
                    {
                        i++;
                    }
                    break;

                case EndIfStep:
                    if (stack.Count > 0 &&
                        stack.Peek().Kind is FrameKind.IfTaken or FrameKind.IfSkipped)
                        stack.Pop();
                    i++;
                    break;

                case WaitForConditionStep wait:
                    _executionRegistry.Report(macro.Name, MacroExecutionState.Waiting);
                    var satisfied = await WaitForCondition(wait, context, token);
                    if (!token.IsCancellationRequested)
                        _executionRegistry.Report(macro.Name, MacroExecutionState.Running);

                    if (!satisfied && !token.IsCancellationRequested &&
                        wait.OnTimeout == WaitTimeoutBehavior.Fail)
                    {
                        Console.Error.WriteLine(
                            $"[MacroRunner] Macro '{macro.Name}' wait timed out ({wait.Condition?.Summary}) — aborting.");
                        return true;
                    }

                    i++;
                    break;

                case PromptStep prompt:
                    _executionRegistry.Report(macro.Name, MacroExecutionState.Waiting);
                    var answer = await _promptService.RequestInputAsync(
                        context.Expand(prompt.Message), context.Expand(prompt.DefaultValue), token);
                    if (!token.IsCancellationRequested)
                        _executionRegistry.Report(macro.Name, MacroExecutionState.Running);

                    // Cancelling the prompt (or a Stop) leaves the variable unchanged.
                    if (answer != null && !string.IsNullOrWhiteSpace(prompt.VariableName))
                        context.Variables[prompt.VariableName.Trim()] = answer;
                    i++;
                    break;

                default:
                    try
                    {
                        await ExecuteStep(step, context, token);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine(
                            $"[MacroRunner] Step '{step.TypeText}' in macro '{macro.Name}' failed: {ex.Message}");
                    }

                    i++;
                    break;
            }
        }

        return false;
    }

    /// <summary>
    /// From an If marker, finds where execution continues when the condition is false:
    /// the matching Else (returns its index, <paramref name="landedOnElse"/> = true) or just
    /// past the matching EndIf (returns that index). Nested If/EndIf are depth-counted so
    /// only the marker that closes THIS If counts.
    /// </summary>
    private static int SkipToElseOrEndIf(List<MacroStep> steps, int ifIndex, out bool landedOnElse)
    {
        var depth = 0;
        for (var j = ifIndex + 1; j < steps.Count; j++)
        {
            switch (steps[j])
            {
                case IfStep:
                    depth++;
                    break;
                case ElseStep when depth == 0:
                    landedOnElse = true;
                    return j;
                case EndIfStep when depth == 0:
                    landedOnElse = false;
                    return j + 1;
                case EndIfStep:
                    depth--;
                    break;
            }
        }

        landedOnElse = false;
        return steps.Count;
    }

    /// <summary>
    /// Polls the step's condition until it is true, the timeout elapses, or the macro is
    /// cancelled. Returns true when the condition was met, false on timeout/cancellation.
    /// A timeout of 0 waits indefinitely (until stopped).
    /// </summary>
    private async Task<bool> WaitForCondition(WaitForConditionStep step, MacroContext context, CancellationToken token)
    {
        if (step.Condition == null)
            return true;

        var pollMs = Math.Max(10, step.PollIntervalMilliseconds);
        var elapsed = Stopwatch.StartNew();

        while (true)
        {
            if (token.IsCancellationRequested)
                return false;

            if (_conditionEvaluator.Evaluate(step.Condition, context))
                return true;

            if (step.TimeoutMilliseconds > 0 && elapsed.Elapsed.TotalMilliseconds >= step.TimeoutMilliseconds)
                return false;

            await Delay(pollMs, token);
        }
    }

    /// <summary>From an Else marker, returns the index just past the matching EndIf.</summary>
    private static int SkipPastMatchingEndIf(List<MacroStep> steps, int elseIndex)
    {
        var depth = 0;
        for (var j = elseIndex + 1; j < steps.Count; j++)
        {
            switch (steps[j])
            {
                case IfStep:
                    depth++;
                    break;
                case EndIfStep when depth == 0:
                    return j + 1;
                case EndIfStep:
                    depth--;
                    break;
            }
        }

        return steps.Count;
    }

    private async Task ExecuteStep(MacroStep step, MacroContext context, CancellationToken token)
    {
        switch (step)
        {
            case TextStep text:
                var expandedText = context.Expand(text.Text);
                if (!string.IsNullOrEmpty(expandedText))
                    _keyboard.SendText(expandedText);
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
                {
                    _keyboard.KeyDown(keyDown.Key);
                    context.MarkKeyDown(keyDown.Key);
                }
                break;

            case KeyUpStep keyUp:
                if (!string.IsNullOrWhiteSpace(keyUp.Key))
                {
                    _keyboard.KeyUp(keyUp.Key);
                    context.MarkKeyUp(keyUp.Key);
                }
                break;

            case MouseStep mouse:
                ExecuteMouseStep(mouse, context);
                break;

            case CommandStep command:
                var expandedCommand = context.Expand(command.CommandString);
                if (!string.IsNullOrWhiteSpace(expandedCommand))
                    await _commandService.ExecuteCommand(expandedCommand, ButtonTargets.None);
                break;

            case SetVariableStep setVariable:
                ExecuteSetVariable(setVariable, context);
                break;
        }
    }

    private static void ExecuteSetVariable(SetVariableStep step, MacroContext context)
    {
        if (string.IsNullOrWhiteSpace(step.Name))
            return;

        var name = step.Name.Trim();

        switch (step.Operation)
        {
            case VariableOperation.Set:
                context.Variables[name] = context.Expand(step.Value) ?? string.Empty;
                break;

            case VariableOperation.Increment:
            case VariableOperation.Decrement:
                var current = context.Variables.TryGetValue(name, out var raw)
                              && long.TryParse(raw, out var parsed)
                    ? parsed
                    : 0;

                var amountText = context.Expand(step.Value);
                var amount = long.TryParse(string.IsNullOrWhiteSpace(amountText) ? "1" : amountText, out var a)
                    ? a
                    : 1;

                var result = step.Operation == VariableOperation.Increment ? current + amount : current - amount;
                context.Variables[name] = result.ToString();
                break;
        }
    }

    private void ExecuteMouseStep(MouseStep step, MacroContext context)
    {
        switch (step.Action)
        {
            case MouseStepAction.Click:
                _mouse.Click(step.Button);
                break;
            case MouseStepAction.Down:
                _mouse.ButtonDown(step.Button);
                context.MarkButtonDown(step.Button);
                break;
            case MouseStepAction.Up:
                _mouse.ButtonUp(step.Button);
                context.MarkButtonUp(step.Button);
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
