import sys
import time
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'src' / 'VisualizedAgentCoding.Python.Runtime'))
from vac_runtime import clock
from vac_runtime.stage import BackdropBase, ElementBase, StageSession, normalize_direction


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

    def test_glide_to_and_glide_by_use_non_blocking_tween(self):
        clock.ensure_started()
        actor = Actor()
        emitted = []
        session = StageSession(
            {'sessionId': 'run', 'initialStage': {'currentBackdropId': None, 'objects': []}},
            [({'objectId': 'a', 'kind': 'object', 'path': 'objects/a.py', 'id': 's'}, actor)],
            lambda kind, stage=None: emitted.append(stage))
        try:
            actor.goto(0, 0)
            actor.glide_to(10, -10, 0.05)
            time.sleep(0.08)
            session.advance_animations()
            self.assertAlmostEqual(10, actor.x)
            self.assertAlmostEqual(-10, actor.y)
            actor.glide_by(-4, 2, 0.05)
            time.sleep(0.08)
            session.advance_animations()
            self.assertAlmostEqual(6, actor.x)
            self.assertAlmostEqual(-8, actor.y)
            self.assertTrue(emitted)
        finally:
            session.clear_animations()
            clock.stop()

    def _session(self, actor, finished):
        return StageSession(
            {'sessionId': 'run', 'initialStage': {'currentBackdropId': None, 'objects': []}},
            [({'objectId': 'a', 'kind': 'object', 'path': 'objects/a.py', 'id': 's'}, actor)],
            lambda kind, stage=None: None, finished.append)

    def test_animation_finish_broadcast_only_on_natural_completion(self):
        clock.ensure_started()
        actor = Actor()
        finished = []
        session = self._session(actor, finished)
        try:
            actor.goto(0, 0)
            actor.glide_to(10, 0, 0.05, on_finish='move-done')
            time.sleep(0.08)
            session.advance_animations()
            self.assertEqual(['move-done'], finished)
            actor.glide_to(20, 0, 0.05, on_finish='old')
            actor.glide_to(30, 0, 0.05, on_finish='new')
            time.sleep(0.08)
            session.advance_animations()
            self.assertEqual(['move-done', 'new'], finished)
            actor.glide_to(40, 0, 0.05, on_finish='cancelled')
            actor.x = 5
            time.sleep(0.08)
            session.advance_animations()
            self.assertEqual(['move-done', 'new'], finished)
        finally:
            session.clear_animations()
            clock.stop()

    def test_animation_finish_name_is_validated(self):
        actor = Actor()
        with self.assertRaises(ValueError):
            actor.glide_to(10, 0, 0.1, on_finish='')

    def test_rotation_and_size_animations_run_independently(self):
        clock.ensure_started()
        actor = Actor()
        session = StageSession(
            {'sessionId': 'run', 'initialStage': {'currentBackdropId': None, 'objects': []}},
            [({'objectId': 'a', 'kind': 'object', 'path': 'objects/a.py', 'id': 's'}, actor)],
            lambda kind, stage=None: None)
        try:
            actor.goto(0, 0)
            actor.direction = 0
            actor.size = 100
            actor.glide_to(10, 0, 0.05)
            actor.rotate_to(90, 0.05)
            actor.glide_size_to(150, 0.05)
            time.sleep(0.08)
            session.advance_animations()
            self.assertAlmostEqual(10, actor.x)
            self.assertAlmostEqual(0, actor.y)
            self.assertAlmostEqual(90, actor.direction)
            self.assertAlmostEqual(150, actor.size)
            self.assertFalse(session.animations)
        finally:
            session.clear_animations()
            clock.stop()

    def test_rotation_animation_takes_shortest_path(self):
        clock.ensure_started()
        actor = Actor()
        session = StageSession(
            {'sessionId': 'run', 'initialStage': {'currentBackdropId': None, 'objects': []}},
            [({'objectId': 'a', 'kind': 'object', 'path': 'objects/a.py', 'id': 's'}, actor)],
            lambda kind, stage=None: None)
        try:
            actor.direction = 170
            actor.rotate_to(-170, 0.05)
            time.sleep(0.08)
            session.advance_animations()
            self.assertAlmostEqual(-170, actor.direction)
        finally:
            session.clear_animations()
            clock.stop()

    def test_glide_is_cancelled_by_direct_assignment(self):
        clock.ensure_started()
        actor = Actor()
        session = StageSession(
            {'sessionId': 'run', 'initialStage': {'currentBackdropId': None, 'objects': []}},
            [({'objectId': 'a', 'kind': 'object', 'path': 'objects/a.py', 'id': 's'}, actor)],
            lambda kind, stage=None: None)
        try:
            actor.goto(0, 0)
            actor.glide_to(10, 0, 0.05)
            actor.x = 4
            time.sleep(0.08)
            session.advance_animations()
            self.assertEqual(4, actor.x)
            self.assertFalse(session.animations)
        finally:
            session.clear_animations()
            clock.stop()


class CostumeTests(unittest.TestCase):
    def _layout(self):
        return {
            'costumes': [
                {'id': 'c1', 'name': '默认', 'imagePath': 'assets/images/a.png', 'normalizeImage': True},
                {'id': 'c2', 'name': '开心', 'imagePath': 'assets/images/b.png', 'normalizeImage': False},
            ],
            'initialCostumeId': 'c1',
            'imagePath': 'assets/images/a.png',
            'normalizeImage': True,
        }

    def test_costume_is_read_only_and_switch_resolves_by_name(self):
        actor = Actor()
        actor._vac_bind(self._layout(), lambda: None)
        self.assertEqual('默认', actor.costume)
        actor.switch_costume('开心')
        self.assertEqual('开心', actor.costume)
        actor.switch_costume('开心')
        self.assertEqual('开心', actor.costume)
        with self.assertRaises(ValueError):
            actor.switch_costume('不存在')
        with self.assertRaises(AttributeError):
            actor.costume = '默认'
        with self.assertRaises(AttributeError):
            BackdropBase.switch_costume

    def test_missing_or_duplicate_costume_data_is_rejected(self):
        actor = Actor()
        layout = self._layout()
        layout['initialCostumeId'] = 'missing'
        with self.assertRaises(ValueError):
            actor._vac_bind(layout, lambda: None)
        duplicate = self._layout()
        duplicate['costumes'][1]['name'] = '默认'
        with self.assertRaises(ValueError):
            Actor()._vac_bind(duplicate, lambda: None)

    def test_publish_carries_active_costume_image(self):
        emitted = []
        actor = Actor()
        session = StageSession(
            {'sessionId': 'run', 'initialStage': {'currentBackdropId': None, 'objects': [self._layout() | {'objectId': 'a'}]}},
            [({'objectId': 'a', 'kind': 'object', 'path': 'objects/a.py', 'id': 's'}, actor)],
            lambda kind, stage=None: emitted.append(stage))
        try:
            session.publish()
            self.assertEqual('assets/images/a.png', emitted[-1]['objects'][0]['imagePath'])
            self.assertEqual('默认', actor.costume)
            actor.switch_costume('开心')
            session.publish()
            self.assertEqual('assets/images/b.png', emitted[-1]['objects'][0]['imagePath'])
            self.assertFalse(emitted[-1]['objects'][0]['normalizeImage'])
        finally:
            session.close()


if __name__ == '__main__':
    unittest.main()
