//! Machine facts and DNS lookups. Mirrors `Everyday.SysInfo` and `Dev.DnsLookup`.
//!
//! Two backends behind one API: Win32 for the desktop, and `uname`/`sysconf`/`statvfs` for
//! Android and Linux. The output shape is identical; only the sources differ. The one behaviour
//! difference is the DNS reverse lookup — see `reverse_name`.
//!
//! Both read the environment, so their output is machine-specific by nature — the parity tests
//! compare the lines that can be compared and the shape of the rest.
//!
//! One line genuinely cannot be produced here: `RUNTIME    .NET 8.0.29` reports the *host runtime*,
//! which a Rust core has no way to query. Rather than print something false or drop the line, the
//! shell tells the core what it is running on through `krate_set_runtime`, exactly as it already
//! tells it the language. Standalone (the Rust CLI), the default is honest about what it is.

use std::sync::RwLock;

/// What to print on the RUNTIME line. The C# shell sets this to `.NET {Environment.Version}`.
static RUNTIME: RwLock<Option<String>> = RwLock::new(None);

pub fn set_runtime(text: &str) {
    if let Ok(mut slot) = RUNTIME.write() {
        *slot = Some(text.to_string());
    }
}

fn runtime() -> String {
    RUNTIME
        .read()
        .ok()
        .and_then(|slot| slot.clone())
        .unwrap_or_else(|| "Rust".to_string())
}

#[cfg(windows)]
#[repr(C)]
struct OsVersionInfo {
    size: u32,
    major: u32,
    minor: u32,
    build: u32,
    platform_id: u32,
    /// Service-pack string. Unread, but it is part of the struct the API expects.
    csd_version: [u16; 128],
}

#[cfg(windows)]
impl Default for OsVersionInfo {
    fn default() -> Self {
        Self { size: 0, major: 0, minor: 0, build: 0, platform_id: 0, csd_version: [0; 128] }
    }
}

#[cfg(windows)]
#[repr(C)]
#[derive(Default)]
struct MemoryStatus {
    length: u32,
    memory_load: u32,
    total_physical: u64,
    available_physical: u64,
    total_page_file: u64,
    available_page_file: u64,
    total_virtual: u64,
    available_virtual: u64,
    available_extended_virtual: u64,
}

#[cfg(windows)]
#[repr(C)]
#[derive(Default)]
struct SystemInfo {
    processor_architecture: u16,
    reserved: u16,
    page_size: u32,
    minimum_application_address: usize,
    maximum_application_address: usize,
    active_processor_mask: usize,
    number_of_processors: u32,
    processor_type: u32,
    allocation_granularity: u32,
    processor_level: u16,
    processor_revision: u16,
}

#[cfg(windows)]
#[link(name = "ntdll")]
unsafe extern "system" {
    /// The real version, unaffected by the application manifest that caps `GetVersionEx`.
    fn RtlGetVersion(info: *mut OsVersionInfo) -> i32;
}

#[cfg(windows)]
#[link(name = "kernel32")]
unsafe extern "system" {
    fn GetComputerNameW(buffer: *mut u16, size: *mut u32) -> i32;
    fn GlobalMemoryStatusEx(status: *mut MemoryStatus) -> i32;
    fn GetNativeSystemInfo(info: *mut SystemInfo);
    fn GetLogicalDriveStringsW(length: u32, buffer: *mut u16) -> u32;
    fn GetDiskFreeSpaceExW(
        directory: *const u16,
        free_bytes_available_to_caller: *mut u64,
        total_bytes: *mut u64,
        total_free_bytes: *mut u64,
    ) -> i32;
}

#[cfg(windows)]
/// `RuntimeInformation.OSDescription`, which on Windows is "Microsoft Windows major.minor.build".
fn os_description() -> String {
    let mut info = OsVersionInfo {
        size: std::mem::size_of::<OsVersionInfo>() as u32,
        ..Default::default()
    };
    // SAFETY: the struct is ours and its size field is set as the API requires.
    unsafe { RtlGetVersion(&mut info) };
    format!("Microsoft Windows {}.{}.{}", info.major, info.minor, info.build)
}

