//! A scientific calculator. Mirrors `Krate.Core.Calc`: hand-written recursive descent, no
//! dependency, exact same grammar and precedence.
//!
//! Grammar (lowest to highest precedence):
//!   expression → term (('+' | '-') term)*
//!   term       → power (('*' | '/' | '%') power)*
//!   power      → unary ('^' power)?          right-associative
//!   unary      → ('-' | '+')? postfix
//!   postfix    → atom '!'?                    factorial
//!   atom       → number | constant | func '(' expression ')' | '(' expression ')'

use crate::i18n;
use crate::tools::round_half_even;

/// Renders like .NET's `ToString("G15")`: 15 significant digits, then fixed-point notation when
/// the exponent is in (-5, 15) and scientific otherwise, with a two-digit signed exponent.
/// Measured against the C# build — `1e-5` is scientific but `1e-4` is not, and the cut-off at
/// the top is `1e15`.
fn format_g15(v: f64) -> String {
    if v == 0.0 {
        return "0".to_string();
    }
    let scientific = format!("{v:.14e}");
    let (mantissa, exponent) = scientific.split_once('e').expect("Rust always emits an exponent");
    let exponent: i32 = exponent.parse().expect("exponent is an integer");

    if exponent > -5 && exponent < 15 {
        let decimals = (14 - exponent).max(0) as usize;
        let mut s = format!("{v:.decimals$}");
        if s.contains('.') {
            s = s.trim_end_matches('0').trim_end_matches('.').to_string();
        }
        s
    } else {
        let m = mantissa.trim_end_matches('0').trim_end_matches('.');
        let sign = if exponent < 0 { '-' } else { '+' };
        format!("{m}E{sign}{:02}", exponent.abs())
    }
}

fn err(key: &str) -> String {
    i18n::get(key).to_string()
}

pub fn evaluate(input: &str) -> Result<String, String> {
    if input.trim().is_empty() {
        return Err(err("Error_NeedExpression"));
    }
    let value = evaluate_at(input, None)?;
    if value.is_nan() {
        return Err(err("Error_Undefined"));
    }
    if value.is_infinite() {
        return Err(err("Error_Overflow"));
    }
    Ok(format_g15(value))
}

/// Evaluates with the variable `x` optionally bound — used by the graphing page.
pub fn evaluate_at(input: &str, x: Option<f64>) -> Result<f64, String> {
    let mut parser = Parser { text: input.chars().collect(), pos: 0, x };
    let value = parser.expression()?;
    parser.expect_end()?;
    Ok(value)
}

struct Parser {
    text: Vec<char>,
    pos: usize,
    x: Option<f64>,
}

impl Parser {
    fn peek(&self) -> char {
        self.text.get(self.pos).copied().unwrap_or('\0')
    }

    fn skip(&mut self) {
        while self.pos < self.text.len() && self.text[self.pos].is_whitespace() {
            self.pos += 1;
        }
    }

    fn matches(&mut self, c: char) -> bool {
        if self.peek() == c {
            self.pos += 1;
            true
        } else {
            false
        }
    }

    fn expect_end(&mut self) -> Result<(), String> {
        self.skip();
        if self.pos < self.text.len() {
            return Err(err("Error_BadExpression"));
        }
        Ok(())
    }

    fn expression(&mut self) -> Result<f64, String> {
        let mut value = self.term()?;
        loop {
            self.skip();
            if self.matches('+') {
                value += self.term()?;
            } else if self.matches('-') {
                value -= self.term()?;
            } else {
                return Ok(value);
            }
        }
    }

    fn term(&mut self) -> Result<f64, String> {
        let mut value = self.unary()?;
        loop {
            self.skip();
            if self.matches('*') {
                value *= self.unary()?;
            } else if self.matches('/') {
                value /= self.unary()?;
            } else if self.matches('%') {
                value %= self.unary()?;
            } else {
                return Ok(value);
            }
        }
    }

