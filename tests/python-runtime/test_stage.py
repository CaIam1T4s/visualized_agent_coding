import sys
import time
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'src' / 'VisualizedAgentCoding.Python.Runtime'))
from vac_runtime import clock
from vac_runtime.stage import BackdropBase, ElementBase, StageSession, normalize_direction


class Actor(ElementBase):
    pass


class ClickActor(ElementBase):
    def on_click(self):
        self.clicked = True

    def on_clone_start(self):
        self.clone_started = True


class DeletingActor(ClickActor):
    def on_clone_start(self):
        self.delete_clone()


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


class CloningTests(unittest.TestCase):
    def _session(self, actor, emitted, clicks=None, clone_starts=None):
        return StageSession(
            {'sessionId': 'run', 'initialStage': {'currentBackdropId': None, 'objects': []}},
            [({'objectId': 'a', 'kind': 'object', 'path': 'objects/a.py', 'id': 's'}, actor)],
            lambda kind, stage=None: emitted.append(stage), click_handlers=clicks,
            clone_handlers=clone_starts)

    def test_clone_copies_runtime_state_and_has_independent_state(self):
        emitted = []
        actor = Actor()
        session = self._session(actor, emitted)
        try:
            actor.goto(12, -8)
            actor.direction = 45
            actor.size = 150
            clone = actor.clone()
            self.assertIsNot(actor, clone)
            self.assertEqual((12, -8, 45, 150), (clone.x, clone.y, clone.direction, clone.size))
            clone.x = 99
            clone.direction = -45
            self.assertEqual((12, -8, 45), (actor.x, actor.y, actor.direction))
            self.assertEqual([actor, clone], [instance for _, instance in session.instances])
            self.assertIn('clone:a:1', session.instances_by_id)
            session.publish()
            self.assertEqual(2, len(emitted[-1]['objects']))
            self.assertEqual(99, emitted[-1]['objects'][1]['x'])
        finally:
            session.close()

    def test_clone_gets_its_own_click_handler_and_is_clone_flag(self):
        actor = ClickActor()
        clicks = {}
        session = self._session(actor, [], clicks)
        clicks['a'] = (actor, actor.on_click)
        try:
            clone = actor.clone()
            self.assertFalse(actor.is_clone)
            self.assertTrue(clone.is_clone)
            clone_instance, clone_callback = clicks['clone:a:1']
            self.assertIs(clone, clone_instance)
            self.assertIs(clone, clone_callback.__self__)
            clone_callback()
            self.assertTrue(clone.clicked)
        finally:
            session.close()

    def test_clone_start_runs_only_for_the_clone(self):
        actor = ClickActor()
        session = self._session(actor, [], clone_starts={'a': actor.on_clone_start})
        try:
            clone = actor.clone()
            session.run_pending_clone_starts()
            self.assertTrue(getattr(clone, 'clone_started', False))
            self.assertFalse(getattr(actor, 'clone_started', False))
        finally:
            session.close()

    def test_clone_start_can_delete_the_new_clone(self):
        actor = DeletingActor()
        emitted = []
        session = self._session(actor, emitted, clone_starts={'a': actor.on_clone_start})
        try:
            clone = actor.clone()
            session.run_pending_clone_starts()
            self.assertNotIn(clone, [instance for _, instance in session.instances])
            self.assertFalse(any(key[0].startswith('clone:') for key in session.animations))
            session.publish()
            self.assertEqual(1, len(emitted[-1]['objects']))
            self.assertEqual('a', emitted[-1]['objects'][0]['objectId'])
        finally:
            session.close()

    def test_clone_copies_active_costume(self):
        actor = Actor()
        layout = {
            'costumes': [
                {'id': 'c1', 'name': '默认', 'imagePath': 'assets/images/a.png', 'normalizeImage': True},
                {'id': 'c2', 'name': '开心', 'imagePath': 'assets/images/b.png', 'normalizeImage': False},
            ],
            'initialCostumeId': 'c1',
            'imagePath': 'assets/images/a.png',
            'normalizeImage': True,
        }
        emitted = []
        session = StageSession(
            {'sessionId': 'run', 'initialStage': {'currentBackdropId': None, 'objects': [layout | {'objectId': 'a'}]}},
            [({'objectId': 'a', 'kind': 'object', 'path': 'objects/a.py', 'id': 's'}, actor)],
            lambda kind, stage=None: emitted.append(stage))
        try:
            actor.switch_costume('开心')
            clone = actor.clone()
            self.assertEqual('开心', clone.costume)
            clone.switch_costume('默认')
            self.assertEqual('开心', actor.costume)
            self.assertEqual('默认', clone.costume)
        finally:
            session.close()

    def test_delete_clone_removes_state_handler_and_animation(self):
        clock.ensure_started()
        actor = ClickActor()
        clicks = {}
        emitted = []
        session = self._session(actor, emitted, clicks)
        clicks['a'] = (actor, actor.on_click)
        try:
            clone = actor.clone()
            clone.glide_to(10, 0, 5)
            self.assertTrue(session.animations)
            self.assertTrue(clone.delete_clone())
            self.assertNotIn(clone, [instance for _, instance in session.instances])
            self.assertNotIn('clone:a:1', session.instances_by_id)
            self.assertNotIn('clone:a:1', clicks)
            self.assertFalse(any(key[0] == 'clone:a:1' for key in session.animations))
            session.publish()
            self.assertEqual(['a'], [o['objectId'] for o in emitted[-1]['objects']])
            self.assertFalse(clone.delete_clone())
            with self.assertRaises(RuntimeError):
                actor.delete_clone()
        finally:
            session.close()
            clock.stop()

    def test_clone_is_forbidden_for_backdrops_and_clones(self):
        emitted = []
        actor = Actor()
        session = self._session(actor, emitted)
        try:
            clone = actor.clone()
            with self.assertRaises(RuntimeError):
                clone.clone()
        finally:
            session.close()
        self.assertFalse(hasattr(BackdropBase, 'clone'))

    def test_clone_limit_is_enforced(self):
        actor = Actor()
        session = self._session(actor, [])
        session.MAX_CLONES = 1
        try:
            actor.clone()
            with self.assertRaises(RuntimeError):
                actor.clone()
        finally:
            session.close()


