import sys, json
from graphify.extract import collect_files, extract
from graphify.cache import check_semantic_cache
from pathlib import Path

# Part A
print("Starting AST...")
code_files = []
detect = json.loads(Path('graphify-out/.graphify_detect.json').read_text(encoding="utf-8"))
for f in detect.get('files', {}).get('code', []):
    code_files.extend(collect_files(Path(f)) if Path(f).is_dir() else [Path(f)])

if code_files:
    result = extract(code_files, cache_root=Path('.'))
    Path('graphify-out/.graphify_ast.json').write_text(json.dumps(result, indent=2, ensure_ascii=False), encoding="utf-8")
    print(f'AST: {len(result["nodes"])} nodes, {len(result["edges"])} edges')
else:
    Path('graphify-out/.graphify_ast.json').write_text(json.dumps({'nodes':[],'edges':[],'input_tokens':0,'output_tokens':0}, ensure_ascii=False), encoding="utf-8")
    print('No code files - skipping AST extraction')

# Part B0
print("Checking cache...")
all_files = [f for cat in ('document', 'paper', 'image') for f in detect['files'].get(cat, [])]
prompt_file = r'C:\Users\londh\.gemini\config\skills\graphify\references\extraction-spec.md'

cached_nodes, cached_edges, cached_hyperedges, uncached = check_semantic_cache(all_files, root='.', prompt_file=prompt_file)

if cached_nodes or cached_edges or cached_hyperedges:
    Path('graphify-out/.graphify_cached.json').write_text(json.dumps({'nodes': cached_nodes, 'edges': cached_edges, 'hyperedges': cached_hyperedges}, ensure_ascii=False), encoding="utf-8")
else:
    Path('graphify-out/.graphify_cached.json').unlink(missing_ok=True)

Path('graphify-out/.graphify_uncached.txt').write_text('\n'.join(uncached), encoding="utf-8")
print(f'Cache: {len(all_files)-len(uncached)} files hit, {len(uncached)} files need extraction')