#[cfg(windows)]
/// `RuntimeInformation.OSArchitecture`, spelled as .NET's `Architecture` enum.
fn os_architecture() -> &'static str {
    let mut info = SystemInfo::default();
    // SAFETY: writes into a struct we own.
    unsafe { GetNativeSystemInfo(&mut info) };
    match info.processor_architecture {
        9 => "X64",
        0 => "X86",
        12 => "Arm64",
        5 => "Arm",
        _ => "X64",
    }
}

#[cfg(windows)]
fn machine_name() -> String {
    // MAX_COMPUTERNAME_LENGTH is 15, but the call wants room for the NUL.
    let mut buffer = [0u16; 256];
    let mut size = buffer.len() as u32;
    // SAFETY: the buffer and the size are ours, and size is the element count as documented.
    let ok = unsafe { GetComputerNameW(buffer.as_mut_ptr(), &mut size) };
    if ok == 0 {
        return String::new();
    }
    String::from_utf16_lossy(&buffer[..size as usize])
}

#[cfg(windows)]
fn total_memory() -> u64 {
    let mut status = MemoryStatus {
        length: std::mem::size_of::<MemoryStatus>() as u32,
        ..Default::default()
    };
    // SAFETY: the struct is ours and its length field is set as the API requires.
    unsafe { GlobalMemoryStatusEx(&mut status) };
    status.total_physical
}

#[cfg(windows)]
/// Ready drives, as `DriveInfo.GetDrives().Where(d => d.IsReady)` yields them: alphabetical, and
/// only the ones that answer a free-space query — which is what "ready" means in practice.
fn drives() -> Vec<(String, u64, u64)> {
    let mut buffer = [0u16; 512];
    // SAFETY: the buffer is ours and its length is passed as the API requires.
    let written = unsafe { GetLogicalDriveStringsW(buffer.len() as u32, buffer.as_mut_ptr()) };
    if written == 0 {
        return Vec::new();
    }

    let mut found = Vec::new();
    for chunk in buffer[..written as usize].split(|unit| *unit == 0) {
        if chunk.is_empty() {
            continue;
        }
        let name = String::from_utf16_lossy(chunk);
        let mut wide: Vec<u16> = chunk.to_vec();
        wide.push(0);
        let (mut free, mut total, mut total_free) = (0u64, 0u64, 0u64);
        // SAFETY: `wide` is NUL-terminated and the outputs are ours.
        let ok = unsafe {
            GetDiskFreeSpaceExW(wide.as_ptr(), &mut free, &mut total, &mut total_free)
        };
        // A drive with no media fails here, which is exactly the not-ready case.
        if ok != 0 {
            found.push((name, free, total));
        }
    }
    found
}

/// `Everyday.Bytes`: 1024-based, invariant, at most one decimal.
pub(crate) fn bytes(value: u64) -> String {
    const UNITS: [&str; 5] = ["B", "KB", "MB", "GB", "TB"];
    let mut size = value as f64;
    let mut unit = 0;
    while size >= 1024.0 && unit < UNITS.len() - 1 {
        size /= 1024.0;
        unit += 1;
    }
    // "0.#" rounds half away from zero and drops a trailing zero.
    let rounded = (size * 10.0).round() / 10.0;
    let mut text = format!("{rounded:.1}");
    if text.ends_with(".0") {
        text.truncate(text.len() - 2);
    }
    format!("{text} {}", UNITS[unit])
}

