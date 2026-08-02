//! Password-based file encryption. Mirrors `Krate.Core.Crypt`.
//!
//! Encrypt-then-MAC, byte-for-byte the same container the C# writes, so a file encrypted by
//! either implementation opens in the other:
//!
//! ```text
//! "KRATE01\n" (8) | salt (16) | iv (16) | AES-256-CBC ciphertext | HMAC-SHA256 (32)
//! ```
//!
//! PBKDF2-HMAC-SHA256 with 600k iterations stretches the password over the salt into 64 bytes:
//! the first 32 are the AES key, the last 32 the MAC key. The MAC covers salt, IV and ciphertext,
//! and decryption **verifies it before writing any plaintext** — a wrong password or a tampered
//! file fails cleanly instead of emitting garbage.
//!
//! The primitives come from RustCrypto rather than being hand-written. Hand-rolled AES is how
//! timing side channels and silent correctness bugs get shipped; this is the one place in the port
//! where writing it myself would be worse than taking a dependency.

use crate::csprng;
use crate::i18n;
use aes::cipher::block_padding::Pkcs7;
use aes::cipher::{Block, BlockModeDecrypt, BlockModeEncrypt, KeyIvInit};
use hmac::{Hmac, KeyInit, Mac};
use sha2::Sha256;
use std::fs::File;
use std::io::{Read, Seek, SeekFrom, Write};
use std::path::Path;

type HmacSha256 = Hmac<Sha256>;
type Encryptor = cbc::Encryptor<aes::Aes256>;
type Decryptor = cbc::Decryptor<aes::Aes256>;

const MAGIC: &[u8] = b"KRATE01\n";
const SALT_SIZE: usize = 16;
const IV_SIZE: usize = 16;
const KEY_SIZE: usize = 32;
const MAC_SIZE: usize = 32;
const ITERATIONS: u32 = 600_000;
const CHUNK_SIZE: usize = 64 * 1024;
const BLOCK_SIZE: usize = 16;

fn derive_keys(password: &str, salt: &[u8]) -> ([u8; KEY_SIZE], [u8; KEY_SIZE]) {
    let mut material = [0u8; KEY_SIZE * 2];
    pbkdf2::pbkdf2_hmac::<Sha256>(password.as_bytes(), salt, ITERATIONS, &mut material);
    let mut enc = [0u8; KEY_SIZE];
    let mut mac = [0u8; KEY_SIZE];
    enc.copy_from_slice(&material[..KEY_SIZE]);
    mac.copy_from_slice(&material[KEY_SIZE..]);
    (enc, mac)
}

/// Text-tool entry: `path | password`. Split on the **last** `|` so a path may contain one.
fn with_password<F>(input: &str, operation: F) -> Result<String, String>
where
    F: Fn(&str, &str) -> Result<String, String>,
{
    let cut = input.rfind('|').ok_or_else(|| i18n::get("Error_CryptUsage").to_string())?;
    let path = input[..cut].trim().trim_matches('"');
    let password = input[cut + 1..].trim();
    operation(path, password)
}

pub fn encrypt(input: &str) -> Result<String, String> {
    with_password(input, encrypt_file)
}

pub fn decrypt(input: &str) -> Result<String, String> {
    with_password(input, decrypt_file)
}

fn file_name(path: &Path) -> String {
    path.file_name().unwrap_or_default().to_string_lossy().into_owned()
}

fn check_source(path: &Path, password: &str) -> Result<(), String> {
    if !path.is_file() {
        return Err(i18n::format("Error_NoFile", &[&path.to_string_lossy()]));
    }
    if password.is_empty() {
        return Err(i18n::get("Error_NeedPassword").to_string());
    }
    Ok(())
}

