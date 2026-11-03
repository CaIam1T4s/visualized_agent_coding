"""Read-only node projection for a user region or a whole Python module.

The source is already dedented when it belongs to one region. Unsupported
statements stay as raw nodes; this module never rewrites source.
"""
import ast
import io
import tokenize


def build_blocks(params):
    """Project one region body. Kept for the legacy `python.blocks` entry."""
    source = params.get('source')
    if not isinstance(source, str):
        raise ValueError('source must be a string')
    return _build(source, 'region')


def build_document(params):
    """Project a whole module, including class/function definitions and members."""
    source = params.get('source')
    if not isinstance(source, str):
        raise ValueError('source must be a string')
    return _build(source, 'document')


def _build(source, mode):
    try:
        tree = ast.parse(source)
    except (SyntaxError, ValueError) as error:
        return dict(error=_error_text(error), nodes=[], mode=mode)
    lines = source.splitlines(keepends=True)
    comments = _comment_map(source)
    nodes = _body_nodes(tree.body, source, lines, 1, comments)
    return dict(error=None, nodes=nodes, mode=mode)


def _error_text(error):
    if isinstance(error, SyntaxError):
        return f'第 {error.lineno or "?"} 行：{error.msg}'
    return str(error)


def _body_nodes(body, source, lines, start=None, comments=None):
    nodes = []
    cursor = start if start is not None else (body[0].lineno if body else None)
    for statement in body:
        if cursor is not None:
            nodes.extend(_comments(source, lines, cursor, statement.lineno - 1))
        node = _node(statement, source, lines, comments or {})
        if node is not None:
            nodes.append(_finish(node, statement, source, comments or {}))
        cursor = (statement.end_lineno or statement.lineno) + 1
    return nodes


def _comments(source, lines, start, end):
    result = []
    line_offsets = []
    offset = 0
    for line in lines:
        line_offsets.append(offset)
        offset += len(line)
    for number in range(max(1, start), min(len(lines), end) + 1):
        text = lines[number - 1].rstrip('\r\n')
        stripped = text.strip()
        if stripped.startswith('#'):
            leading = len(text) - len(text.lstrip())
            char_start = line_offsets[number - 1] + leading
            result.append(dict(type='comment', text=stripped, arguments={},
                               children=[], elseChildren=[], line=number,
                               start=_utf16_len(source[:char_start]),
                               length=_utf16_len(stripped),
                               endLine=number))
    return result


def _comment_map(source):
    result = {}
    try:
        tokens = tokenize.generate_tokens(io.StringIO(source).readline)
        for token in tokens:
            if token.type == tokenize.COMMENT:
                result.setdefault(token.start[0], []).append(
                    (token.start[1], token.string))
    except (tokenize.TokenError, IndentationError):
        pass
    return result


def _node(statement, source, lines, comments):
    if isinstance(statement, ast.For):
        return _for(statement, source, lines, comments)
    if isinstance(statement, ast.While):
        return dict(type='control.while',
                    arguments=dict(condition=_segment(source, statement.test) or '?'),
                    children=_body_nodes(statement.body, source, lines,
                                         statement.lineno + 1, comments),
                    elseChildren=_body_nodes(statement.orelse, source, lines,
                                             statement.orelse[0].lineno if statement.orelse else None,
                                             comments),
                    text='', line=statement.lineno)
    if isinstance(statement, ast.If):
        return dict(type='control.if',
                    arguments=dict(condition=_segment(source, statement.test) or '?'),
                    children=_body_nodes(statement.body, source, lines,
                                         statement.lineno + 1, comments),
                    elseChildren=_body_nodes(statement.orelse, source, lines,
                                             statement.orelse[0].lineno if statement.orelse else None,
                                             comments),
                    text='', line=statement.lineno)
    if isinstance(statement, ast.Match):
        return dict(type='control.match',
                    arguments=dict(subject=_segment(source, statement.subject) or '?'),
                    children=[_case(case, source, lines, comments) for case in statement.cases],
                    elseChildren=[], text='', line=statement.lineno)
    if isinstance(statement, (ast.FunctionDef, ast.AsyncFunctionDef)):
        return dict(type='def',
                    arguments=dict(name=statement.name,
                                   parameters=_parameters(statement.args),
                                   decorators=', '.join(_segment(source, item) or ''
                                                        for item in statement.decorator_list)),
                    children=_body_nodes(statement.body, source, lines,
                                         statement.lineno + 1, comments),
                    elseChildren=[], text='', line=statement.lineno)
    if isinstance(statement, ast.ClassDef):
        children = _body_nodes(statement.body, source, lines,
                               statement.lineno + 1, comments)
        for child in children:
            if child.get('type') == 'data.set_local':
                child['type'] = 'data.set_class_attribute'
                child.setdefault('arguments', {})['kind'] = 'class'
        return dict(type='class',
                    arguments=dict(name=statement.name,
                                   bases=', '.join(_segment(source, item) or ''
                                                   for item in statement.bases),
                                   decorators=', '.join(_segment(source, item) or ''
                                                        for item in statement.decorator_list)),
                    children=children,
                    elseChildren=[], text='', line=statement.lineno)
    if isinstance(statement, ast.Return):
        return dict(type='control.return',
                    arguments=dict(value=_segment(source, statement.value) or ''),
                    children=[], elseChildren=[], text='', line=statement.lineno)
    if isinstance(statement, ast.Break):
        return _simple('control.break', statement)
    if isinstance(statement, ast.Continue):
        return _simple('control.continue', statement)
    if isinstance(statement, ast.Pass):
        return _simple('control.pass', statement)
    if isinstance(statement, ast.Assign):
        return _assign(statement, source)
    if isinstance(statement, ast.AnnAssign):
        return _ann_assign(statement, source)
    if isinstance(statement, ast.AugAssign):
        return _aug_assign(statement, source)
    if isinstance(statement, ast.Expr) and isinstance(statement.value, ast.Call):
        return _call(statement.value, source)
    if (isinstance(statement, ast.Expr) and isinstance(statement.value, ast.Constant)
            and isinstance(statement.value.value, str)):
        return dict(type='docstring', arguments={}, children=[], elseChildren=[],
                    text=statement.value.value, line=statement.lineno)
    return _raw(statement, source)


