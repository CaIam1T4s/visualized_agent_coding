"""Restore hash-pinned private dependencies; never install into global Python."""
import hashlib
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile


def main():
    helper = Path(__file__).resolve().parents[1]
    root = helper.parents[1]
    requirements = helper / 'requirements.txt'
    destination = root / 'artifacts' / 'python-helper' / 'vendor'
    fingerprint = hashlib.sha256(requirements.read_bytes()).hexdigest()
    stamp = destination / '.requirements.sha256'
    if (stamp.is_file() and stamp.read_text() == fingerprint
            and (destination / 'jedi' / '__init__.py').is_file()
            and (destination / 'parso' / '__init__.py').is_file()):
        return
    destination.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='restore-', dir=destination.parent) as temporary:
        staged = Path(temporary) / 'vendor'
        command = [sys.executable, '-m', 'pip', 'install', '--disable-pip-version-check',
                   '--require-hashes', '--only-binary=:all:', '--no-compile',
                   '--target', str(staged), '-r', str(requirements)]
        wheels = destination.parent / 'wheels'
        if wheels.is_dir():
            command.extend(['--no-index', '--find-links', str(wheels)])
        subprocess.run(command, check=True)
        (staged / '.requirements.sha256').write_text(fingerprint)
        # Only this fixed, generated workspace directory may be replaced.
        if destination.exists():
            shutil.rmtree(destination)
        shutil.move(str(staged), str(destination))


if __name__ == '__main__':
    main()
