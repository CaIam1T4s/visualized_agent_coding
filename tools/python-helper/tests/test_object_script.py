import unittest
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from object_script_service import inspect_object_script
from main import dispatch
from protocol import ProtocolError


def template(user='', name='Hero', kind='object', newline='\n'):
    module, base = ('runtime.element_base', 'ElementBase') if kind == 'object' else ('runtime.backdrop_base', 'BackdropBase')
    source = (f'# vac:template-version=1\nfrom {module} import {base}\n\n\nclass {name}({base}):\n'
              '    pass\n\n    # vac:user-code-begin\n' + user + '    # vac:user-code-end\n')
    return source.replace('\n', newline)


class ObjectScriptTests(unittest.TestCase):
    def inspect(self, content, **kwargs):
        params = dict(uri='file:///hero.py', version=4, source=content, expectedClassName='Hero', kind='object')
        params.update(kwargs)
        return inspect_object_script(params)

    def user_source(self, source, result):
        region = result['userRegion']
        encoded = source.encode('utf-16-le')
        return encoded[region['offset'] * 2:(region['offset'] + region['length']) * 2].decode('utf-16-le')

    def test_empty_region_and_both_script_kinds(self):
        for kind in ('object', 'backdrop'):
            source = template(kind=kind)
            result = self.inspect(source, kind=kind)
            self.assertEqual(result['status'], 'recognized')
            self.assertEqual(result['templateVersion'], 1)
            self.assertEqual(self.user_source(source, result), '')
            self.assertEqual(result['problems'], [])

    def test_unicode_crlf_and_exact_region_boundaries(self):
        user = '    title = "你好😀"\n    def act(self):\n        return self.title\n'
        for newline in ('\n', '\r\n'):
            source = template(user, newline=newline).replace('    pass', '    pass  # 系统😀', 1)
            result = self.inspect(source)
            self.assertEqual(result['status'], 'recognized')
            expected = user.replace('\n', newline)
            self.assertEqual(self.user_source(source, result), expected)
            region = result['userRegion']
            prefix = source.encode('utf-16-le')[:region['offset'] * 2].decode('utf-16-le')
            suffix = source.encode('utf-16-le')[(region['offset'] + region['length']) * 2:].decode('utf-16-le')
            self.assertTrue(prefix.endswith('# vac:user-code-begin' + newline))
            self.assertTrue(suffix.startswith('    # vac:user-code-end'))
            self.assertEqual(prefix + expected + suffix, source)

    def test_marker_text_in_strings_is_not_a_comment(self):
        user = '    text = """\n# vac:user-code-begin\n# vac:user-code-end\n"""\n'
        source = template(user)
        result = self.inspect(source)
        self.assertEqual(result['status'], 'recognized')
        self.assertEqual(self.user_source(source, result), user)
        fake = 'text = """\n# vac:template-version=1\n# vac:user-code-begin\n# vac:user-code-end\n"""\n'
        self.assertEqual(self.inspect(fake)['status'], 'legacyUnmarked')

    def test_broken_missing_duplicate_reversed_and_inline_markers(self):
        source = template()
        broken = [source.replace('    # vac:user-code-end\n', ''),
                  source.replace('    # vac:user-code-begin', '    # vac:user-code-begin\n    # vac:user-code-begin'),
                  source.replace('user-code-begin', 'TEMP').replace('user-code-end', 'user-code-begin').replace('TEMP', 'user-code-end'),
                  source.replace('    # vac:user-code-begin', '    x = 1  # vac:user-code-begin'),
                  source.replace('    # vac:user-code-end', '\t# vac:user-code-end')]
        for text in broken:
            with self.subTest(source=text):
                result = self.inspect(text)
                self.assertEqual(result['status'], 'invalidStructure')
                self.assertIsNone(result['userRegion'])

    def test_old_script_and_unsupported_version_are_not_migrated(self):
        self.assertEqual(self.inspect('class Hero: pass\n')['status'], 'legacyUnmarked')
        for version in ('2', '9' * 10000):
            result = self.inspect(template().replace('version=1', 'version=' + version))
            self.assertEqual(result['status'], 'unsupportedVersion')
            self.assertIsNone(result['userRegion'])

    def test_user_syntax_error_is_separate_from_wrapper_damage(self):
        for user in ('    value =\n', '    def bad(:\n', '    print(\n'):
            with self.subTest(user=user):
                self.assertEqual(self.inspect(template(user))['status'], 'recognized')
        for text in (template().replace('class Hero(', 'class Wrong('),
                     template().replace('class Hero(ElementBase)', 'class Hero(object)'),
                     template().replace('from runtime.element_base', 'from unknown'),
                     template() + 'outside = 1\n', template().replace('    pass\n', '    value = 1\n')):
            self.assertEqual(self.inspect(text)['status'], 'invalidStructure')

    def test_mixed_tabs_and_bad_dedent_do_not_hide_real_closing_marker(self):
        for user in ('\tvalue = j\n', '    def act(self):\n        pass\n      value = 1\n'):
            source = template(user)
            result = self.inspect(source)
            self.assertEqual(result['status'], 'recognized')
            self.assertEqual(self.user_source(source, result), user)

    def test_indentation_recovery_still_ignores_markers_inside_strings(self):
        user = '\ttext = """\n    # vac:user-code-end\n"""\n'
        source = template(user)
        result = self.inspect(source)
        self.assertEqual(result['status'], 'recognized')
        self.assertEqual(self.user_source(source, result), user)
        self.assertEqual(self.inspect(template('\ttext = """\n'))['status'], 'invalidStructure')

    def test_unterminated_string_cannot_supply_a_fake_closing_marker(self):
        result = self.inspect(template('    text = """\n'))
        self.assertEqual(result['status'], 'invalidStructure')
        self.assertIsNone(result['userRegion'])

    def test_dedented_user_code_does_not_invalidate_region_ownership(self):
        for user in ('sdf\n', 'outside = 1\n', 'def custom():\n    pass\n', 'class Custom:\n    pass\n'):
            for kind in ('object', 'backdrop'):
                source = template(user, kind=kind)
                result = self.inspect(source, kind=kind)
                self.assertEqual(result['status'], 'recognized')
                self.assertEqual(self.user_source(source, result), user)

    def test_end_marker_without_final_newline(self):
        self.assertEqual(self.inspect(template().rstrip('\n'))['status'], 'recognized')

    def test_protocol_requires_handshake_and_does_not_execute_source(self):
        source = template('    raise RuntimeError("must not run")\n')
        request = dict(jsonrpc='2.0', id='1', method='python.inspectObjectScript',
                       params=dict(uri='file:///hero.py', version=9, source=source, expectedClassName='Hero', kind='object'))
        state = {'negotiated': None}
        response, _ = dispatch(request, state)
        self.assertEqual(response['error']['code'], -32005)
        hello, _ = dispatch(dict(jsonrpc='2.0', id='2', method='helper.hello', params={'protocolVersions': [1]}), state)
        self.assertIn('inspectObjectScript', hello['result']['capabilities'])
        response, _ = dispatch(request, state)
        self.assertEqual(response['result']['status'], 'recognized')
        self.assertEqual(response['result']['version'], 9)

    def test_invalid_parameters_are_protocol_errors(self):
        for values in (dict(version=-1), dict(kind='user'), dict(expectedClassName='class'), dict(source=None)):
            with self.assertRaises(ProtocolError):
                self.inspect(template(), **values)
