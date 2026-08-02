using System.Globalization;
using System.Numerics;

namespace Krate.Core;

public static class Maths
{
    /// <summary>Combinatorics: "n k" gives factorials plus permutations P(n,k) and combinations
    /// C(n,k); one number gives n!. BigInteger keeps the results exact however large they get.</summary>
    public static string Combinatorics(string input)
    {
        var parts = input.Split([' ', ',', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || !int.TryParse(parts[0], out var n) || n < 0)
            throw new ArgumentException(Strings.Get("Error_NeedNumber"));
        if (n > 100_000) throw new ArgumentException(Strings.Get("Error_TooLarge"));

        if (parts.Length == 1)
        {
            if (n > 5000) throw new ArgumentException(Strings.Get("Error_TooLarge")); // 5000! already has ~16k digits
            return $"{n}! = {Factorial(n)}";
        }

        if (!int.TryParse(parts[1], out var k) || k < 0 || k > n)
            throw new ArgumentException(Strings.Get("Error_CombinatoricsUsage"));

        return string.Join('\n',
            $"C({n},{k}) = {Combinations(n, k)}",   // order does not matter
            $"P({n},{k}) = {Permutations(n, k)}");   // order matters
    }

    static BigInteger Factorial(int n)
    {
        BigInteger result = 1;
        for (var i = 2; i <= n; i++) result *= i;
        return result;
    }

    /// <summary>C(n,k) via the multiplicative formula — avoids ever building the full n!.</summary>
    public static BigInteger Combinations(int n, int k)
    {
        k = Math.Min(k, n - k); // C(n,k) == C(n,n-k); pick the cheaper side
        BigInteger result = 1;
        for (var i = 0; i < k; i++) result = result * (n - i) / (i + 1);
        return result;
    }

    public static BigInteger Permutations(int n, int k)
    {
        BigInteger result = 1;
        for (var i = 0; i < k; i++) result *= n - i;
        return result;
    }
    /// <summary>double.Parse throws a raw, untranslated FormatException that reached the user
    /// verbatim in every language — and every maths tool goes through here. TryParse instead.</summary>
    static double[] Numbers(string input)
    {
        var parts = input.Split([' ', ',', ';', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var values = new double[parts.Length];
        for (var i = 0; i < parts.Length; i++)
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
                throw new ArgumentException(Strings.Get("Error_NeedNumber"));
        return values.Length > 0 ? values : throw new ArgumentException(Strings.Get("Error_NeedNumber"));
    }

    static string Fmt(double v) => string.Create(CultureInfo.InvariantCulture, $"{v:0.##########}");

    /// <summary>"20 150" answers the three questions people actually mean by "percentage".</summary>
    public static string Percent(string input)
    {
        var n = Numbers(input);
        if (n.Length < 2) throw new ArgumentException(Strings.Get("Error_NeedTwoNumbers"));
        var (a, b) = (n[0], n[1]);
        return string.Join('\n',
            Strings.Get("Percent_Of", Fmt(a), Fmt(b), Fmt(a / 100 * b)),
            Strings.Get("Percent_Ratio", Fmt(a), Fmt(b), b == 0 ? "—" : Fmt(a / b * 100)),
            Strings.Get("Percent_Change", Fmt(a), Fmt(b), a == 0 ? "—" : Fmt((b - a) / Math.Abs(a) * 100)));
    }

    /// <summary>Decimal ↔ fraction, direction detected from the input.</summary>
    public static string Fraction(string input)
    {
        var s = input.Trim();
        if (s.Contains('/'))
        {
            var parts = s.Split('/', 2);
            if (!double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var num) ||
                !double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var den))
                throw new ArgumentException(Strings.Get("Error_NeedNumber"));
            if (den == 0) throw new ArgumentException(Strings.Get("Error_DivideByZero"));
            return Fmt(num / den);
        }

        if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            throw new ArgumentException(Strings.Get("Error_NeedNumber"));
        var (n, d) = ToFraction(value);
        var whole = n / d;
        return string.Join('\n',
            $"{n}/{d}",
            whole != 0 && Math.Abs(n) > d ? $"{whole} {Math.Abs(n % d)}/{d}" : $"{n}/{d}",
            Fmt(value));
    }

    /// <summary>Stern-Brocot search: finds the simplest fraction matching the value,
    /// so 0.333333 comes back as 1/3 rather than 333333/1000000.</summary>
    public static (long Numerator, long Denominator) ToFraction(double value, double tolerance = 1e-9)
    {
        var sign = value < 0 ? -1 : 1;
        value = Math.Abs(value);
        long lowN = 0, lowD = 1, highN = 1, highD = 0;
        for (var i = 0; i < 10_000; i++)
        {
            var midN = lowN + highN;
            var midD = lowD + highD;
            if (midD == 0) break;
            var mid = (double)midN / midD;
            if (Math.Abs(mid - value) < tolerance) return (sign * midN, midD);
            if (mid < value) (lowN, lowD) = (midN, midD); else (highN, highD) = (midN, midD);
        }
        return (sign * (long)Math.Round(value * 1_000_000), 1_000_000);
    }

    /// <summary>GCD, LCM and prime factors of the numbers given.</summary>
    public static string Factor(string input)
    {
        var values = Numbers(input).Select(v => (long)Math.Abs(Math.Round(v))).Where(v => v > 0).ToArray();
        if (values.Length == 0) throw new ArgumentException(Strings.Get("Error_NeedNumber"));

        var lines = values.Select(v =>
        {
            var factors = PrimeFactors(v).ToArray();
            var isPrime = factors.Length == 1 && v > 1;
            return $"{v} = {(v == 1 ? "1" : string.Join(" × ", factors))}{(isPrime ? "  " + Strings.Get("Math_Prime") : "")}";
        }).ToList();

        if (values.Length > 1)
        {
            lines.Add(Strings.Get("Math_Gcd", values.Aggregate(Gcd)));
            lines.Add(Strings.Get("Math_Lcm", values.Aggregate(Lcm)));
        }
        return string.Join('\n', lines);
    }

    public static IEnumerable<long> PrimeFactors(long n)
    {
        for (long p = 2; p * p <= n; p += p == 2 ? 1 : 2) // trial division, 2 then odds only
            while (n % p == 0) { yield return p; n /= p; }
        if (n > 1) yield return n;
    }

    public static long Gcd(long a, long b) { while (b != 0) (a, b) = (b, a % b); return a; }
    public static long Lcm(long a, long b) => a / Gcd(a, b) * b;

    /// <summary>Everything you'd want about a list of numbers, in one pass of reading.</summary>
    public static string Statistics(string input)
    {
        var values = Numbers(input).Order().ToArray();
        var mean = values.Average();
        // Sample standard deviation (n-1): a pasted list is nearly always a sample, not a population.
        var variance = values.Length > 1 ? values.Sum(v => (v - mean) * (v - mean)) / (values.Length - 1) : 0;
        var median = values.Length % 2 == 1
            ? values[values.Length / 2]
            : (values[values.Length / 2 - 1] + values[values.Length / 2]) / 2;

        return string.Join('\n',
            $"COUNT   {values.Length}",
            $"SUM     {Fmt(values.Sum())}",
            $"MEAN    {Fmt(mean)}",
            $"MEDIAN  {Fmt(median)}",
            $"MIN     {Fmt(values[0])}",
            $"MAX     {Fmt(values[^1])}",
            $"RANGE   {Fmt(values[^1] - values[0])}",
            $"STDDEV  {Fmt(Math.Sqrt(variance))}");
    }

    /// <summary>"fib 10", "arith 2 3 10" (start, step, count), "geom 2 3 10" (start, ratio, count).</summary>
    public static string Sequence(string input)
    {
        var parts = input.Trim().Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) throw new ArgumentException(Strings.Get("Error_SequenceUsage"));
        var kind = parts[0].ToLowerInvariant();
        var n = parts.Length > 1 ? Numbers(string.Join(' ', parts[1..])) : [];

        int Count(int index, int fallback) => n.Length > index ? Math.Clamp((int)n[index], 1, 1000) : fallback;

        return kind switch
        {
            "fib" or "fibonacci" => string.Join(", ", Fibonacci(Count(0, 20))),
            "arith" or "arithmetic" when n.Length >= 2 =>
                string.Join(", ", Enumerable.Range(0, Count(2, 10)).Select(i => Fmt(n[0] + i * n[1]))),
            "geom" or "geometric" when n.Length >= 2 =>
                string.Join(", ", Enumerable.Range(0, Count(2, 10)).Select(i => Fmt(n[0] * Math.Pow(n[1], i)))),
            "prime" or "primes" => string.Join(", ", Primes(Count(0, 20))),
            _ => throw new ArgumentException(Strings.Get("Error_SequenceUsage")),
        };
    }

