using ProductManager.Api.Simulation;

namespace ProductManager.Api.Tests;

/// <summary>
/// PRD8 requirement 7: the holdout's predictive intervals. Expected values were computed independently
/// with Python's exact rational arithmetic (fractions.Fraction; Beta-Binomial pmf
/// C(n,k) B(k+a, n-k+b) / B(a,b) with integer shapes, negative binomial pmf C(k+alpha-1, k) p^alpha q^k with
/// p = beta / (beta + exposure)) and math.lgamma, not by re-running this code.
/// </summary>
public class HoldoutMathTests
{
    [Theory]
    [InlineData(0.5, 0.5723649429247004)] // ln sqrt(pi)
    [InlineData(1.0, 0.0)]
    [InlineData(2.0, 0.0)]
    [InlineData(3.7, 1.4280723266653885)]
    [InlineData(10.0, 12.801827480081467)] // ln 9!
    [InlineData(100.5, 361.4355404677776)]
    [InlineData(0.001, 6.907178885383854)]
    public void LogGamma_matches_reference_values(double x, double expected) =>
        Assert.Equal(expected, CalibrationReadiness.LogGamma(x), 10);

    [Theory]
    [InlineData(8, 7, 5, 2, 7)]   // the WinRate holdout of the API test: Beta(7,5) posterior, 8 held-out deals
    [InlineData(1, 1, 1, 0, 1)]   // n = 1: both outcomes have probability 1/2
    [InlineData(5, 1, 1, 0, 5)]   // uniform predictive over 0..5
    [InlineData(5, 50, 1, 4, 5)]  // near-certain success: the interval reaches all successes
    public void BetaBinomialInterval_matches_exact_quantiles(int n, double a, double b, int lower, int upper) =>
        Assert.Equal((lower, upper), CalibrationReadiness.BetaBinomialInterval(n, a, b, 0.90));

    [Fact]
    public void BetaBinomial_pmf_matches_exact_values_and_sums_to_one()
    {
        double[] expected =
        [
            0.006549178375803763, 0.03056283242041756, 0.07779630070651743, 0.14003334127173137,
            0.19449075176629357, 0.2139398269429229, 0.18337699452250536, 0.11351909184726522, 0.03973168214654283
        ];
        for (int k = 0; k <= 8; k++)
            Assert.Equal(expected[k], CalibrationReadiness.BetaBinomialPmf(8, k, 7, 5), 12);
        Assert.Equal(1.0, Enumerable.Range(0, 9).Sum(k => CalibrationReadiness.BetaBinomialPmf(8, k, 7, 5)), 12);
        Assert.Equal(1.0, Enumerable.Range(0, 2).Sum(k => CalibrationReadiness.BetaBinomialPmf(1, k, 0.5, 0.5)), 12);
    }

    [Fact]
    public void GammaPoisson_matches_exact_negative_binomial_values_and_interval()
    {
        // Gamma(56, 53) posterior, two held-out months (exposure 2).
        double[] expected =
        [
            0.1256429387806228, 0.2558547116987228, 0.2651585193968582, 0.18641447424263968,
            0.09998594527559765, 0.043630230665715336, 0.01612996406429476, 0.005195105309019611
        ];
        for (int k = 0; k < expected.Length; k++)
            Assert.Equal(expected[k], CalibrationReadiness.GammaPoissonPmf(k, 56, 53, 2), 12);
        Assert.Equal(1.0, Enumerable.Range(0, 200).Sum(k => CalibrationReadiness.GammaPoissonPmf(k, 56, 53, 2)), 12);
        Assert.Equal((0, 5), CalibrationReadiness.GammaPoissonInterval(56, 53, 2, 0.90));
    }
}
