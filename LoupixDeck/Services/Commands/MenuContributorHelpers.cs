namespace LoupixDeck.Services.Commands;

/// <summary>
/// Shared helpers for menu contributors that call into a possibly slow or
/// offline integration while building dynamic submenus.
/// </summary>
internal static class MenuContributorHelpers
{
    /// <summary>
    /// Awaits <paramref name="task"/> but throws <see cref="TimeoutException"/>
    /// once <paramref name="timeout"/> elapses, so an unreachable integration
    /// cannot block the settings dialog.
    /// </summary>
    public static async Task<T> WithTimeout<T>(Task<T> task, TimeSpan timeout)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout));
        if (completed != task)
            throw new TimeoutException($"Operation timed out after {timeout.TotalSeconds:0}s.");

        return await task;
    }
}
