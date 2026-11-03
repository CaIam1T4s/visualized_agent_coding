import os
import sys
import unittest

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, HERE)
import nodes_service


class NodeProjectionTests(unittest.TestCase):
    def test_source_spans_and_trailing_comment(self):
        result = nodes_service.build_blocks(dict(source='''\
self.move(10)  # 前进
'''))
        node = result['nodes'][0]
        self.assertEqual('motion.move', node['type'])
        self.assertGreater(node['length'], 0)
        self.assertEqual(1, node['line'])
        self.assertEqual(1, node['endLine'])
        self.assertEqual('# 前进', node['arguments']['comment'])

    def test_function_class_and_members(self):
        result = nodes_service.build_document(dict(source='''\
class Actor:
    health = 100

    def initialize(self):
        self.health = 80
        return self.health
'''))
        self.assertIsNone(result['error'])
        cls = result['nodes'][0]
        self.assertEqual('class', cls['type'])
        self.assertEqual('Actor', cls['arguments']['name'])
        self.assertEqual(['data.set_class_attribute', 'def'],
                         [node['type'] for node in cls['children']])
        method = cls['children'][1]
        self.assertEqual('initialize', method['arguments']['name'])
        self.assertEqual(['data.set_attribute', 'control.return'],
                         [node['type'] for node in method['children']])

    def test_while_break_continue_pass_and_match(self):
        result = nodes_service.build_blocks(dict(source='''\
while ready:
    break
else:
    pass
match command:
    case "go" if speed > 0:
        self.move(speed)
    case _:
        pass
'''))
        self.assertEqual(['control.while', 'control.match'],
                         [node['type'] for node in result['nodes']])
        self.assertEqual('control.break', result['nodes'][0]['children'][0]['type'])
        self.assertEqual('control.pass', result['nodes'][0]['elseChildren'][0]['type'])
        match = result['nodes'][1]
        self.assertEqual('command', match['arguments']['subject'])
        self.assertEqual(['control.case', 'control.case'],
                         [node['type'] for node in match['children']])
        self.assertEqual('speed > 0', match['children'][0]['arguments']['guard'])
        self.assertEqual('motion.move', match['children'][0]['children'][0]['type'])

    def test_standalone_comment_has_span_and_docstring_is_kept(self):
        source = '# 说明\n"""文档"""\nself.move(1)\n'
        result = nodes_service.build_blocks(dict(source=source))
        self.assertEqual(['comment', 'docstring', 'motion.move'],
                         [node['type'] for node in result['nodes']])
        comment = result['nodes'][0]
        self.assertGreater(comment['length'], 0)
        self.assertEqual(0, comment['start'])
        self.assertEqual('文档', result['nodes'][1]['text'])

    def test_legacy_blocks_entry_still_works(self):
        result = nodes_service.build_blocks(dict(source='self.turn(15)'))
        self.assertEqual('motion.turn', result['nodes'][0]['type'])


if __name__ == '__main__':
    unittest.main()