    /// Unary sits looser than '^', so -2^2 parses as -(2^2) = -4, the mathematical convention.
    fn unary(&mut self) -> Result<f64, String> {
        self.skip();
        if self.matches('-') {
            return Ok(-self.unary()?);
        }
        if self.matches('+') {
            return self.unary();
        }
        self.power()
    }

    fn power(&mut self) -> Result<f64, String> {
        let base = self.postfix()?;
        self.skip();
        // Right-associative (2^3^2 = 512); the exponent may itself be signed (2^-3).
        if self.matches('^') {
            let exponent = self.unary()?;
            return Ok(base.powf(exponent));
        }
        Ok(base)
    }

    fn postfix(&mut self) -> Result<f64, String> {
        let value = self.atom()?;
        self.skip();
        if self.matches('!') {
            return factorial(value);
        }
        Ok(value)
    }

    fn atom(&mut self) -> Result<f64, String> {
        self.skip();
        if self.matches('(') {
            let value = self.expression()?;
            self.skip();
            if !self.matches(')') {
                return Err(err("Error_MissingParen"));
            }
            return Ok(value);
        }

        if self.peek().is_alphabetic() {
            let name = self.read_name();
            self.skip();
            if self.matches('(') {
                let arg = self.expression()?;
                self.skip();
                if !self.matches(')') {
                    return Err(err("Error_MissingParen"));
                }
                return apply(&name, arg);
            }
            return self.constant(&name);
        }

        self.read_number()
    }

    fn read_number(&mut self) -> Result<f64, String> {
        let start = self.pos;
        while self.pos < self.text.len() {
            let c = self.text[self.pos];
            let exponent_sign = (c == '+' || c == '-')
                && self.pos > start
                && matches!(self.text[self.pos - 1], 'e' | 'E');
            if c.is_ascii_digit() || c == '.' || c == 'e' || c == 'E' || exponent_sign {
                self.pos += 1;
            } else {
                break;
            }
        }
        if self.pos == start {
            return Err(err("Error_BadExpression"));
        }
        self.text[start..self.pos]
            .iter()
            .collect::<String>()
            .parse()
            .map_err(|_| err("Error_BadExpression"))
    }

    fn read_name(&mut self) -> String {
        let start = self.pos;
        while self.pos < self.text.len() && self.text[self.pos].is_alphanumeric() {
            self.pos += 1;
        }
        self.text[start..self.pos].iter().collect::<String>().to_lowercase()
    }

    fn constant(&mut self, name: &str) -> Result<f64, String> {
        match name {
            "x" if self.x.is_some() => Ok(self.x.expect("checked")),
            "pi" => Ok(std::f64::consts::PI),
            "e" => Ok(std::f64::consts::E),
            "tau" => Ok(std::f64::consts::TAU),
            "phi" => Ok(1.618033988749895),
            _ => Err(i18n::format("Error_UnknownName", &[name])),
        }
    }
}

fn apply(function: &str, x: f64) -> Result<f64, String> {
    Ok(match function {
        "sqrt" => x.sqrt(),
        "cbrt" => x.cbrt(),
        "abs" => x.abs(),
        "sin" => x.sin(),
        "cos" => x.cos(),
        "tan" => x.tan(),
        "asin" => x.asin(),
        "acos" => x.acos(),
        "atan" => x.atan(),
        "sinh" => x.sinh(),
        "cosh" => x.cosh(),
        "tanh" => x.tanh(),
        "ln" => x.ln(),
        "log" => x.log10(),
        "log2" => x.log2(),
        "exp" => x.exp(),
        "floor" => x.floor(),
        "ceil" => x.ceil(),
        // .NET's Math.Round is banker's rounding, not Rust's half-away-from-zero.
        "round" => round_half_even(x),
        // Math.Sign returns 0 at zero; f64::signum returns ±1, so it cannot be used here.
        "sign" => {
            if x > 0.0 {
                1.0
            } else if x < 0.0 {
                -1.0
            } else {
                0.0
            }
        }
        "deg" => x * 180.0 / std::f64::consts::PI, // radians to degrees
        "rad" => x * std::f64::consts::PI / 180.0, // degrees to radians
        _ => return Err(i18n::format("Error_UnknownFunction", &[function])),
    })
}

