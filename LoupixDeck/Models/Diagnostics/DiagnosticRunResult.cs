namespace LoupixDeck.Models.Diagnostics;

/// <summary>
/// The outcome of one diagnostics run. <see cref="Results"/> is in registration order, so the
/// UI and the report stay identical from run to run.
/// </summary>
/// <param name="CompletedAt">When the run finished.</param>
/// <param name="WasCancelled">True when the user stopped the run before every check had finished.</param>
/// <param name="Results">The individual check results, in registration order.</param>
public sealed record DiagnosticRunResult(
    DateTimeOffset CompletedAt,
    bool WasCancelled,
    IReadOnlyList<DiagnosticCheckResult> Results);
