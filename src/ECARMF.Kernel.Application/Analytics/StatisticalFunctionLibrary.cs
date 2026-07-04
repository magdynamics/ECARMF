namespace ECARMF.Kernel.Application.Analytics;

/// <summary>
/// Kernel math primitives — the one deliberate exception to "no domain logic
/// in the kernel": statistics are arithmetic, not business decisions.
/// Packages decide WHICH metric matters and WHAT threshold flags; rules and
/// forecasting capabilities call in here instead of re-deriving the math.
/// </summary>
public static class StatisticalFunctionLibrary
{
    public static decimal Mean(IReadOnlyList<decimal> values) =>
        values.Count == 0 ? 0 : values.Average();

    public static decimal Median(IReadOnlyList<decimal> values)
    {
        if (values.Count == 0) return 0;
        var sorted = values.Order().ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2m;
    }

    public static decimal Variance(IReadOnlyList<decimal> values)
    {
        if (values.Count < 2) return 0;
        var mean = Mean(values);
        return values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1);
    }

    public static decimal StandardDeviation(IReadOnlyList<decimal> values) =>
        (decimal)Math.Sqrt((double)Variance(values));

    public static IReadOnlyList<decimal> MovingAverage(IReadOnlyList<decimal> values, int window)
    {
        if (window <= 0 || values.Count < window) return [];
        var result = new List<decimal>();
        for (var i = window - 1; i < values.Count; i++)
        {
            result.Add(values.Skip(i - window + 1).Take(window).Average());
        }
        return result;
    }

    /// <summary>Least-squares fit over (0..n-1, values). Returns slope + intercept.</summary>
    public static (decimal Slope, decimal Intercept) LinearRegression(IReadOnlyList<decimal> values)
    {
        var n = values.Count;
        if (n < 2) return (0, n == 1 ? values[0] : 0);

        decimal sumX = 0, sumY = 0, sumXY = 0, sumXX = 0;
        for (var i = 0; i < n; i++)
        {
            sumX += i;
            sumY += values[i];
            sumXY += i * values[i];
            sumXX += (decimal)i * i;
        }

        var denominator = n * sumXX - sumX * sumX;
        if (denominator == 0) return (0, Mean(values));

        var slope = (n * sumXY - sumX * sumY) / denominator;
        return (slope, (sumY - slope * sumX) / n);
    }

    public static decimal ZScore(decimal value, IReadOnlyList<decimal> population)
    {
        var sd = StandardDeviation(population);
        return sd == 0 ? 0 : (value - Mean(population)) / sd;
    }

    /// <summary>95% confidence interval for the mean.</summary>
    public static (decimal Lower, decimal Upper) ConfidenceInterval95(IReadOnlyList<decimal> values)
    {
        if (values.Count == 0) return (0, 0);
        var mean = Mean(values);
        var margin = 1.96m * StandardDeviation(values) / (decimal)Math.Sqrt(values.Count);
        return (mean - margin, mean + margin);
    }
}

/// <summary>Financial arithmetic primitives (NPV like a spreadsheet's NPV()
/// — a function, not domain logic).</summary>
public static class FinancialFunctionLibrary
{
    /// <summary>NPV of cash flows at t=1..n (initial outlay excluded; pass it
    /// as a negative flow at index 0 of a combined series if desired).</summary>
    public static decimal Npv(decimal ratePerPeriod, IReadOnlyList<decimal> cashFlows)
    {
        decimal npv = 0;
        for (var t = 0; t < cashFlows.Count; t++)
        {
            npv += cashFlows[t] / (decimal)Math.Pow(1 + (double)ratePerPeriod, t + 1);
        }
        return npv;
    }

    /// <summary>IRR via bisection over [-0.99, 10]; cashFlows[0] is the initial
    /// (negative) outlay at t=0. Returns null when no sign change exists.</summary>
    public static decimal? Irr(IReadOnlyList<decimal> cashFlows, decimal tolerance = 0.0001m)
    {
        decimal NpvAt(decimal rate)
        {
            decimal total = 0;
            for (var t = 0; t < cashFlows.Count; t++)
            {
                total += cashFlows[t] / (decimal)Math.Pow(1 + (double)rate, t);
            }
            return total;
        }

        decimal low = -0.99m, high = 10m;
        var npvLow = NpvAt(low);
        if (npvLow * NpvAt(high) > 0) return null;

        for (var i = 0; i < 200; i++)
        {
            var mid = (low + high) / 2;
            var npvMid = NpvAt(mid);
            if (Math.Abs(npvMid) < tolerance) return mid;
            if (npvLow * npvMid < 0) high = mid; else { low = mid; npvLow = npvMid; }
        }

        return (low + high) / 2;
    }

    /// <summary>Periods until cumulative flows repay the initial investment
    /// (fractional within the recovering period); null if never recovered.</summary>
    public static decimal? PaybackPeriod(decimal initialInvestment, IReadOnlyList<decimal> cashFlows)
    {
        decimal cumulative = 0;
        for (var t = 0; t < cashFlows.Count; t++)
        {
            var previous = cumulative;
            cumulative += cashFlows[t];
            if (cumulative >= initialInvestment)
            {
                var needed = initialInvestment - previous;
                return t + (cashFlows[t] == 0 ? 0 : needed / cashFlows[t]);
            }
        }
        return null;
    }

    public static decimal Dscr(decimal noi, decimal debtService) =>
        debtService == 0 ? 0 : noi / debtService;

    public static decimal CapRate(decimal noi, decimal propertyValue) =>
        propertyValue == 0 ? 0 : noi / propertyValue;

    public static decimal Roi(decimal gain, decimal cost) =>
        cost == 0 ? 0 : (gain - cost) / cost;

    public static decimal Cagr(decimal beginValue, decimal endValue, decimal years) =>
        beginValue <= 0 || years <= 0 ? 0
            : (decimal)Math.Pow((double)(endValue / beginValue), 1.0 / (double)years) - 1m;
}
