import json
from pathlib import Path
from collections import Counter

data = json.loads(Path('graphify-out/.graphify_detect.json').read_text(encoding='utf-8'))
scan_root = data['scan_root']

all_files = []
for cat in ['code', 'document', 'paper', 'image', 'video']:
    all_files.extend(data.get('files', {}).get(cat, []))

counter = Counter()
root_path = Path(scan_root)
graphify_out = root_path / 'graphify-out'

for f in all_files:
    p = Path(f)
    try:
        if p.is_relative_to(graphify_out):
            continue
        rel = p.relative_to(root_path)
        parts = rel.parts
        if len(parts) > 1:
            counter[parts[0]] += 1
        else:
            counter['(root)'] += 1
    except ValueError:
        pass

for k, v in counter.most_common(5):
    print(f'{k}: {v}')
