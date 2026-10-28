"""Import dependency analysis, separate from caret symbol references."""
import ast
import io
import tokenize
from pathlib import Path
from dependencies import configure_dependencies
configure_dependencies()
import jedi
import parso.cache
from jedi.api.environment import InterpreterEnvironment
from analysis_environment import create_project, file_path
from protocol import ERR_INVALID_PARAMS, ProtocolError


def find_module_dependencies(params):
    root_value, uri, version = params.get('projectRoot'), params.get('uri'), params.get('version')
    if not isinstance(root_value, str) or not isinstance(uri, str) or type(version) is not int or version < 0:
        raise ProtocolError(ERR_INVALID_PARAMS, 'expected projectRoot, uri and nonnegative version')
    root, target = Path(root_value).resolve(), file_path(uri)
    if not root.is_dir() or not target.is_relative_to(root) or target.suffix.lower() != '.py' or not target.is_file():
        raise ProtocolError(ERR_INVALID_PARAMS, 'target must be a Python file inside the snapshot')
    return find_module_impact(root, target, uri, version)


def find_module_impact(root, target, uri, version):
    """Resolve import tokens statically; never import or execute project code."""
    project = create_project(root)
    locations, issues = [], []
    try:
        for file in sorted(root.rglob('*.py')):
            if file == target or not file.resolve().is_relative_to(root):
                continue
            source = file.read_text(encoding='utf-8')
            try:
                tree = ast.parse(source)
            except SyntaxError as error:
                issues.append({'uri': file.as_uri(), 'message': f'第 {error.lineno} 行存在语法错误，无法完整分析此文件的导入。'})
                continue
            script = jedi.Script(code=source, path=file, project=project, environment=InterpreterEnvironment())
            tokens = list(tokenize.generate_tokens(io.StringIO(source).readline))
            imports = [node for node in ast.walk(tree) if isinstance(node, (ast.Import, ast.ImportFrom))]
            lines = source.split("\n")
            for node in ast.walk(tree):
                if isinstance(node, ast.Call) and (isinstance(node.func, ast.Name) and node.func.id == '__import__'
                        or isinstance(node.func, ast.Attribute) and node.func.attr == 'import_module'
                        or isinstance(node.func, ast.Name) and node.func.id == 'import_module'):
                    issues.append({'uri': file.as_uri(), 'message': f'第 {node.lineno} 行存在动态导入，需要手动检查。'})
                if not isinstance(node, (ast.Import, ast.ImportFrom)):
                    continue
                if isinstance(node, ast.ImportFrom) and node.level:
                    base = file.parent
                    for _ in range(node.level - 1):
                        base = base.parent
                    module = base.joinpath(*(node.module or '').split('.')) if node.module else base
                    candidates = [module.with_suffix('.py'), module / '__init__.py'] if node.module else []
                    candidates += [module / (alias.name + '.py') for alias in node.names]
                    if target in [p.resolve() for p in candidates]:
                        text = source.split('\n')[node.lineno - 1]
                        col = len(text.encode('utf-8')[:node.col_offset].decode('utf-8').encode('utf-16-le')) // 2 + 1
                        locations.append({'uri': file.as_uri(), 'range': {'line': node.lineno, 'column': col,
                            'endLine': node.lineno, 'endColumn': col + 4}, 'isDefinition': False, 'name': 'from'})
                        continue
                for token in tokens:
                    if token.type != tokenize.NAME or token.string in ('from', 'import', 'as'):
                        continue
                    if not (node.lineno <= token.start[0] <= node.end_lineno):
                        continue
                    if token.start[0] == node.lineno and token.start[1] < node.col_offset:
                        continue
                    if token.end[0] == node.end_lineno and token.end[1] > node.end_col_offset:
                        continue
                    definitions = script.goto(*token.start, follow_imports=True)
                    if not any(d.module_path is not None and Path(d.module_path).resolve() == target for d in definitions):
                        continue
                    line = source.split('\n')[token.start[0] - 1]
                    column = len(line[:token.start[1]].encode('utf-16-le')) // 2 + 1
                    locations.append({'uri': file.as_uri(), 'range': {'line': token.start[0], 'column': column,
                        'endLine': token.start[0], 'endColumn': column + len(token.string.encode('utf-16-le')) // 2},
                        'isDefinition': False, 'name': token.string})
                    break
            # Import statements have one entry each; also include every resolved use
            # outside those statements. Jedi distinguishes aliases and local shadows.
            for token in tokens:
                if token.type != tokenize.NAME:
                    continue
                row, col = token.start
                byte_col = len(lines[row - 1][:col].encode('utf-8'))
                if any((node.lineno, node.col_offset) <= (row, byte_col) < (node.end_lineno, node.end_col_offset)
                       for node in imports):
                    continue
                definitions = script.goto(row, col, follow_imports=True)
                if not any(d.module_path is not None and Path(d.module_path).resolve() == target for d in definitions):
                    continue
                column = len(lines[row - 1][:col].encode('utf-16-le')) // 2 + 1
                locations.append({'uri': file.as_uri(), 'range': {'line': row, 'column': column,
                    'endLine': row, 'endColumn': column + len(token.string.encode('utf-16-le')) // 2},
                    'isDefinition': False, 'name': token.string})
        locations.sort(key=lambda item: (item['uri'], item['range']['line'], item['range']['column']))
        return {'uri': uri, 'version': version, 'references': locations, 'issues': issues, 'isIncomplete': bool(issues)}
    finally:
        parso.cache.parser_cache.clear()
