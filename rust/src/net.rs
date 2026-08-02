//! IPv4 subnet maths and small developer helpers. Mirrors the corresponding parts of
//! `Krate.Core.Everyday` and `Dev`.

use crate::i18n;

fn to_u32(ip: &str) -> Option<u32> {
    let octets: Vec<&str> = ip.split('.').collect();
    if octets.len() != 4 {
        return None;
    }
    let mut value: u32 = 0;
    for octet in octets {
        // Reject empty and out-of-range parts; "1.2.3.999" is not an address.
        if octet.is_empty() || octet.len() > 3 || !octet.bytes().all(|b| b.is_ascii_digit()) {
            return None;
        }
        let n: u32 = octet.parse().ok()?;
        if n > 255 {
            return None;
        }
        value = (value << 8) | n;
    }
    Some(value)
}

fn to_ip(value: u32) -> String {
    format!(
        "{}.{}.{}.{}",
        value >> 24,
        (value >> 16) & 0xFF,
        (value >> 8) & 0xFF,
        value & 0xFF
    )
}

fn is_private(network: u32) -> bool {
    (network & 0xFF00_0000) == 0x0A00_0000        // 10.0.0.0/8
        || (network & 0xFFF0_0000) == 0xAC10_0000 // 172.16.0.0/12
        || (network & 0xFFFF_0000) == 0xC0A8_0000 // 192.168.0.0/16
}

/// "192.168.1.10/24" — network, mask, broadcast and the usable host range.
pub fn subnet(input: &str) -> Result<String, String> {
    let usage = || i18n::get("Error_CidrUsage").to_string();
    let (address_text, prefix_text) = input.trim().split_once('/').ok_or_else(usage)?;
    let address = to_u32(address_text).ok_or_else(usage)?;
    let prefix: u32 = prefix_text.parse().map_err(|_| usage())?;
    if prefix > 32 {
        return Err(usage());
    }

    let mask = if prefix == 0 { 0u32 } else { u32::MAX << (32 - prefix) };
    let network = address & mask;
    let broadcast = network | !mask;
    // A /31 or /32 has no separate network and broadcast address, so nothing is subtracted.
    // Counted in 64 bits: a /0 is 2^32 addresses, which does not fit a u32 before subtracting.
    let size = 1u64 << (32 - prefix);
    let total = if prefix >= 31 { size } else { size - 2 };

    let (first, last) = if prefix >= 31 {
        (network, broadcast)
    } else {
        (network + 1, broadcast - 1)
    };

    Ok([
        format!("NETWORK    {}/{prefix}", to_ip(network)),
        format!("NETMASK    {}", to_ip(mask)),
        format!("WILDCARD   {}", to_ip(!mask)),
        format!("BROADCAST  {}", to_ip(broadcast)),
        format!("HOSTS      {} – {}", to_ip(first), to_ip(last)),
        format!("USABLE     {total}"),
        i18n::get(if is_private(network) { "Cidr_Private" } else { "Cidr_Public" }).to_string(),
    ]
    .join("\n"))
}

/// Turns a curl command into equivalent C#. Only the method and URL are read, as in the original.
pub fn curl_to_code(input: &str) -> Result<String, String> {
    let s = input.trim();
    if !s.starts_with("curl ") {
        return Err("Input must be a curl command".to_string());
    }

    // Mirrors `curl\s+(?:-X\s+(?<method>\w+)\s+)?['"]?(?<url>https?://[^'"\s]+)['"]?`.
    let mut method = "GET".to_string();
    let mut url = None;
    let mut tokens = s.split_whitespace().skip(1).peekable();
    while let Some(token) = tokens.next() {
        if token == "-X" {
            if let Some(verb) = tokens.next() {
                method = verb.to_uppercase();
            }
            continue;
        }
        let cleaned = token.trim_matches(['\'', '"']);
        if cleaned.starts_with("http://") || cleaned.starts_with("https://") {
            url = Some(cleaned.to_string());
            break;
        }
    }

    match url {
        Some(url) => Ok(format!(
            "var client = new HttpClient();\n\
             var request = new HttpRequestMessage(HttpMethod.{method}, \"{url}\");\n\
             var response = await client.SendAsync(request);"
        )),
        None => Ok("Could not parse curl command.".to_string()),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn subnet_computes_the_usual_fields() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let r = subnet("192.168.1.10/24").unwrap();
        assert!(r.contains("NETWORK    192.168.1.0/24"), "{r}");
        assert!(r.contains("NETMASK    255.255.255.0"), "{r}");
        assert!(r.contains("WILDCARD   0.0.0.255"), "{r}");
        assert!(r.contains("BROADCAST  192.168.1.255"), "{r}");
        assert!(r.contains("USABLE     254"), "{r}");
    }

    /// /31 and /32 have no network/broadcast pair to subtract, so the usable count is not
    /// "size minus two" — getting this wrong underflows to a huge number.
    #[test]
    fn tiny_prefixes_do_not_subtract_a_host_pair() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert!(subnet("10.0.0.1/32").unwrap().contains("USABLE     1"));
        assert!(subnet("10.0.0.0/31").unwrap().contains("USABLE     2"));
        assert!(subnet("10.0.0.0/0").unwrap().contains("USABLE     4294967294"));
    }

    #[test]
    fn private_ranges_are_recognised() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        for private in ["10.1.2.3/8", "172.16.5.5/12", "192.168.0.1/24"] {
            assert!(subnet(private).unwrap().contains(i18n::get("Cidr_Private")), "{private}");
        }
        assert!(subnet("8.8.8.8/24").unwrap().contains(i18n::get("Cidr_Public")));
    }

    #[test]
    fn malformed_cidr_is_rejected() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        for bad in ["192.168.1.1", "192.168.1.1/33", "1.2.3.999/24", "zzz/24", "1.2.3/24", ""] {
            assert!(subnet(bad).is_err(), "{bad:?} should not parse");
        }
    }

    #[test]
    fn curl_to_code_reads_the_method_and_url() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let get = curl_to_code("curl https://api.example.com/users").unwrap();
        assert!(get.contains("HttpMethod.GET"), "{get}");
        assert!(get.contains("https://api.example.com/users"), "{get}");
        assert!(curl_to_code("curl -X POST https://x.com").unwrap().contains("HttpMethod.POST"));
        assert!(curl_to_code("curl -X delete https://x.com/1").unwrap().contains("HttpMethod.DELETE"));
        assert!(curl_to_code("wget https://x.com").is_err(), "not a curl command");
        assert_eq!(curl_to_code("curl not-a-url").unwrap(), "Could not parse curl command.");
    }
}