    public static IEnumerable<long> Fibonacci(int count)
    {
        (long a, long b) = (0, 1);
        for (var i = 0; i < count; i++) { yield return a; (a, b) = (b, a + b); }
    }

    public static IEnumerable<long> Primes(int count)
    {
        var found = 0;
        for (long n = 2; found < count; n++)
            if (PrimeFactors(n).Take(2).Count() == 1) { found++; yield return n; }
    }

    /// <summary>Solves ax² + bx + c = 0, or ax + b = 0 when a is 0. Input: the coefficients.</summary>
    public static string Solve(string input)
    {
        var c = Numbers(input);
        if (c.Length == 2) return c[0] == 0 ? Strings.Get("Math_NoSolution") : $"x = {Fmt(-c[1] / c[0])}";
        if (c.Length != 3) throw new ArgumentException(Strings.Get("Error_SolveUsage"));
        if (c[0] == 0) return c[1] == 0 ? Strings.Get("Math_NoSolution") : $"x = {Fmt(-c[2] / c[1])}";

        var delta = c[1] * c[1] - 4 * c[0] * c[2];
        var lines = new List<string> { $"Δ = {Fmt(delta)}" };
        if (delta > 0)
        {
            lines.Add($"x₁ = {Fmt((-c[1] - Math.Sqrt(delta)) / (2 * c[0]))}");
            lines.Add($"x₂ = {Fmt((-c[1] + Math.Sqrt(delta)) / (2 * c[0]))}");
        }
        else if (delta == 0) lines.Add($"x = {Fmt(-c[1] / (2 * c[0]))}");
        else
        {
            var (re, im) = (-c[1] / (2 * c[0]), Math.Sqrt(-delta) / (2 * c[0]));
            lines.Add(Strings.Get("Math_ComplexRoots"));
            lines.Add($"x₁ = {Fmt(re)} - {Fmt(im)}i");
            lines.Add($"x₂ = {Fmt(re)} + {Fmt(im)}i");
        }
        return string.Join('\n', lines);
    }
}
