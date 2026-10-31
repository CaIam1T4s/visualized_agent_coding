import sys
import unittest
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'src' / 'VisualizedAgentCoding.Python.Runtime'))
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
