namespace LoupixDeck.Models;

/// <summary>
/// An equality comparer for doubles that considers two values equal if they are within a specified epsilon of each other.
/// </summary>
/// <param name="epsilon">The minumum difference in two values to be considered "different"</param>
public sealed class EpsilonComparer(double epsilon) : IEqualityComparer<double>
{
    private const double DefaultEpsilon = 0.0001;
    public static EpsilonComparer Default { get; } = new(DefaultEpsilon);

    private readonly double epsilon = epsilon;

    public bool Equals(double x, double y) => Math.Abs(x - y) < epsilon;
    public int GetHashCode(double obj) => obj.GetHashCode();
}