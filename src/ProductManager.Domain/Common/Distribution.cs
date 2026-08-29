namespace ProductManager.Domain.Common;

/// <summary>
/// A parameter distribution: a type plus two shape numbers.
/// Point(value) is the zero-width special case. Immutable value object.
/// </summary>
public sealed record Distribution
{
    public DistributionType Type { get; }

    /// <summary>Point: value. Normal: mean. Lognormal: mu (log-scale). Beta: alpha.</summary>
    public double ParamA { get; }

    /// <summary>Point: unused (0). Normal: std dev. Lognormal: sigma. Beta: beta.</summary>
    public double ParamB { get; }

    private Distribution() { } // EF

    public Distribution(DistributionType type, double paramA, double paramB)
    {
        switch (type)
        {
            case DistributionType.Point:
                if (paramB != 0)
                    throw new ArgumentException("A point distribution has no second parameter (ParamB must be 0).");
                break;
            case DistributionType.Normal:
            case DistributionType.Lognormal:
                if (paramB < 0)
                    throw new ArgumentException($"{type} requires a non-negative scale parameter (ParamB).");
                break;
            case DistributionType.Beta:
                if (paramA <= 0 || paramB <= 0)
                    throw new ArgumentException("Beta requires strictly positive alpha (ParamA) and beta (ParamB).");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type));
        }

        Type = type;
        ParamA = paramA;
        ParamB = paramB;
    }

    public static Distribution PointValue(double value) => new(DistributionType.Point, value, 0);
}