pub fn encrypt_file(path: &str, password: &str) -> Result<String, String> {
    let path = Path::new(path.trim().trim_matches('"'));
    check_source(path, password)?;

    let out_path = {
        let mut p = path.as_os_str().to_owned();
        p.push(".krate");
        std::path::PathBuf::from(p)
    };
    if out_path.exists() {
        return Err(i18n::format("Error_FileExists", &[&out_path.to_string_lossy()]));
    }

    let mut salt = [0u8; SALT_SIZE];
    let mut iv = [0u8; IV_SIZE];
    csprng::fill(&mut salt);
    csprng::fill(&mut iv);
    let (enc_key, mac_key) = derive_keys(password, &salt);

    let mut cipher = Encryptor::new_from_slices(&enc_key, &iv)
        .expect("the key and IV are fixed-size arrays of the right length");
    let mut mac = HmacSha256::new_from_slice(&mac_key).expect("HMAC takes a key of any length");
    mac.update(&salt);
    mac.update(&iv);

    let mut source = File::open(path).map_err(|e| e.to_string())?;
    let mut sink = File::create(&out_path).map_err(|e| e.to_string())?;
    let written = (|| -> std::io::Result<()> {
        sink.write_all(MAGIC)?;
        sink.write_all(&salt)?;
        sink.write_all(&iv)?;

        // Stream in chunks, as the C# does, so a large file does not have to fit in memory.
        // Whole blocks go straight through; whatever is left over waits for the next read.
        let mut buffer = vec![0u8; CHUNK_SIZE];
        let mut carry: Vec<u8> = Vec::with_capacity(BLOCK_SIZE);
        loop {
            let read = source.read(&mut buffer)?;
            if read == 0 {
                break;
            }
            carry.extend_from_slice(&buffer[..read]);
            let whole = carry.len() - carry.len() % BLOCK_SIZE;
            for block in carry[..whole].chunks_exact_mut(BLOCK_SIZE) {
                let block: &mut Block<Encryptor> =
                    block.try_into().expect("chunks_exact_mut yields exactly one block");
                cipher.encrypt_block(block);
            }
            if whole > 0 {
                mac.update(&carry[..whole]);
                sink.write_all(&carry[..whole])?;
                carry.drain(..whole);
            }
        }

        // PKCS7 always adds a final block, even when the input divided evenly.
        let mut tail = [0u8; BLOCK_SIZE * 2];
        let remaining = carry.len();
        tail[..remaining].copy_from_slice(&carry);
        let final_block = cipher
            .encrypt_padded::<Pkcs7>(&mut tail, remaining)
            .expect("the tail buffer holds one padded block");
        mac.update(final_block);
        sink.write_all(final_block)?;

        // The MAC trails the ciphertext.
        sink.write_all(&mac.finalize().into_bytes())?;
        Ok(())
    })();

    if let Err(e) = written {
        // Never leave a half-written container behind to be mistaken for a real one.
        drop(sink);
        std::fs::remove_file(&out_path).ok();
        return Err(e.to_string());
    }
    Ok(i18n::format("Crypt_Encrypted", &[&file_name(&out_path)]))
}

pub fn decrypt_file(path: &str, password: &str) -> Result<String, String> {
    let path = Path::new(path.trim().trim_matches('"'));
    check_source(path, password)?;

    let not_encrypted = || i18n::format("Error_NotEncrypted", &[&file_name(path)]);
    let mut source = File::open(path).map_err(|e| e.to_string())?;
    let length = source.metadata().map_err(|e| e.to_string())?.len();

    let header_len = MAGIC.len() + SALT_SIZE + IV_SIZE;
    if length < (header_len + MAC_SIZE) as u64 {
        return Err(not_encrypted());
    }

    let mut header = vec![0u8; header_len];
    source.read_exact(&mut header).map_err(|_| not_encrypted())?;
    if &header[..MAGIC.len()] != MAGIC {
        return Err(not_encrypted());
    }
    let salt = &header[MAGIC.len()..MAGIC.len() + SALT_SIZE];
    let iv = &header[MAGIC.len() + SALT_SIZE..];
    let (enc_key, mac_key) = derive_keys(password, salt);

    let cipher_start = header_len as u64;
    let cipher_len = length - cipher_start - MAC_SIZE as u64;

    // Pass 1: authenticate everything before a single byte of plaintext is written.
    let mut mac = HmacSha256::new_from_slice(&mac_key).expect("HMAC takes a key of any length");
    mac.update(salt);
    mac.update(iv);
    source.seek(SeekFrom::Start(cipher_start)).map_err(|e| e.to_string())?;
    let mut buffer = vec![0u8; CHUNK_SIZE];
    let mut left = cipher_len;
    while left > 0 {
        let want = std::cmp::min(buffer.len() as u64, left) as usize;
        let read = source.read(&mut buffer[..want]).map_err(|e| e.to_string())?;
        if read == 0 {
            break;
        }
        mac.update(&buffer[..read]);
        left -= read as u64;
    }
    let mut stored = [0u8; MAC_SIZE];
    source.read_exact(&mut stored).map_err(|_| not_encrypted())?;
    // Constant-time, so a wrong password leaks nothing about how nearly right it was.
    if mac.verify_slice(&stored).is_err() {
        return Err(i18n::get("Error_WrongPassword").to_string());
    }

    // Pass 2: the MAC checked out, so it is safe to decrypt.
    let text = path.to_string_lossy().into_owned();
    let out_path = if text.to_lowercase().ends_with(".krate") {
        std::path::PathBuf::from(&text[..text.len() - 6])
    } else {
        std::path::PathBuf::from(format!("{text}.dec"))
    };
    if out_path.exists() {
        return Err(i18n::format("Error_FileExists", &[&out_path.to_string_lossy()]));
    }
    // A MAC-verified container always has a whole number of blocks; refuse rather than panic.
    if cipher_len == 0 || !cipher_len.is_multiple_of(BLOCK_SIZE as u64) {
        return Err(not_encrypted());
    }

    let mut cipher = Decryptor::new_from_slices(&enc_key, iv)
        .expect("the key and IV are fixed-size slices of the right length");
    source.seek(SeekFrom::Start(cipher_start)).map_err(|e| e.to_string())?;
    let mut sink = File::create(&out_path).map_err(|e| e.to_string())?;

    let written = (|| -> std::io::Result<()> {
        let mut carry: Vec<u8> = Vec::with_capacity(CHUNK_SIZE + BLOCK_SIZE);
        let mut left = cipher_len;
        while left > 0 {
            let want = std::cmp::min(CHUNK_SIZE as u64, left) as usize;
            let read = source.read(&mut buffer[..want])?;
            if read == 0 {
                break;
            }
            left -= read as u64;
            carry.extend_from_slice(&buffer[..read]);
            // Hold the last block back: only at the end is it known to be the padded one.
            let releasable = carry.len().saturating_sub(BLOCK_SIZE);
            let whole = releasable - releasable % BLOCK_SIZE;
            for block in carry[..whole].chunks_exact_mut(BLOCK_SIZE) {
                let block: &mut Block<Decryptor> =
                    block.try_into().expect("chunks_exact_mut yields exactly one block");
                cipher.decrypt_block(block);
            }
            if whole > 0 {
                sink.write_all(&carry[..whole])?;
                carry.drain(..whole);
            }
        }
        // The final block carries the PKCS7 padding.
        let plain = cipher
            .decrypt_padded::<Pkcs7>(&mut carry)
            .map_err(|_| std::io::Error::other("bad padding"))?;
        sink.write_all(plain)?;
        Ok(())
    })();

    if let Err(e) = written {
        drop(sink);
        std::fs::remove_file(&out_path).ok();
        // Padding can only be wrong here if the MAC was forged, which needs the MAC key.
        return Err(if e.to_string() == "bad padding" {
            i18n::get("Error_WrongPassword").to_string()
        } else {
            e.to_string()
        });
    }
    Ok(i18n::format("Crypt_Decrypted", &[&file_name(&out_path)]))
}

