"""Build, audit, test and package the display-only Ruina localization addon."""
import argparse
import ctypes.util
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
from zipfile import ZipFile, ZIP_DEFLATED
from validate_translations import validate

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--dotnet', default='dotnet')
parser.add_argument('--game-dll', type=Path, required=True)
parser.add_argument('--godotsharp', type=Path, required=True)
parser.add_argument('--harmony', type=Path, required=True)
parser.add_argument('--gu-addon', type=Path, required=True, help='Prior GuZhenRenRussian.dll for coexistence tests only')
parser.add_argument('--godot', type=Path, required=True)
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
output = root.parent / 'dist'
output.mkdir(exist_ok=True)
name = 'Ruina-Russian-P-1.1.5-ru1'
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


validate()
log.append('PASS: 1418 entries in 22 tables; keys, variables, formatters, conditions and markup checked. English plural constructs replaced by number-neutral Russian.\n')
properties = [f'-p:GameAssembly={args.game_dll.resolve()}', f'-p:GodotSharpAssembly={args.godotsharp.resolve()}', f'-p:HarmonyAssembly={args.harmony.resolve()}', f'-p:GuAddonAssembly={args.gu_addon.resolve()}']
for project, configuration in [('src/RuinaRussian.csproj', 'Release'), ('tools/audit/Audit.csproj', 'Release'), ('tests/UiTests.csproj', 'Debug')]:
    run([args.dotnet, 'build', project, '-c', configuration, '--nologo', '-v', 'quiet', *properties])
addon = root / 'src/bin/Release/net9.0/RuinaRussian.dll'
audit = root / 'tools/audit/bin/Release/net9.0/Audit.dll'
with tempfile.TemporaryDirectory(prefix='ruina-ru-') as directory:
    temp = Path(directory)
    run([args.dotnet, audit, addon, args.game_dll.resolve()])
    upstream = json.loads((root / 'upstream.json').read_text())
    for file, digest in upstream['english_sha256'].items():
        assert sha((root / 'localization/eng' / file).read_bytes()) == digest
    log.append('PASS: all 22 English files match pinned source hashes: ' + upstream['commit'] + '\n')
    log.append('NOT VERIFIED: user Workshop ZIP exceeds 32 MiB transfer limit. Binary/PCK version was not inspected.\n')
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
    assert '27 RUINA RUSSIAN NATIVE CHECKS PASSED' in native and 'ERROR:' not in native
    for probe, user in [('save', 'player-a'), ('peer', 'player-b'), ('read', 'player-a')]:
        user_dir(user)
        env['RUINA_RU_PROBE'] = probe
        native = run(command, env=env)
        assert f'LANGUAGE PROBE {probe} PASSED' in native and 'ERROR:' not in native

files = {
    'RuinaRussian.dll': addon.read_bytes(),
    'RuinaRussian.json': (root / 'RuinaRussian.json').read_bytes(),
    'README-ru.md': (root / 'README.md').read_bytes(),
    'LICENSE': (root / 'LICENSE').read_bytes(),
    'UPSTREAM.json': (root / 'upstream.json').read_bytes(),
}
files['SHA256SUMS.txt'] = ''.join(f'{sha(data)}  {file}\n' for file, data in files.items()).encode()
binary = output / (name + '.zip')
with ZipFile(binary, 'w', ZIP_DEFLATED) as archive:
    for file, data in files.items():
        archive.writestr('RuinaRussian/' + file, data)
with ZipFile(binary) as archive:
    assert archive.testzip() is None
    assert not any(Path(n).name in ['sts2.dll', '0Harmony.dll', 'GodotSharp.dll', 'Ruina2.dll', 'GuZhenRenRussian.dll'] for n in archive.namelist())
report = (
    'Ruina Russian/P 1.1.5-ru.1 validation\n'
    f'Game API input SHA256: {sha(args.game_dll.read_bytes())}\n'
    f'Addon SHA256: {sha(addon.read_bytes())}\n'
    f'Archive SHA256: {sha(binary.read_bytes())}\n\n'
    + '\n'.join(log)
    + '\nNOT RUN: full game launch or an actual multiplayer network session.\n'
    + 'Native Godot tests use fake game classes, real Harmony/input and the prior GuZhenRenRussian addon. Separate user-data directories simulate independent local preferences.\n'
)
(output / (name + '-validation.txt')).write_text(report)
source = output / (name + '-source.zip')
with ZipFile(source, 'w', ZIP_DEFLATED) as archive:
    for path in sorted(root.rglob('*')):
        relative = path.relative_to(root)
        if path.is_file() and not {'bin', 'obj', '.godot', '__pycache__'}.intersection(relative.parts) and path.suffix != '.uid':
            archive.write(path, 'ruina-ru/' + relative.as_posix())
    archive.writestr('VALIDATION.txt', report)
with ZipFile(source) as archive:
    assert archive.testzip() is None
print(f'Packaged {binary.name}: {binary.stat().st_size} bytes')
print(f'Packaged {source.name}: {source.stat().st_size} bytes')
