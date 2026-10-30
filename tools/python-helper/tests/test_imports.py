import ast
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(__file__)))
from imports_service import inspect_imports, inspect_block, BEGIN, END
from object_script_service import inspect_object_script
from protocol import ProtocolError


class ImportsTests(unittest.TestCase):
    def context(self, kind='user'):
        return dict(scriptId='current', kind=kind, regions=[], scripts=[
            dict(id='current', module='scripts.user.main', kind='user', displayName='main'),
            dict(id='tools', module='scripts.user.tools', kind='user', displayName='tools'),
            dict(id='hero', module='scripts.objects.hero', kind='object', displayName='hero'),
            dict(id='forest', module='scripts.backdrops.forest', kind='backdrop', displayName='forest')])

    def inspect(self, source, **kw):
        return inspect_imports(dict(uri='file:///demo.py', version=1, source=source,
                                   context=kw.pop('context', self.context()), **kw))

    def test_insert_preserves_docstring_future_imports_and_source(self):
        source = '"""用户文档😀"""\r\nfrom __future__ import annotations\r\nvalue = 1\r\n'
        result = self.inspect(source, imports=[dict(module='math', alias='m', source='standard')])
        self.assertTrue(result['syntaxValid'])
        self.assertIn('from __future__ import annotations\r\n' + BEGIN, result['source'])
        self.assertEqual(ast.get_docstring(ast.parse(result['source'])), '用户文档😀')
        block = result['block']
        self.assertEqual(block['entries'][0]['alias'], 'm')
        self.assertEqual(result['source'].encode('utf-16-le')[block['span']['offset'] * 2:
                         (block['span']['offset'] + block['span']['length']) * 2].decode('utf-16-le').splitlines()[0], BEGIN)

    def test_rejects_object_backdrop_self_private_and_third_party(self):
        for module in ['scripts.objects.hero', 'hero', 'forest', 'scripts.user.main', 'runtime.element_base', 'vac_runtime.internal', 'not_installed_xyz']:
            with self.subTest(module=module), self.assertRaises(ProtocolError):
                self.inspect('pass\n', imports=[dict(module=module)])

    def test_public_broadcast_roundtrips_alias_and_keeps_internal_modules_private(self):
        result = self.inspect('pass\n', imports=[dict(module='vac_runtime', name='broadcast', alias='send', source='runtime')])
        self.assertFalse(result['diagnostics'])
        self.assertIn('from vac_runtime import broadcast as send', result['source'])
        self.assertEqual('runtime', self.inspect(result['source'])['block']['entries'][0]['source'])
        self.assertFalse(self.inspect('from vac_runtime import broadcast\nbroadcast("hello")\n')['diagnostics'])
        for entry in [dict(module='vac_runtime', source='runtime'),
                      dict(module='vac_runtime._entry', name='run_project', source='runtime'),
                      dict(module='vac_runtime', name='ElementBase', source='runtime'),
                      dict(module='vac_runtime', name='broadcast', source='standard')]:
            with self.subTest(entry=entry), self.assertRaises(ProtocolError):
                self.inspect('pass\n', imports=[entry])

    def test_project_identity_and_alias_survive_actual_source_roundtrip(self):
        result = self.inspect('value = 1\n', imports=[dict(module='scripts.user.tools', alias='utils', source='project', scriptId='tools')])
        entry = result['block']['entries'][0]
        self.assertEqual(entry['scriptId'], 'tools')
        self.assertEqual(entry['module'], 'scripts.user.tools')
        self.assertIn('import scripts.user.tools as utils', result['source'])
        self.assertEqual(self.inspect(result['source'])['block'], result['block'])

    def test_duplicate_binding_invalid_alias_and_defined_name_conflict_are_rejected(self):
        for entries in [[dict(module='math', alias='x'), dict(module='random', alias='x')],
                        [dict(module='math', alias='class')], [dict(module='math', alias='value')]]:
            with self.subTest(entries=entries), self.assertRaises(ProtocolError):
                self.inspect('value = 1\n', imports=entries)

    def test_marker_text_in_string_does_not_create_import_block(self):
        self.assertIsNone(inspect_block('text = """\n' + BEGIN + '\n' + END + '\n"""\n'))
        result = self.inspect('text = "import hero"\n# import hero\n')
        self.assertEqual(result['diagnostics'], [])

    def test_manual_user_import_is_preserved_and_illegal_target_diagnosed(self):
        source = 'import math\nfrom hero import Hero\n'
        result = self.inspect(source)
        self.assertEqual(result['source'], source)
        self.assertEqual(len(result['diagnostics']), 1)
        self.assertIn('角色', result['diagnostics'][0]['message'])

    def test_object_manual_import_is_a_rule_error_and_can_be_converted(self):
        source = 'class Hero:\n    pass\n    from math import sqrt as root\n'
        context = self.context('object')
        context['regions'] = [dict(offset=source.index('    from'), length=len('    from math import sqrt as root\n'))]
        result = self.inspect(source, context=context)
        self.assertTrue(any(d['category'] == 'import-rule' for d in result['diagnostics']))
        converted = self.inspect(source, context=context, convertLine=3)
        self.assertTrue(converted['syntaxValid'])
        self.assertEqual(converted['block']['entries'][0]['name'], 'sqrt')
        self.assertEqual(converted['block']['entries'][0]['alias'], 'root')

    def test_incomplete_syntax_does_not_add_guessed_manual_import_rules(self):
        self.assertEqual(self.inspect('import hero\nx =\n')['diagnostics'], [])

    def test_conversion_of_inline_suite_is_rejected_without_removing_user_logic(self):
        with self.assertRaises(ProtocolError):
            self.inspect('if True: import math\n', convertLine=1)

    def test_conversion_keeps_comments_and_preserves_an_otherwise_empty_method(self):
        result = self.inspect('def f():\n    import math  # important\n', convertLine=2)
        self.assertTrue(result['syntaxValid'])
        self.assertIn('    # important\n    pass\n', result['source'])

    def test_docstring_without_final_newline_can_accept_imports(self):
        result = self.inspect('"""doc"""', imports=[dict(module='math')])
        self.assertTrue(result['syntaxValid'])
        self.assertIsNotNone(result['block'])
        self.assertEqual(ast.get_docstring(ast.parse(result['source'])), 'doc')

    def test_managed_target_deleted_reports_rule_error_and_can_be_removed(self):
        source = self.inspect('pass\n', imports=[dict(module='scripts.user.tools', source='project', scriptId='tools')])['source']
        context = self.context()
        context['scripts'] = [s for s in context['scripts'] if s['id'] != 'tools']
        result = self.inspect(source, context=context)
        self.assertIn('已不存在', result['diagnostics'][0]['message'])
        self.assertEqual(result['diagnostics'][0]['navigation'], 'imports')
        self.assertEqual(self.inspect(source, context=context, imports=[])['diagnostics'], [])

    def test_standard_library_names_are_checked_without_import_execution(self):
        result = self.inspect('raise RuntimeError("never execute")\n')
        self.assertIn('math', result['standardModules'])
        self.assertNotIn('not_installed_xyz', result['standardModules'])

    def test_standard_library_os_path_alias_is_allowed(self):
        self.assertEqual(self.inspect('import os.path\n')['diagnostics'], [])


if __name__ == '__main__':
    unittest.main()
