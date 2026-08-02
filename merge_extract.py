import sys, json
from pathlib import Path

ast = json.loads(Path('graphify-out/.graphify_ast.json').read_text(encoding='utf-8'))
sem = json.loads(Path('graphify-out/.graphify_semantic.json').read_text(encoding='utf-8'))

# Merge AST and semantic sets
merged_nodes = {n['id']: n for n in ast['nodes'] + sem['nodes']}
merged_edges = ast['edges'] + sem['edges']

# Ensure every edge points to valid nodes
final_edges = [e for e in merged_edges if e['source'] in merged_nodes and e['target'] in merged_nodes]
final_hyperedges = sem.get('hyperedges', [])

graph = {
    'nodes': list(merged_nodes.values()),
    'edges': final_edges,
    'hyperedges': final_hyperedges,
    'input_tokens': ast.get('input_tokens', 0) + sem.get('input_tokens', 0),
    'output_tokens': ast.get('output_tokens', 0) + sem.get('output_tokens', 0)
}

Path('graphify-out/graph.json').write_text(json.dumps(graph, indent=2, ensure_ascii=False), encoding='utf-8')
print(f"Extraction complete - AST+Semantic merged: {len(graph['nodes'])} nodes, {len(graph['edges'])} edges")
