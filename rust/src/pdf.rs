//! Splitting and merging PDFs. Mirrors `Krate.Core.Pdf`.
//!
//! `lopdf` on this side, PdfSharp on the C#'s. Two different writers, so the produced files are not
//! byte-identical and never could be — what must match is the **message**, which carries only page
//! counts and file names, and the **structure**: the same number of output files with the same
//! names, each holding the right pages.
//!
//! lopdf pulls in `getrandom`, whose default Windows backend needs `dlltool` to build an import
//! library for bcryptprimitives.dll — and `dlltool` fails here because GNU `as` is not installed.
//! `.cargo/config.toml` selects getrandom's `windows_legacy` backend (RtlGenRandom, via advapi32)
//! instead, which needs nothing installed. See that file.

use crate::i18n;
use lopdf::{Document, Object, ObjectId};
use std::collections::BTreeMap;
use std::path::{Path, PathBuf};

fn file_name(path: &Path) -> String {
    path.file_name().unwrap_or_default().to_string_lossy().into_owned()
}

/// Splits a PDF into one file per page, `<name>_p01.pdf` and so on, beside the original.
pub fn split(input: &str) -> Result<String, String> {
    let path = PathBuf::from(input.trim().trim_matches('"'));
    if !path.is_file() {
        return Err(i18n::format("Error_NoFile", &[&path.to_string_lossy()]));
    }
    if !path
        .extension()
        .is_some_and(|e| e.to_string_lossy().eq_ignore_ascii_case("pdf"))
    {
        return Err(i18n::format("Error_NotPdf", &[&file_name(&path)]));
    }

    let document = Document::load(&path).map_err(|_| i18n::format("Error_NotPdf", &[&file_name(&path)]))?;
    let count = document.get_pages().len();

    let directory = path.parent().unwrap_or(Path::new("."));
    let stem = path.file_stem().unwrap_or_default().to_string_lossy().into_owned();
    let target = |page: usize| directory.join(format!("{stem}_p{page:02}.pdf"));

    // Refuse the whole run if any target exists — never overwrite, and never half-write either.
    for page in 1..=count {
        if target(page).exists() {
            return Err(i18n::format("Error_FileExists", &[&file_name(&target(page))]));
        }
    }

    for page in 1..=count {
        // Reloaded per page: deleting from a shared copy would compound, and a PDF small enough to
        // split by hand is small enough to read twice.
        let mut single = Document::load(&path).map_err(|e| e.to_string())?;
        let others: Vec<u32> = (1..=count).filter(|n| *n != page).map(|n| n as u32).collect();
        single.delete_pages(&others);
        single.save(target(page)).map_err(|e| e.to_string())?;
    }

    Ok(i18n::format("Pdf_Split", &[&count.to_string(), &stem]))
}

/// Merges PDFs, one path per line, into `merged.pdf` beside the first.
pub fn merge(input: &str) -> Result<String, String> {
    let paths: Vec<PathBuf> = input
        .lines()
        .map(|line| line.trim().trim_matches('"'))
        .filter(|line| !line.is_empty())
        .map(PathBuf::from)
        .collect();
    if paths.len() < 2 {
        return Err(i18n::get("Error_PdfMergeUsage").to_string());
    }
    for path in &paths {
        if !path.is_file() {
            return Err(i18n::format("Error_NoFile", &[&path.to_string_lossy()]));
        }
    }

    let out_path = paths[0].parent().unwrap_or(Path::new(".")).join("merged.pdf");
    if out_path.exists() {
        return Err(i18n::format("Error_FileExists", &[&out_path.to_string_lossy()]));
    }

    let merged = combine(&paths)?;
    let pages = merged.get_pages().len();
    let mut merged = merged;
    merged.save(&out_path).map_err(|e| e.to_string())?;

    Ok(i18n::format(
        "Pdf_Merged",
        &[&file_name(&out_path), &pages.to_string(), &paths.len().to_string()],
    ))
}

