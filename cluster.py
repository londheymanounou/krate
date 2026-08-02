import sys, json, networkx as nx
from graphify.cluster import cluster
from pathlib import Path

graph = json.loads(Path('graphify-out/graph.json').read_text(encoding='utf-8'))
if graph.get('nodes'):
    G = nx.Graph()
    for n in graph['nodes']: G.add_node(n['id'], **n)
    for e in graph.get('edges', []): G.add_edge(e['source'], e['target'], **e)
    communities = cluster(G)
    graph['communities'] = communities
    Path('graphify-out/graph.json').write_text(json.dumps(graph, indent=2, ensure_ascii=False), encoding='utf-8')
    print(f"Clustered into {len(communities)} communities")
else:
    print('No nodes to cluster')
