import json
from graphify.detect import detect
from pathlib import Path
result = detect(Path('.'))
with open('graphify-out/.graphify_detect.json', 'w', encoding='utf-8') as f:
    json.dump(result, f, ensure_ascii=False)
print(f'Total files: {result.get("total_files", 0)}')
print(f'Total words: {result.get("total_words", 0)}')
print(f'Code files: {len(result.get("files", {}).get("code", []))}')
print(f'Docs: {len(result.get("files", {}).get("document", []))}')
print(f'Papers: {len(result.get("files", {}).get("paper", []))}')
print(f'Images: {len(result.get("files", {}).get("image", []))}')
print(f'Video: {len(result.get("files", {}).get("video", []))}')