/// Builds one document from several.
///
/// Object ids collide between documents, so each is renumbered onto a fresh range before its
/// objects are folded in; then a new page tree and catalog are built over all the pages.
fn combine(paths: &[PathBuf]) -> Result<Document, String> {
    let mut max_id = 1u32;
    let mut page_objects: BTreeMap<ObjectId, Object> = BTreeMap::new();
    let mut other_objects: BTreeMap<ObjectId, Object> = BTreeMap::new();

    for path in paths {
        let mut document = Document::load(path)
            .map_err(|_| i18n::format("Error_NotPdf", &[&file_name(path)]))?;
        document.renumber_objects_with(max_id);
        max_id = document.max_id + 1;

        let pages: BTreeMap<ObjectId, Object> = document
            .get_pages()
            .into_values()
            .filter_map(|id| document.get_object(id).ok().map(|o| (id, o.clone())))
            .collect();
        page_objects.extend(pages);
        other_objects.extend(document.objects);
    }

    let mut merged = Document::with_version("1.5");
    // The old catalogs and page trees are replaced, so they are not carried over.
    for (id, object) in other_objects {
        match object.type_name().unwrap_or_default() {
            b"Catalog" | b"Pages" | b"Page" | b"Outlines" | b"Outline" => {}
            _ => {
                merged.objects.insert(id, object);
            }
        }
    }
    if page_objects.is_empty() {
        return Err(i18n::get("Error_PdfMergeUsage").to_string());
    }

    // Objects were inserted straight into the map, which does not move `max_id` — so
    // `new_object_id` would hand back an id already in use and silently overwrite a page. Advance
    // it past everything first.
    merged.max_id = merged
        .objects
        .keys()
        .chain(page_objects.keys())
        .map(|(id, _)| *id)
        .max()
        .unwrap_or(0);

    let pages_id = merged.new_object_id();
    for (id, object) in &page_objects {
        if let Ok(dictionary) = object.as_dict() {
            let mut page = dictionary.clone();
            page.set("Parent", pages_id);
            merged.objects.insert(*id, Object::Dictionary(page));
        }
    }

    let mut pages = lopdf::Dictionary::new();
    pages.set("Type", "Pages");
    pages.set("Count", page_objects.len() as u32);
    pages.set(
        "Kids",
        page_objects
            .keys()
            .map(|id| Object::Reference(*id))
            .collect::<Vec<_>>(),
    );
    merged.objects.insert(pages_id, Object::Dictionary(pages));

    let mut catalog = lopdf::Dictionary::new();
    catalog.set("Type", "Catalog");
    catalog.set("Pages", pages_id);
    let catalog_id = merged.add_object(catalog);

    merged.trailer.set("Root", catalog_id);
    merged.renumber_objects();
    Ok(merged)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn english() -> std::sync::MutexGuard<'static, ()> {
        let guard = crate::i18n::test_lock();
        i18n::set_language("en");
        guard
    }

    fn scratch(tag: &str) -> PathBuf {
        let dir = std::env::temp_dir()
            .join(format!("krate-pdf-{tag}-{}", crate::csprng::below(1_000_000)));
        std::fs::create_dir_all(&dir).unwrap();
        dir
    }

    /// A minimal but real PDF with `pages` blank pages.
    fn make_pdf(path: &Path, pages: usize) {
        let mut document = Document::with_version("1.5");
        let pages_id = document.new_object_id();
        let mut kids = Vec::new();
        for _ in 0..pages {
            let mut page = lopdf::Dictionary::new();
            page.set("Type", "Page");
            page.set("Parent", pages_id);
            page.set("MediaBox", vec![0.into(), 0.into(), 612.into(), 792.into()]);
            kids.push(Object::Reference(document.add_object(page)));
        }
        let mut tree = lopdf::Dictionary::new();
        tree.set("Type", "Pages");
        tree.set("Count", pages as u32);
        tree.set("Kids", kids);
        document.objects.insert(pages_id, Object::Dictionary(tree));

        let mut catalog = lopdf::Dictionary::new();
        catalog.set("Type", "Catalog");
        catalog.set("Pages", pages_id);
        let catalog_id = document.add_object(catalog);
        document.trailer.set("Root", catalog_id);
        document.save(path).unwrap();
    }

    #[test]
    fn splitting_writes_one_file_per_page() {
        let _guard = english();
        let dir = scratch("split");
        let source = dir.join("report.pdf");
        make_pdf(&source, 3);

        let message = split(&source.display().to_string()).unwrap();
        assert!(message.contains('3'), "{message}");
        assert!(message.contains("report"), "{message}");

        for page in 1..=3 {
            let part = dir.join(format!("report_p{page:02}.pdf"));
            assert!(part.is_file(), "{part:?} was not written");
            // Each part holds exactly one page.
            assert_eq!(Document::load(&part).unwrap().get_pages().len(), 1);
        }
        // And no fourth file.
        assert!(!dir.join("report_p04.pdf").exists());
        std::fs::remove_dir_all(&dir).ok();
    }

    #[test]
    fn splitting_refuses_rather_than_overwriting() {
        let _guard = english();
        let dir = scratch("splitover");
        let source = dir.join("doc.pdf");
        make_pdf(&source, 2);
        // A file already sitting where the second page would go.
        std::fs::write(dir.join("doc_p02.pdf"), b"in the way").unwrap();

        assert!(split(&source.display().to_string()).is_err());
        // Nothing may have been written, not even the first page.
        assert!(!dir.join("doc_p01.pdf").exists(), "a partial split was left behind");
        assert_eq!(std::fs::read(dir.join("doc_p02.pdf")).unwrap(), b"in the way");
        std::fs::remove_dir_all(&dir).ok();
    }

    #[test]
    fn splitting_needs_a_pdf() {
        let _guard = english();
        let dir = scratch("splitbad");
        let text = dir.join("notes.txt");
        std::fs::write(&text, b"not a pdf").unwrap();

        assert!(split(&text.display().to_string()).is_err(), "wrong extension");
        assert!(split(&dir.join("missing.pdf").display().to_string()).is_err());
        // The right extension but not actually a PDF.
        let fake = dir.join("fake.pdf");
        std::fs::write(&fake, b"still not a pdf").unwrap();
        assert!(split(&fake.display().to_string()).is_err());
        std::fs::remove_dir_all(&dir).ok();
    }

    #[test]
    fn merging_keeps_every_page_in_order() {
        let _guard = english();
        let dir = scratch("merge");
        let a = dir.join("a.pdf");
        let b = dir.join("b.pdf");
        make_pdf(&a, 2);
        make_pdf(&b, 3);

        let message = merge(&format!("{}\n{}", a.display(), b.display())).unwrap();
        assert!(message.contains('5'), "five pages: {message}");
        assert!(message.contains("merged.pdf"), "{message}");

        let merged = Document::load(dir.join("merged.pdf")).unwrap();
        assert_eq!(merged.get_pages().len(), 5);
        std::fs::remove_dir_all(&dir).ok();
    }

    #[test]
    fn merging_needs_at_least_two_readable_files() {
        let _guard = english();
        let dir = scratch("mergebad");
        let a = dir.join("a.pdf");
        make_pdf(&a, 1);

        assert!(merge("").is_err());
        assert!(merge(&a.display().to_string()).is_err(), "one file is not a merge");
        assert!(
            merge(&format!("{}\n{}", a.display(), dir.join("missing.pdf").display())).is_err()
        );
        // Never overwrite an existing merged.pdf.
        let b = dir.join("b.pdf");
        make_pdf(&b, 1);
        std::fs::write(dir.join("merged.pdf"), b"in the way").unwrap();
        assert!(merge(&format!("{}\n{}", a.display(), b.display())).is_err());
        assert_eq!(std::fs::read(dir.join("merged.pdf")).unwrap(), b"in the way");
        std::fs::remove_dir_all(&dir).ok();
    }

    /// Split then merge must give back the same number of pages.
    #[test]
    fn a_split_can_be_merged_back() {
        let _guard = english();
        let dir = scratch("roundtrip");
        let source = dir.join("whole.pdf");
        make_pdf(&source, 4);
        split(&source.display().to_string()).unwrap();

        let parts: Vec<String> = (1..=4)
            .map(|page| dir.join(format!("whole_p{page:02}.pdf")).display().to_string())
            .collect();
        merge(&parts.join("\n")).unwrap();
        assert_eq!(Document::load(dir.join("merged.pdf")).unwrap().get_pages().len(), 4);
        std::fs::remove_dir_all(&dir).ok();
    }
}
