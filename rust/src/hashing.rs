//! Digests. Lower-case hex, UTF-8 input — identical to `Krate.Core.Hashing`.

use md5::Md5;
use sha1::Sha1;
use sha2::{Digest, Sha256, Sha512};

fn hex(bytes: &[u8]) -> String {
    bytes.iter().map(|b| format!("{b:02x}")).collect()
}

pub fn md5(text: &str) -> String {
    hex(&Md5::digest(text.as_bytes()))
}

pub fn sha1(text: &str) -> String {
    hex(&Sha1::digest(text.as_bytes()))
}

pub fn sha256(text: &str) -> String {
    hex(&Sha256::digest(text.as_bytes()))
}

pub fn sha512(text: &str) -> String {
    hex(&Sha512::digest(text.as_bytes()))
}

/// Streamed, so hashing a 4 GB file does not load it into memory.
pub fn sha256_file(path: &std::path::Path) -> Result<String, String> {
    use std::io::Read;
    let mut file = std::fs::File::open(path).map_err(|e| e.to_string())?;
    let mut hasher = Sha256::new();
    let mut buffer = vec![0u8; 1 << 16];
    loop {
        let read = file.read(&mut buffer).map_err(|e| e.to_string())?;
        if read == 0 {
            break;
        }
        hasher.update(&buffer[..read]);
    }
    Ok(hex(&hasher.finalize()))
}

/// All four digests of one text, for when you don't know which one you need.
pub fn all(text: &str) -> String {
    [
        format!("MD5      {}", md5(text)),
        format!("SHA-1    {}", sha1(text)),
        format!("SHA-256  {}", sha256(text)),
        format!("SHA-512  {}", sha512(text)),
    ]
    .join("\n")
}

#[cfg(test)]
mod tests {
    use super::*;

    // Published vectors, same as the C# suite asserts.
    const HELLO_MD5: &str = "5d41402abc4b2a76b9719d911017c592";
    const HELLO_SHA1: &str = "aaf4c61ddcc5e8a2dabede0f3b482cd9aea9434d";
    const HELLO_SHA256: &str = "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824";
    const HELLO_SHA512: &str = concat!(
        "9b71d224bd62f3785d96d46ad3ea3d73319bfbc2890caadae2dff72519673ca7",
        "2323c3d99ba5c11d7c7acc6e14b8c5da0c4663475c2e5c3adef46f73bcdec043"
    );

    #[test]
    fn digests_match_the_known_vectors() {
        assert_eq!(md5("hello"), HELLO_MD5);
        assert_eq!(sha1("hello"), HELLO_SHA1);
        assert_eq!(sha256("hello"), HELLO_SHA256);
        assert_eq!(sha512("hello"), HELLO_SHA512);
    }

    #[test]
    fn empty_input_still_hashes() {
        assert_eq!(md5(""), "d41d8cd98f00b204e9800998ecf8427e");
        assert_eq!(sha1(""), "da39a3ee5e6b4b0d3255bfef95601890afd80709");
    }

    #[test]
    fn hashing_is_utf8_based_not_utf16() {
        assert_eq!(md5("café"), "07117fe4a1ebd544965dc19573183da2");
    }

    #[test]
    fn all_lists_every_digest_labelled() {
        let output = all("hello");
        let lines: Vec<&str> = output.lines().collect();
        assert_eq!(lines.len(), 4);
        assert_eq!(lines[0], format!("MD5      {HELLO_MD5}"));
        assert_eq!(lines[3], format!("SHA-512  {HELLO_SHA512}"));
    }
}
