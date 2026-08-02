//! CSV and JSON interchange. Mirrors `Krate.Core.Data`.

use crate::i18n;
use crate::json::write_value_public as write_json;
use serde_json::Value;

/// RFC 4180 reader: honours quoted fields, `""` escapes, and commas or newlines inside quotes.
pub fn parse_csv(text: &str) -> Vec<Vec<String>> {
    let s = text.replace("\r\n", "\n").replace('\r', "\n");
    let chars: Vec<char> = s.chars().collect();

    let mut rows: Vec<Vec<String>> = Vec::new();
    let mut row: Vec<String> = Vec::new();
    let mut field = String::new();
    let mut in_quotes = false;
    // Tracks whether the row had content, so a trailing newline does not add a blank record.
    let mut any = false;
    let mut i = 0;

    while i < chars.len() {
        let c = chars[i];
        if in_quotes {
            if c == '"' && chars.get(i + 1) == Some(&'"') {
                field.push('"');
                i += 2;
            } else if c == '"' {
                in_quotes = false;
                i += 1;
            } else {
                field.push(c);
                i += 1;
            }
            continue;
        }
        match c {
            '"' => {
                in_quotes = true;
                any = true;
                i += 1;
            }
            ',' => {
                row.push(std::mem::take(&mut field));
                any = true;
                i += 1;
            }
            '\n' => {
                row.push(std::mem::take(&mut field));
                rows.push(std::mem::take(&mut row));
                any = false;
                i += 1;
            }
            _ => {
                field.push(c);
                any = true;
                i += 1;
            }
        }
    }
    if any || !field.is_empty() || !row.is_empty() {
        row.push(field);
        rows.push(row);
    }
    rows
}

/// A leading zero followed by a digit means an identifier, not a number — "007" must stay a
/// string or the value changes.
fn looks_numeric(v: &str) -> Option<f64> {
    let bytes = v.as_bytes();
    if bytes.len() > 1 && bytes[0] == b'0' && bytes[1].is_ascii_digit() {
        return None;
    }
    v.parse::<f64>().ok().filter(|n| n.is_finite())
}

/// First row is the header. Numbers and booleans go in unquoted, so the JSON is actually typed
/// rather than all-strings.
pub fn csv_to_json(csv: &str) -> Result<String, String> {
    let rows = parse_csv(csv);
    if rows.is_empty() {
        return Ok("[]".to_string());
    }
    let headers = &rows[0];

    let items: Vec<Value> = rows[1..]
        .iter()
        .map(|row| {
            let mut object = serde_json::Map::new();
            for (i, header) in headers.iter().enumerate() {
                let cell = row.get(i).map(String::as_str).unwrap_or("");
                let value = match cell {
                    "true" | "True" | "TRUE" => Value::Bool(true),
                    "false" | "False" | "FALSE" => Value::Bool(false),
                    other => match looks_numeric(other) {
                        // Written through the raw-number path so "1.50" keeps its shape.
                        Some(_) => serde_json::from_str(other).unwrap_or_else(|_| Value::String(other.into())),
                        None => Value::String(other.to_string()),
                    },
                };
                object.insert(header.clone(), value);
            }
            Value::Object(object)
        })
        .collect();

    let mut out = String::new();
    write_json(&mut out, &Value::Array(items), Some(2), 0);
    Ok(out)
}

fn scalar(value: &Value) -> String {
    match value {
        Value::String(s) => s.clone(),
        Value::Null => String::new(),
        // A nested value keeps its JSON verbatim in the cell.
        other => other.to_string(),
    }
}

/// A field needs quoting if it contains a comma, a quote, or a line break.
fn escape(field: &str) -> String {
    if field.contains([',', '"', '\n', '\r']) {
        format!("\"{}\"", field.replace('"', "\"\""))
    } else {
        field.to_string()
    }
}

