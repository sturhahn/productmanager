namespace ProductManager.Engine;

/// <summary>
/// Deterministic RNG owned by the engine so results are byte-identical across .NET versions
/// (System.Random makes no cross-version guarantee). xoshiro256** seeded via SplitMix64 from
/// (seed, pathIndex), so each MC path has a stable, independent stream regardless of how many
/// paths a run requests.
/// </summary>
internal sealed class EngineRandom
{
    private ulong _s0, _s1, _s2, _s3;
    private double _cachedGaussian;
    private bool _hasCachedGaussian;

    internal EngineRandom(int seed, int pathIndex)
    {
        // Stable per-(seed, path) derivation: mix both into a SplitMix64 stream.
        ulong x = unchecked((ulong)seed * 0x9E3779B97F4A7C15UL
                            + (ulong)(uint)pathIndex * 0xBF58476D1CE4E5B9UL
                            + 0x94D049BB133111EBUL);
        _s0 = SplitMix64(ref x);
        _s1 = SplitMix64(ref x);
        _s2 = SplitMix64(ref x);
        _s3 = SplitMix64(ref x);
        if ((_s0 | _s1 | _s2 | _s3) == 0) _s0 = 1; // xoshiro must not be all-zero
    }

    private static ulong SplitMix64(ref ulong state)
    {
        ulong z = state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    private static ulong RotL(ulong x, int k) => (x << k) | (x >> (64 - k));

    private ulong NextUInt64()
    {
        ulong result = RotL(_s1 * 5, 7) * 9;
        ulong t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = RotL(_s3, 45);
        return result;
    }

    /// <summary>Uniform in (0, 1) — never exactly 0, safe for log().</summary>
    internal double NextUniform()
    {
        // 53-bit mantissa; add half-ulp offset to exclude zero.
        return ((NextUInt64() >> 11) + 0.5) * (1.0 / 9007199254740992.0);
    }

    /// <summary>Standard normal via Box-Muller (pair-cached).</summary>
    internal double NextGaussian()
    {
        if (_hasCachedGaussian)
        {
            _hasCachedGaussian = false;
            return _cachedGaussian;
        }

        double u1 = NextUniform();
        double u2 = NextUniform();
        double r = Math.Sqrt(-2.0 * Math.Log(u1));
        double theta = 2.0 * Math.PI * u2;
        _cachedGaussian = r * Math.Sin(theta);
        _hasCachedGaussian = true;
        return r * Math.Cos(theta);
    }

    /// <summary>Gamma(shape, scale=1) via Marsaglia-Tsang, with the alpha&lt;1 boost.</summary>
    internal double NextGamma(double shape)
    {
        if (shape < 1.0)
        {
            double u = NextUniform();
            return NextGamma(shape + 1.0) * Math.Pow(u, 1.0 / shape);
        }

        double d = shape - 1.0 / 3.0;
        double c = 1.0 / Math.Sqrt(9.0 * d);
        while (true)
        {
            double x, v;
            do
            {
                x = NextGaussian();
                v = 1.0 + c * x;
            } while (v <= 0.0);

            v = v * v * v;
            double u2 = NextUniform();
            double x2 = x * x;
            if (u2 < 1.0 - 0.0331 * x2 * x2) return d * v;
            if (Math.Log(u2) < 0.5 * x2 + d * (1.0 - v + Math.Log(v))) return d * v;
        }
    }

    /// <summary>Beta(alpha, beta) via two gammas.</summary>
    internal double NextBeta(double alpha, double beta)
    {
        double x = NextGamma(alpha);
        double y = NextGamma(beta);
        double sum = x + y;
        return sum <= 0.0 ? 0.0 : x / sum;
    }

    /// <summary>Poisson(lambda): Knuth for small lambda, clamped normal approximation for large.</summary>
    internal long NextPoisson(double lambda)
    {
        if (lambda <= 0.0) return 0;
        if (lambda < 30.0)
        {
            double l = Math.Exp(-lambda);
            long k = 0;
            double p = 1.0;
            do
            {
                k++;
                p *= NextUniform();
            } while (p > l);
            return k - 1;
        }

        double sample = lambda + Math.Sqrt(lambda) * NextGaussian();
        return sample <= 0.0 ? 0 : (long)Math.Round(sample);
    }

    /// <summary>Binomial(n, p): exact Bernoulli/inversion for small cases, clamped normal approximation for large np.</summary>
    internal long NextBinomial(long n, double p)
    {
        if (n <= 0 || p <= 0.0) return 0;
        if (p >= 1.0) return n;

        // Sample the rarer outcome for efficiency.
        if (p > 0.5) return n - NextBinomial(n, 1.0 - p);

        double np = n * p;
        if (n <= 30)
        {
            long count = 0;
            for (long i = 0; i < n; i++)
                if (NextUniform() < p) count++;
            return count;
        }

        if (np < 30.0)
        {
            // Inversion by CDF walk; expected iterations ~ np (< 30 here).
            double q = 1.0 - p;
            double s = p / q;
            double f = Math.Pow(q, n);
            double u = NextUniform();
            double cdf = f;
            long k = 0;
            while (u > cdf && k < n)
            {
                k++;
                f *= s * (n - k + 1) / k;
                cdf += f;
                if (f <= 0.0) break; // numeric underflow guard
            }
            return k;
        }

        double mean = np;
        double sd = Math.Sqrt(np * (1.0 - p));
        double sample = mean + sd * NextGaussian();
        if (sample <= 0.0) return 0;
        long result = (long)Math.Round(sample);
        return result > n ? n : result;
    }

    /// <summary>Sample one value from a distribution spec (unclamped; callers clamp per use).</summary>
    internal double Sample(DistributionSpec spec) => spec.Type switch
    {
        DistributionType.Point => spec.P1,
        DistributionType.Normal => spec.P1 + spec.P2 * NextGaussian(),
        DistributionType.LogNormal => Math.Exp(spec.P1 + spec.P2 * NextGaussian()),
        DistributionType.Beta => NextBeta(spec.P1, spec.P2),
        _ => throw new ArgumentOutOfRangeException(nameof(spec), $"Unknown distribution type {spec.Type}"),
    };
}
