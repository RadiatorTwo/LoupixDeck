namespace LoupixDeck.Utils;

internal sealed class EpsilonComparer(double epsilon) : IEqualityComparer<double>
{
    private const double DefaultEpsilon = 0.0001;
    public static EpsilonComparer Default { get; } = new(DefaultEpsilon);

    private readonly double epsilon = epsilon;

    public bool Equals(double x, double y) => Math.Abs(x - y) < epsilon;
    public int GetHashCode(double obj) => obj.GetHashCode();
}
