"""Managed module imports and product rules. No submitted module is executed."""
import ast
import io
import json
import keyword
import re
import sys
import sysconfig
import tokenize
from functools import lru_cache
from importlib.machinery import BuiltinImporter, FrozenImporter, PathFinder
from pathlib import Path
from protocol import ProtocolError, ERR_INVALID_PARAMS

BEGIN = '# vac:imports-begin'
END = '# vac:imports-end'
META = '# vac:import '


def _offset(source, row, col=0):
    lines = source.splitlines(keepends=True)
    return sum(len(x) for x in lines[:row - 1]) + col


def _utf16(source, offset):
    return len(source[:offset].encode('utf-16-le')) // 2


def _char_offset(source, offset):
    return len(source.encode('utf-16-le')[:offset * 2].decode('utf-16-le'))


def _ident(name):
    return isinstance(name, str) and name.isidentifier() and not keyword.iskeyword(name)


def _module(name):
    return isinstance(name, str) and all(_ident(x) for x in name.split('.'))


@lru_cache(maxsize=1)
def standard_modules():
    paths = list(dict.fromkeys([sysconfig.get_path('stdlib'), sysconfig.get_path('platstdlib'),
                               str(Path(sys.base_prefix) / 'DLLs'),
                               str(Path(sysconfig.get_path('stdlib')) / 'lib-dynload')]))
    available = []
    for name in sorted(getattr(sys, 'stdlib_module_names', set(sys.builtin_module_names))):
        if name.startswith('_'):
            continue
        try:
            spec = BuiltinImporter.find_spec(name) or FrozenImporter.find_spec(name) or PathFinder.find_spec(name, paths)
            if spec is not None:
                available.append(name)
        except (ImportError, ValueError, OSError):
            pass
    return tuple(available)


def _entries(node, source_kind='', script_id=None):
    return [dict(module=alias.name if isinstance(node, ast.Import) else ('.' * node.level + (node.module or '')),
                 name=None if isinstance(node, ast.Import) else alias.name, alias=alias.asname,
                 source=source_kind, scriptId=script_id) for alias in node.names]


def binding(entry):
    return entry.get('alias') or entry.get('name') or entry['module'].split('.')[0]


def statement(entry):
    text = ('from ' + entry['module'] + ' import ' + entry['name'] if entry.get('name')
            else 'import ' + entry['module'])
    return text + (' as ' + entry['alias'] if entry.get('alias') else '')


def inspect_block(source):
    tokens = []
    try:
        for token in tokenize.generate_tokens(io.StringIO(source).readline):
            if token.type == tokenize.COMMENT and token.string in (BEGIN, END):
                tokens.append(token)
    except (tokenize.TokenError, IndentationError, SyntaxError):
        pass
    if not tokens:
        return None
    if (len(tokens) != 2 or [x.string for x in tokens] != [BEGIN, END]
            or any(x.start[1] != 0 for x in tokens)):
        raise ValueError('受管理导入区标记缺失、重复或位置错误。')
    first = _offset(source, tokens[0].start[0])
    last = _offset(source, tokens[1].start[0] + 1)
    lines = source.splitlines(keepends=True)
    body = ''.join(lines[tokens[0].start[0]:tokens[1].start[0] - 1])
    tree = ast.parse(body)
    entries = []
    for node in tree.body:
        if not isinstance(node, (ast.Import, ast.ImportFrom)) or len(node.names) != 1 or node.lineno != node.end_lineno:
            raise ValueError('受管理导入区只允许单行、单目标导入。')
        line = body.splitlines()[node.lineno - 1]
        position = line.find(META)
        if position < 0:
            raise ValueError('受管理导入缺少身份信息。')
        metadata = json.loads(line[position + len(META):])
        if not isinstance(metadata, dict) or set(metadata) - {'source', 'scriptId'}:
            raise ValueError('受管理导入身份信息无效。')
        if metadata.get('source') not in ('project', 'standard', 'runtime') or (metadata.get('source') == 'project'
                and (not isinstance(metadata.get('scriptId'), str) or not metadata['scriptId'])):
            raise ValueError('受管理导入必须包含有效的来源和项目脚本身份。')
        entries.extend(_entries(node, metadata.get('source'), metadata.get('scriptId')))
    if len(entries) > 128:
        raise ValueError('导入数量不能超过 128。')
    return dict(span=dict(offset=_utf16(source, first), length=_utf16(source, last) - _utf16(source, first)), entries=entries)


