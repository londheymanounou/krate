using System.Globalization;

namespace Krate.Core;

/// <summary>Windows Calculator's Programmer mode: 64-bit integer maths in HEX/DEC/OCT/BIN with the
/// bitwise operators, all four bases shown at once. Immediate-execution like the others. Pure/testable.</summary>
public sealed class ProgrammerCalculator
{
    public int Base { get; private set; } = 10;   // the base the user is currently typing in
    public long Value { get; private set; }

    long _accumulator;
    string? _pending;
    bool _fresh = true;

    public string Hex => Convert.ToString(Value, 16).ToUpperInvariant();
    public string Dec => Value.ToString(CultureInfo.InvariantCulture);
    public string Oct => Convert.ToString(Value, 8);
    public string Bin => Value == 0 ? "0" : Convert.ToString(Value, 2);

    /// <summary>The current value formatted in the active base — what the main display shows.</summary>
    public string Display => Base switch { 16 => Hex, 8 => Oct, 2 => Bin, _ => Dec };

    static readonly Dictionary<int, string> Digits = new()
    {
        [2] = "01", [8] = "01234567", [10] = "0123456789", [16] = "0123456789ABCDEF",
    };

    public void SetBase(int b) { Base = b; _fresh = true; } // switching base never changes the value, only its view

    public void Input(string key)
    {
        switch (key)
        {
            case "C": Value = _accumulator = 0; _pending = null; _fresh = true; break;
            case "back": Value /= Base; _fresh = false; break;

            case "and" or "or" or "xor" or "lsh" or "rsh" or "+" or "-" or "*" or "/": Operator(key); break;
            case "=": Equals(); break;
            case "not": Value = ~Value; _fresh = true; break;

            default:
                // A digit valid in the current base extends the entry (shifting left by the base).
                if (key.Length == 1 && Digits[Base].Contains(char.ToUpperInvariant(key[0])))
                {
                    var d = Convert.ToInt64(key, 16); // hex parse covers 0-9 and A-F
                    Value = _fresh ? d : Value * Base + d;
                    _fresh = false;
                }
                break;
        }
    }

    void Operator(string op)
    {
        if (_pending is not null && !_fresh) Compute();
        else _accumulator = Value;
        _pending = op;
        _fresh = true;
    }

    void Equals() { if (_pending is not null) { Compute(); _pending = null; _fresh = true; } }

    void Compute()
    {
        var r = Value;
        _accumulator = _pending switch
        {
            "and" => _accumulator & r,
            "or" => _accumulator | r,
            "xor" => _accumulator ^ r,
            "lsh" => _accumulator << (int)(r & 63),
            "rsh" => _accumulator >> (int)(r & 63),
            "+" => _accumulator + r,
            "-" => _accumulator - r,
            "*" => _accumulator * r,
            "/" => r == 0 ? _accumulator : _accumulator / r, // integer calc quietly ignores /0, like Windows shows 0
            _ => r,
        };
        Value = _accumulator;
    }
}