/// The POSIX side of the same facts.
///
/// Android and Linux answer these from `uname`, `sysconf` and the filesystem rather than Win32.
/// The shapes are kept identical so `sys_info` below is platform-agnostic — only the sources differ.
#[cfg(not(windows))]
mod posix {
    /// `uname -sr`, which is the closest analogue of `RuntimeInformation.OSDescription`.
    pub fn os_description() -> String {
        let mut buf: libc::utsname = unsafe { std::mem::zeroed() };
        // SAFETY: `buf` is ours and fully written by uname on success.
        if unsafe { libc::uname(&mut buf) } != 0 {
            return "Unknown".to_string();
        }
        let take = |field: &[libc::c_char]| {
            let bytes: Vec<u8> = field
                .iter()
                .take_while(|c| **c != 0)
                .map(|c| *c as u8)
                .collect();
            String::from_utf8_lossy(&bytes).into_owned()
        };
        format!("{} {}", take(&buf.sysname), take(&buf.release))
    }

    /// Spelled as .NET's `Architecture` enum, so both platforms print the same vocabulary.
    pub fn os_architecture() -> &'static str {
        match std::env::consts::ARCH {
            "x86_64" => "X64",
            "x86" => "X86",
            "aarch64" => "Arm64",
            "arm" => "Arm",
            other => {
                let _ = other;
                "Arm64"
            }
        }
    }

    pub fn machine_name() -> String {
        let mut buf = [0i8; 256];
        // SAFETY: the buffer is ours and its length is passed as the API requires.
        let ok = unsafe { libc::gethostname(buf.as_mut_ptr() as *mut libc::c_char, buf.len()) };
        if ok != 0 {
            return String::new();
        }
        let bytes: Vec<u8> = buf.iter().take_while(|c| **c != 0).map(|c| *c as u8).collect();
        String::from_utf8_lossy(&bytes).into_owned()
    }

    /// Total physical memory, from `sysconf`. `GlobalMemoryStatusEx`'s counterpart.
    pub fn total_memory() -> u64 {
        // SAFETY: sysconf takes an int and returns a long; no pointers involved.
        let pages = unsafe { libc::sysconf(libc::_SC_PHYS_PAGES) };
        let page_size = unsafe { libc::sysconf(libc::_SC_PAGESIZE) };
        if pages <= 0 || page_size <= 0 {
            return 0;
        }
        pages as u64 * page_size as u64
    }

    /// The mount points worth reporting. There are no drive letters, so this reports the roots a
    /// user of the app can actually write to, which is the same question "DISK C:\" answers.
    pub fn drives() -> Vec<(String, u64, u64)> {
        let mut found = Vec::new();
        for point in ["/", "/data", "/storage/emulated/0"] {
            let path = std::ffi::CString::new(point).expect("no interior NUL in a literal");
            let mut stat: libc::statvfs = unsafe { std::mem::zeroed() };
            // SAFETY: the path is NUL-terminated and `stat` is ours.
            if unsafe { libc::statvfs(path.as_ptr(), &mut stat) } != 0 {
                continue;
            }
            let block = stat.f_frsize as u64;
            let total = stat.f_blocks as u64 * block;
            let free = stat.f_bavail as u64 * block;
            if total > 0 {
                found.push((point.to_string(), free, total));
            }
        }
        found
    }
}

#[cfg(not(windows))]
use posix::{drives, machine_name, os_architecture, os_description, total_memory};

pub fn sys_info(_: &str) -> Result<String, String> {
    let mut lines = vec![
        format!("OS         {}", os_description()),
        format!("ARCH       {}", os_architecture()),
        format!("RUNTIME    {}", runtime()),
        format!("MACHINE    {}", machine_name()),
        format!("CPU CORES  {}", processor_count()),
        format!("MEMORY     {}", bytes(total_memory())),
    ];
    for (name, free, total) in drives() {
        lines.push(format!("DISK {name:<6} {} free / {}", bytes(free), bytes(total)));
    }
    Ok(lines.join("\n"))
}

/// `Environment.ProcessorCount`. Both this and .NET honour process affinity, so they agree.
fn processor_count() -> usize {
    std::thread::available_parallelism().map(|n| n.get()).unwrap_or(1)
}

