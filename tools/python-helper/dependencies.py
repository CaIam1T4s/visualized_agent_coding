"""Locate private dependencies in a published app or a prepared source checkout."""
from pathlib import Path
import sys


def configure_dependencies():
    here = Path(__file__).resolve().parent
    vendor = here / 'vendor'
    if not vendor.is_dir():
        vendor = here.parents[1] / 'artifacts' / 'python-helper' / 'vendor'
    if not (vendor / 'jedi').is_dir() or not (vendor / 'parso').is_dir():
        raise RuntimeError('Helper dependencies missing. Run tools/python-helper/scripts/prepare_dependencies.py.')
    if str(vendor) not in sys.path:
        sys.path.insert(0, str(vendor))
