import unittest
import test_references
from module_dependencies import find_module_dependencies
from protocol import ProtocolError
from main import dispatch

class ModuleImpactTests(unittest.TestCase):
    setUp = test_references.ReferenceTests.setUp
    tearDown = test_references.ReferenceTests.tearDown
    write = test_references.ReferenceTests.write
    def impact(self):
        return find_module_dependencies(dict(projectRoot=str(self.root), uri=(self.root / 'hero.py').as_uri(), version=1))

    def test_module_import_forms_and_false_matches(self):
        self.write('hero.py', 'class Hero: pass\n')
        self.write('use.py', 'import hero as h\nfrom hero import Hero as H\nfrom hero import *\n')
        self.write('other.py', '# import hero\nx = "hero"\n')
        result = self.impact()
        self.assertFalse(result['isIncomplete'])
        self.assertEqual([r['range']['line'] for r in result['references']], [1, 2, 3])

    def test_relative_import_and_dynamic_import_warning(self):
        self.write('hero.py', '')
        self.write('__init__.py', '')
        self.write('use.py', 'from . import hero\n__import__("hero")\n')
        result = self.impact()
        self.assertTrue(result['isIncomplete'])
        self.assertEqual(len(result['references']), 1)

    def test_broken_file_does_not_claim_complete(self):
        self.write('hero.py', '')
        self.write('broken.py', 'import hero\ndef bad(:\n')
        self.assertTrue(self.impact()['isIncomplete'])

    def test_repeated_alias_calls_include_each_use_but_not_shadowed_names(self):
        self.write('hero.py', 'def greet(): pass\n')
        self.write('use.py', 'from hero import greet as welcome\n' + 'welcome()\n' * 5
                   + 'def local(welcome):\n    welcome()\n# welcome()\ntext = "welcome()"\n')
        result = self.impact()
        self.assertFalse(result['isIncomplete'])
        self.assertEqual([r['range']['line'] for r in result['references']], [1, 2, 3, 4, 5, 6])

    def test_module_alias_member_calls_and_star_import_uses(self):
        self.write('hero.py', 'def greet(): pass\n')
        self.write('use.py', 'import hero as h\nh.greet()\nfrom hero import *\ngreet()\n')
        result = self.impact()
        self.assertEqual([r['range']['line'] for r in result['references']], [1, 2, 2, 3, 4])

    def test_separate_protocol_method_requires_handshake_and_no_caret(self):
        path = self.write('hero.py', '')
        params = dict(projectRoot=str(self.root), uri=path.as_uri(), version=12)
        request = dict(jsonrpc='2.0', id='1', method='python.findModuleDependencies', params=params)
        state = {'negotiated': None}
        response, _ = dispatch(request, state)
        self.assertEqual(response['error']['code'], -32005)
        hello, _ = dispatch(dict(jsonrpc='2.0', id='2', method='helper.hello', params={'protocolVersions': [1]}), state)
        self.assertIn('findModuleDependencies', hello['result']['capabilities'])
        response, _ = dispatch(request, state)
        self.assertEqual(response['result']['version'], 12)

    def test_outside_target_is_rejected(self):
        with self.assertRaises(ProtocolError):
            find_module_dependencies(dict(projectRoot=str(self.root), uri=(self.root.parent / 'outside.py').as_uri(), version=1))
