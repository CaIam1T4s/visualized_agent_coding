"""Conservative module rename plan. No user code is executed and no files are written."""
import ast
import keyword
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


def plan_module_rename(params):
    root_value, uri, version, new_name, old_class_name, new_class_name = (
        params.get(key) for key in ('projectRoot', 'uri', 'version', 'newName', 'oldClassName', 'newClassName'))
    if (not isinstance(root_value, str) or not isinstance(uri, str) or type(version) is not int or version < 0
            or not isinstance(new_name, str) or not new_name.isidentifier() or keyword.iskeyword(new_name)
            or new_name == '__init__'):
        raise ProtocolError(ERR_INVALID_PARAMS, 'expected projectRoot, uri, version and a Python module identifier')
    if ((old_class_name is None) != (new_class_name is None)
            or old_class_name is not None and (not isinstance(old_class_name, str) or not old_class_name.isidentifier()
                                               or keyword.iskeyword(old_class_name))
            or new_class_name is not None and (not isinstance(new_class_name, str) or not new_class_name.isidentifier()
                                               or keyword.iskeyword(new_class_name))):
        raise ProtocolError(ERR_INVALID_PARAMS, 'class names must be Python identifiers')
    root, target = Path(root_value).resolve(), file_path(uri)
    if not target.is_relative_to(root) or target.suffix != '.py' or not target.is_file() or target.stem == '__init__':
        raise ProtocolError(ERR_INVALID_PARAMS, 'target must be a Python file in the snapshot')
    project = create_project(root)
    changes, issues = [], []
    try:
        for file in sorted(root.rglob('*.py')):
            with file.open(encoding='utf-8', newline='') as stream:
                source = stream.read()
            try:
                tree = ast.parse(source)
            except SyntaxError as error:
                issues.append({'uri': file.as_uri(), 'message': f'第 {error.lineno} 行语法错误，无法安全重命名。'})
                continue
            dynamic = [node for node in ast.walk(tree) if isinstance(node, ast.Call) and
                       (isinstance(node.func, ast.Name) and node.func.id in ('__import__', 'import_module', 'exec', 'eval')
                        or isinstance(node.func, ast.Attribute) and node.func.attr == 'import_module')]
            imported_loader = any(isinstance(node, ast.ImportFrom) and node.module == 'importlib'
                                  and any(alias.name == 'import_module' for alias in node.names) for node in ast.walk(tree))
            if dynamic or imported_loader:
                issues.append({'uri': file.as_uri(), 'message': '存在动态导入或动态执行，需人工处理后再重命名。'})
                continue
            lines = source.splitlines(keepends=True)
            starts, offset = [], 0
            for text in lines:
                starts.append(offset)
                offset += len(text)
            script = jedi.Script(code=source, path=file, project=project, environment=InterpreterEnvironment())
            tokens = list(tokenize.generate_tokens(io.StringIO(source).readline))
            imports = [node for node in ast.walk(tree) if isinstance(node, (ast.Import, ast.ImportFrom))]
            alias_bindings = set()
            for node in imports:
                for alias in node.names:
                    if alias.asname:
                        text = lines[alias.end_lineno - 1]
                        end = len(text.encode('utf-8')[:alias.end_col_offset].decode('utf-8'))
                        alias_bindings.add((alias.end_lineno, end - len(alias.asname)))
            edits = []
            class_edit = None
            if old_class_name is not None and file.resolve() == target:
                classes = [node for node in tree.body if isinstance(node, ast.ClassDef) and node.name == old_class_name]
                if len(classes) != 1:
                    issues.append({'uri': file.as_uri(), 'message': '目标文件必须包含唯一的旧类定义，无法安全重命名。'})
                    continue
                class_node = classes[0]
                class_token = None
                for index, token in enumerate(tokens[:-1]):
                    if token.type != tokenize.NAME or token.string != 'class':
                        continue
                    name_token = tokens[index + 1]
                    if (name_token.type == tokenize.NAME and name_token.string == old_class_name
                            and name_token.start[0] == class_node.lineno):
                        class_token = name_token
                        break
                if class_token is None:
                    issues.append({'uri': file.as_uri(), 'message': '无法定位旧类名 token，不能安全重命名。'})
                    continue
                if any(token.type == tokenize.NAME and token.string == new_class_name for token in tokens):
                    issues.append({'uri': file.as_uri(), 'message': '新类名与文件中的现有名称冲突，请选择其他名称。'})
                    continue
                row, col = class_token.start
                start = starts[row - 1] + col
                class_edit = (start, start + len(class_token.string), new_class_name)
                edits.append(class_edit)
            for token in tokens:
                if token.type != tokenize.NAME or token.string != target.stem:
                    continue
                row, col = token.start
                byte_col = len(lines[row - 1][:col].encode('utf-8'))
                owner = next((node for node in imports if (node.lineno, node.col_offset) <= (row, byte_col)
                              < (node.end_lineno, node.end_col_offset)), None)
                definitions = script.goto(row, col, follow_imports=True)
                resolved = [d for d in definitions if d.module_path is not None and Path(d.module_path).resolve() == target and d.type == 'module']
                if not resolved:
                    # Jedi can fail to resolve a relative module in a root package. Do
                    # not guess at spelling: block that import rather than miss it.
                    if owner is not None and isinstance(owner, ast.ImportFrom) and owner.level:
                        issues.append({'uri': file.as_uri(), 'message': f'第 {row} 行相对导入无法确定，请人工核对。'})
                    continue
                if len(resolved) != len(definitions):
                    issues.append({'uri': file.as_uri(), 'message': f'第 {row} 行名称解析存在歧义，无法安全改写。'})
                    continue
                if owner is not None:
                    # An explicit alias is a local binding, even when spelled like the
                    # module. Only edit module path tokens, never the alias token.
                    preceding = [t for t in tokens if t.end <= token.start and t.start >= (owner.lineno, 0)]
                    if preceding and preceding[-1].string == 'as':
                        continue
                else:
                    bindings = script.goto(row, col, follow_imports=False)
                    if any(d.module_path is not None and Path(d.module_path).resolve() == file
                           and (d.line, d.column) in alias_bindings for d in bindings):
                        continue
                start = starts[row - 1] + col
                edits.append((start, start + len(token.string), new_name))
            if edits and any(t.type == tokenize.NAME and t.string == new_name for t in tokens):
                issues.append({'uri': file.as_uri(), 'message': '新模块名与文件中的现有名称冲突，请选择其他名称。'})
                continue
            ordered = sorted(set(edits))
            if any(left[1] > right[0] for left, right in zip(ordered, ordered[1:])):
                issues.append({'uri': file.as_uri(), 'message': '重命名编辑区间重叠，未生成修改。'})
                continue
            updated = source
            for start, end, replacement in sorted(set(edits), reverse=True):
                updated = updated[:start] + replacement + updated[end:]
            if updated != source:
                ast.parse(updated)
                changes.append({'uri': file.as_uri(), 'source': updated})
        return {'uri': uri, 'version': version, 'changes': changes, 'issues': issues, 'isIncomplete': bool(issues)}
    finally:
        parso.cache.parser_cache.clear()
