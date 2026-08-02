//! XML formatting and well-formedness checking. Mirrors `Dev.XmlFormat` and `Dev.XmlValidate`.
//!
//! No crate does this: the output has to match `XDocument.Parse(.., PreserveWhitespace)` piped
//! through `XmlWriter` with `Indent = true`, and every rule below was derived by probing the C#
//! build rather than read off a spec.
//!
//!  * Two-space indent, CRLF line ends. An empty element closes as `<a />`, **with a space**.
//!  * `<a/>` and `<a></a>` are different documents. LINQ to XML stores no content for the first
//!    and an empty string for the second, so the first round-trips to `<a />` and the second
//!    stays `<a></a>`. The parser therefore has to remember which spelling it saw.
//!  * Indenting stops for the remainder of an element as soon as any text is written into it.
//!    That is why `<a><b/>text</a>` comes back as `<a>\r\n  <b />text</a>` — the `<b/>` was
//!    indented because no text had been seen yet, and the close tag was not because it had.
//!    `PreserveWhitespace` makes the whitespace between tags a text node, so an already-indented
//!    document is left exactly as it was rather than reindented.
//!  * The declaration is emitted only if the input had one, and always with `encoding="utf-16"`
//!    because the writer targets a `StringBuilder`.
//!  * Character references are resolved (`&#65;` becomes `A`); the five named entities are
//!    re-escaped on the way out. In text `<`, `>` and `&` are escaped but `"` is not; in an
//!    attribute `"` becomes `&quot;` and a newline becomes `&#xA;`.

use crate::i18n;

/// Where parsing stopped, 1-based, as `XmlException` reports it.
struct Position {
    line: usize,
    column: usize,
}

struct Error {
    at: Position,
}

type Parsed<T> = Result<T, Error>;

enum Node {
    Element(Element),
    Text(String),
    CData(String),
    Comment(String),
    /// `<?name data?>`
    Instruction { name: String, data: String },
}

struct Element {
    name: String,
    attributes: Vec<(String, String)>,
    children: Vec<Node>,
    /// True for `<a/>`, false for `<a></a>` — a distinction the output preserves.
    self_closed: bool,
}

struct Declaration {
    version: String,
    standalone: Option<String>,
}

struct Document {
    declaration: Option<Declaration>,
    doctype: Option<String>,
    nodes: Vec<Node>,
}

/// Character-oriented cursor that keeps track of line and column for error reporting.
struct Cursor {
    chars: Vec<char>,
    index: usize,
    line: usize,
    column: usize,
}

impl Cursor {
    fn new(source: &str) -> Self {
        Self { chars: source.chars().collect(), index: 0, line: 1, column: 1 }
    }

    fn peek(&self) -> Option<char> {
        self.chars.get(self.index).copied()
    }

    fn peek_at(&self, offset: usize) -> Option<char> {
        self.chars.get(self.index + offset).copied()
    }

    fn starts_with(&self, text: &str) -> bool {
        text.chars().enumerate().all(|(i, c)| self.peek_at(i) == Some(c))
    }

    fn bump(&mut self) -> Option<char> {
        let c = self.peek()?;
        self.index += 1;
        if c == '\n' {
            self.line += 1;
            self.column = 1;
        } else {
            self.column += 1;
        }
        Some(c)
    }

    fn eat(&mut self, text: &str) -> bool {
        if self.starts_with(text) {
            for _ in text.chars() {
                self.bump();
            }
            true
        } else {
            false
        }
    }

    fn at(&self) -> Position {
        Position { line: self.line, column: self.column }
    }

    fn fail<T>(&self) -> Parsed<T> {
        Err(Error { at: self.at() })
    }

    fn skip_whitespace(&mut self) {
        while self.peek().is_some_and(|c| c.is_whitespace()) {
            self.bump();
        }
    }
}

/// `NameStartChar`/`NameChar`, loosely: enough to accept every name .NET does without pulling in
/// the full production, since a malformed name simply becomes a parse error either way.
fn is_name_start(c: char) -> bool {
    c.is_alphabetic() || c == '_' || c == ':'
}

fn is_name_char(c: char) -> bool {
    is_name_start(c) || c.is_numeric() || c == '-' || c == '.'
}