class CollisionTests(unittest.TestCase):
    def _session(self, image_sizes=None):
        cat, dog = Actor(), Actor()
        session = StageSession(
            {'sessionId': 'run', 'initialStage': {'currentBackdropId': None, 'objects': [
                {'objectId': 'cat'}, {'objectId': 'dog'}]}, 'imageSizes': image_sizes or {}},
            [({'objectId': 'cat', 'kind': 'object', 'path': 'objects/cat.py', 'id': 'cat-script'}, cat),
             ({'objectId': 'dog', 'kind': 'object', 'path': 'objects/dog.py', 'id': 'dog-script'}, dog)],
            lambda kind, stage=None: None)
        return session, cat, dog

    def test_visible_instances_overlap_by_role_name(self):
        session, cat, dog = self._session()
        try:
            self.assertTrue(cat.touching('dog'))
            self.assertTrue(dog.touching('cat'))
            dog.goto(100, 0)
            self.assertFalse(cat.touching('dog'))
            self.assertFalse(dog.touching('cat'))
        finally:
            session.close()

    def test_invisible_and_deleted_instances_do_not_collide(self):
        session, cat, dog = self._session()
        try:
            dog.visible = False
            self.assertFalse(cat.touching('dog'))
            dog.visible = True
        finally:
            session.close()

    def test_clones_can_collide_with_siblings(self):
        session, cat, dog = self._session()
        try:
            clone = cat.clone()
            dog.goto(100, 0)
            clone.goto(100, 0)
            self.assertTrue(clone.touching('dog'))
            clone.goto(0, 0)
            self.assertFalse(clone.touching('dog'))
        finally:
            session.close()

    def test_edge_and_pointer_collision(self):
        session, cat, dog = self._session(image_sizes={'cat': {'width': 20, 'height': 20}})
        try:
            cat.goto(230, 0)
            self.assertTrue(cat.touching_edge())
            cat.goto(225, 0)
            self.assertFalse(cat.touching_edge())
            cat.goto(0, 0)
            self.assertFalse(cat.touching_pointer())
            session.set_pointer(0, 0)
            self.assertTrue(cat.touching_pointer())
            session.set_pointer(100, 0)
            self.assertFalse(cat.touching_pointer())
        finally:
            session.close()

    def test_unknown_role_is_rejected(self):
        session, cat, dog = self._session()
        try:
            with self.assertRaises(ValueError):
                cat.touching('Missing')
        finally:
            session.close()

    def test_collision_uses_the_active_costume_size(self):
        cat, dog = Actor(), Actor()
        session = StageSession(
            {'sessionId': 'run', 'initialStage': {'currentBackdropId': None, 'objects': [
                {'objectId': 'cat', 'costumes': [
                    {'id': 'small', 'name': '小', 'imagePath': 'assets/images/small.png',
                     'normalizeImage': False},
                    {'id': 'large', 'name': '大', 'imagePath': 'assets/images/large.png',
                     'normalizeImage': False}],
                 'initialCostumeId': 'small'},
                {'objectId': 'dog'}]},
             'stageCostumeImageSizes': {'small': {'width': 10, 'height': 10},
                                        'large': {'width': 100, 'height': 100}}},
            [({'objectId': 'cat', 'kind': 'object', 'path': 'objects/cat.py', 'id': 'cat-script'}, cat),
             ({'objectId': 'dog', 'kind': 'object', 'path': 'objects/dog.py', 'id': 'dog-script'}, dog)],
            lambda kind, stage=None: None)
        try:
            dog.goto(60, 0)
            self.assertFalse(cat.touching('dog'))
            cat.switch_costume('大')
            self.assertTrue(cat.touching('dog'))
        finally:
            session.close()

    def test_rotation_changes_overlap_and_pointer_bounds(self):
        session, cat, dog = self._session(image_sizes={'cat': {'width': 100, 'height': 20}})
        try:
            cat.goto(0, 0)
            cat.direction = 90
            dog.goto(0, 100)
            self.assertFalse(cat.touching('dog'))
            cat.direction = 0
            dog.goto(0, 70)
            self.assertTrue(cat.touching('dog'))
            session.set_pointer(30, 0)
            self.assertFalse(cat.touching_pointer())
            cat.direction = 90
            self.assertTrue(cat.touching_pointer())
        finally:
            session.close()


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
