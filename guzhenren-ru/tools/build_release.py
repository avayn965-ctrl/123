"""Build, audit, test and package the display-only GuZhenRen localization addon."""
import argparse
import ctypes.util
import hashlib
import json
import os
from pathlib import Path
import shutil
import struct
import subprocess
import tempfile
from zipfile import ZipFile, ZIP_DEFLATED
from validate_translations import validate

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--dotnet', default='dotnet')
parser.add_argument('--game-dll', type=Path, required=True)
parser.add_argument('--godotsharp', type=Path, required=True)
parser.add_argument('--harmony', type=Path, required=True)
parser.add_argument('--original-mod', type=Path, action='append', required=True)
parser.add_argument('--godot', type=Path, required=True)
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
output = root.parent / 'dist'
output.mkdir(exist_ok=True)
name = 'GuZhenRen-Russian-P-0.7.2-ru1'
log = []


def sha(data):
    return hashlib.sha256(data).hexdigest()


def run(command, **kwargs):
    result = subprocess.run(list(map(str, command)), cwd=root, capture_output=True, text=True, timeout=90, **kwargs)
    text = result.stdout + result.stderr
    print(text, end='', flush=True)
    if result.returncode:
        raise RuntimeError(text)
    log.append(text)
    return text


def read_english_pck(data):
    magic, version, major, minor, patch, flags, base, directory = struct.unpack_from('<6I2Q', data)
    assert magic == 0x43504447 and version == 3 and not flags & 1, 'Unsupported PCK'
    count, = struct.unpack_from('<I', data, directory)
    position = directory + 4
    tables = {}
    for _ in range(count):
        length, = struct.unpack_from('<I', data, position)
        position += 4
        path = data[position:position + length].rstrip(b'\0').decode()
        position += length
        offset, size = struct.unpack_from('<QQ', data, position)
        position += 36
        if path.startswith('GuZhenRen/localization/eng/') and path.endswith('.json'):
            tables[Path(path).name] = json.loads(data[base + offset:base + offset + size].decode('utf-8-sig'))
    return tables


validate()
log.append('PASS: 1388 entries in 13 tables; keys, SmartFormat variable/selector trees and BBCode preserved.\n')
properties = [f'-p:GameAssembly={args.game_dll.resolve()}', f'-p:GodotSharpAssembly={args.godotsharp.resolve()}', f'-p:HarmonyAssembly={args.harmony.resolve()}']
for project, configuration in [('src/GuZhenRenRussian.csproj', 'Release'), ('tools/audit/Audit.csproj', 'Release'), ('tests/UiTests.csproj', 'Debug')]:
    run([args.dotnet, 'build', project, '-c', configuration, '--nologo', '-v', 'quiet', *properties])
addon = root / 'src/bin/Release/net9.0/GuZhenRenRussian.dll'
audit = root / 'tools/audit/bin/Release/net9.0/Audit.dll'
with tempfile.TemporaryDirectory(prefix='guzhenren-ru-') as directory:
    temp = Path(directory)
    for index, path in enumerate(args.original_mod):
        with ZipFile(path) as archive:
            manifest_path = next(n for n in archive.namelist() if n.endswith('/GuZhenRen.json'))
            manifest = json.loads(archive.read(manifest_path).decode('utf-8-sig'))
            assert manifest['version'] in ['0.7.2', '0.7.2-beta.1']
            pck_path = manifest_path.removesuffix('.json') + '.pck'
            english = read_english_pck(archive.read(pck_path))
            for table, values in english.items():
                assert values == json.loads((root / 'localization/eng' / table).read_text()), table
            assert len(english) == 13
            original = temp / f'original-{index}.dll'
            original.write_bytes(archive.read(manifest_path.removesuffix('.json') + '.dll'))
            run([args.dotnet, audit, addon, args.game_dll.resolve(), original])
            log.append(f'PASS: exact original English values match all 13 PCK tables for {manifest["version"]}.\n')
            log.append(f'Original ZIP {path.name} SHA256: {sha(path.read_bytes())}\n')
    env = os.environ.copy()
    executable = shutil.which(args.dotnet)
    if executable:
        env['DOTNET_ROOT'] = str(Path(executable).resolve().parent)
    env['DOTNET_ROLL_FORWARD'] = 'Major'
    if os.name == 'posix' and ctypes.util.find_library('gcc_s'):
        env['LD_PRELOAD'] = ctypes.util.find_library('gcc_s')
    def user_dir(user):
        for category in ['DATA', 'CONFIG', 'CACHE']:
            env[f'XDG_{category}_HOME'] = str(temp / user / category.lower())
    command = [args.godot.resolve(), '--headless', '--path', root / 'tests', '--audio-driver', 'Dummy', '--quit-after', '300']
    user_dir('player-a')
    native = run(command, env=env)
    assert '20 GU RUSSIAN NATIVE CHECKS PASSED' in native and 'ERROR:' not in native
    for probe, user in [('save', 'player-a'), ('peer', 'player-b'), ('read', 'player-a')]:
        user_dir(user)
        env['GU_RU_PROBE'] = probe
        native = run(command, env=env)
        assert f'LANGUAGE PROBE {probe} PASSED' in native and 'ERROR:' not in native

files = {
    'GuZhenRenRussian.dll': addon.read_bytes(),
    'GuZhenRenRussian.json': (root / 'GuZhenRenRussian.json').read_bytes(),
    'README-ru.md': (root / 'README.md').read_bytes(),
    'LICENSE': (root / 'LICENSE').read_bytes(),
}
files['SHA256SUMS.txt'] = ''.join(f'{sha(data)}  {file}\n' for file, data in files.items()).encode()
binary = output / (name + '.zip')
with ZipFile(binary, 'w', ZIP_DEFLATED) as archive:
    for file, data in files.items():
        archive.writestr('GuZhenRenRussian/' + file, data)
with ZipFile(binary) as archive:
    assert archive.testzip() is None
    assert not any(Path(n).name in ['sts2.dll', '0Harmony.dll', 'GodotSharp.dll', 'GuZhenRen.dll'] for n in archive.namelist())
report = (
    'GuZhenRen Russian/P 0.7.2-ru.1 validation\n'
    f'Game API input SHA256: {sha(args.game_dll.read_bytes())}\n'
    f'Addon SHA256: {sha(addon.read_bytes())}\n'
    f'Archive SHA256: {sha(binary.read_bytes())}\n\n'
    + '\n'.join(log)
    + '\nNOT RUN: full game launch or an actual multiplayer network session.\n'
    + 'Native Godot tests use fake game classes and real Harmony/input. Separate user-data directories simulate independent local preferences.\n'
)
(output / (name + '-validation.txt')).write_text(report)
source = output / (name + '-source.zip')
with ZipFile(source, 'w', ZIP_DEFLATED) as archive:
    for path in sorted(root.rglob('*')):
        relative = path.relative_to(root)
        if path.is_file() and not {'bin', 'obj', '.godot', '__pycache__'}.intersection(relative.parts) and path.suffix != '.uid':
            archive.write(path, 'guzhenren-ru/' + relative.as_posix())
    archive.writestr('VALIDATION.txt', report)
with ZipFile(source) as archive:
    assert archive.testzip() is None
print(f'Packaged {binary.name}: {binary.stat().st_size} bytes')
print(f'Packaged {source.name}: {source.stat().st_size} bytes')