def classify(entry, context):
    module = entry.get('module')
    if not _module(module) or entry.get('name') == '*' or (entry.get('name') is not None and not _ident(entry['name'])):
        return None, '不支持相对导入、通配符或无效模块名称。'
    if entry.get('alias') is not None and not _ident(entry['alias']):
        return None, '别名必须是有效且非保留的 Python 名称。'
    if module == 'vac_runtime' and entry.get('name') in ('broadcast', 'timer', 'switch_backdrop'):
        if entry.get('source') not in (None, '', 'runtime') or entry.get('scriptId'):
            return None, '运行时 API 必须来自产品运行时。'
        return dict(entry, source='runtime', scriptId=None), None
    if entry.get('source') == 'runtime':
        return None, '当前公开运行时 API 仅支持 from vac_runtime import broadcast, timer, switch_backdrop。'
    scripts = context.get('scripts', [])
    forms = {module, module + '.' + entry['name'] if entry.get('name') else module}
    target = next((s for s in scripts if forms.intersection((s['module'], s['module'].split('.')[-1]))), None)
    if entry.get('source') == 'project' and entry.get('scriptId'):
        target = next((s for s in scripts if s['id'] == entry['scriptId']), None)
        if target is None:
            return None, '导入的项目脚本已不存在。'
        if not forms.intersection((target['module'], target['module'].split('.')[-1])):
            return None, '导入路径与脚本身份不一致，请重新选择目标。'
    if target is not None:
        if entry.get('source') == 'standard':
            return None, '项目脚本与该标准库同名，请先重命名项目脚本以消除遮蔽。'
        if target['kind'] != 'user':
            return None, '禁止导入角色或背景脚本。'
        if target['id'] == context.get('scriptId'):
            return None, '不能导入当前脚本自身。'
        return dict(entry, source='project', scriptId=target['id']), None
    if module == 'runtime' or module.startswith(('runtime.', 'vac_runtime')):
        return None, '该运行时模块为内部实现，不能直接导入；请使用公开的 broadcast、timer 或 switch_backdrop API。'
    if module.split('.')[0] in standard_modules():
        if module != module.split('.')[0] and module != 'os.path':
            paths = list(dict.fromkeys([sysconfig.get_path('stdlib'), sysconfig.get_path('platstdlib')]))
            spec = None
            for part in module.split('.'):
                spec = PathFinder.find_spec(part if spec is None else spec.name + '.' + part,
                                            paths if spec is None else spec.submodule_search_locations)
                if spec is None:
                    return None, '当前 Python 环境不提供该标准库模块。'
        return dict(entry, source='standard', scriptId=None), None
    return None, '目标脚本或标准库不可用；暂不支持第三方包。'


def _diagnostic(message, line, column=1, navigation=''):
    return dict(severity='error', message=message, line=max(1, line), column=column,
                parser='rule', category='import-rule', navigation=navigation)


def _identity(entry):
    return (entry.get('source'), entry.get('scriptId') if entry.get('source') == 'project' else entry['module'], entry.get('name'))