#[cfg(windows)]
#[link(name = "ws2_32")]
unsafe extern "system" {
    fn getnameinfo(
        address: *const u8,
        address_length: i32,
        host: *mut u8,
        host_length: u32,
        service: *mut u8,
        service_length: u32,
        flags: i32,
    ) -> i32;
}

#[cfg(windows)]
/// The canonical host name for an address, as `Dns.GetHostEntry` reports it.
///
/// .NET resolves the name, then reverse-resolves the first address to get `HostName` — which is why
/// looking up "localhost" reports the machine's own name rather than "localhost".
fn reverse_name(address: &std::net::SocketAddr) -> Option<String> {
    // Lay out the sockaddr by hand: only the family, port and address are read.
    let mut storage = [0u8; 128];
    let length: i32 = match address {
        std::net::SocketAddr::V4(v4) => {
            storage[0..2].copy_from_slice(&2u16.to_le_bytes()); // AF_INET
            storage[2..4].copy_from_slice(&v4.port().to_be_bytes());
            storage[4..8].copy_from_slice(&v4.ip().octets());
            16
        }
        std::net::SocketAddr::V6(v6) => {
            storage[0..2].copy_from_slice(&23u16.to_le_bytes()); // AF_INET6
            storage[2..4].copy_from_slice(&v6.port().to_be_bytes());
            storage[8..24].copy_from_slice(&v6.ip().octets());
            28
        }
    };

    let mut host = [0u8; 1025];
    // SAFETY: both buffers are ours, and the lengths passed match their sizes. Winsock is already
    // started up: resolving the address above went through std, which does that.
    let result = unsafe {
        getnameinfo(
            storage.as_ptr(),
            length,
            host.as_mut_ptr(),
            host.len() as u32,
            std::ptr::null_mut(),
            0,
            0,
        )
    };
    if result != 0 {
        return None;
    }
    let end = host.iter().position(|b| *b == 0).unwrap_or(host.len());
    Some(String::from_utf8_lossy(&host[..end]).into_owned())
}

/// Without ws2_32's `getnameinfo` there is no reverse lookup here; the queried name is reported
/// instead. `Dns.GetHostEntry` would resolve the machine's own name for a loopback address, so this
/// is a real difference on non-Windows — recorded rather than papered over, since the alternative is
/// binding getnameinfo per platform for one line of output.
#[cfg(not(windows))]
fn reverse_name(_address: &std::net::SocketAddr) -> Option<String> {
    None
}

