using LoupixDeck.Models;

namespace LoupixDeck.ViewModels;

/// <summary>One entry of the Profile Rules "when leaving matched apps" dropdown.</summary>
public sealed record NoMatchBehaviorOption(NoMatchProfileBehavior Value, string Label);