fn parse_name(cursor: &mut Cursor) -> Parsed<String> {
    if !cursor.peek().is_some_and(is_name_start) {
        return cursor.fail();
    }
    let mut name = String::new();
    while cursor.peek().is_some_and(is_name_char) {
        name.push(cursor.bump().unwrap());
    }
    Ok(name)
}

/// Resolves the five named entities plus decimal and hex character references. Anything else is
/// an undeclared entity, which .NET rejects too.
fn parse_reference(cursor: &mut Cursor) -> Parsed<char> {
    let start = cursor.at();
    cursor.bump(); // '&'
    if cursor.eat("#") {
        let hex = cursor.eat("x") || cursor.eat("X");
        let mut digits = String::new();
        while cursor.peek().is_some_and(|c| c.is_ascii_alphanumeric()) {
            digits.push(cursor.bump().unwrap());
        }
        if !cursor.eat(";") || digits.is_empty() {
            return Err(Error { at: start });
        }
        let value = u32::from_str_radix(&digits, if hex { 16 } else { 10 })
            .map_err(|_| Error { at: Position { line: start.line, column: start.column } })?;
        return char::from_u32(value).ok_or(Error { at: start });
    }
    let mut name = String::new();
    while cursor.peek().is_some_and(|c| c != ';' && !c.is_whitespace()) {
        name.push(cursor.bump().unwrap());
    }
    if !cursor.eat(";") {
        return Err(Error { at: start });
    }
    match name.as_str() {
        "lt" => Ok('<'),
        "gt" => Ok('>'),
        "amp" => Ok('&'),
        "quot" => Ok('"'),
        "apos" => Ok('\''),
        _ => Err(Error { at: start }),
    }
}

fn parse_attribute_value(cursor: &mut Cursor) -> Parsed<String> {
    let quote = match cursor.peek() {
        Some(q @ ('"' | '\'')) => {
            cursor.bump();
            q
        }
        _ => return cursor.fail(),
    };
    let mut value = String::new();
    loop {
        match cursor.peek() {
            None => return cursor.fail(),
            Some(c) if c == quote => {
                cursor.bump();
                return Ok(value);
            }
            Some('<') => return cursor.fail(), // never allowed raw in an attribute
            Some('&') => value.push(parse_reference(cursor)?),
            Some(_) => value.push(cursor.bump().unwrap()),
        }
    }
}

fn parse_attributes(cursor: &mut Cursor) -> Parsed<Vec<(String, String)>> {
    let mut attributes = Vec::new();
    loop {
        cursor.skip_whitespace();
        if !cursor.peek().is_some_and(is_name_start) {
            return Ok(attributes);
        }
        let name = parse_name(cursor)?;
        cursor.skip_whitespace();
        if !cursor.eat("=") {
            return cursor.fail();
        }
        cursor.skip_whitespace();
        let value = parse_attribute_value(cursor)?;
        if attributes.iter().any(|(existing, _): &(String, String)| *existing == name) {
            return cursor.fail(); // duplicate attribute
        }
        attributes.push((name, value));
    }
}

/// Everything up to `-->`.
fn parse_comment(cursor: &mut Cursor) -> Parsed<String> {
    let mut body = String::new();
    while !cursor.starts_with("-->") {
        match cursor.bump() {
            Some(c) => body.push(c),
            None => return cursor.fail(),
        }
    }
    cursor.eat("-->");
    Ok(body)
}

fn parse_cdata(cursor: &mut Cursor) -> Parsed<String> {
    let mut body = String::new();
    while !cursor.starts_with("]]>") {
        match cursor.bump() {
            Some(c) => body.push(c),
            None => return cursor.fail(),
        }
    }
    cursor.eat("]]>");
    Ok(body)
}

fn parse_instruction(cursor: &mut Cursor) -> Parsed<Node> {
    let name = parse_name(cursor)?;
    let mut data = String::new();
    cursor.skip_whitespace();
    while !cursor.starts_with("?>") {
        match cursor.bump() {
            Some(c) => data.push(c),
            None => return cursor.fail(),
        }
    }
    cursor.eat("?>");
    Ok(Node::Instruction { name, data: data.trim_end().to_string() })
}

