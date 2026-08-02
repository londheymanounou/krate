//! C ABI for the shells. The WinUI/C# shell reaches this with P/Invoke; Android will reach the
//! same functions over JNI.
//!
//! Every string handed out is owned by Rust and must come back to `krate_free`. Returning an
//! allocation the caller frees with its own allocator is the classic way to corrupt the heap
//! across an FFI boundary, so there is exactly one way in and one way out.

use crate::{i18n, tools};
use std::ffi::{CStr, CString, c_char};

/// Result of a tool run. `ok == 0` means `text` holds the error message rather than output —
/// the shells show it the same way either way, but must not treat it as a result.
#[repr(C)]
pub struct KrateResult {
    pub ok: i32,
    pub text: *mut c_char,
}

/// Reads a caller-owned UTF-8 C string. Returns None for null or invalid UTF-8 rather than
/// panicking: a panic unwinding into C# is undefined behaviour.
///
/// # Safety
/// `ptr` must be null or point to a NUL-terminated string that stays valid for this call.
unsafe fn borrow(ptr: *const c_char) -> Option<&'static str> {
    if ptr.is_null() {
        return None;
    }
    unsafe { CStr::from_ptr(ptr) }.to_str().ok()
}

fn hand_out(text: String) -> *mut c_char {
    // A NUL inside the text would truncate it on the C side; replace rather than fail, since
    // this is display text and losing the tail silently is worse than showing a marker.
    CString::new(text.replace('\0', "\u{fffd}"))
        .unwrap_or_default()
        .into_raw()
}

/// Runs a tool by id. Free `text` with `krate_free`.
///
/// # Safety
/// `id` and `input` must be null or valid NUL-terminated UTF-8.
#[no_mangle]
pub unsafe extern "C" fn krate_run(id: *const c_char, input: *const c_char) -> KrateResult {
    let Some(id) = (unsafe { borrow(id) }) else {
        return KrateResult { ok: 0, text: hand_out("invalid tool id".into()) };
    };
    let input = unsafe { borrow(input) }.unwrap_or("");

    match tools::run(id, input) {
        Ok(text) => KrateResult { ok: 1, text: hand_out(text) },
        Err(text) => KrateResult { ok: 0, text: hand_out(text) },
    }
}

/// Sets the interface language for subsequent calls.
///
/// # Safety
/// `language` must be null or valid NUL-terminated UTF-8.
#[no_mangle]
pub unsafe extern "C" fn krate_set_language(language: *const c_char) {
    if let Some(language) = unsafe { borrow(language) } {
        i18n::set_language(language);
    }
}

/// Tells the core what runtime it is hosted in, for `SysInfo`'s RUNTIME line — the one fact a
/// library cannot discover about its host. The C# shell passes `.NET {Environment.Version}`.
/// Without this the line reads "Rust", which is true of the standalone CLI.
///
/// # Safety
/// `text` must be null or valid NUL-terminated UTF-8.
#[no_mangle]
pub unsafe extern "C" fn krate_set_runtime(text: *const c_char) {
    if let Some(text) = unsafe { borrow(text) } {
        crate::system::set_runtime(text);
    }
}

/// Hands the core a currency rate table the host fetched, in the provider's own JSON.
///
/// Only Windows fetches inside the core (WinHTTP); every other host does its own HTTP and calls
/// this. Returns 1 on success, 0 if the body was not a successful response — so a failed fetch or
/// an error page cannot poison the cache.
///
/// # Safety
/// `base` and `json` must be null or valid NUL-terminated UTF-8.
#[no_mangle]
pub unsafe extern "C" fn krate_currency_store_rates(
    base: *const c_char,
    json: *const c_char,
) -> i32 {
    let (Some(base), Some(json)) = (unsafe { borrow(base) }, unsafe { borrow(json) }) else {
        return 0;
    };
    match crate::currency::store_rates(base, json) {
        Ok(()) => 1,
        Err(_) => 0,
    }
}

/// Number of tools in the catalogue.
#[no_mangle]
pub extern "C" fn krate_tool_count() -> i32 {
    tools::catalog().len() as i32
}

/// Resolves a caller-supplied index. `try_from` rejects negatives outright — clamping them to 0
/// would hand back the first tool and quietly hide the caller's bug.
fn at(index: i32) -> Option<&'static tools::Tool> {
    usize::try_from(index).ok().and_then(|i| tools::catalog().get(i))
}

