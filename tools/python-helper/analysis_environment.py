"""Shared isolated static analysis configuration; user code is never executed."""
from pathlib import Path
from urllib.parse import urlparse
from urllib.request import url2pathname
from protocol import ERR_INVALID_PARAMS, ProtocolError
from dependencies import configure_dependencies
configure_dependencies()
import jedi
import parso.cache


def create_project(root):
    parso.cache.parser_cache.clear()
    jedi.settings.fast_parser = True
    jedi.settings.cache_directory = str(root / '.analysis-cache')
    jedi.settings.auto_import_modules = []
    jedi.settings.allow_unsafe_interpreter_executions = False
    search_paths = [root, root / 'scripts', root / 'scripts/user', root / 'scripts/objects', root / 'scripts/backdrops']
    return jedi.Project(root, sys_path=[str(p) for p in search_paths],
                           smart_sys_path=False, load_unsafe_extensions=False)


def file_path(uri):
    parsed = urlparse(uri)
    if parsed.scheme != 'file' or parsed.netloc not in ('', 'localhost'):
        raise ProtocolError(ERR_INVALID_PARAMS, 'expected a local file URI')
    return Path(url2pathname(parsed.path)).resolve()