/// `<!DOCTYPE …>`, kept as the raw text between the name and the closing `>`.
fn parse_doctype(cursor: &mut Cursor) -> Parsed<String> {
    cursor.skip_whitespace();
    let name = parse_name(cursor)?;
    // Skip to the closing '>', stepping over an internal subset if present.
    loop {
        match cursor.peek() {
            None => return cursor.fail(),
            Some('[') => {
                while !cursor.starts_with("]") {
                    if cursor.bump().is_none() {
                        return cursor.fail();
                    }
                }
                cursor.eat("]");
            }
            Some('>') => {
                cursor.bump();
                return Ok(name);
            }
            Some(_) => {
                cursor.bump();
            }
        }
    }
}

fn parse_element(cursor: &mut Cursor) -> Parsed<Element> {
    cursor.eat("<");
    let name = parse_name(cursor)?;
    let attributes = parse_attributes(cursor)?;
    cursor.skip_whitespace();

    if cursor.eat("/>") {
        return Ok(Element { name, attributes, children: Vec::new(), self_closed: true });
    }
    if !cursor.eat(">") {
        return cursor.fail();
    }

    let mut children = Vec::new();
    loop {
        if cursor.peek().is_none() {
            return cursor.fail(); // unclosed element
        }
        if cursor.starts_with("</") {
            let close_at = cursor.at();
            cursor.eat("</");
            let closing = parse_name(cursor)?;
            cursor.skip_whitespace();
            if !cursor.eat(">") || closing != name {
                return Err(Error { at: close_at });
            }
            return Ok(Element { name, attributes, children, self_closed: false });
        }
        children.push(parse_node(cursor)?);
    }
}

fn parse_node(cursor: &mut Cursor) -> Parsed<Node> {
    if cursor.starts_with("<!--") {
        cursor.eat("<!--");
        return Ok(Node::Comment(parse_comment(cursor)?));
    }
    if cursor.starts_with("<![CDATA[") {
        cursor.eat("<![CDATA[");
        return Ok(Node::CData(parse_cdata(cursor)?));
    }
    if cursor.starts_with("<?") {
        cursor.eat("<?");
        return parse_instruction(cursor);
    }
    if cursor.starts_with("<") {
        return Ok(Node::Element(parse_element(cursor)?));
    }

    // Character data up to the next '<'.
    let mut text = String::new();
    while let Some(c) = cursor.peek() {
        if c == '<' {
            break;
        }
        if c == '&' {
            text.push(parse_reference(cursor)?);
        } else {
            text.push(cursor.bump().unwrap());
        }
    }
    Ok(Node::Text(text))
}

fn parse_declaration(cursor: &mut Cursor) -> Parsed<Declaration> {
    let start = cursor.at();
    cursor.eat("<?xml");
    let attributes = parse_attributes(cursor)?;
    cursor.skip_whitespace();
    if !cursor.eat("?>") {
        return cursor.fail();
    }
    let get = |key: &str| {
        attributes.iter().find(|(name, _)| name == key).map(|(_, value)| value.clone())
    };
    let version = get("version").unwrap_or_else(|| "1.0".to_string());
    // .NET accepts 1.0 only; "Version number '1.1' is invalid".
    if version != "1.0" {
        return Err(Error { at: start });
    }
    Ok(Declaration { version, standalone: get("standalone") })
}

fn parse_document(source: &str) -> Parsed<Document> {
    let mut cursor = Cursor::new(source);
    // An empty document is "Root element is missing", reported at 0,0.
    if source.trim().is_empty() {
        return Err(Error { at: Position { line: 0, column: 0 } });
    }

    let declaration = if cursor.starts_with("<?xml")
        && !cursor.peek_at(5).is_some_and(is_name_char)
    {
        Some(parse_declaration(&mut cursor)?)
    } else {
        None
    };

    let mut doctype = None;
    let mut nodes: Vec<Node> = Vec::new();
    let mut roots = 0;
    loop {
        cursor.skip_whitespace();
        if cursor.peek().is_none() {
            break;
        }
        if cursor.starts_with("<!DOCTYPE") {
            cursor.eat("<!DOCTYPE");
            doctype = Some(parse_doctype(&mut cursor)?);
            continue;
        }
        // Only markup is allowed at the root; stray text is "Data at the root level is invalid".
        if !cursor.starts_with("<") {
            return cursor.fail();
        }
        let node = parse_node(&mut cursor)?;
        if matches!(node, Node::Element(_)) {
            roots += 1;
            if roots > 1 {
                return cursor.fail();
            }
        }
        nodes.push(node);
    }
    if roots == 0 {
        return Err(Error { at: Position { line: 0, column: 0 } });
    }
    Ok(Document { declaration, doctype, nodes })
}

