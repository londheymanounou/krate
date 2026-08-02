import sys
sys.stdout.reconfigure(encoding='utf-8')
import os
import glob
import re

rs_path = r"D:\crate\rust\target\aarch64-linux-android\debug\build\krate-core-7b4c028f5f43cee5\out\strings.rs"
with open(rs_path, "r", encoding="utf-8") as f:
    rs_content = f.read()

langs = {}
current_lang = None
for line in rs_content.split("\n"):
    m = re.match(r'^static L_([A-Z0-9_]+):', line)
    if m:
        current_lang = m.group(1).lower()
        langs[current_lang] = {}
        continue
    
    if current_lang is not None:
        if line.startswith('];'):
            current_lang = None
            continue
            
        # Match the tuple ("Key", "Value")
        # Note: some values might contain \", so we use a regex that matches until the last ",)"
        # Actually, ast.literal_eval is safer
        import ast
        line = line.strip()
        if line.endswith(','):
            line = line[:-1]
        if line.startswith('(') and line.endswith(')'):
            # Convert Rust \u{XXXX} to Python \UXXXXXXXX
            line = re.sub(r'\\u\{([0-9a-fA-F]+)\}', lambda m: '\\U' + m.group(1).zfill(8), line)
            try:
                k, v = ast.literal_eval(line)
                langs[current_lang][k] = v
            except Exception as e:
                print(f"Failed to parse line: {line}\nError: {e}")
                pass

def escape_xml(v):
    return v.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;").replace('"', "&quot;").replace("'", "&apos;")

resx_dir = r"D:\crate\src\KRATE.Core\Resources"
resx_files = glob.glob(os.path.join(resx_dir, "*.resx"))

for resx_path in resx_files:
    filename = os.path.basename(resx_path)
    if filename == "Strings.resx":
        lang_key = "en"
    else:
        # e.g., Strings.zh-CN.resx -> zh_cn
        m = re.match(r'Strings\.(.+)\.resx', filename)
        if m:
            lang_key = m.group(1).replace("-", "_").lower()
        else:
            continue
            
    if lang_key not in langs:
        print(f"Skipping {filename} as {lang_key} not found in strings.rs")
        continue
        
    dic = langs[lang_key]
    
    # read the corrupted file just to get the structure, but wait, the corrupted file has wrong encodings.
    # To fix encoding, we can read as 'mbcs' (ANSI) which is how PowerShell wrote it, but 'mbcs' to unicode will just give the wrong chars.
    # Actually, PowerShell `Get-Content` read it as ANSI, meaning the UTF-8 bytes were interpreted as ANSI, so the original UTF-8 bytes were corrupted, and then saved as ANSI.
    # We can't recover the exact structure without parsing it, but we don't care about the corrupted text, we are going to replace it anyway.
    # We will read it as utf-8, ignoring errors, so we keep the structure.
    with open(resx_path, "r", encoding="utf-8", errors="ignore") as f:
        content = f.read()
        
    # Replace all <data name="KEY"...><value>.*?</value> with the uncorrupted values
    def replacer(match):
        key = match.group(1)
        if key in dic:
            val = dic[key]
            # also replace .crate with .krate if it exists in the uncorrupted string
            val = val.replace(".crate", ".krate").replace(".CRATE", ".KRATE")
            esc_val = escape_xml(val)
            return f'<data name="{key}" xml:space="preserve"><value>{esc_val}</value>'
        return match.group(0)
    
    new_content = re.sub(r'<data name="([^"]+)" xml:space="preserve">\s*<value>.*?</value>', replacer, content, flags=re.DOTALL)
    
    with open(resx_path, "w", encoding="utf-8") as f:
        f.write(new_content)
        
    print(f"Restored {filename}")