#[cfg(test)]
mod tests {
    use super::*;

    fn scratch(tag: &str) -> std::path::PathBuf {
        let dir = std::env::temp_dir().join(format!("krate-crypt-{tag}-{}", csprng::below(1_000_000)));
        std::fs::create_dir_all(&dir).unwrap();
        dir
    }

    /// Known-answer check on the key schedule, so a broken PBKDF2 cannot hide behind a
    /// round-trip that is wrong in both directions. Values from RFC 6070's construction with
    /// SHA-256 and this file's parameters would take 600k iterations, so this uses a small count
    /// against a published vector instead.
    #[test]
    fn pbkdf2_matches_a_published_vector() {
        // RFC 7914 §11 / RFC 6070-style vector: P="password", S="salt", c=1, dkLen=32, SHA-256.
        let mut out = [0u8; 32];
        pbkdf2::pbkdf2_hmac::<Sha256>(b"password", b"salt", 1, &mut out);
        assert_eq!(
            out[..8],
            [0x12, 0x0f, 0xb6, 0xcf, 0xfc, 0xf8, 0xb3, 0x2c],
            "PBKDF2-HMAC-SHA256(password, salt, 1) starts 120fb6cffcf8b32c"
        );
        let mut out2 = [0u8; 32];
        pbkdf2::pbkdf2_hmac::<Sha256>(b"password", b"salt", 2, &mut out2);
        assert_eq!(out2[..8], [0xae, 0x4d, 0x0c, 0x95, 0xaf, 0x6b, 0x46, 0xd3]);
    }