pub fn dns_lookup(input: &str) -> Result<String, String> {
    let domain = input
        .trim()
        .replace("https://", "")
        .replace("http://", "");
    let domain = domain.split('/').next().unwrap_or("").to_string();

    // Not localized in the C# either: a hardcoded English sentence, returned rather than thrown.
    let failed = || Ok(format!("DNS lookup failed for {domain}"));

    use std::net::ToSocketAddrs;
    let Ok(addresses) = (domain.as_str(), 0u16).to_socket_addrs() else {
        return failed();
    };
    let addresses: Vec<std::net::SocketAddr> = addresses.collect();
    if addresses.is_empty() {
        return failed();
    }

    let host = match reverse_name(&addresses[0]) {
        Some(name) => name,
        None => domain.clone(),
    };
    // Built with AppendLine then Trim()ed, so the separators are Environment.NewLine.
    let mut lines = vec![format!("Host: {host}")];
    for address in &addresses {
        // .NET prints a link-local IPv6 address with its scope id ("fe80::1%41"); Rust's Display
        // for Ipv6Addr drops it, so it is put back here.
        let text = match address {
            std::net::SocketAddr::V6(v6) if v6.scope_id() != 0 => {
                format!("{}%{}", v6.ip(), v6.scope_id())
            }
            other => other.ip().to_string(),
        };
        lines.push(format!("IP:   {text}"));
    }
    Ok(lines.join(crate::tools::newline()))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn byte_sizes_match_the_cs_formatter() {
        assert_eq!(bytes(0), "0 B");
        assert_eq!(bytes(512), "512 B");
        assert_eq!(bytes(1024), "1 KB");
        assert_eq!(bytes(1536), "1.5 KB");
        // 15.7 GB, the shape the probe showed.
        assert_eq!(bytes(16_858_726_400), "15.7 GB");
        // Rounding: 0.# keeps one decimal and drops a trailing zero.
        assert_eq!(bytes(1024 * 1024), "1 MB");
        assert_eq!(bytes(1024u64.pow(4)), "1 TB");
        // Past the last unit it keeps growing in TB rather than inventing one.
        assert!(bytes(1024u64.pow(5)).ends_with(" TB"));
    }

    #[test]
    fn the_runtime_line_is_settable_and_has_an_honest_default() {
        let _guard = crate::i18n::test_lock();
        // The default says what it actually is, rather than claiming a .NET version.
        set_runtime("");
        if let Ok(mut slot) = RUNTIME.write() {
            *slot = None;
        }
        assert_eq!(runtime(), "Rust");

        set_runtime(".NET 8.0.29");
        assert_eq!(runtime(), ".NET 8.0.29");
        assert!(sys_info("").unwrap().contains("RUNTIME    .NET 8.0.29"));
        if let Ok(mut slot) = RUNTIME.write() {
            *slot = None;
        }
    }

    #[test]
    fn sys_info_reports_every_expected_field() {
        let _guard = crate::i18n::test_lock();
        let report = sys_info("").unwrap();
        let lines: Vec<&str> = report.lines().collect();
        assert!(lines[0].starts_with("OS         Microsoft Windows "), "{}", lines[0]);
        assert!(lines[1].starts_with("ARCH       "), "{}", lines[1]);
        assert!(lines[2].starts_with("RUNTIME    "), "{}", lines[2]);
        assert!(lines[3].starts_with("MACHINE    "), "{}", lines[3]);
        assert!(lines[4].starts_with("CPU CORES  "), "{}", lines[4]);
        assert!(lines[5].starts_with("MEMORY     "), "{}", lines[5]);

        // A machine name and a plausible core count.
        assert!(!lines[3]["MACHINE    ".len()..].trim().is_empty());
        let cores: usize = lines[4]["CPU CORES  ".len()..].trim().parse().unwrap();
        assert!((1..=1024).contains(&cores), "{cores}");
        // At least one drive answered, and its line is shaped as the C# writes it.
        assert!(lines.len() > 6, "no drives listed:\n{report}");
        for line in &lines[6..] {
            assert!(line.starts_with("DISK "), "{line}");
            assert!(line.contains(" free / "), "{line}");
        }
    }

    #[test]
    fn dns_resolves_localhost_and_reports_addresses() {
        let _guard = crate::i18n::test_lock();
        let out = dns_lookup("localhost").unwrap();
        assert!(out.starts_with("Host: "), "{out}");
        assert!(out.contains("IP:   "), "{out}");
        // Loopback resolves to at least one of the two loopback addresses.
        assert!(out.contains("127.0.0.1") || out.contains("::1"), "{out}");
    }

    #[test]
    fn dns_strips_a_scheme_and_a_path() {
        let _guard = crate::i18n::test_lock();
        // The same host, however it is written.
        let plain = dns_lookup("localhost").unwrap();
        for written in ["http://localhost", "https://localhost", "localhost/some/path", "  localhost  "] {
            assert_eq!(dns_lookup(written).unwrap(), plain, "{written}");
        }
    }

    /// A name that cannot resolve is reported, not thrown — and the message is the C#'s wording.
    #[test]
    fn an_unresolvable_name_is_reported() {
        let _guard = crate::i18n::test_lock();
        let out = dns_lookup("nonexistent.invalid").unwrap();
        assert_eq!(out, "DNS lookup failed for nonexistent.invalid");
        // Empty input is NOT a failure: like Dns.GetHostEntry(""), it resolves to this machine.
        let local = dns_lookup("").unwrap();
        assert!(local.starts_with("Host: "), "{local}");
        assert!(local.contains("IP:   "), "{local}");
    }
}