const NEWLINE: &str = "\r\n";

/// Text-node escaping. `NewLineHandling.Replace` rewrites every line ending as `NewLineChars`,
/// and `"` is left alone — only markup-significant characters are escaped.
fn escape_text(text: &str) -> String {
    let mut out = String::with_capacity(text.len());
    let normalized = text.replace("\r\n", "\n").replace('\r', "\n");
    for c in normalized.chars() {
        match c {
            '<' => out.push_str("&lt;"),
            '>' => out.push_str("&gt;"),
            '&' => out.push_str("&amp;"),
            '\n' => out.push_str(NEWLINE),
            c => out.push(c),
        }
    }
    out
}

/// Attribute escaping: also `"`, and whitespace becomes a character reference so the value
/// survives attribute-value normalisation on the way back in.
fn escape_attribute(value: &str) -> String {
    let mut out = String::with_capacity(value.len());
    for c in value.chars() {
        match c {
            '<' => out.push_str("&lt;"),
            '>' => out.push_str("&gt;"),
            '&' => out.push_str("&amp;"),
            '"' => out.push_str("&quot;"),
            '\n' => out.push_str("&#xA;"),
            '\r' => out.push_str("&#xD;"),
            '\t' => out.push_str("&#x9;"),
            c => out.push(c),
        }
    }
    out
}

fn write_node(out: &mut String, node: &Node, depth: usize) {
    match node {
        Node::Text(text) => out.push_str(&escape_text(text)),
        Node::CData(body) => out.push_str(&format!("<![CDATA[{body}]]>")),
        Node::Comment(body) => out.push_str(&format!("<!--{body}-->")),
        Node::Instruction { name, data } => {
            if data.is_empty() {
                out.push_str(&format!("<?{name}?>"));
            } else {
                out.push_str(&format!("<?{name} {data}?>"));
            }
        }
        Node::Element(element) => write_element(out, element, depth),
    }
}

fn write_element(out: &mut String, element: &Element, depth: usize) {
    out.push('<');
    out.push_str(&element.name);
    for (name, value) in &element.attributes {
        out.push_str(&format!(" {name}=\"{}\"", escape_attribute(value)));
    }

    if element.self_closed && element.children.is_empty() {
        out.push_str(" />");
        return;
    }
    out.push('>');

    // Once text has been written into this element, indenting is off for the rest of it —
    // including the closing tag.
    let mut text_seen = false;
    for child in &element.children {
        match child {
            Node::Text(_) | Node::CData(_) => text_seen = true,
            _ if !text_seen => {
                out.push_str(NEWLINE);
                out.push_str(&"  ".repeat(depth + 1));
            }
            _ => {}
        }
        write_node(out, child, depth + 1);
    }
    // An element written as `<a></a>` has no children at all and stays on one line.
    if !text_seen && !element.children.is_empty() {
        out.push_str(NEWLINE);
        out.push_str(&"  ".repeat(depth));
    }
    out.push_str(&format!("</{}>", element.name));
}

fn write_document(document: &Document) -> String {
    let mut out = String::new();
    let mut first = true;
    let mut separate = |out: &mut String| {
        if first {
            first = false;
        } else {
            out.push_str(NEWLINE);
        }
    };

    if let Some(declaration) = &document.declaration {
        separate(&mut out);
        // The encoding is always utf-16: the C# writes into a StringBuilder.
        out.push_str(&format!("<?xml version=\"{}\" encoding=\"utf-16\"", declaration.version));
        if let Some(standalone) = &declaration.standalone {
            out.push_str(&format!(" standalone=\"{standalone}\""));
        }
        out.push_str("?>");
    }
    if let Some(name) = &document.doctype {
        separate(&mut out);
        out.push_str(&format!("<!DOCTYPE {name} []>"));
    }
    for node in &document.nodes {
        // Whitespace between root-level nodes is not written back out; the indenter supplies
        // the line breaks instead.
        if matches!(node, Node::Text(text) if text.trim().is_empty()) {
            continue;
        }
        separate(&mut out);
        write_node(&mut out, node, 0);
    }
    out
}