def inspect_imports(params):
    source, context = params.get('source'), params.get('context')
    if (not isinstance(source, str) or not isinstance(context, dict) or type(params.get('version')) is not int
            or not isinstance(params.get('uri'), str) or context.get('kind') not in ('user', 'object', 'backdrop')):
        raise ProtocolError(ERR_INVALID_PARAMS, 'expected source, context, uri and version')
    try:
        block = inspect_block(source)
    except (ValueError, SyntaxError, TypeError, KeyError) as error:
        return dict(uri=params['uri'], version=params['version'], source=source, block=None, standardModules=list(standard_modules()),
                    diagnostics=[_diagnostic('导入区结构异常：' + str(error), 0, navigation='imports')],
                    syntaxValid=False, insertOffset=0, problem='导入区结构异常，请修复后重新加载。')
    diagnostics, manual, bindings = [], [], set()
    declared_bindings = set()
    start = _char_offset(source, block['span']['offset']) if block else -1
    end = _char_offset(source, block['span']['offset'] + block['span']['length']) if block else -1
    try:
        tree = ast.parse(source)
    except SyntaxError:
        tree = None
    insert = 0
    if tree is not None:
        for i, node in enumerate(tree.body):
            if (i == 0 and isinstance(node, ast.Expr) and isinstance(node.value, ast.Constant) and isinstance(node.value.value, str)
                    or isinstance(node, ast.ImportFrom) and node.module == '__future__'):
                insert = _offset(source, node.end_lineno + 1)
            else:
                break
        for node in tree.body:
            if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef)):
                bindings.add(node.name)
            elif isinstance(node, (ast.Assign, ast.AnnAssign)):
                for target in node.targets if isinstance(node, ast.Assign) else [node.target]:
                    bindings.update(n.id for n in ast.walk(target) if isinstance(n, ast.Name))
        declared_bindings = bindings.copy()
        for node in ast.walk(tree):
            if not isinstance(node, (ast.Import, ast.ImportFrom)):
                continue
            offset = _offset(source, node.lineno)
            if start <= offset < end:
                continue
            user = context['kind'] == 'user' or any(r['offset'] <= _utf16(source, offset) < r['offset'] + r['length']
                                                  for r in context.get('regions', []))
            if not user:
                if node in tree.body:
                    protected = {binding(e) for e in _entries(node)}
                    bindings.update(protected)
                    declared_bindings.update(protected)
                continue
            if isinstance(node, ast.ImportFrom) and node.module == '__future__':
                continue
            entries = _entries(node)
            errors = []
            for entry in entries:
                _, error = classify(entry, context)
                if error:
                    errors.append(error)
            if context['kind'] != 'user':
                errors.append('请通过“更多 → 添加 → 导入脚本”管理导入。')
            if errors:
                line_text = source.splitlines()[node.lineno - 1]
                prefix = line_text.encode('utf-8')[:node.col_offset].decode('utf-8')
                diagnostics.append(_diagnostic('；'.join(dict.fromkeys(errors)), node.lineno, len(prefix.encode('utf-16-le')) // 2 + 1))
            bindings.update(binding(e) for e in entries)
            manual.append((node, entries))
    existing = block['entries'] if block else []
    seen, seen_targets = set(), set()
    for entry in existing:
        _, error = classify(entry, context)
        if not error and binding(entry) in bindings | seen:
            error = '导入名称与已有定义或导入冲突：' + binding(entry)
        if not error and _identity(entry) in seen_targets:
            error = '重复导入同一模块或名称：' + entry['module']
        if error:
            diagnostics.append(_diagnostic(error, source[:start].count('\n') + 2, navigation='imports'))
        seen.add(binding(entry))
        seen_targets.add(_identity(entry))
    desired = params.get('imports')
    convert_line = params.get('convertLine')
    if desired is not None or convert_line is not None:
        if desired is None:
            desired = existing.copy()
        if not isinstance(desired, list) or len(desired) > 128 or any(not isinstance(e, dict) for e in desired):
            raise ProtocolError(ERR_INVALID_PARAMS, 'imports must be an array of at most 128 entries')
        if tree is None and not all(entry in existing for entry in desired):
            raise ProtocolError(ERR_INVALID_PARAMS, '请先修复语法错误，再添加或转换导入。')
        removal = None
        if convert_line is not None:
            found = next(((n, e) for n, e in manual if n.lineno == convert_line), None)
            if found is None:
                raise ProtocolError(ERR_INVALID_PARAMS, '当前行不是可转换的完整导入语句。')
            node, entries = found
            line_text = source.splitlines()[node.lineno - 1]
            prefix_text = line_text.encode('utf-8')[:node.col_offset].decode('utf-8')
            if node.lineno != node.end_lineno or ';' in line_text or prefix_text.strip():
                raise ProtocolError(ERR_INVALID_PARAMS, '请先将导入拆为独立单行语句，再进行转换。')
            desired += entries
            needs_pass = any(isinstance(value, list) and len(value) == 1 and value[0] is node
                             and not isinstance(parent, ast.Module)
                             for parent in ast.walk(tree) for _, value in ast.iter_fields(parent))
            suffix = line_text.encode('utf-8')[node.end_col_offset:].decode('utf-8').strip()
            replacement = (prefix_text + suffix + '\n' if suffix.startswith('#') else '')
            if needs_pass:
                replacement += prefix_text + 'pass\n'
            removal = (_offset(source, node.lineno), _offset(source, node.end_lineno + 1), replacement)
            bindings.difference_update(binding(e) for e in entries)
            bindings.update(declared_bindings)
            bindings.update(binding(e) for other, values in manual if other is not node for e in values)
        normalized, seen, seen_targets = [], set(), set()
        for entry in desired:
            value, error = classify(entry, context)
            if error:
                raise ProtocolError(ERR_INVALID_PARAMS, error)
            name = binding(value)
            if name in bindings | seen:
                raise ProtocolError(ERR_INVALID_PARAMS, '导入名称与已有定义或导入冲突：' + name)
            if _identity(value) in seen_targets:
                raise ProtocolError(ERR_INVALID_PARAMS, '重复导入同一模块或名称：' + value['module'])
            seen.add(name)
            seen_targets.add(_identity(value))
            normalized.append(value)
        nl = '\r\n' if '\r\n' in source else '\n'
        generated = BEGIN + nl
        for entry in normalized:
            metadata = dict(source=entry['source'])
            if entry.get('scriptId'):
                metadata['scriptId'] = entry['scriptId']
            generated += statement(entry) + '  ' + META + json.dumps(metadata, ensure_ascii=False, separators=(',', ':')) + nl
        generated += END + nl
        if block is None and insert > 0 and source[insert - 1] != '\n':
            generated = nl + generated
        edits = [(start, end, generated)] if block else [(insert, insert, generated)]
        if removal:
            edits.append((removal[0], removal[1], removal[2].replace('\n', nl)))
        for first, last, replacement in sorted(edits, reverse=True):
            source = source[:first] + replacement + source[last:]
        return inspect_imports(dict(params, source=source, imports=None, convertLine=None))
    return dict(uri=params['uri'], version=params['version'], source=source, block=block,
                standardModules=list(standard_modules()), diagnostics=diagnostics, syntaxValid=tree is not None,
                insertOffset=_utf16(source, insert), problem=None)
