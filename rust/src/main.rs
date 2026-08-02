//! `krate` — the Rust CLI front end.
//!
//! Exists mainly to measure the thing the port is actually for: startup latency. A utility
//! toolbox is invoked once per use, so wall-clock time is dominated by process start, not by
//! the tool's arithmetic.

use krate_core::{catalog, i18n, run};
use std::io::Read;

fn main() -> std::process::ExitCode {
    let args: Vec<String> = std::env::args().skip(1).collect();

    if args.is_empty() {
        list();
        return std::process::ExitCode::SUCCESS;
    }

    match args[0].as_str() {
        "--version" | "-v" => {
            println!("krate {} ({} tools)", env!("CARGO_PKG_VERSION"), catalog().len());
            return std::process::ExitCode::SUCCESS;
        }
        "--lang" if args.len() > 1 => {
            i18n::set_language(&args[1]);
            return std::process::ExitCode::SUCCESS;
        }
        _ => {}
    }

    // No text after the tool name means read standard input, matching the C# CLI.
    let input = if args.len() > 1 {
        args[1..].join(" ")
    } else {
        let mut buffer = String::new();
        std::io::stdin().read_to_string(&mut buffer).ok();
        buffer.trim_end_matches(['\n', '\r']).to_string()
    };

    match run(&args[0], &input) {
        Ok(text) => {
            println!("{text}");
            std::process::ExitCode::SUCCESS
        }
        Err(text) => {
            eprintln!("{text}");
            std::process::ExitCode::FAILURE
        }
    }
}

fn list() {
    println!("{}", i18n::get("Cli_Usage"));
    println!();
    for tool in catalog() {
        println!("  {:<18} {}", tool.id, tool.name());
    }
}
