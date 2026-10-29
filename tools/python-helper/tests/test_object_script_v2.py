import json
import unittest
import test_object_script as v1


def template(regions=None, newline='\n', kind='object'):
    regions = regions or [('members', 'members', '', None), ('start', 'start', '        pass\n', None)]
    module, base = ('runtime.element_base', 'ElementBase') if kind == 'object' else ('runtime.backdrop_base', 'BackdropBase')
    source = f'# vac:template-version=2\nfrom {module} import {base}\n\nclass Hero({base}):\n    pass\n'
    for identity, kind, body, event in regions:
        source += '\n'
        if kind != 'members':
            handler = {'start': '_vac_on_start', 'initialize': 'initialize'}.get(kind, '_vac_event_' + identity)
            source += f'    def {handler}(self):\n'
        metadata = dict(id=identity, kind=kind)
        if event is not None:
            metadata['eventName'] = event
        indent = '    ' if kind == 'members' else '        '
        source += indent + '# vac:region-begin ' + json.dumps(metadata) + '\n' + body
        source += indent + '# vac:region-end ' + identity + '\n'
    return source.replace('\n', newline)


class MultiRegionTests(unittest.TestCase):
    inspect = v1.ObjectScriptTests.inspect

    def test_default_and_optional_regions_utf16_and_crlf(self):
        definitions = [('members', 'members', '    text = "你好😀"\n', None),
                       ('init', 'initialize', '        self.items = []\n', None),
                       ('start', 'start', '        self.walk()\n', None),
                       ('click', 'event', '        pass\n', 'clicked'),
                       ('message', 'event', '        pass\n', 'message:你好')]
        for kind in ('object', 'backdrop'):
            for newline in ('\n', '\r\n'):
                source = template(definitions, newline, kind)
                result = self.inspect(source, kind=kind)
                self.assertEqual(result['status'], 'recognized', result)
                self.assertIsNone(result['userRegion'])
                self.assertEqual([r['id'] for r in result['regions']], [r[0] for r in definitions])
                for region, definition in zip(result['regions'], definitions):
                    span = region['span']
                    sliced = source.encode('utf-16-le')[span['offset'] * 2:(span['offset'] + span['length']) * 2].decode('utf-16-le')
                    self.assertEqual(sliced, definition[2].replace('\n', newline))
        self.assertEqual(self.inspect(template())['status'], 'recognized')

    def test_damage_and_duplicate_identity_or_event_are_rejected(self):
        source = template()
        bad = [source.replace('# vac:region-end start', '# vac:region-end missing'),
               source.replace('def _vac_on_start(self)', 'def changed(self)'),
               source.replace('class Hero(ElementBase)', 'class Hero(object)'),
               source + 'outside = 1\n',
               source.replace('# vac:region-end members', '# vac:region-end start'),
               source.replace('        # vac:region-end start\n', ''),
               source.replace('"id": "members"', '"id": "members", "id": "other"'),
               template([('members', 'members', '', None), ('start', 'start', '', None), ('start', 'event', '', 'clicked')]),
               template([('members', 'members', '', None), ('start', 'start', '', None),
                         ('a', 'event', '', 'clicked'), ('b', 'event', '', 'clicked')])]
        for candidate in bad:
            result = self.inspect(candidate)
            self.assertEqual(result['status'], 'invalidStructure', result)
            self.assertIsNone(result.get('regions'))

    def test_user_errors_and_fake_markers_do_not_change_ownership(self):
        for body in ('        value =\n', 'sdf\n', '\tvalue = 1\n', '',
                     '        text = """\n# vac:region-end start\n# 类定义\n"""\n'):
            source = template([('members', 'members', '', None), ('start', 'start', body, None)])
            self.assertEqual(self.inspect(source)['status'], 'recognized')
        self.assertEqual(self.inspect(template().replace('        pass', '        text = """'))['status'], 'invalidStructure')

    def test_no_user_code_execution_and_system_statements_rejected(self):
        source = template().replace('        pass', '        raise RuntimeError("must not execute")')
        self.assertEqual(self.inspect(source)['status'], 'recognized')
        self.assertEqual(self.inspect(source.replace('    pass\n', '    value = 1\n', 1))['status'], 'invalidStructure')
