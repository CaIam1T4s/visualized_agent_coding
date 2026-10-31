import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'src' / 'VisualizedAgentCoding.Python.Runtime'))
from vac_runtime.stage import BackdropBase, ElementBase, normalize_direction


class Actor(ElementBase):
    pass


class StageMotionTests(unittest.TestCase):
    def setUp(self):
        self.actor = Actor()
        self.actor._vac_bind({}, lambda: None, lambda: (0.0, 0.0, False))

    def test_move_uses_stage_direction(self):
        self.actor.goto(0, 0)
        self.actor.direction = 0
        self.actor.move(10)
        self.assertAlmostEqual(0, self.actor.x)
        self.assertAlmostEqual(10, self.actor.y)

        self.actor.goto(0, 0)
        self.actor.direction = 90
        self.actor.move(10)
        self.assertAlmostEqual(10, self.actor.x)
        self.assertAlmostEqual(0, self.actor.y)

        self.actor.goto(0, 0)
        self.actor.direction = 180
        self.actor.move(10)
        self.assertAlmostEqual(0, self.actor.x)
        self.assertAlmostEqual(-10, self.actor.y)

    def test_turn_and_point_normalize_to_signed_range(self):
        self.actor.direction = 170
        self.actor.turn(20)
        self.assertAlmostEqual(-170, self.actor.direction)
        self.actor.point_in_direction(540)
        self.assertAlmostEqual(180, self.actor.direction)
        self.assertAlmostEqual(180, normalize_direction(540))
        self.assertAlmostEqual(-180, normalize_direction(-540))

    def test_goto_and_relative_changes(self):
        self.actor.goto(12, -8)
        self.assertEqual((12, -8), (self.actor.x, self.actor.y))
        self.actor.change_x(3)
        self.actor.change_y(4)
        self.assertEqual((15, -4), (self.actor.x, self.actor.y))

    def test_move_and_goto_do_not_partially_apply_invalid_coordinates(self):
        self.actor.goto(100000, 0)
        with self.assertRaises(ValueError):
            self.actor.goto(1, 100001)
        self.assertEqual((100000, 0), (self.actor.x, self.actor.y))
        with self.assertRaises(ValueError):
            self.actor.move(1)

    def test_pointer_direction_and_move_to_pointer(self):
        self.actor.goto(0, 0)
        self.actor.direction = 0
        self.actor._vac_bind({}, lambda: None, lambda: (100.0, 0.0, True))
        self.actor.point_towards_pointer()
        self.assertAlmostEqual(90, self.actor.direction)
        self.actor.move_to_pointer()
        self.assertEqual((100, 0), (self.actor.x, self.actor.y))

    def test_unseen_pointer_keeps_direction_and_position(self):
        self.actor.goto(4, 5)
        self.actor.direction = 45
        self.actor.point_towards_pointer()
        self.actor.move_to_pointer()
        self.assertEqual((4, 5), (self.actor.x, self.actor.y))
        self.assertEqual(45, self.actor.direction)

    def test_background_has_no_motion_api(self):
        self.assertFalse(hasattr(BackdropBase, 'move'))
        self.assertFalse(hasattr(BackdropBase, 'move_to_pointer'))


if __name__ == '__main__':
    unittest.main()
