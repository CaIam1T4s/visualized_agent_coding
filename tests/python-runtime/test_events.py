import sys
import time
import unittest
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'src' / 'VisualizedAgentCoding.Python.Runtime'))
from vac_runtime import clock
from vac_runtime.events import EventInbox


class EventInboxTests(unittest.TestCase):
    def test_bounded_session_queue_and_stop(self):
        inbox = EventInbox('run')
        command = dict(kind='sprite-click', sessionId='run', objectId='cat')
        self.assertFalse(inbox.offer(command))
        inbox.accepting = True
        self.assertFalse(inbox.offer(dict(command, sessionId='old')))
        self.assertFalse(inbox.offer(dict(command, kind='unknown')))
        for _ in range(64):
            self.assertTrue(inbox.offer(command))
        self.assertFalse(inbox.offer(command))
        self.assertEqual(64, inbox.pending.qsize())
        inbox.stop(requested=True)
        self.assertEqual(0, inbox.pending.qsize())
        self.assertFalse(inbox.offer(command))
        self.assertTrue(inbox.stop_requested.is_set())

    def test_cleanup_is_not_a_user_stop(self):
        inbox = EventInbox('run')
        inbox.stop()
        self.assertFalse(inbox.stop_requested.is_set())


class WaitTests(unittest.TestCase):
    def _inbox(self):
        inbox = EventInbox('run')
        inbox.prepare({}, lambda kind, text='', stage=None: None, None)
        return inbox

    def tearDown(self):
        clock.stop()

    def test_wait_requires_prepared_event_loop(self):
        inbox = EventInbox('run')
        with self.assertRaises(RuntimeError):
            inbox.wait(0.01)

    def test_wait_keeps_the_deadline(self):
        clock.ensure_started()
        inbox = self._inbox()
        started = clock.timer()
        inbox.wait(0.06)
        self.assertGreaterEqual(clock.timer() - started, 0.05)
        inbox.stop()

    def test_wait_lets_later_key_handlers_run(self):
        clock.ensure_started()
        events = []
        inbox = EventInbox('run')

        def first():
            events.append('first-start')
            inbox.wait(0.04)
            events.append('first-end')

        def second():
            events.append('second')

        inbox.prepare({}, lambda kind, text='', stage=None: None, None,
                      keys={'a': [(None, first), (None, second)]})
        inbox._dispatch(('key', 'a'), {}, {'a': [(None, first), (None, second)]}, None)
        self.assertEqual(['first-start', 'second', 'first-end'], events)
        inbox.stop()

    def test_stop_interrupts_wait(self):
        import threading
        clock.ensure_started()
        inbox = self._inbox()
        stopper = threading.Thread(target=lambda: (time.sleep(0.03), inbox.stop(True)))
        stopper.start()
        with self.assertRaises(KeyboardInterrupt):
            inbox.wait(2.0)
        stopper.join()


class StageClickTests(unittest.TestCase):
    def _inbox(self):
        inbox = EventInbox('run')
        inbox.accepting = True
        return inbox

    def test_stage_click_requires_no_object_id(self):
        inbox = self._inbox()
        self.assertTrue(inbox.offer(dict(kind='stage-click', sessionId='run')))
        self.assertEqual(('stage-click', None), inbox.pending.get_nowait())

    def test_stage_click_rejects_other_session(self):
        inbox = self._inbox()
        self.assertFalse(inbox.offer(dict(kind='stage-click', sessionId='other')))

    def test_unknown_kind_is_still_rejected(self):
        inbox = self._inbox()
        self.assertFalse(inbox.offer(dict(kind='unknown', sessionId='run')))

    def test_key_commands_are_queued_by_name(self):
        inbox = self._inbox()
        self.assertTrue(inbox.offer(dict(kind='key', sessionId='run', key='a')))
        self.assertEqual(('key', 'a'), inbox.pending.get_nowait())
        self.assertFalse(inbox.offer(dict(kind='key', sessionId='run', key=1)))


class ClockTests(unittest.TestCase):
    def tearDown(self):
        clock.stop()

    def test_timer_starts_and_advances(self):
        clock.stop()
        with self.assertRaises(RuntimeError):
            clock.timer()
        clock.ensure_started()
        first = clock.timer()
        time.sleep(0.01)
        self.assertGreaterEqual(clock.timer(), first)

    def test_timer_specs_parse_threshold_and_repeat(self):
        self.assertEqual((False, 1.5), clock.parse_spec('1.5'))
        self.assertEqual((True, 2.0), clock.parse_spec('every:2'))
        with self.assertRaises(ValueError):
            clock.parse_spec('0')
        with self.assertRaises(ValueError):
            clock.parse_spec('every:')
