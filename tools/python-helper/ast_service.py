import ast
import io
from pathlib import Path
import sys
import tokenize

# Bundled, pinned pure-Python dependency; no pip or network access at runtime.
from dependencies import configure_dependencies
configure_dependencies()
import parso

_GRAMMAR = parso.load_grammar()



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


def _native_valid_blocks(source):
    # Suppress recovery-parser complaints about newer syntax CPython accepts,
    # such as match/case, when an unrelated statement in the file is broken.
    # This only suppresses false positives; it never manufactures diagnostics.
    lines = source.splitlines(keepends=True)
    starts = {1}
    ignored = {tokenize.COMMENT, tokenize.NL, tokenize.NEWLINE,
               tokenize.INDENT, tokenize.DEDENT, tokenize.ENDMARKER, tokenize.ENCODING}
    try:
        for token in tokenize.generate_tokens(io.StringIO(source).readline):
            if token.start[1] == 0 and token.type not in ignored:
                starts.add(token.start[0])
    except (tokenize.TokenError, IndentationError, SyntaxError):
        pass
    starts = sorted(starts | {len(lines) + 1})
    valid = set()
    for start, end in zip(starts, starts[1:]):
        try:
            ast.parse(''.join(lines[start - 1:end - 1]))
        except (SyntaxError, ValueError):
            continue
        valid.update(range(start, end))
    return valid


def _recover_diagnostics(source, lines, first):
    diagnostics = [first]
    valid_lines = _native_valid_blocks(source)
    module = _GRAMMAR.parse(source, error_recovery=True, cache=False, diff_cache=False)
    seen = {(first['line'], first['column'])}
    for issue in _GRAMMAR.iter_errors(module):
        line, char_column = issue.start_pos
        if line == first['line'] or line in valid_lines:
            continue
        text = _line_text(lines, line)
        column = len(text[:char_column].encode('utf-16-le')) // 2 + 1
        if (line, column) in seen:
            continue
        seen.add((line, column))
        message = issue.message
        for prefix in ('SyntaxError: ', 'IndentationError: '):
            if message.startswith(prefix):
                message = message[len(prefix):]
                break
        # An unmatched opener makes recovery continue into trailing comments/EOF,
        # including hidden region markers. That generic tail error is a cascade,
        # not an independent error in the generated system wrapper. Keep recovery
        # diagnostics on actual code lines so independent mistakes remain visible.
        if ('was never closed' in first['message'] and message == 'invalid syntax'
                and (not text.strip() or text.lstrip().startswith('#'))):
            continue
        diagnostic = {'severity': 'error', 'message': message,
                      'line': line, 'column': column, 'parser': 'recovery'}
        if message == 'expected an indented block':
            # The issue can point to EOF or the next generated handler. Retain
            # the actual suite header so clients need not guess from text lines.
            leaf = module.get_leaf_for_position(issue.start_pos, include_prefixes=True)
            if leaf is not None and leaf.start_pos >= issue.start_pos:
                leaf = leaf.get_previous_leaf()
            while leaf is not None and leaf.type in ('newline', 'endmarker'):
                leaf = leaf.get_previous_leaf()
            if leaf is not None and leaf.value == ':':
                diagnostic['relatedLine'] = leaf.start_pos[0]
        diagnostics.append(diagnostic)
    return sorted(diagnostics, key=lambda diagnostic: (diagnostic['line'], diagnostic['column']))


def parse(source):
    lines = source.splitlines()
    try:
        tree = ast.parse(source)
    except SyntaxError as err:
        lineno = err.lineno or 1
        text = _line_text(lines, lineno)
        chars = max((err.offset or 1) - 1, 0)
        column = len(text[:chars].encode('utf-16-le')) // 2 + 1
        first = {'severity': 'error', 'message': err.msg, 'line': lineno, 'column': column, 'parser': 'native'}
        try:
            diagnostics = _recover_diagnostics(source, lines, first)
        except (SyntaxError, ValueError, NotImplementedError, RecursionError):
            # Keep the native diagnostic even when recovery cannot handle a construct.
            diagnostics = [first]
        return {'diagnostics': diagnostics, 'symbols': None}

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