/// Tool id at `index`, or null if out of range. Free with `krate_free`.
#[no_mangle]
pub extern "C" fn krate_tool_id(index: i32) -> *mut c_char {
    match at(index) {
        Some(tool) => hand_out(tool.id.to_string()),
        None => std::ptr::null_mut(),
    }
}

/// Localized name of the tool at `index`, or null. Free with `krate_free`.
#[no_mangle]
pub extern "C" fn krate_tool_name(index: i32) -> *mut c_char {
    match at(index) {
        Some(tool) => hand_out(tool.name()),
        None => std::ptr::null_mut(),
    }
}

/// Generates a password with explicit character classes. Returns null if the length is out of
/// range (1..=4096) or every class is off. Free with `krate_free`.
///
/// The `Password` tool itself takes only a length and forces all four classes on, because its
/// input contract is shared with the CLI and the C# and is parity-tested. This exposes the
/// underlying generator instead of widening that contract.
#[no_mangle]
pub extern "C" fn krate_password(
    length: i64,
    upper: bool,
    lower: bool,
    digits: bool,
    symbols: bool,
) -> *mut c_char {
    match crate::generators::password_from(length, upper, lower, digits, symbols) {
        Ok(text) => hand_out(text),
        Err(_) => std::ptr::null_mut(),
    }
}

/// Raw, unlocalized category key of the tool at `index` (`"Image"`, `"Date"`, ...), or null.
/// Callers that need to *branch* on category — picking an icon, grouping, filtering — must use
/// this rather than [`krate_tool_category_name`], whose result is a display string that changes
/// with the active language and would silently stop matching in 16 of the 17. Free with
/// `krate_free`.
#[no_mangle]
pub extern "C" fn krate_tool_category(index: i32) -> *mut c_char {
    match at(index) {
        Some(tool) => hand_out(tool.category.to_string()),
        None => std::ptr::null_mut(),
    }
}

/// Localized category name of the tool at `index`, for *display* only, or null. Free with
/// `krate_free`.
#[no_mangle]
pub extern "C" fn krate_tool_category_name(index: i32) -> *mut c_char {
    match at(index) {
        Some(tool) => hand_out(tool.category_name()),
        None => std::ptr::null_mut(),
    }
}

/// Releases a string returned by this library. Safe to call with null.
///
/// # Safety
/// `text` must be null or a pointer this library returned, freed exactly once.
#[no_mangle]
pub unsafe extern "C" fn krate_free(text: *mut c_char) {
    if !text.is_null() {
        drop(unsafe { CString::from_raw(text) });
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn take(ptr: *mut c_char) -> String {
        assert!(!ptr.is_null());
        let text = unsafe { CStr::from_ptr(ptr) }.to_str().unwrap().to_string();
        unsafe { krate_free(ptr) };
        text
    }

    #[test]
    fn run_returns_output_and_frees_cleanly() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let id = CString::new("Upper").unwrap();
        let input = CString::new("hello").unwrap();
        let result = unsafe { krate_run(id.as_ptr(), input.as_ptr()) };
        assert_eq!(result.ok, 1);
        assert_eq!(take(result.text), "HELLO");
    }

    #[test]
    fn an_unknown_tool_reports_an_error_rather_than_output() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let id = CString::new("NoSuchTool").unwrap();
        let input = CString::new("").unwrap();
        let result = unsafe { krate_run(id.as_ptr(), input.as_ptr()) };
        assert_eq!(result.ok, 0);
        assert!(!take(result.text).is_empty());
    }

    #[test]
    fn null_pointers_are_survivable() {
        let result = unsafe { krate_run(std::ptr::null(), std::ptr::null()) };
        assert_eq!(result.ok, 0);
        take(result.text);
        unsafe { krate_free(std::ptr::null_mut()) }; // must not crash
        assert!(krate_tool_id(-1).is_null());
        assert!(krate_tool_id(9999).is_null());
    }

    #[test]
    fn the_catalogue_is_enumerable_across_the_boundary() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let count = krate_tool_count();
        assert!(count > 0);
        for index in 0..count {
            assert!(!take(krate_tool_id(index)).is_empty());
            assert!(!take(krate_tool_name(index)).is_empty());
        }
    }

    #[test]
    fn language_set_over_ffi_changes_output() {
        let _guard = crate::i18n::test_lock();
        let french = CString::new("fr").unwrap();
        unsafe { krate_set_language(french.as_ptr()) };
        assert_eq!(i18n::get("Error_NoFile"), "Fichier introuvable : {0}");
        let english = CString::new("en").unwrap();
        unsafe { krate_set_language(english.as_ptr()) };
    }
}