def _simple(kind, statement):
    return dict(type=kind, arguments={}, children=[], elseChildren=[],
                text='', line=getattr(statement, 'lineno', 0))


def _case(case, source, lines, comments):
    pattern = case.pattern
    guard = _segment(source, case.guard) or '' if case.guard else ''
    return dict(type='control.case',
                arguments=dict(pattern=_segment(source, pattern) or '?', guard=guard),
                children=_body_nodes(case.body, source, lines,
                                     pattern.lineno if pattern else None, comments),
                elseChildren=[], text='', line=getattr(pattern, 'lineno', 0))


def _parameters(args):
    names = [arg.arg for arg in args.posonlyargs + args.args]
    if args.vararg:
        names.append('*' + args.vararg.arg)
    names.extend(arg.arg for arg in args.kwonlyargs)
    if args.kwarg:
        names.append('**' + args.kwarg.arg)
    return ', '.join(names)


def _assign(statement, source):
    if len(statement.targets) != 1:
        return _raw(statement, source)
    target = _target(statement.targets[0])
    if target is None:
        return _raw(statement, source)
    kind, arguments = target
    arguments['value'] = _segment(source, statement.value) or '?'
    return dict(type=kind, arguments=arguments, children=[], elseChildren=[],
                text='', line=statement.lineno)


def _ann_assign(statement, source):
    target = _target(statement.target)
    if target is None:
        return _raw(statement, source)
    kind, arguments = target
    arguments['annotation'] = _segment(source, statement.annotation) or ''
    arguments['value'] = _segment(source, statement.value) or ''
    return dict(type='data.annotate', arguments=arguments, children=[], elseChildren=[],
                text='', line=statement.lineno)


def _aug_assign(statement, source):
    target = _target(statement.target)
    if target is None:
        return _raw(statement, source)
    kind, arguments = target
    arguments['operator'] = type(statement.op).__name__
    arguments['value'] = _segment(source, statement.value) or '?'
    if kind == 'data.set_attribute':
        kind = 'data.add_attribute'
    elif kind == 'data.set_class_attribute':
        kind = 'data.add_class_attribute'
    else:
        kind = 'data.add_local'
    return dict(type=kind, arguments=arguments, children=[], elseChildren=[],
                text='', line=statement.lineno)


def _target(target):
    if isinstance(target, ast.Name):
        return 'data.set_local', dict(target=target.id, name=target.id, kind='local')
    if isinstance(target, ast.Attribute) and isinstance(target.value, ast.Name):
        if target.value.id == 'self':
            return 'data.set_attribute', dict(target=f'self.{target.attr}', name=target.attr,
                                              owner='self', kind='instance')
        return 'data.set_class_attribute', dict(target=f'{target.value.id}.{target.attr}',
                                                name=target.attr, owner=target.value.id,
                                                kind='class')
    return None


