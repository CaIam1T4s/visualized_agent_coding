"""Read-only template region inspection. Markers must be actual standalone comments."""
import ast
import io
import keyword
import re
import tokenize

from protocol import ERR_INVALID_PARAMS, ProtocolError

BEGIN = '# vac:user-code-begin'
END = '# vac:user-code-end'
VERSION = re.compile(r'# vac:template-version=([0-9]+)\Z')
BASES = {'object': ('runtime.element_base', 'ElementBase'),
         'backdrop': ('runtime.backdrop_base', 'BackdropBase')}


def _directive_tokens(source):
    directives = []

    def scan(text, indents=None):
        try:
            for token in tokenize.generate_tokens(io.StringIO(text).readline):
                if token.type != tokenize.COMMENT or not token.string.startswith('# vac:'):
                    continue
                if indents is not None:
                    row, col = token.start
                    end_row, end_col = token.end
                    token = token._replace(start=(row, col + indents[row - 1]),
                                           end=(end_row, end_col + indents[end_row - 1]))
                directives.append(token)
        except IndentationError:
            return False
        except (tokenize.TokenError, SyntaxError):
            pass
        return True

    if not scan(source):
        # Indentation errors in user code must not hide later comment tokens.
        # Retry a lexical-only view without leading indentation. Quote state is
        # still tracked by tokenize, so strings never become marker comments.
        # All structural checks and returned offsets use the untouched source.
        lines = source.split('\n')
        lexical_lines = [line.lstrip(' \t\f') for line in lines]
        indents = [len(line) - len(lexical) for line, lexical in zip(lines, lexical_lines)]
        directives.clear()
        scan('\n'.join(lexical_lines), indents)
    return directives


def inspect_object_script(params):
    uri, version, source, name, kind = (params.get(key) for key in
                                      ('uri', 'version', 'source', 'expectedClassName', 'kind'))
    if (not isinstance(uri, str) or type(version) is not int or version < 0 or not isinstance(source, str)
            or not isinstance(name, str) or not name.isidentifier() or keyword.iskeyword(name)
            or not isinstance(kind, str) or kind not in BASES):
        raise ProtocolError(ERR_INVALID_PARAMS, 'expected uri, version, source, expectedClassName and object/backdrop kind')

    def result(status, template=None, region=None, problem=None):
        return dict(uri=uri, version=version, status=status, templateVersion=template,
                    userRegion=region, problems=[problem] if problem else [])

    # Preserve exact source characters and CRLF. Python token columns count Unicode
    # characters; convert to UTF-16 only after finding absolute character offsets.
    lines = source.split('\n')
    starts, offset = [], 0
    for line in lines:
        starts.append(offset)
        offset += len(line) + 1
    directives = _directive_tokens(source)
    if not directives:
        return result('legacyUnmarked', problem='未发现真实的区域标记；保留原文件，不自动迁移。')

    versions = [token for token in directives if VERSION.fullmatch(token.string)]
    begins = [token for token in directives if token.string == BEGIN]
    ends = [token for token in directives if token.string == END]
    if len(versions) != 1:
        return result('invalidStructure', problem='版本及开始/结束标记必须各出现一次，不能重复、缺失或嵌套。')
    version_text = VERSION.fullmatch(versions[0].string).group(1)
    if len(version_text) > 9:
        return result('unsupportedVersion', problem='模板版本超出支持范围。')
    template = int(version_text)
    if template in (2, 3):
        from object_script_v2 import inspect_v2
        module, base = BASES[kind]
        return inspect_v2(uri, version, source, name, module, base, directives, lines, starts, template)
    if template != 1:
        return result('unsupportedVersion', template, problem='不支持此对象脚本模板版本。')
    if len(begins) != 1 or len(ends) != 1 or len(directives) != 3:
        return result('invalidStructure', template, problem='区域标记必须成对且不能重复或嵌套。')
    begin, end = begins[0], ends[0]
    if versions[0].start != (1, 0) or begin.start >= end.start:
        return result('invalidStructure', template, problem='模板版本位置或区域标记顺序错误。')
    for token, indent in [(versions[0], ''), (begin, '    '), (end, '    ')]:
        line = lines[token.start[0] - 1].removesuffix('\r')
        if line != indent + token.string or token.start[1] != len(indent):
            return result('invalidStructure', template, problem='区域标记必须独占一行并使用模板规定的缩进。')
    # The editable slice starts after the begin marker's newline and stops before
    # all indentation of the end marker. Both marker lines remain system-owned.
    user_start = starts[begin.start[0]]
    user_end = starts[end.start[0] - 1]
    module, base = BASES[kind]
    newline = '\r\n' if lines[begin.start[0] - 1].endswith('\r') else '\n'
    skeleton = source[:user_start] + '    pass' + newline + source[user_end:]
    expected = f'from {module} import {base}\nclass {name}({base}):\n    pass\n    pass\n'
    try:
        tree = ast.parse(skeleton)
        if ast.dump(tree, include_attributes=False) != ast.dump(ast.parse(expected), include_attributes=False):
            return result('invalidStructure', template, problem='系统导入、类名、基类或占位结构与模板不符。')
    except (SyntaxError, ValueError):
        return result('invalidStructure', template, problem='系统包装无法解析，不能确认用户区域。')
    # User contents do not determine ownership of the editable slice. In
    # particular, a dedented statement must remain editable after reopening.
    # The exact wrapper is validated above with the entire user slice removed.
    # A tokenizer error is tolerable only when real paired markers survived and
    # removing their contents produced the exact valid wrapper. Never use a raw
    # text search to recover a closing marker swallowed by an unterminated string.
    utf16_start = len(source[:user_start].encode('utf-16-le')) // 2
    utf16_length = len(source[user_start:user_end].encode('utf-16-le')) // 2
    return result('recognized', template, dict(offset=utf16_start, length=utf16_length))
