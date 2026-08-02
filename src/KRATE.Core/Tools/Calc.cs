using System.Globalization;

namespace Krate.Core;

/// <summary>A scientific calculator: evaluates a math expression with the usual precedence,
/// parentheses, functions and constants. Hand-written recursive descent — small, exact, and no
/// dependency (and no reflection, so it stays AOT-safe).</summary>
public static class Calc
{
    public static string Evaluate(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) throw new ArgumentException(Strings.Get("Error_NeedExpression"));
        var parser = new Parser(input);
        var value = parser.ParseExpression();
        parser.ExpectEnd();
        return Format(value);
    }

    /// <summary>Evaluates an expression with the variable <c>x</c> bound to a value — for graphing.</summary>
    public static double EvaluateAt(string input, double x)
    {
        var parser = new Parser(input, x);
        var value = parser.ParseExpression();
        parser.ExpectEnd();
        return value;
    }

    /// <summary>Samples y = f(x) across [xMin, xMax]. Points where f is undefined (NaN/∞, e.g. 1/x
    /// at 0) come back as NaN so the caller can break the line there rather than draw through it.</summary>
    public static (double X, double Y)[] Plot(string expr, double xMin, double xMax, int samples)
    {
        if (string.IsNullOrWhiteSpace(expr)) throw new ArgumentException(Strings.Get("Error_NeedExpression"));
        if (samples < 2 || xMax <= xMin) throw new ArgumentException(Strings.Get("Error_BadExpression"));
        var points = new (double, double)[samples];
        for (var i = 0; i < samples; i++)
        {
            var x = xMin + (xMax - xMin) * i / (samples - 1);
            double y;
            // NaN or ∞ (e.g. 1/x at 0, which is a double Infinity, not an exception) both mean
            // "no point here" — normalise to NaN so the caller breaks the line.
            try { y = EvaluateAt(expr, x); if (double.IsInfinity(y)) y = double.NaN; } catch { y = double.NaN; }
            points[i] = (x, y);
        }
        return points;
    }

    static string Format(double v)
    {
        if (double.IsNaN(v)) throw new ArgumentException(Strings.Get("Error_Undefined"));
        if (double.IsInfinity(v)) throw new ArgumentException(Strings.Get("Error_Overflow"));
        // Round-trip "G15" avoids showing 0.30000000000000004 while keeping real precision.
        return v.ToString("G15", CultureInfo.InvariantCulture);
    }

    // Grammar (lowest to highest precedence):
    //   expression → term (('+' | '-') term)*
    //   term       → power (('*' | '/' | '%') power)*
    //   power      → unary ('^' power)?          right-associative
    //   unary      → ('-' | '+')? postfix
    //   postfix    → atom '!'?                    factorial
    //   atom       → number | constant | func '(' expression ')' | '(' expression ')'
    sealed class Parser(string text, double? x = null)
    {
        int _pos;

        public double ParseExpression()
        {
            var value = ParseTerm();
            while (true)
            {
                Skip();
                if (Match('+')) value += ParseTerm();
                else if (Match('-')) value -= ParseTerm();
                else return value;
            }
        }

        double ParseTerm()
        {
            var value = ParseUnary();
            while (true)
            {
                Skip();
                if (Match('*')) value *= ParseUnary();
                else if (Match('/')) value /= ParseUnary();
                else if (Match('%')) value %= ParseUnary();
                else return value;
            }
        }

        // Unary sits looser than '^', so -2^2 parses as -(2^2) = -4, the mathematical convention.
        double ParseUnary()
        {
            Skip();
            if (Match('-')) return -ParseUnary();
            if (Match('+')) return ParseUnary();
            return ParsePower();
        }

        double ParsePower()
        {
            var b = ParsePostfix();
            Skip();
            // Right-associative (2^3^2 = 512); the exponent may itself be signed (2^-3).
            return Match('^') ? Math.Pow(b, ParseUnary()) : b;
        }

        double ParsePostfix()
        {
            var value = ParseAtom();
            Skip();
            if (Match('!')) return Factorial(value);
            return value;
        }

        double ParseAtom()
        {
            Skip();
            if (Match('('))
            {
                var value = ParseExpression();
                Skip();
                if (!Match(')')) throw Error("Error_MissingParen");
                return value;
            }

            if (char.IsLetter(Peek()))
            {
                var name = ReadName();
                Skip();
                if (Match('(')) // function call
                {
                    var arg = ParseExpression();
                    Skip();
                    if (!Match(')')) throw Error("Error_MissingParen");
                    return Apply(name, arg);
                }
                return Constant(name);
            }

            return ReadNumber();
        }

        double ReadNumber()
        {
            var start = _pos;
            while (_pos < text.Length && (char.IsDigit(text[_pos]) || text[_pos] is '.' or 'e' or 'E'
                   || (text[_pos] is '+' or '-' && _pos > start && text[_pos - 1] is 'e' or 'E'))) _pos++;
            if (_pos == start) throw Error("Error_BadExpression");
            return double.Parse(text[start.._pos], NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        string ReadName()
        {
            var start = _pos;
            while (_pos < text.Length && char.IsLetterOrDigit(text[_pos])) _pos++;
            return text[start.._pos].ToLowerInvariant();
        }

        static double Apply(string fn, double x) => fn switch
        {
            "sqrt" => Math.Sqrt(x),
            "cbrt" => Math.Cbrt(x),
            "abs" => Math.Abs(x),
            "sin" => Math.Sin(x),
            "cos" => Math.Cos(x),
            "tan" => Math.Tan(x),
            "asin" => Math.Asin(x),
            "acos" => Math.Acos(x),
            "atan" => Math.Atan(x),
            "sinh" => Math.Sinh(x),
            "cosh" => Math.Cosh(x),
            "tanh" => Math.Tanh(x),
            "ln" => Math.Log(x),
            "log" => Math.Log10(x),
            "log2" => Math.Log2(x),
            "exp" => Math.Exp(x),
            "floor" => Math.Floor(x),
            "ceil" => Math.Ceiling(x),
            "round" => Math.Round(x),
            "sign" => Math.Sign(x),
            "deg" => x * 180 / Math.PI,   // radians → degrees
            "rad" => x * Math.PI / 180,   // degrees → radians
            _ => throw new ArgumentException(Strings.Get("Error_UnknownFunction", fn)),
        };

        // Instance (not static) so "x" can resolve to the graphing variable when one is bound.
        double Constant(string name) => name switch
        {
            "x" when x is { } value => value,
            "pi" => Math.PI,
            "e" => Math.E,
            "tau" => Math.Tau,
            "phi" => 1.618033988749895,
            _ => throw new ArgumentException(Strings.Get("Error_UnknownName", name)),
        };

        static double Factorial(double n)
        {
            if (n < 0 || n != Math.Floor(n) || n > 170) throw new ArgumentException(Strings.Get("Error_BadFactorial"));
            var result = 1.0;
            for (var i = 2; i <= n; i++) result *= i;
            return result;
        }

        public void ExpectEnd() { Skip(); if (_pos < text.Length) throw Error("Error_BadExpression"); }

        char Peek() => _pos < text.Length ? text[_pos] : '\0';
        void Skip() { while (_pos < text.Length && char.IsWhiteSpace(text[_pos])) _pos++; }
        bool Match(char c) { if (Peek() == c) { _pos++; return true; } return false; }
        static ArgumentException Error(string key) => new(Strings.Get(key));
    }
}
