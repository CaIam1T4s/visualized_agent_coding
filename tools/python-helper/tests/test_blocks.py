import os
import sys
import unittest

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, HERE)
import blocks_service


class BlockProjectionTests(unittest.TestCase):
    def test_supported_motion_and_control_blocks(self):
        result = blocks_service.build_blocks(dict(source='''\
self.move(10)
wait(1)
for _ in range(3):
    self.turn(15)
if get_variable("ready"):
    self.goto(0, 0)
else:
    self.switch_costume("sad")
'''))
        self.assertIsNone(result['error'])
        self.assertEqual(['motion.move', 'control.wait', 'control.repeat', 'control.if'],
                         [node['type'] for node in result['nodes']])
        self.assertEqual('motion.turn', result['nodes'][2]['children'][0]['type'])
        self.assertEqual('motion.goto', result['nodes'][3]['children'][0]['type'])
        self.assertEqual('looks.switch_costume', result['nodes'][3]['elseChildren'][0]['type'])

    def test_range_and_list_iteration_are_distinct(self):
        result = blocks_service.build_blocks(dict(source='''\
for i in range(1, 10, 2):
    self.move(i)
for item in list_values("items"):
    print(item)
'''))
        self.assertEqual('control.repeat_range', result['nodes'][0]['type'])
        self.assertEqual(dict(name='i', start='1', stop='10', step='2'),
                         result['nodes'][0]['arguments'])
        self.assertEqual('control.for_each', result['nodes'][1]['type'])
        self.assertEqual(dict(item='item', name='items'), result['nodes'][1]['arguments'])

    def test_unknown_code_is_raw_and_comments_are_kept(self):
        result = blocks_service.build_blocks(dict(source='''\
# keep this
x = 1
self.move(2)
'''))
        self.assertIsNone(result['error'])
        self.assertEqual(['comment', 'raw', 'motion.move'], [node['type'] for node in result['nodes']])
        self.assertEqual('# keep this', result['nodes'][0]['text'])

    def test_syntax_errors_do_not_throw(self):
        result = blocks_service.build_blocks(dict(source='if True:\n'))
        self.assertTrue(result['error'])
        self.assertEqual([], result['nodes'])


if __name__ == '__main__':
    unittest.main()
