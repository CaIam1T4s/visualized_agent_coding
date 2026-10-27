import ast


def utf16_column(line, byte_offset):
    prefix = line.encode('utf-8')[:byte_offset].decode('utf-8', errors='ignore')
    return len(prefix.encode('utf-16-le')) // 2


def _line_text(lines, lineno):
    if 1 <= lineno <= len(lines):
        return lines[lineno - 1]
    return ''


def _span(node, lines):
    start_text = _line_text(lines, node.lineno)
    end_text = _line_text(lines, node.end_lineno)
    return {
        'line': node.lineno,
        'column': utf16_column(start_text, node.col_offset) + 1,
        'endLine': node.end_lineno,
        'endColumn': utf16_column(end_text, node.end_col_offset) + 1,
    }


def parse(source):
    lines = source.splitlines()
    try:
        tree = ast.parse(source)
    except SyntaxError as err:
        lineno = err.lineno or 1
        text = _line_text(lines, lineno)
        chars = max((err.offset or 1) - 1, 0)
        column = len(text[:chars].encode('utf-16-le')) // 2 + 1
        return {
            'diagnostics': [{'severity': 'error', 'message': err.msg, 'line': lineno, 'column': column}],
            'symbols': None,
        }

    symbols = {'classes': [], 'functions': [], 'imports': []}
    for node in tree.body:
        if isinstance(node, ast.ClassDef):
            symbols['classes'].append({'name': node.name, 'range': _span(node, lines)})
        elif isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
            symbols['functions'].append({'name': node.name, 'range': _span(node, lines)})
        elif isinstance(node, (ast.Import, ast.ImportFrom)):
            module = getattr(node, 'module', None) or ''
            names = [alias.name for alias in node.names]
            symbols['imports'].append({'module': module, 'names': names, 'range': _span(node, lines)})
    return {'diagnostics': [], 'symbols': symbols}