fn message(error: &Error) -> String {
    i18n::format(
        "Xml_Invalid",
        &[&error.at.line.to_string(), &error.at.column.to_string()],
    )
}

pub fn xml_format(xml: &str) -> Result<String, String> {
    let document = parse_document(xml).map_err(|e| message(&e))?;
    Ok(write_document(&document))
}

pub fn xml_validate(xml: &str) -> Result<String, String> {
    match parse_document(xml) {
        Ok(_) => Ok(i18n::get("Xml_Valid").to_string()),
        // Reports rather than throws, matching the C#: this tool's job is to say what is wrong.
        Err(e) => Ok(message(&e)),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn show(xml: &str) -> String {
        crate::i18n::set_language("en");
        xml_format(xml).unwrap()
    }

    #[test]
    fn nested_elements_are_indented_two_spaces() {
        let _guard = crate::i18n::test_lock();
        assert_eq!(show("<a><b>1</b></a>"), "<a>\r\n  <b>1</b>\r\n</a>");
        assert_eq!(
            show("<a><b><c><d/></c></b></a>"),
            "<a>\r\n  <b>\r\n    <c>\r\n      <d />\r\n    </c>\r\n  </b>\r\n</a>"
        );
    }

    /// `<a/>` and `<a></a>` are genuinely different documents to LINQ to XML.
    #[test]
    fn self_closed_and_empty_pair_are_kept_apart() {
        let _guard = crate::i18n::test_lock();
        assert_eq!(show("<a/>"), "<a />");
        assert_eq!(show("<a></a>"), "<a></a>");
        assert_eq!(show("<a><b></b></a>"), "<a>\r\n  <b></b>\r\n</a>");
        assert_eq!(show("<a b=\"1\" c=\"2\"/>"), "<a b=\"1\" c=\"2\" />");
    }

    /// The rule that makes this tool awkward: text switches indenting off mid-element.
    #[test]
    fn text_content_suppresses_indenting_from_that_point_on() {
        let _guard = crate::i18n::test_lock();
        assert_eq!(show("<a>text</a>"), "<a>text</a>");
        assert_eq!(show("<a>text<b/></a>"), "<a>text<b /></a>");
        assert_eq!(show("<a><b/>text</a>"), "<a>\r\n  <b />text</a>");
        assert_eq!(show("<a> </a>"), "<a> </a>");
        // PreserveWhitespace means an already-indented document is not reindented: the line
        // breaks below come from its own text nodes.
        assert_eq!(show("<a>\n  <b/>\n</a>"), "<a>\r\n  <b />\r\n</a>");
        assert_eq!(show("<a>  <b/>  </a>"), "<a>  <b />  </a>");
    }

    #[test]
    fn comments_and_instructions_indent_like_elements() {
        let _guard = crate::i18n::test_lock();
        assert_eq!(show("<a><!--c--><b/></a>"), "<a>\r\n  <!--c-->\r\n  <b />\r\n</a>");
        assert_eq!(show("<a><?pi d?><b/></a>"), "<a>\r\n  <?pi d?>\r\n  <b />\r\n</a>");
        assert_eq!(show("<!--top--><a/>"), "<!--top-->\r\n<a />");
        assert_eq!(show("<a/><!--after-->"), "<a />\r\n<!--after-->");
        assert_eq!(show("<!--c1--><!--c2--><a/>"), "<!--c1-->\r\n<!--c2-->\r\n<a />");
    }

    #[test]
    fn cdata_counts_as_text() {
        let _guard = crate::i18n::test_lock();
        assert_eq!(show("<a><![CDATA[x<y]]></a>"), "<a><![CDATA[x<y]]></a>");
        assert_eq!(show("<a><![CDATA[]]></a>"), "<a><![CDATA[]]></a>");
    }

    #[test]
    fn the_declaration_is_rewritten_as_utf16() {
        let _guard = crate::i18n::test_lock();
        assert_eq!(
            show("<?xml version=\"1.0\" encoding=\"UTF-8\"?><a/>"),
            "<?xml version=\"1.0\" encoding=\"utf-16\"?>\r\n<a />"
        );
        assert_eq!(
            show("<?xml version=\"1.0\" standalone=\"yes\"?><a/>"),
            "<?xml version=\"1.0\" encoding=\"utf-16\" standalone=\"yes\"?>\r\n<a />"
        );
        // Only 1.0 exists as far as .NET is concerned.
        assert!(xml_format("<?xml version=\"1.1\"?><a/>").is_err());
    }

    #[test]
    fn escaping_differs_between_text_and_attributes() {
        let _guard = crate::i18n::test_lock();
        assert_eq!(show("<a>a&gt;b</a>"), "<a>a&gt;b</a>");
        assert_eq!(show("<a>&lt;&amp;&gt;&quot;</a>"), "<a>&lt;&amp;&gt;\"</a>");
        assert_eq!(show("<a x=\"a>b\"/>"), "<a x=\"a&gt;b\" />");
        assert_eq!(show("<a x=\"&quot;q&quot;\"/>"), "<a x=\"&quot;q&quot;\" />");
        assert_eq!(show("<a x=\"1&#10;2\"/>"), "<a x=\"1&#xA;2\" />");
        // Character references resolve to the character itself.
        assert_eq!(show("<a>&#65;&#x42;</a>"), "<a>AB</a>");
        // A bare LF in text becomes CRLF.
        assert_eq!(show("<a>l1\nl2</a>"), "<a>l1\r\nl2</a>");
        assert_eq!(show("<a>tab\there</a>"), "<a>tab\there</a>");
    }

    #[test]
    fn attribute_order_and_prefixes_survive() {
        let _guard = crate::i18n::test_lock();
        assert_eq!(show("<a b=\"1\" a=\"2\" c=\"3\"/>"), "<a b=\"1\" a=\"2\" c=\"3\" />");
        assert_eq!(show("<p:a xmlns:p=\"u\"><p:b/></p:a>"), "<p:a xmlns:p=\"u\">\r\n  <p:b />\r\n</p:a>");
        assert_eq!(show("<a xmlns=\"u\"><b/></a>"), "<a xmlns=\"u\">\r\n  <b />\r\n</a>");
        // Newlines inside a start tag are just whitespace between attributes.
        assert_eq!(show("<a\n  b=\"1\"\n  c=\"2\"/>"), "<a b=\"1\" c=\"2\" />");
    }

    #[test]
    fn validate_accepts_well_formed_and_reports_the_rest() {
        let _guard = crate::i18n::test_lock();
        crate::i18n::set_language("en");
        assert_eq!(xml_validate("<a><b/></a>").unwrap(), i18n::get("Xml_Valid"));
        for bad in ["<a>", "<a></b>", "<a", "notxml", "<a>&bad;</a>", "", "<a><b></a></b>",
                    "<a b=/>", "<a b=\"1\" b=\"2\"/>", "<a>x</a><b/>"] {
            let reported = xml_validate(bad).unwrap();
            assert_ne!(reported, i18n::get("Xml_Valid"), "{bad:?} is not well-formed");
            assert!(!reported.contains("Xml_Invalid"), "the key leaked: {reported}");
        }
    }

    /// Formatting an already-formatted document must not change it again.
    #[test]
    fn formatting_is_idempotent() {
        let _guard = crate::i18n::test_lock();
        for xml in ["<a><b>1</b></a>", "<a/>", "<a></a>", "<a>text</a>", "<a><b/>text</a>",
                    "<!--c--><a><b/><!--d--></a>", "<a x=\"1\"><b><c/></b></a>",
                    "<?xml version=\"1.0\"?><a><b/></a>"] {
            let once = show(xml);
            let twice = show(&once);
            assert_eq!(once, twice, "reformatting changed {xml:?}");
        }
    }
}
