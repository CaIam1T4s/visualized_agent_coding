import unittest
import test_references
from module_rename import plan_module_rename
from main import dispatch
from protocol import ProtocolError


class ModuleRenameTests(unittest.TestCase):
    setUp = test_references.ReferenceTests.setUp
    tearDown = test_references.ReferenceTests.tearDown
    def write(self, path, source):
        target = self.root / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(source.encode('utf-8'))
        return target

    def plan(self):
        return plan_module_rename(dict(projectRoot=str(self.root), uri=(self.root / 'hello.py').as_uri(), version=1, newName='greetings'))

    def test_from_import_preserves_symbol_alias_calls_and_comments(self):
        self.write('hello.py', 'def greet(): pass\n')
        self.write('use.py', 'from hello import greet as welcome\nwelcome()\n# hello\ntext = "hello"\n')
        result = self.plan()
        self.assertFalse(result['isIncomplete'])
        self.assertEqual(result['changes'][0]['source'], 'from greetings import greet as welcome\nwelcome()\n# hello\ntext = "hello"\n')

    def test_plain_module_import_updates_uses_but_not_local_shadow(self):
        self.write('hello.py', 'def greet(): pass\n')
        self.write('use.py', 'import hello\nhello.greet()\ndef f(hello):\n    return hello\n')
        result = self.plan()
        self.assertEqual(result['changes'][0]['source'], 'import greetings\ngreetings.greet()\ndef f(hello):\n    return hello\n')

    def test_explicit_alias_is_preserved_even_when_named_like_module(self):
        self.write('hello.py', 'def greet(): pass\n')
        self.write('use.py', 'import hello as hello\nhello.greet()\n')
        result = self.plan()
        self.assertEqual(result['changes'][0]['source'], 'import greetings as hello\nhello.greet()\n')

    def test_dynamic_and_syntax_errors_block_plan(self):
        self.write('hello.py', '')
        self.write('use.py', '__import__("hello")\n')
        self.assertTrue(self.plan()['isIncomplete'])
        self.write('use.py', 'def bad(:\n')
        self.assertTrue(self.plan()['isIncomplete'])

    def test_existing_local_binding_blocks_implicit_module_name_change(self):
        self.write('hello.py', '')
        self.write('use.py', 'greetings = 1\nimport hello\nhello.x\n')
        self.assertTrue(self.plan()['isIncomplete'])

    def test_explicit_module_alias_uses_remain_unchanged(self):
        self.write('hello.py', 'def greet(): pass\n')
        self.write('use.py', 'import hello as h\nh.greet()\n')
        self.assertEqual(self.plan()['changes'][0]['source'], 'import greetings as h\nh.greet()\n')

    def test_preserves_crlf_and_imported_symbol_named_like_module(self):
        self.write('hello.py', 'def hello(): pass\n')
        path = self.root / 'use.py'
        path.write_bytes(b'from hello import hello\r\nhello()\r\n')
        self.assertEqual(self.plan()['changes'][0]['source'], 'from greetings import hello\r\nhello()\r\n')

    def test_aliased_dynamic_loader_blocks_plan(self):
        self.write('hello.py', '')
        self.write('use.py', 'from importlib import import_module as load\nload("hello")\n')
        self.assertTrue(self.plan()['isIncomplete'])

    def test_rename_protocol_requires_hello_and_returns_unsaved_plan(self):
        path = self.write('hello.py', 'def greet(): pass\n')
        self.write('use.py', 'import hello\nhello.greet()\n')
        request = dict(jsonrpc='2.0', id='1', method='python.planModuleRename',
                       params=dict(projectRoot=str(self.root), uri=path.as_uri(), version=3, newName='greetings'))
        state = {'negotiated': None}
        response, _ = dispatch(request, state)
        self.assertEqual(response['error']['code'], -32005)
        hello, _ = dispatch(dict(jsonrpc='2.0', id='2', method='helper.hello', params={'protocolVersions': [1]}), state)
        self.assertIn('planModuleRename', hello['result']['capabilities'])
        response, _ = dispatch(request, state)
        self.assertEqual(response['result']['version'], 3)
        self.assertEqual(len(response['result']['changes']), 1)
        self.assertFalse((self.root / 'greetings.py').exists())
        self.assertEqual((self.root / 'use.py').read_text(), 'import hello\nhello.greet()\n')

    def plan_class(self, source, old='Hero', new='FireHero'):
        self.write('hero.py', source)
        return plan_module_rename(dict(projectRoot=str(self.root),
            uri=(self.root / 'hero.py').as_uri(), version=1, newName='fire_hero',
            oldClassName=old, newClassName=new))

    def test_class_declaration_is_renamed_with_module(self):
        result = self.plan_class('class Hero(ElementBase):\n    pass\n')
        self.assertFalse(result['isIncomplete'])
        self.assertEqual(result['changes'][0]['source'], 'class FireHero(ElementBase):\n    pass\n')

    def test_missing_or_duplicate_class_blocks_plan(self):
        self.assertTrue(self.plan_class('class Other(ElementBase):\n    pass\n')['isIncomplete'])
        self.assertTrue(self.plan_class('class Hero: pass\nclass Hero: pass\n')['isIncomplete'])

    def test_new_class_name_conflict_blocks_plan(self):
        result = self.plan_class('class Hero: pass\nFireHero = 1\n')
        self.assertTrue(result['isIncomplete'])

    def test_keyword_new_name_is_rejected(self):
        path = self.write('hello.py', '')
        with self.assertRaises(ProtocolError):
            plan_module_rename(dict(projectRoot=str(self.root), uri=path.as_uri(), version=1, newName='class'))