/// Array of objects to CSV. Columns are the union of every object's keys, in first-seen order.
pub fn json_to_csv(json: &str) -> Result<String, String> {
    let not_array = || i18n::get("Error_JsonNotArray").to_string();
    let value: Value = serde_json::from_str(json).map_err(|_| not_array())?;
    let rows = value.as_array().ok_or_else(not_array)?;

    let mut columns: Vec<String> = Vec::new();
    for row in rows {
        if let Some(object) = row.as_object() {
            for key in object.keys() {
                if !columns.contains(key) {
                    columns.push(key.clone());
                }
            }
        }
    }
    if columns.is_empty() {
        return Err(not_array());
    }

    // Joined with '\n' explicitly: the C# comments that AppendLine would emit \r\n on Windows
    // and change the output, so this is one of the tools that must NOT use the platform newline.
    let mut lines = vec![columns.iter().map(|c| escape(c)).collect::<Vec<_>>().join(",")];
    for row in rows {
        lines.push(
            columns
                .iter()
                .map(|c| {
                    let cell = row.as_object().and_then(|o| o.get(c)).map(scalar).unwrap_or_default();
                    escape(&cell)
                })
                .collect::<Vec<_>>()
                .join(","),
        );
    }
    Ok(lines.join("\n"))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn csv_reader_honours_quotes_and_embedded_separators() {
        let rows = parse_csv("a,b\n1,\"two, three\"");
        assert_eq!(rows.len(), 2);
        assert_eq!(rows[1], vec!["1", "two, three"], "a quoted comma is one field");

        let escaped = parse_csv("\"say \"\"hi\"\"\"");
        assert_eq!(escaped[0], vec!["say \"hi\""], "doubled quotes are one quote");

        let multiline = parse_csv("a\n\"line1\nline2\"");
        assert_eq!(multiline.len(), 2, "a newline inside quotes does not end the row");
    }

    #[test]
    fn csv_reader_ignores_a_trailing_newline() {
        assert_eq!(parse_csv("a,b\n1,2\n").len(), 2, "no blank third record");
        assert!(parse_csv("").is_empty());
    }

    #[test]
    fn csv_to_json_types_the_values() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let out = csv_to_json("name,age,active\nada,36,true").unwrap();
        assert!(out.contains("\"age\": 36"), "numbers are unquoted: {out}");
        assert!(out.contains("\"active\": true"), "booleans are unquoted: {out}");
        assert!(out.contains("\"name\": \"ada\""), "text stays quoted: {out}");
        assert_eq!(csv_to_json("").unwrap(), "[]");
    }

    /// "007" is an identifier, not the number seven — quoting it is the whole point.
    #[test]
    fn csv_to_json_keeps_leading_zero_values_as_strings() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let out = csv_to_json("id\n007").unwrap();
        assert!(out.contains("\"id\": \"007\""), "{out}");
    }

    #[test]
    fn json_to_csv_unions_the_columns() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let out = json_to_csv(r#"[{"a":1,"b":2},{"b":3,"c":4}]"#).unwrap();
        let lines: Vec<&str> = out.lines().collect();
        assert_eq!(lines[0], "a,b,c", "columns are first-seen order across all rows");
        assert_eq!(lines[1], "1,2,");
        assert_eq!(lines[2], ",3,4", "missing keys become empty cells");
    }

    #[test]
    fn json_to_csv_quotes_only_what_needs_it() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let out = json_to_csv(r#"[{"a":"two, three","b":"say \"hi\""}]"#).unwrap();
        assert!(out.contains("\"two, three\""), "{out}");
        assert!(out.contains("\"say \"\"hi\"\"\""), "{out}");
    }

    #[test]
    fn json_to_csv_rejects_anything_but_an_array_of_objects() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert!(json_to_csv(r#"{"a":1}"#).is_err(), "an object is not an array");
        assert!(json_to_csv("[1,2,3]").is_err(), "no columns to derive");
        assert!(json_to_csv("[]").is_err());
        assert!(json_to_csv("nonsense").is_err());
    }
}
