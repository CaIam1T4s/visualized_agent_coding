"""Read-only block projection for a user region.

The source is already dedented and belongs to one region. Unsupported statements
stay as raw blocks; this module never rewrites source.
"""
import ast


def build_blocks(params):
    source = params.get('source')
    if not isinstance(source, str):
        raise ValueError('source must be a string')
    try:
        tree = ast.parse(source)
    except (SyntaxError, ValueError) as error:
        return dict(error=_error_text(error), nodes=[])
    lines = source.splitlines(keepends=True)
    nodes = _body_nodes(tree.body, source, lines, 1)
    return dict(error=None, nodes=nodes)


def _error_text(error):
    if isinstance(error, SyntaxError):
        return f'第 {error.lineno or "?"} 行：{error.msg}'
    return str(error)


def _body_nodes(body, source, lines, start=None):
    nodes = []
    cursor = start if start is not None else (body[0].lineno if body else None)
    for statement in body:
        if cursor is not None:
            nodes.extend(_comments(lines, cursor, statement.lineno - 1))
        node = _node(statement, source, lines)
        if node is not None:
            nodes.append(node)
        cursor = (statement.end_lineno or statement.lineno) + 1
    return nodes


def _comments(lines, start, end):
    result = []
    for number in range(max(1, start), min(len(lines), end) + 1):
        text = lines[number - 1].rstrip('\r\n')
        if text.strip().startswith('#'):
            result.append(dict(type='comment', text=text.strip(), arguments={},
                               children=[], elseChildren=[], line=number))
    return result


def _node(statement, source, lines):
    if isinstance(statement, ast.For):
        return _for(statement, source, lines)
    if isinstance(statement, ast.If):
        return dict(type='control.if',
                    arguments=dict(condition=_segment(source, statement.test) or '?'),
                    children=_body_nodes(statement.body, source, lines, statement.lineno + 1),
                    elseChildren=_body_nodes(statement.orelse, source, lines, statement.orelse[0].lineno if statement.orelse else None),
                    text='', line=statement.lineno)
    if isinstance(statement, ast.Expr) and isinstance(statement.value, ast.Call):
        return _call(statement.value, source)
    return dict(type='raw', text=_segment(source, statement) or '',
                arguments={}, children=[], elseChildren=[], line=statement.lineno)


def _for(statement, source, lines):
    if statement.orelse or not isinstance(statement.target, ast.Name):
        return _raw(statement, source)
    target = statement.target.id
    loop = statement.iter
    children = _body_nodes(statement.body, source, lines, statement.lineno + 1)
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
    return ast.get_source_segment(source, node) or ''
