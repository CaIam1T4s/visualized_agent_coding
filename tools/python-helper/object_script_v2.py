"""Version 2 multi-region inspection; source slices and metadata are read-only."""
import ast
import json
import re
import unicodedata

BEGIN = '# vac:region-begin '
END = '# vac:region-end '
ID = re.compile(r'[a-z][a-z0-9_]{0,63}\Z')


def inspect_v2(uri, version, source, name, module, base, directives, lines, starts, template=2):
    import_block = None
    def result(status, regions=None, problem=None):
        return dict(uri=uri, version=version, status=status, templateVersion=template,
                    userRegion=None, regions=regions, problems=[problem] if problem else [], imports=import_block if status == 'recognized' else None)

    def invalid(message):
        return result('invalidStructure', problem=message)

    def unique_object(pairs):
        obj = {}
        for key, value in pairs:
            if key in obj:
                raise ValueError('duplicate metadata key')
            obj[key] = value
        return obj

    if directives[0].string != '# vac:template-version=' + str(template) or directives[0].start != (1, 0):
        return invalid('v2 version marker must be first.')
    if template == 3:
        from imports_service import inspect_block, BEGIN as IMPORT_BEGIN, END as IMPORT_END
        try:
            import_block = inspect_block(source)
        except (ValueError, SyntaxError, TypeError, KeyError):
            return invalid('Managed import block is malformed.')
        if import_block is None:
            return invalid('v3 requires a module-level managed import block.')
        directives = [t for t in directives if t.string not in (IMPORT_BEGIN, IMPORT_END) and not t.string.startswith('# vac:import ')]
    tokens = directives[1:]
    if len(tokens) < 4 or len(tokens) % 2:
        return invalid('Regions require paired, non-nested begin/end markers.')
    regions, slices, identities, events = [], [], set(), set()
    expected = f'from {module} import {base}\nclass {name}({base}):\n    pass\n'
    for index in range(0, len(tokens), 2):
        begin, end = tokens[index:index + 2]
        if not begin.string.startswith(BEGIN) or not end.string.startswith(END):
            return invalid('Unknown, missing, reversed or nested region marker.')
        try:
            metadata = json.loads(begin.string[len(BEGIN):], object_pairs_hook=unique_object)
        except (ValueError, TypeError):
            return invalid('Invalid region metadata JSON.')
        if not isinstance(metadata, dict) or set(metadata) - {'id', 'kind', 'eventName'}:
            return invalid('Invalid region metadata fields.')
        identity, kind, event = (metadata.get(k) for k in ('id', 'kind', 'eventName'))
        if (not isinstance(identity, str) or not ID.fullmatch(identity) or identity in identities
                or kind not in ('members', 'initialize', 'start', 'event')
                or end.string != END + identity):
            return invalid('Invalid or duplicate region identity/type, or mismatched end marker.')
        if kind == 'event':
            if not isinstance(event, str) or not event.strip() or len(event) > 256 or event in events:
                return invalid('Event identity must be non-empty and unique.')
            if event.startswith('message:'):
                message = event[len('message:'):]
                if (not message or message != message.strip() or len(message.encode('utf-16-le')) // 2 > 248
                        or any(unicodedata.category(char) == 'Cc' for char in message)):
                    return invalid('Invalid message event name.')
            events.add(event)
        elif 'eventName' in metadata:
            return invalid('Only event regions may specify eventName.')
        identities.add(identity)
        indentation = 4 if kind == 'members' else 8
        indent = ' ' * indentation
        for token in (begin, end):
            if (token.start[1] != indentation
                    or lines[token.start[0] - 1].removesuffix('\r') != indent + token.string):
                return invalid('Region markers must be standalone with the prescribed indentation.')
        first, last = starts[begin.start[0]], starts[end.start[0] - 1]
        newline = '\r\n' if lines[begin.start[0] - 1].endswith('\r') else '\n'
        span = dict(offset=len(source[:first].encode('utf-16-le')) // 2,
                    length=len(source[first:last].encode('utf-16-le')) // 2)
        regions.append(dict(id=identity, kind=kind, span=span, indentation=indentation, eventName=event))
        slices.append((first, last, indent + 'pass' + newline))
        if kind == 'members':
            expected += '    pass\n'
        else:
            handler = {'initialize': 'initialize', 'start': '_vac_on_start'}.get(kind, '_vac_event_' + identity)
            expected += f'    def {handler}(self):\n        pass\n'
    kinds = [r['kind'] for r in regions]
    if kinds[0] != 'members' or kinds.count('members') != 1 or kinds.count('start') != 1 or kinds.count('initialize') > 1:
        return invalid('Require members first, exactly one start and at most one initialize.')
    skeleton = source
    for first, last, replacement in reversed(slices):
        skeleton = skeleton[:first] + replacement + skeleton[last:]
    if import_block is not None:
        first = len(source.encode('utf-16-le')[:import_block['span']['offset'] * 2].decode('utf-16-le'))
        last = len(source.encode('utf-16-le')[:(import_block['span']['offset'] + import_block['span']['length']) * 2].decode('utf-16-le'))
        class_row = next((i for i, text in enumerate(lines) if re.match(r'^class\s+' + re.escape(name) + r'\b', text)), None)
        if class_row is None or last > starts[class_row]:
            return invalid('Managed imports must precede the class definition.')
        skeleton = skeleton[:first] + skeleton[last:]
    try:
        if ast.dump(ast.parse(skeleton), include_attributes=False) != ast.dump(ast.parse(expected), include_attributes=False):
            return invalid('System imports, class, handlers or placeholders differ from the template.')
    except (SyntaxError, ValueError):
        return invalid('System wrapper cannot be parsed.')
    return result('recognized', regions)