    #[test]
    fn round_trip_restores_the_original_bytes() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let dir = scratch("round");
        // Includes a length that is an exact multiple of the block size, which is the case
        // PKCS7 handles by adding a whole extra block.
        for (name, body) in [
            ("empty.bin", vec![]),
            ("exact.bin", vec![7u8; 32]),
            ("short.bin", b"hello".to_vec()),
            ("big.bin", (0..200_000u32).map(|i| (i % 251) as u8).collect()),
        ] {
            let file = dir.join(name);
            std::fs::write(&file, &body).unwrap();
            let encrypted = format!("{} | pw", file.display());
            assert!(encrypt(&encrypted).is_ok(), "{name}");

            let container = dir.join(format!("{name}.krate"));
            assert!(container.exists(), "{name}");
            assert_eq!(&std::fs::read(&container).unwrap()[..8], MAGIC);
            std::fs::remove_file(&file).unwrap();

            assert!(decrypt(&format!("{} | pw", container.display())).is_ok(), "{name}");
            assert_eq!(std::fs::read(&file).unwrap(), body, "{name} did not round-trip");
        }
        std::fs::remove_dir_all(&dir).ok();
    }

    #[test]
    fn a_wrong_password_is_refused_without_writing_anything() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let dir = scratch("wrong");
        let file = dir.join("secret.txt");
        std::fs::write(&file, b"top secret").unwrap();
        encrypt(&format!("{} | right", file.display())).unwrap();
        let container = dir.join("secret.txt.krate");
        std::fs::remove_file(&file).unwrap();

        let err = decrypt(&format!("{} | wrong", container.display())).unwrap_err();
        assert_eq!(err, i18n::get("Error_WrongPassword"));
        assert!(!file.exists(), "no plaintext may be written when the MAC fails");
        std::fs::remove_dir_all(&dir).ok();
    }

    /// Encrypt-then-MAC's whole point: a flipped ciphertext byte is caught before decryption.
    #[test]
    fn tampering_is_detected() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let dir = scratch("tamper");
        let file = dir.join("data.bin");
        std::fs::write(&file, vec![1u8; 100]).unwrap();
        encrypt(&format!("{} | pw", file.display())).unwrap();
        let container = dir.join("data.bin.krate");
        std::fs::remove_file(&file).unwrap();

        let mut bytes = std::fs::read(&container).unwrap();
        let middle = bytes.len() / 2;
        bytes[middle] ^= 0xFF;
        std::fs::write(&container, &bytes).unwrap();

        assert_eq!(
            decrypt(&format!("{} | pw", container.display())).unwrap_err(),
            i18n::get("Error_WrongPassword")
        );
        assert!(!file.exists());
        std::fs::remove_dir_all(&dir).ok();
    }

    #[test]
    fn the_same_plaintext_encrypts_differently_every_time() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let dir = scratch("iv");
        let mut containers = Vec::new();
        for n in 0..2 {
            let file = dir.join(format!("same{n}.txt"));
            std::fs::write(&file, b"identical content").unwrap();
            encrypt(&format!("{} | pw", file.display())).unwrap();
            containers.push(std::fs::read(dir.join(format!("same{n}.txt.krate"))).unwrap());
        }
        assert_ne!(containers[0], containers[1], "a fresh salt and IV are required each time");
        std::fs::remove_dir_all(&dir).ok();
    }

    #[test]
    fn bad_requests_are_reported_not_crashed() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let dir = scratch("bad");
        let file = dir.join("a.txt");
        std::fs::write(&file, b"x").unwrap();

        assert!(encrypt("no pipe here").is_err(), "the separator is required");
        assert!(encrypt(&format!("{} | ", file.display())).is_err(), "an empty password is refused");
        assert!(encrypt("Z:\\nope.txt | pw").is_err());
        assert!(decrypt("Z:\\nope.txt | pw").is_err());

        // Encrypting twice would overwrite the first container.
        encrypt(&format!("{} | pw", file.display())).unwrap();
        assert!(encrypt(&format!("{} | pw", file.display())).is_err(), "never clobber");

        // A file that is not a container at all.
        let plain = dir.join("plain.txt");
        std::fs::write(&plain, vec![0u8; 200]).unwrap();
        let err = decrypt(&format!("{} | pw", plain.display())).unwrap_err();
        assert!(err.contains("plain.txt"), "{err}");

        // Too short to even hold a header.
        let tiny = dir.join("tiny.bin");
        std::fs::write(&tiny, b"abc").unwrap();
        assert!(decrypt(&format!("{} | pw", tiny.display())).is_err());

        std::fs::remove_dir_all(&dir).ok();
    }

    /// A container whose name does not end in .crate decrypts to "<name>.dec".
    #[test]
    fn an_unusual_extension_gets_dec_appended() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let dir = scratch("ext");
        let file = dir.join("doc.txt");
        std::fs::write(&file, b"body").unwrap();
        encrypt(&format!("{} | pw", file.display())).unwrap();

        let renamed = dir.join("container.bin");
        std::fs::rename(dir.join("doc.txt.krate"), &renamed).unwrap();
        decrypt(&format!("{} | pw", renamed.display())).unwrap();
        assert_eq!(std::fs::read(dir.join("container.bin.dec")).unwrap(), b"body");
        std::fs::remove_dir_all(&dir).ok();
    }
}
