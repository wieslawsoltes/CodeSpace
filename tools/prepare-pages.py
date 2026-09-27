#!/usr/bin/env python3
"""Stage a generated Uno site without assuming the SDK output nesting."""
from pathlib import Path
import json
import os
import shutil
import sys

source = Path(sys.argv[1] if len(sys.argv) > 1 else 'artifacts/browser')
target = Path(sys.argv[2] if len(sys.argv) > 2 else 'artifacts/site')
indexes = sorted(source.rglob('index.html'), key=lambda p: len(p.parts))
if not indexes:
    raise SystemExit(f'No generated index.html below {source}')
root = indexes[0].parent
if target.exists():
    shutil.rmtree(target)
shutil.copytree(root, target)
host = target / 'extension-host'
host.mkdir(exist_ok=True)
for module in Path('src/CodeSpace.ExtensionHost').glob('*.mjs'):
    shutil.copy2(module, host / module.name)
(target / '.nojekyll').touch()
(target / 'build-info.json').write_text(json.dumps({'commit': os.environ.get('GITHUB_SHA', 'local'), 'version': '0.1.0'}), encoding='utf-8')
print(f'Staged {root} -> {target}')
