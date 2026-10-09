namespace LoupixDeck.Services.SystemPower;

/// <summary>Fallback for platforms without a display-state source.</summary>
public sealed class NoOpDisplayStateService : DisplayStateServiceBase
{
    protected override void Start() { }
}
