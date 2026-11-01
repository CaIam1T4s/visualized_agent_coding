import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'src' / 'VisualizedAgentCoding.Python.Runtime'))
from vac_runtime import data


class ProjectDataTests(unittest.TestCase):
    def setUp(self):
        data.initialize({
            'variables': [
                {'name': 'score', 'type': 'number', 'value': '0'},
                {'name': 'name', 'type': 'text', 'value': 'player'},
                {'name': 'ready', 'type': 'boolean', 'value': 'true'},
            ],
            'lists': [{'name': 'items', 'values': ['coin', 'gem']}],
        })

    def test_variables_are_shared_and_typed(self):
        self.assertEqual(0.0, data.get_variable('score'))
        data.set_variable('score', 5)
        data.add_to_variable('score', 2.5)
        self.assertEqual(7.5, data.get_variable('score'))
        data.change_variable('score', 0.5)  # deprecated alias
        self.assertEqual(8.0, data.get_variable('score'))
        self.assertEqual('player', data.get_variable('name'))
        self.assertTrue(data.get_variable('ready'))
        with self.assertRaises(TypeError):
            data.set_variable('name', 1)
        with self.assertRaises(TypeError):
            data.add_to_variable('name', 1)
        with self.assertRaises(ValueError):
            data.get_variable('missing')

    def test_lists_support_basic_operations(self):
        self.assertEqual(2, data.list_length('items'))
        self.assertEqual(['coin', 'gem'], data.list_values('items'))
        snapshot = data.list_values('items')
        snapshot.append('not stored')
        self.assertEqual(2, data.list_length('items'))
        data.list_append('items', 'key')
        self.assertEqual('key', data.list_get('items', 2))
        data.list_set('items', 0, 'start')
        self.assertEqual('start', data.list_get('items', 0))
        self.assertEqual('start', data.list_remove('items', 0))
        data.list_clear('items')
        self.assertEqual(0, data.list_length('items'))
        with self.assertRaises(IndexError):
            data.list_get('items', 0)

    def test_invalid_initial_data_is_rejected(self):
        with self.assertRaises(ValueError):
            data.initialize({'variables': [{'name': 'x', 'type': 'number', 'value': 'nope'}]})
        with self.assertRaises(ValueError):
            data.initialize({'lists': [{'name': 'x', 'values': [1]}]})


if __name__ == '__main__':
    unittest.main()
