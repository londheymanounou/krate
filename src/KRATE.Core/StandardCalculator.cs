using System.Globalization;

namespace Krate.Core;

/// <summary>Immediate-execution calculator matching Windows Calculator's Standard mode, plus the
/// Scientific functions (trig with DEG/RAD/GRAD, logs, powers, constants, factorial) and a running
/// history. Modelled after that app's UX — it's C++/UWP, so this is a faithful reimplementation, not
/// its code. Pure and testable; the GUI page is just buttons over this.</summary>
public sealed class StandardCalculator
{
    public enum Angle { Deg, Rad, Grad }

    public string Display { get; private set; } = "0";
    public string Expression { get; private set; } = ""; // the faint line above, e.g. "5 + " or "5 + 3 ="
    public double Memory { get; private set; }
    public Angle AngleMode { get; set; } = Angle.Deg;
    public List<string> History { get; } = [];           // "5 + 3 = 8", newest last

    double _accumulator;
    string? _pending;
    bool _fresh = true;
    bool _justEquated;
    bool _error;

    static string Symbol(string op) => op switch { "+" => "+", "-" => "−", "*" => "×", "/" => "÷", "^" => "^", "mod" => " mod ", _ => op };
    static double Val(string s) => double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0;

    static string Fmt(double d) =>
        double.IsNaN(d) || double.IsInfinity(d) ? "∞" : d.ToString("G15", CultureInfo.InvariantCulture);

    public void Input(string key)
    {
        if (_error && key != "C") return;

        switch (key)
        {
            case "C": Reset(); break;
            case "CE": Display = "0"; _fresh = true; break;
            case "back":
                if (_fresh) break;
                Display = Display.Length > 1 ? Display[..^1] : "0";
                if (Display is "-" or "0") { Display = "0"; _fresh = true; }
                break;

            case "+" or "-" or "*" or "/" or "^" or "mod": Operator(key); break;
            case "=": Equals(); break;

            case "+/-": if (Display != "0") Display = Display.StartsWith('-') ? Display[1..] : "-" + Display; break;
            case "%": Unary(v => _pending is null ? v / 100 : _accumulator * v / 100); break;
            case "1/x": Unary(v => v == 0 ? throw new DivideByZeroException() : 1 / v); break;
            case "sqr": Unary(v => v * v); break;
            case "sqrt": Unary(Math.Sqrt); break;

            case "pi": Constant(Math.PI); break;
            case "e": Constant(Math.E); break;
            case "sin" or "cos" or "tan" or "asin" or "acos" or "atan"
                 or "log" or "ln" or "exp" or "tenx" or "abs" or "fact" or "cbrt": Science(key); break;

            case "MS": Memory = Val(Display); _fresh = true; break;
            case "MC": Memory = 0; break;
            case "MR": Display = Fmt(Memory); _fresh = true; break;
            case "M+": Memory += Val(Display); break;
            case "M-": Memory -= Val(Display); break;

            case ".": Dot(); break;
            default: if (key.Length == 1 && char.IsDigit(key[0])) Digit(key); break;
        }
    }

    void Digit(string d)
    {
        if (_justEquated) { Expression = ""; _justEquated = false; }
        if (_fresh || Display == "0") { Display = d; _fresh = false; }
        else if (Display == "-0") Display = "-" + d;
        else Display += d;
    }

    void Dot()
    {
        if (_fresh) { Display = "0."; _fresh = false; }
        else if (!Display.Contains('.')) Display += ".";
    }

    void Constant(double v) { Display = Fmt(v); _fresh = true; }

    void Operator(string op)
    {
        if (_pending is not null && !_fresh) Compute();
        else _accumulator = Val(Display);
        if (_error) return;
        _pending = op;
        _fresh = true;
        _justEquated = false;
        Expression = $"{Fmt(_accumulator)} {Symbol(op)}";
    }

    void Equals()
    {
        if (_pending is null) return;
        Expression = $"{Fmt(_accumulator)} {Symbol(_pending)} {Display} =";
        Compute();
        if (!_error) History.Add($"{Expression} {Display}");
        _pending = null;
        _fresh = true;
        _justEquated = true;
    }

    void Compute()
    {
        var right = Val(Display);
        try
        {
            _accumulator = _pending switch
            {
                "+" => _accumulator + right,
                "-" => _accumulator - right,
                "*" => _accumulator * right,
                "/" => right == 0 ? throw new DivideByZeroException() : _accumulator / right,
                "^" => Math.Pow(_accumulator, right),
                "mod" => right == 0 ? throw new DivideByZeroException() : _accumulator % right,
                _ => right,
            };
            Display = Fmt(_accumulator);
        }
        catch (DivideByZeroException) { Fail("Calc_DivZero"); }
    }

    void Science(string fn)
    {
        double ToRad(double v) => AngleMode switch { Angle.Deg => v * Math.PI / 180, Angle.Grad => v * Math.PI / 200, _ => v };
        double FromRad(double v) => AngleMode switch { Angle.Deg => v * 180 / Math.PI, Angle.Grad => v * 200 / Math.PI, _ => v };
        Unary(fn switch
        {
            "sin" => v => Math.Sin(ToRad(v)),
            "cos" => v => Math.Cos(ToRad(v)),
            "tan" => v => Math.Tan(ToRad(v)),
            "asin" => v => FromRad(Math.Asin(v)),
            "acos" => v => FromRad(Math.Acos(v)),
            "atan" => v => FromRad(Math.Atan(v)),
            "log" => Math.Log10,
            "ln" => Math.Log,
            "exp" => Math.Exp,
            "tenx" => v => Math.Pow(10, v),
            "abs" => Math.Abs,
            "cbrt" => Math.Cbrt,
            "fact" => Factorial,
            _ => v => v,
        });
    }

    static double Factorial(double n)
    {
        if (n < 0 || n != Math.Floor(n) || n > 170) throw new ArgumentException("factorial");
        var r = 1.0;
        for (var i = 2; i <= n; i++) r *= i;
        return r;
    }

    void Unary(Func<double, double> f)
    {
        try
        {
            var r = f(Val(Display));
            if (double.IsNaN(r) || double.IsInfinity(r)) { Fail("Calc_Error"); return; }
            Display = Fmt(r);
            _fresh = true;
        }
        catch (DivideByZeroException) { Fail("Calc_DivZero"); }
        catch (Exception) { Fail("Calc_Error"); }
    }

    void Fail(string key) { Display = Strings.Get(key); _error = true; }

    void Reset() { Display = "0"; Expression = ""; _accumulator = 0; _pending = null; _fresh = true; _justEquated = false; _error = false; }
}
