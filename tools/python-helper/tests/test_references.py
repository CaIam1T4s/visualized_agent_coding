import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from references_service import find_references
from protocol import ProtocolError


class ReferenceTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.root = Path(self.directory.name)

    def tearDown(self):
        self.directory.cleanup()

    def write(self, path, source):
        target = self.root / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(source, encoding='utf-8')
        return target

    def query(self, path, line, column):
        return find_references(dict(projectRoot=str(self.root), uri=(self.root / path).as_uri(),
                                    version=7, line=line, column=column))

    def test_cross_file_import_alias_and_original_symbol(self):
        self.write('hero.py', 'class Hero: pass\n')
        self.write('use.py', 'from hero import Hero as H\nvalue = H()\n')
        result = self.query('hero.py', 1, 8)
        self.assertEqual(len(result['references']), 4)
        self.assertTrue(any(item['name'] == 'H' and item['range']['line'] == 2 for item in result['references']))
        reverse = self.query('use.py', 2, 9)
        self.assertEqual(result['references'], reverse['references'])

    def test_shadowed_names_comments_and_strings_are_not_references(self):
        self.write('hero.py', 'class Hero: pass\n# Hero\ntext = "Hero"\ndef f(Hero):\n    return Hero\nx = Hero()\n')
        result = self.query('hero.py', 1, 8)
        self.assertEqual([r['range']['line'] for r in result['references']], [1, 6])
        local = self.query('hero.py', 4, 9)
        self.assertEqual([r['range']['line'] for r in local['references']], [4, 5])

    def test_same_named_symbol_in_other_module_is_not_included(self):
        self.write('a.py', 'class Hero: pass\nx = Hero()\n')
        self.write('b.py', 'class Hero: pass\nx = Hero()\n')
        result = self.query('a.py', 1, 8)
        self.assertTrue(all(r['uri'].endswith('/a.py') for r in result['references']))

    def test_syntax_errors_are_reported_as_incomplete(self):
        self.write('hero.py', 'class Hero: pass\nx = Hero()\n')
        self.write('broken.py', 'def invalid(:\n')
        result = self.query('hero.py', 1, 8)
        self.assertTrue(result['isIncomplete'])
        self.assertTrue(any(issue['uri'].endswith('/broken.py') for issue in result['issues']))

    def test_utf16_columns_and_percent_encoded_file_names(self):
        self.write('module%20name.py', 'class Hero: pass\n"\U0001f600"; x = Hero()\n')
        result = self.query('module%20name.py', 2, 12)
        use = next(r for r in result['references'] if r['range']['line'] == 2)
        self.assertEqual(use['range']['column'], 11)
        self.assertIn('%2520', use['uri'])

    def test_user_code_is_never_executed(self):
        marker = self.root / 'executed'
        self.write('hero.py', f'open({str(marker)!r}, "w").write("bad")\nclass Hero: pass\nx = Hero()\n')
        self.query('hero.py', 2, 8)
        self.assertFalse(marker.exists())

    def test_invalid_positions_and_outside_uri_are_rejected(self):
        self.write('hero.py', 'x = 1\n')
        for line, column in [(0, 1), (1, 0), (9, 1), (1, 99)]:
            with self.subTest(line=line, column=column), self.assertRaises(ProtocolError):
                self.query('hero.py', line, column)
        with self.assertRaises(ProtocolError):
            find_references(dict(projectRoot=str(self.root), uri=self.root.parent.joinpath('outside.py').as_uri(),
                                 version=1, line=1, column=1))

    def test_jsonrpc_method_requires_hello_and_echoes_document_version(self):
        path = self.write('hero.py', 'class Hero: pass\nx = Hero()\n')
        params = dict(projectRoot=str(self.root), uri=path.as_uri(), version=17, line=1, column=8)
        messages = [dict(jsonrpc='2.0', id='1', method='python.findReferences', params=params),
                    dict(jsonrpc='2.0', id='2', method='helper.hello', params=dict(protocolVersions=[1])),
                    dict(jsonrpc='2.0', id='3', method='python.findReferences', params=params)]
        process = subprocess.run([sys.executable, '-B', str(Path(__file__).resolve().parents[1] / 'main.py')],
                                 input=''.join(json.dumps(m) + '\n' for m in messages).encode(),
                                 capture_output=True, timeout=30)
        results = [json.loads(line) for line in process.stdout.splitlines()]
        self.assertEqual(results[0]['error']['code'], -32005)
        self.assertIn('findReferences', results[1]['result']['capabilities'])
        self.assertEqual(results[2]['result']['version'], 17)


if __name__ == '__main__':
    unittest.main()