fn factorial(n: f64) -> Result<f64, String> {
    if n < 0.0 || n != n.floor() || n > 170.0 {
        return Err(err("Error_BadFactorial"));
    }
    let mut result = 1.0;
    let mut i = 2.0;
    while i <= n {
        result *= i;
        i += 1.0;
    }
    Ok(result)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn ev(s: &str) -> String {
        i18n::set_language("en");
        evaluate(s).unwrap()
    }

    #[test]
    fn arithmetic_follows_the_usual_precedence() {
        assert_eq!(ev("2+3*4"), "14");
        assert_eq!(ev("(2+3)*4"), "20");
        assert_eq!(ev("10/4"), "2.5");
        assert_eq!(ev("10%3"), "1");
    }

    /// Two conventions that are easy to get backwards, both asserted against the C# behaviour.
    #[test]
    fn unary_minus_is_looser_than_power_and_power_is_right_associative() {
        assert_eq!(ev("-2^2"), "-4", "reads as -(2^2)");
        assert_eq!(ev("2^3^2"), "512", "reads as 2^(3^2)");
        assert_eq!(ev("2^-3"), "0.125", "a signed exponent still parses");
    }

    #[test]
    fn g15_rounds_away_float_noise() {
        assert_eq!(ev("0.1+0.2"), "0.3");
        assert_eq!(ev("1/3"), "0.333333333333333");
    }

    /// The notation cut-offs: scientific at 1e-5 and below, and at 1e15 and above.
    #[test]
    fn g15_switches_notation_at_the_right_boundaries() {
        assert_eq!(ev("1e14"), "100000000000000");
        assert_eq!(ev("1e15"), "1E+15");
        assert_eq!(ev("0.0001"), "0.0001");
        assert_eq!(ev("0.00001"), "1E-05");
        assert_eq!(ev("2^100"), "1.26765060022823E+30");
        assert_eq!(ev("1/3000000"), "3.33333333333333E-07");
    }

    #[test]
    fn functions_and_constants_resolve() {
        assert_eq!(ev("sqrt(2)"), "1.4142135623731");
        assert_eq!(ev("pi"), "3.14159265358979");
        assert_eq!(ev("floor(2.7)"), "2");
        assert_eq!(ev("ceil(2.1)"), "3");
        assert_eq!(ev("sign(0)"), "0", "Math.Sign is 0 at zero, unlike f64::signum");
        assert_eq!(ev("abs(-5)"), "5");
        assert_eq!(ev("deg(pi)"), "180");
    }

    #[test]
    fn factorial_is_postfix_and_bounded() {
        let _guard = crate::i18n::test_lock();
        assert_eq!(ev("5!"), "120");
        assert_eq!(ev("0!"), "1");
        i18n::set_language("en");
        assert!(evaluate("(-1)!").is_err(), "negative");
        assert!(evaluate("2.5!").is_err(), "non-integer");
        assert!(evaluate("200!").is_err(), "overflows a double");
    }

    #[test]
    fn malformed_input_is_rejected() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        for bad in ["", "   ", "2+", "(2+3", "2 3", "nosuchfn(2)", "nosuchname"] {
            assert!(evaluate(bad).is_err(), "{bad:?} should not evaluate");
        }
    }

    #[test]
    fn division_by_zero_is_reported_not_returned() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert!(evaluate("1/0").is_err(), "infinity is an error, not a result");
        assert!(evaluate("0/0").is_err(), "NaN too");
    }

    #[test]
    fn x_resolves_only_when_bound() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert_eq!(evaluate_at("x*2", Some(21.0)).unwrap(), 42.0);
        assert!(evaluate_at("x*2", None).is_err(), "unbound x is an unknown name");
    }
}
