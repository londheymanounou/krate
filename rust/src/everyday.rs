//! Everyday calculators. Mirrors `Krate.Core.Everyday`.

use crate::i18n;

/// Splits on the separators the C# side accepts — note it also splits on `/`, unlike the maths
/// helper, so "70/175" works.
fn numbers(input: &str) -> Result<Vec<f64>, String> {
    input
        .split([' ', ',', ';', '\t', '\n', '/'])
        .map(str::trim)
        .filter(|p| !p.is_empty())
        .map(|p| p.parse::<f64>().map_err(|_| i18n::get("Error_NeedNumber").to_string()))
        .collect()
}

/// C#'s "0.##": at most two decimals, trailing zeros dropped. Uses the shared decimal
/// formatter, because `{:.2}` rounds the binary value and .NET rounds the shortest decimal.
fn fmt(v: f64) -> String {
    crate::tools::format_decimal(v, 2)
}

/// "70 175" gives the BMI for 70 kg and 175 cm.
pub fn bmi(input: &str) -> Result<String, String> {
    let n = numbers(input)?;
    let usage = || i18n::get("Error_BmiUsage").to_string();
    if n.len() < 2 {
        return Err(usage());
    }
    let kg = n[0];
    // Accept centimetres or metres: anything over 3 must be centimetres.
    let metres = if n[1] > 3.0 { n[1] / 100.0 } else { n[1] };
    if kg <= 0.0 || metres <= 0.0 {
        return Err(usage());
    }

    let bmi = kg / (metres * metres);
    let band = if bmi < 18.5 {
        "Bmi_Under"
    } else if bmi < 25.0 {
        "Bmi_Normal"
    } else if bmi < 30.0 {
        "Bmi_Over"
    } else {
        "Bmi_Obese"
    };
    Ok([
        format!("BMI  {}", fmt(bmi)),
        i18n::get(band).to_string(),
        i18n::get("Bmi_Disclaimer").to_string(),
    ]
    .join("\n"))
}

/// "48.50 15 3" is a 15% tip on 48.50, split between 3 people.
pub fn tip(input: &str) -> Result<String, String> {
    let n = numbers(input)?;
    if n.is_empty() {
        return Err(i18n::get("Error_NeedNumber").to_string());
    }
    let bill = n[0];
    let percent = if n.len() > 1 { n[1] } else { 15.0 };
    let people = if n.len() > 2 { n[2].max(1.0) } else { 1.0 };

    let tip = bill * percent / 100.0;
    let total = bill + tip;
    let mut lines = vec![
        i18n::format("Tip_Tip", &[&fmt(percent), &fmt(tip)]),
        i18n::format("Tip_Total", &[&fmt(total)]),
    ];
    if people > 1.0 {
        lines.push(i18n::format("Tip_Each", &[&fmt(people), &fmt(total / people)]));
    }
    Ok(lines.join("\n"))
}

/// "200000 3.5 25" is the monthly payment on 200 000 at 3.5% over 25 years.
pub fn loan(input: &str) -> Result<String, String> {
    let n = numbers(input)?;
    if n.len() < 3 {
        return Err(i18n::get("Error_LoanUsage").to_string());
    }
    let (principal, annual_rate, years) = (n[0], n[1], n[2]);
    // Years, or months already if the number is large.
    let months = (years * if years < 100.0 { 12.0 } else { 1.0 }).round() as i64;
    let monthly_rate = annual_rate / 100.0 / 12.0;

    // Standard amortisation; the zero-interest case would divide by zero.
    let payment = if monthly_rate == 0.0 {
        principal / months as f64
    } else {
        principal * monthly_rate / (1.0 - (1.0 + monthly_rate).powi(-(months as i32)))
    };
    let total = payment * months as f64;

    Ok([
        i18n::format("Loan_Monthly", &[&fmt(payment)]),
        i18n::format("Loan_Total", &[&fmt(total)]),
        i18n::format("Loan_Interest", &[&fmt(total - principal)]),
        i18n::format("Loan_Payments", &[&months.to_string()]),
    ]
    .join("\n"))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn bmi_accepts_centimetres_or_metres_and_bands_the_result() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        // 70 kg at 1.75 m is 22.86 — normal.
        let a = bmi("70 175").unwrap();
        let b = bmi("70 1.75").unwrap();
        assert_eq!(a, b, "cm and m must agree");
        assert!(a.contains("BMI  22.86"), "{a}");
        assert!(bmi("45 175").unwrap().lines().count() == 3);
        assert!(bmi("70").is_err(), "needs both numbers");
        assert!(bmi("0 175").is_err(), "zero weight");
        assert!(bmi("zzz").is_err());
    }

    #[test]
    fn tip_defaults_to_fifteen_percent_and_splits_only_when_asked() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let plain = tip("100").unwrap();
        assert!(plain.contains("15"), "{plain}");
        assert_eq!(plain.lines().count(), 2, "no per-person line for one diner");
        let split = tip("100 20 4").unwrap();
        assert_eq!(split.lines().count(), 3);
        assert!(split.contains("120"), "total: {split}");
        assert!(tip("").is_err());
    }

    #[test]
    fn loan_amortises_and_survives_zero_interest() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let m = loan("200000 3.5 25").unwrap();
        assert!(m.contains("1001.25"), "known payment for this loan: {m}");
        // Zero interest is principal split evenly, not a division by zero.
        let free = loan("12000 0 1").unwrap();
        assert!(free.contains("1000"), "{free}");
        assert!(loan("200000 3.5").is_err(), "needs three numbers");
    }

    /// .NET rounds the shortest decimal, not the binary value: a 15% tip on 48.50 is stored as
    /// 55.774999..., shown as 55.775, and rounds to 55.78 — not the 55.77 that `{:.2}` gives.
    #[test]
    fn totals_round_the_way_dotnet_prints_them() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let split = tip("48.50 15 3").unwrap();
        assert!(split.contains("55.78"), "{split}");
        assert_eq!(fmt(55.775), "55.78");
        assert_eq!(fmt(2.5), "2.5");
        assert_eq!(fmt(2.0), "2");
        assert_eq!(fmt(-0.001), "0", "rounds to zero without a stray minus");
        assert_eq!(fmt(9.999), "10", "carry propagates into the integer part");
    }
}
