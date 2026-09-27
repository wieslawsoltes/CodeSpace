#!/usr/bin/env python3
"""Validate actual package contents, not only dotnet pack's exit code."""
from pathlib import Path
import argparse
import hashlib
import json
import tarfile
import xml.etree.ElementTree as ET
import zipfile

LIBRARIES = {f'CodeSpace.{part}' for part in (
    'Core', 'Editor', 'Docking', 'Languages', 'Extensions', 'Rendering.Skia',
    'Controls.Uno', 'Workbench.Uno')}


def verify(folder: Path) -> list[dict]:
    rows = []
    seen = set()
    versions = set()
    for path in sorted(folder.glob('CodeSpace.*.nupkg')):
        with zipfile.ZipFile(path) as archive:
            names = archive.namelist()
            specs = [name for name in names if name.endswith('.nuspec')]
            if len(specs) != 1:
                raise ValueError(f'{path.name}: expected one NuGet manifest')
            root = ET.fromstring(archive.read(specs[0]))
            metadata = next(child for child in root if child.tag.split('}')[-1] == 'metadata')
            values = {child.tag.split('}')[-1]: child.text for child in metadata}
            name, version = values['id'], values['version']
            if name not in LIBRARIES or name in seen:
                raise ValueError(f'Unexpected or duplicate package: {name}')
            seen.add(name)
            versions.add(version)
            assemblies = [entry for entry in names if entry.startswith('lib/') and entry.endswith('/' + name + '.dll')]
            if not assemblies or not all(archive.getinfo(entry).file_size > 0 for entry in assemblies):
                raise ValueError(f'{name}: public assembly is missing or empty')
            if name.endswith('.Uno'):
                if not any('/net10.0-browserwasm' in entry for entry in assemblies):
                    raise ValueError(f'{name}: browser library target is missing')
                if not any('/net10.0-desktop' in entry for entry in assemblies):
                    raise ValueError(f'{name}: desktop library target is missing')
            if 'README.md' not in names or values.get('license') != 'MIT':
                raise ValueError(f'{name}: README or license metadata is missing')
            rows.append({'id': name, 'version': version, 'file': path.name,
                         'assemblies': assemblies, 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()})
    if seen != LIBRARIES:
        raise ValueError('Missing reusable libraries: ' + ', '.join(sorted(LIBRARIES - seen)))
    if len(versions) != 1:
        raise ValueError('Library versions differ: ' + repr(versions))
    hosts = list(folder.glob('codespace-extension-host-*.tgz'))
    if len(hosts) != 1:
        raise ValueError('Expected exactly one extension-host npm archive')
    host = hosts[0]
    with tarfile.open(host, 'r:gz') as archive:
        manifest = json.load(archive.extractfile('package/package.json'))
        for name in ('runtime.mjs', 'worker.mjs', 'node-host.mjs', 'README.md', 'LICENSE'):
            if not archive.getmember('package/' + name).size:
                raise ValueError('Empty extension host file: ' + name)
    if manifest.get('name') != '@codespace/extension-host' or manifest.get('license') != 'MIT':
        raise ValueError('Unexpected extension host metadata')
    if manifest.get('version') not in versions:
        raise ValueError('npm and NuGet package versions differ')
    rows.append({'id': manifest['name'], 'version': manifest['version'], 'file': host.name,
                 'sha256': hashlib.sha256(host.read_bytes()).hexdigest()})
    return rows


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('folder', type=Path)
    arguments = parser.parse_args()
    try:
        inventory = verify(arguments.folder)
    except (ValueError, KeyError, OSError, StopIteration, zipfile.BadZipFile, tarfile.TarError, ET.ParseError) as error:
        parser.exit(1, f'Package validation failed: {error}\n')
    (arguments.folder / 'package-manifest.json').write_text(json.dumps(inventory, indent=2) + '\n', encoding='utf-8')
    print('Verified eight reusable NuGet libraries and one npm extension host.')
