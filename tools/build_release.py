"""Build/test/package compat.4 without distributing the input game assembly."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile
import os
import shutil
from zipfile import ZipFile, ZIP_DEFLATED

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--game-dll', type=Path, required=True)
parser.add_argument('--original-mod', type=Path, required=True, help='Original 1.4.3 ZIP')
parser.add_argument('--dotnet', default='dotnet')
parser.add_argument('--output-dir', type=Path)
parser.add_argument('--godot', type=Path, help='Optional Godot 4.5.1 Mono binary for native UI tests')
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
output = (args.output_dir or root / 'dist').resolve()
output.mkdir(parents=True, exist_ok=True)
game = args.game_dll.resolve()
logs = []


def run(*arguments):
    result = subprocess.run([args.dotnet, *map(str, arguments)], cwd=root, text=True, capture_output=True)
    print(result.stdout, end='')
    if result.returncode:
        raise RuntimeError(result.stdout + result.stderr)
    logs.append(result.stdout)


def sha(data):
    return hashlib.sha256(data).hexdigest()


for project in ['src/Patcher.csproj', 'runtime/RuntimeFeatures.csproj', 'tests/FeatureTests.csproj']:
    run('build', root / project, '-c', 'Release', '--nologo', '-v', 'quiet')
patcher = root / 'src/bin/Release/net9.0/Patcher.dll'
runtime = root / 'runtime/bin/Release/net9.0/RuntimeFeatures.dll'
tests = root / 'tests/bin/Release/net9.0/sts2.dll'
with ZipFile(args.original_mod) as original_zip, tempfile.TemporaryDirectory(prefix='refresh-ancient-') as directory:
    temp = Path(directory)
    original = temp / 'original.dll'
    original.write_bytes(original_zip.read('RefreshAncient.dll'))
    manifest = json.loads(original_zip.read('RefreshAncient.json'))
    if manifest['version'] != '1.4.3':
        raise ValueError('Expected the original mod version 1.4.3')
    baseline = temp / 'compat1.dll'
    result = temp / 'RefreshAncient.dll'
    run(patcher, 'patch', original, game, baseline)
    run(patcher, 'verify', baseline, game, original)
    run(patcher, 'features', baseline, game, result, runtime)
    run(patcher, 'verify-features', result, game, baseline)
    run(tests)
    run(tests, result)
    if args.godot:
        run('build', root / 'ui-tests/UiTests.csproj', '-c', 'Debug', '--nologo', '-v', 'quiet')
        for mode in ['source', 'merged']:
            environment = os.environ.copy()
            if mode == 'merged':
                environment['REFRESH_TEST_MOD'] = str(result)
            else:
                environment.pop('REFRESH_TEST_MOD', None)
            dotnet = shutil.which(args.dotnet)
            if dotnet:
                environment['DOTNET_ROOT'] = str(Path(dotnet).resolve().parent)
            environment['DOTNET_ROLL_FORWARD'] = 'Major'
            for name in ['DATA', 'CONFIG', 'CACHE']:
                environment[f'XDG_{name}_HOME'] = str(temp / f'ui-{name.lower()}')
            native = subprocess.run([str(args.godot.resolve()), '--headless', '--path', str(root / 'ui-tests'), '--audio-driver', 'Dummy', '--quit-after', '240'],
                                    env=environment, text=True, capture_output=True, timeout=60)
            native_output = native.stdout + native.stderr
            print(native_output, end='')
            if native.returncode or '25 native Godot 4.5.1 UI checks passed' not in native_output or 'ERROR:' in native_output:
                raise RuntimeError('Native UI test failed or did not complete:\n' + native_output)
            logs.append(native_output)
    else:
        logs.append('NOT RUN: native Godot UI tests (pass --godot with Godot 4.5.1 Mono).\n')
    manifest['version'] = '1.4.3-compat.4'
    manifest['min_game_version'] = '0.111.0'
    manifest['description'] += ' Unofficial v0.111.0 patch: unique option/Ancient rerolls, game-styled visual catalogs with reward/top-bar icons and Russian localization.'
    files = {
        'RefreshAncient.dll': result.read_bytes(),
        'RefreshAncient.json': (json.dumps(manifest, ensure_ascii=False, indent=2) + '\n').encode(),
        'RefreshAncient.pck': original_zip.read('RefreshAncient.pck'),
        'README-compat-ru.txt': b'\xef\xbb\xbf' + (root / 'docs/INSTALL-compat4-ru.txt').read_bytes(),
    }
    files['SHA256SUMS.txt'] = ''.join(f'{sha(data)}  {name}\n' for name, data in files.items()).encode()
    binary_zip = output / 'RefreshAncient-compat4-sts2-0.111.0.zip'
    with ZipFile(binary_zip, 'w', ZIP_DEFLATED) as archive:
        for name, data in files.items():
            archive.writestr(name, data)
    with ZipFile(binary_zip) as archive:
        assert archive.testzip() is None
        assert archive.read('RefreshAncient.dll') == files['RefreshAncient.dll']
        assert archive.read('RefreshAncient.pck') == original_zip.read('RefreshAncient.pck')
    validation = (
        'RefreshAncient 1.4.3-compat.4 validation\nTarget: STS2 v0.111.0 (41cef1ea).\n'
        f'Original mod SHA-256: {sha(original.read_bytes())}\n'
        f'Game DLL SHA-256: {sha(game.read_bytes())}\n'
        f'Patched mod SHA-256: {sha(files["RefreshAncient.dll"])}\n'
        f'Archive SHA-256: {sha(binary_zip.read_bytes())}\n\n'
        + '\n'.join(logs)
        + '\nNOT RUN: actual game launch, gameplay and network session.\n'
        + 'Option history is in memory per event instance; Ancient history is per run/player/room. Both reset on restart/save reload.\n'
    )
    (output / 'RefreshAncient-compat4-validation.txt').write_text(validation)
source_zip = output / 'RefreshAncient-compat4-source.zip'
with ZipFile(source_zip, 'w', ZIP_DEFLATED) as archive:
    for folder in ['src', 'runtime', 'tests', 'ui-tests', 'tools', 'docs']:
        for path in sorted((root / folder).rglob('*')):
            relative = path.relative_to(root)
            if path.is_file() and not {'bin', 'obj', '__pycache__', '.godot'}.intersection(relative.parts) and path.suffix != '.uid':
                archive.write(path, relative.as_posix())
    archive.write(root / 'README.md', 'README.md')
    archive.write(output / 'RefreshAncient-compat4-validation.txt', 'VALIDATION-compat4.txt')
with ZipFile(source_zip) as archive:
    assert archive.testzip() is None
print(f'Packaged {binary_zip.name} ({binary_zip.stat().st_size} bytes)')
print(f'Packaged {source_zip.name} ({source_zip.stat().st_size} bytes)')