def _for(statement, source, lines, comments):
    if statement.orelse or not isinstance(statement.target, ast.Name):
        return _raw(statement, source)
    target = statement.target.id
    loop = statement.iter
    children = _body_nodes(statement.body, source, lines, statement.lineno + 1, comments)
    if _call_name(loop) == 'range' and isinstance(loop, ast.Call):
        arguments = [_segment(source, arg) or '?' for arg in loop.args]
        if len(arguments) == 1 and target == '_':
            return dict(type='control.repeat', arguments=dict(count=arguments[0]),
                        children=children, elseChildren=[], text='', line=statement.lineno)
        if len(arguments) in (1, 2, 3):
            start = arguments[0] if len(arguments) >= 2 else '0'
            stop = arguments[0] if len(arguments) == 1 else arguments[1]
            step = arguments[2] if len(arguments) == 3 else '1'
            return dict(type='control.repeat_range',
                        arguments=dict(name=target, start=start, stop=stop, step=step),
                        children=children, elseChildren=[], text='', line=statement.lineno)
    if (_call_name(loop) == 'list_values' and isinstance(loop, ast.Call)
            and len(loop.args) == 1 and isinstance(loop.args[0], ast.Constant)
            and isinstance(loop.args[0].value, str)):
        return dict(type='control.for_each',
                    arguments=dict(item=target, name=loop.args[0].value),
                    children=children, elseChildren=[], text='', line=statement.lineno)
    return _raw(statement, source)


def _call(call, source):
    name = _call_name(call)
    if name is None:
        return _raw(call, source)
    args = [_segment(source, arg) or '?' for arg in call.args]
    if name == 'self.move' and len(args) == 1:
        return _action('motion.move', dict(steps=args[0]), call)
    if name == 'self.turn' and len(args) == 1:
        return _action('motion.turn', dict(degrees=args[0]), call)
    if name == 'self.goto' and len(args) == 2:
        return _action('motion.goto', dict(x=args[0], y=args[1]), call)
    if name == 'self.glide_to' and len(args) >= 3:
        return _action('motion.glide_to', dict(x=args[0], y=args[1], seconds=args[2]), call)
    if name == 'self.switch_costume' and len(args) == 1:
        return _action('looks.switch_costume', dict(name=args[0]), call)
    if name == 'wait' and len(args) == 1:
        return _action('control.wait', dict(seconds=args[0]), call)
    if name == 'set_variable' and len(args) == 2:
        return _action('data.set', dict(name=args[0], value=args[1]), call)
    if name in ('add_to_variable', 'change_variable') and len(args) == 2:
        return _action('data.add', dict(name=args[0], value=args[1]), call)
    if name == 'print' and len(args) == 1:
        return _action('output.print', dict(value=args[0]), call)
    return _raw(call, source)


def _call_name(call):
    if not isinstance(call, ast.Call):
        return None
    function = call.func
    if isinstance(function, ast.Name):
        return function.id
    if (isinstance(function, ast.Attribute) and isinstance(function.value, ast.Name)
            and function.value.id == 'self'):
        return 'self.' + function.attr
    return None


def _action(kind, arguments, node):
    return dict(type=kind, arguments=arguments, children=[], elseChildren=[],
                text='', line=getattr(node, 'lineno', 0))


def _raw(node, source):
    return dict(type='raw', text=_segment(source, node) or '',
                arguments={}, children=[], elseChildren=[],
                line=getattr(node, 'lineno', 0))


def _segment(source, node):
    if node is None:
        return ''
    return ast.get_source_segment(source, node) or ''


def _finish(node, statement, source, comments):
    start = _char_offset(source, statement.lineno, statement.col_offset)
    end = _char_offset(source, statement.end_lineno or statement.lineno,
                       statement.end_col_offset or statement.col_offset)
    node['start'] = _utf16_len(source[:start])
    node['length'] = _utf16_len(source[start:end])
    node['endLine'] = statement.end_lineno or statement.lineno
    if comments:
        trailing = comments.get(statement.end_lineno or statement.lineno)
        if trailing:
            node.setdefault('arguments', {})['comment'] = trailing[-1][1]
    return node


def _char_offset(source, lineno, col_bytes):
    lines = source.splitlines(keepends=True)
    if lineno < 1 or lineno > len(lines):
        return len(source)
    start = sum(len(line) for line in lines[:lineno - 1])
    line = lines[lineno - 1]
    prefix = line.encode('utf-8')[:max(0, col_bytes)]
    return start + len(prefix.decode('utf-8', errors='ignore'))


def _utf16_len(text):
    return len(text.encode('utf-16-le')) // 2
