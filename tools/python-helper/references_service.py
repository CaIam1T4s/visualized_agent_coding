"""On-demand, read-only static reference queries over a client-owned source snapshot."""
import ast
from pathlib import Path
import sys
from urllib.parse import urlparse
from urllib.request import url2pathname

from dependencies import configure_dependencies
configure_dependencies()
import jedi
import parso.cache
from jedi.api.environment import InterpreterEnvironment

from protocol import ERR_INVALID_PARAMS, ProtocolError


def _file_path(uri):
    parsed = urlparse(uri)
    if parsed.scheme != 'file' or parsed.netloc not in ('', 'localhost'):
        raise ProtocolError(ERR_INVALID_PARAMS, 'expected a local file URI')
    return Path(url2pathname(parsed.path)).resolve()


def _collect_references(script, line, column, root, project):
    """Follow import aliases as separate bindings without matching names textually."""
    queue = [(script.path, line, column)]
    for definition in script.goto(line, column, follow_imports=True):
        if definition.module_path is not None and Path(definition.module_path).resolve().is_relative_to(root):
            queue.append((definition.module_path, definition.line, definition.column))
    visited, found, trees = set(), [], {}
    while queue and len(visited) < 100:
        path, row, col = queue.pop(0)
        path = Path(path).resolve()
        key = (path, row, col)
        if key in visited:
            continue
        visited.add(key)
        current = jedi.Script(path=path, project=project, environment=InterpreterEnvironment())
        for reference in current.get_references(row, col, include_builtins=False, scope='project'):
            found.append(reference)
            if reference.module_path is None:
                continue
            file = Path(reference.module_path).resolve()
            if not file.is_relative_to(root):
                continue
            if file not in trees:
                text = file.read_text(encoding='utf-8')
                try:
                    trees[file] = (ast.parse(text), text.split('\n'))
                except SyntaxError:
                    trees[file] = (None, [])
            tree, lines = trees[file]
            if tree is None:
                continue
            for node in ast.walk(tree):
                if not isinstance(node, ast.alias) or not node.asname or node.end_lineno != reference.line:
                    continue
                text = lines[reference.line - 1]
                start = len(text.encode('utf-8')[:node.col_offset].decode('utf-8'))
                end = len(text.encode('utf-8')[:node.end_col_offset].decode('utf-8'))
                if node.lineno == reference.line and start <= reference.column < end:
                    queue.append((file, reference.line, end - len(node.asname)))
    return found, bool(queue)


def find_references(params):
    root_value, uri = params.get('projectRoot'), params.get('uri')
    version, line, column = params.get('version'), params.get('line'), params.get('column')
    if (not isinstance(root_value, str) or not isinstance(uri, str)
            or type(version) is not int or version < 0
            or type(line) is not int or line < 1 or type(column) is not int or column < 1):
        raise ProtocolError(ERR_INVALID_PARAMS, 'expected projectRoot, uri, version and 1-based UTF-16 line/column')
    root = Path(root_value).resolve()
    path = _file_path(uri)
    if not root.is_dir() or not path.is_relative_to(root) or path.suffix.lower() != '.py' or not path.is_file():
        raise ProtocolError(ERR_INVALID_PARAMS, 'target must be a Python file inside the snapshot')
    source = path.read_text(encoding='utf-8')
    lines = source.split('\n')
    if line > len(lines):
        raise ProtocolError(ERR_INVALID_PARAMS, 'line is outside the source')
    encoded = lines[line - 1].rstrip('\r').encode('utf-16-le')
    if (column - 1) * 2 > len(encoded):
        raise ProtocolError(ERR_INVALID_PARAMS, 'column is outside the source')
    try:
        character_column = len(encoded[:(column - 1) * 2].decode('utf-16-le'))
    except UnicodeDecodeError:
        raise ProtocolError(ERR_INVALID_PARAMS, 'column splits a UTF-16 surrogate pair')

    # No user module is imported or executed, and no .jedi settings/cache is written.
    # Jedi requires Parso's in-memory node cache. Scope its disk cache to the
    # disposable snapshot, never to the real project or global user cache.
    parso.cache.parser_cache.clear()
    jedi.settings.fast_parser = True
    jedi.settings.cache_directory = str(root / '.analysis-cache')
    jedi.settings.auto_import_modules = []
    jedi.settings.allow_unsafe_interpreter_executions = False
    search_paths = [root, root / 'scripts', root / 'scripts/user', root / 'scripts/objects', root / 'scripts/backdrops']
    project = jedi.Project(root, sys_path=[str(p) for p in search_paths],
                           smart_sys_path=False, load_unsafe_extensions=False)
    script = jedi.Script(code=source, path=path, project=project, environment=InterpreterEnvironment())
    references, truncated = _collect_references(script, line, character_column, root, project)
    locations, issues, seen = [], [], set()
    for reference in references:
        if reference.module_path is None:
            continue
        target = Path(reference.module_path).resolve()
        if not target.is_relative_to(root):
            continue
        target_lines = target.read_text(encoding='utf-8').split('\n')
        text = target_lines[reference.line - 1]
        start = len(text[:reference.column].encode('utf-16-le')) // 2 + 1
        end = start + len(reference.name.encode('utf-16-le')) // 2
        key = (target.as_uri(), reference.line, start, end)
        if key in seen:
            continue
        seen.add(key)
        locations.append({'uri': key[0], 'range': {'line': reference.line, 'column': start,
                          'endLine': reference.line, 'endColumn': end},
                          'isDefinition': reference.is_definition(), 'name': reference.name})
    for file in sorted(root.rglob('*.py')):
        if not file.resolve().is_relative_to(root):
            continue
        try:
            ast.parse(file.read_text(encoding='utf-8'))
        except SyntaxError as error:
            issues.append({'uri': file.as_uri(), 'message': f'第 {error.lineno} 行存在语法错误，引用可能不完整：{error.msg}'})
    if truncated:
        issues.append({'uri': uri, 'message': '引用路径超过本次查询限制，结果可能不完整。'})
    locations.sort(key=lambda item: (item['uri'], item['range']['line'], item['range']['column']))
    parso.cache.parser_cache.clear()
    return {'uri': uri, 'version': version, 'references': locations, 'issues': issues, 'isIncomplete': bool(issues)}
