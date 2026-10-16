"""Aurora's hand-association tests (ported unchanged from her test_bridge.py) plus HandSession tests."""
import json
import sys
import time
import unittest
from types import SimpleNamespace
from unittest import mock

from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'fruit_tracking'))
from hand_actions import HandSession, hand_packets, hand_state


class HandAssociationTests(unittest.TestCase):
    def test_hand_gesture_mapping_is_deliberately_limited(self):
        self.assertEqual(hand_state('Open_Palm'), 'open')
        self.assertEqual(hand_state('Closed_Fist'), 'fist')
        self.assertEqual(hand_state('Victory'), 'victory')
        self.assertEqual(hand_state('Thumb_Up'), 'unknown')
        self.assertEqual(hand_state('None'), 'unknown')

    def test_hand_packets_use_pose_anatomy_and_reject_ambiguous_hands(self):
        def point(x, y): return SimpleNamespace(x=x, y=y, presence=1., visibility=1.)
        pose = [point(.5, .5) for _ in range(33)]
        pose[15], pose[16] = point(.2, .5), point(.8, .5)
        hand = [point(.21, .5)]
        result = SimpleNamespace(hand_landmarks=[hand], gestures=[[SimpleNamespace(category_name='Closed_Fist', score=.9)]])
        packets = hand_packets(result, [(None, None, None, pose)], {1: 0}, 7)
        self.assertEqual(packets[0]['left']['state'], 'fist')
        self.assertEqual(packets[0]['right']['state'], 'unknown')
        ambiguous = [point(.5, .5)]
        self.assertEqual(hand_packets(SimpleNamespace(hand_landmarks=[ambiguous], gestures=[[SimpleNamespace(category_name='Open_Palm', score=.9)]]), [(None,None,None,pose)], {1:0}, 8)[0]['left']['state'], 'unknown')

    def test_hand_packets_keep_unknown_when_recognition_has_no_result(self):
        packets = hand_packets(None, [], {1: 0, 2: 1}, 4)
        self.assertEqual([(p['left']['state'], p['right']['state']) for p in packets],
                         [('unknown', 'unknown'), ('unknown', 'unknown')])

    def test_hand_packets_assign_multiple_performers_without_image_order(self):
        def point(x, y, quality=1.): return SimpleNamespace(x=x, y=y, presence=quality, visibility=quality)
        left_pose = [point(.5, .5) for _ in range(33)]
        right_pose = [point(.5, .5) for _ in range(33)]
        left_pose[15], left_pose[16] = point(.15, .4), point(.35, .4)
        right_pose[15], right_pose[16] = point(.65, .4), point(.85, .4)
        hands = [[point(.84, .4)], [point(.16, .4)]]  # deliberately reverse body order
        gestures = [[SimpleNamespace(category_name='Open_Palm', score=.9)],
                    [SimpleNamespace(category_name='Closed_Fist', score=.9)]]
        packets = hand_packets(SimpleNamespace(hand_landmarks=hands, gestures=gestures),
                               [(None, None, None, left_pose), (None, None, None, right_pose)],
                               {1: 0, 2: 1}, 9)
        self.assertEqual(packets[0]['left']['state'], 'fist')
        self.assertEqual(packets[1]['right']['state'], 'open')

    def test_hand_packets_reject_low_quality_pose_wrist(self):
        def point(x, y, quality=1.): return SimpleNamespace(x=x, y=y, presence=quality, visibility=quality)
        pose = [point(.5, .5) for _ in range(33)]
        pose[15] = point(.2, .5, .2)
        hand = [point(.2, .5)]
        result = SimpleNamespace(hand_landmarks=[hand], gestures=[[SimpleNamespace(category_name='Closed_Fist', score=.9)]])
        self.assertEqual(hand_packets(result, [(None, None, None, pose)], {1: 0}, 10)[0]['left']['state'], 'unknown')

    def test_hand_packets_reject_hand_outside_pose_association_radius(self):
        def point(x, y): return SimpleNamespace(x=x, y=y, presence=1., visibility=1.)
        pose = [point(.5, .5) for _ in range(33)]
        pose[15], pose[16] = point(.2, .5), point(.8, .5)
        result = SimpleNamespace(hand_landmarks=[[point(.5, .1)]],
                                 gestures=[[SimpleNamespace(category_name='Closed_Fist', score=.9)]])
        packets = hand_packets(result, [(None, None, None, pose)], {1: 0}, 11)
        self.assertEqual(packets[0]['left']['state'], 'unknown')


class FakeImage:
    def __init__(self, **kw):
        self.data = kw['data']


class FakeRgb:
    def copy(self):
        return self


class HandSessionTests(unittest.TestCase):
    def args(self, enabled=True):
        return SimpleNamespace(hand_actions=enabled, hand_port=5059, gesture_model=None)

    def run_frames(self, session, frames=3):
        sender = mock.Mock()
        mp = SimpleNamespace(Image=FakeImage, ImageFormat=SimpleNamespace(SRGB=1))
        with mock.patch.dict('sys.modules', {'mediapipe': mp}):
            for _ in range(frames):
                session.process(FakeRgb(), [], {1: 0}, sender)
                session.last_submit = 0
        return sender

    def test_disabled_never_creates_recognizer(self):
        factory = mock.Mock()
        session = HandSession(self.args(False), factory=factory)
        self.run_frames(session)
        factory.assert_not_called()

    def test_sends_hand_packets_with_session_to_hand_port(self):
        recognizer = mock.Mock()
        recognizer.recognize.return_value = None
        with HandSession(self.args(), factory=lambda: recognizer) as session:
            session.command({'version': 1, 'handsEnabled': True})
            sender = self.run_frames(session)
        packet, address = sender.sendto.call_args[0]
        packet = json.loads(packet)
        self.assertEqual(address, ('127.0.0.1', 5059))
        self.assertEqual((packet['performerId'], packet['left']['state']), (1, 'unknown'))
        self.assertEqual(packet['session'], session.session)

    def test_identity_change_drops_in_flight_association(self):
        session = HandSession(self.args())
        session.latest = (None, [], {1: 0}, 1, session.epoch, time.monotonic())
        session.command({'version': 1, 'reset': True})
        self.assertIsNone(session.latest)
        self.assertEqual(session.epoch, 1)

    def test_recognizer_failure_disables_hands_only(self):
        session = HandSession(self.args(), factory=mock.Mock(side_effect=RuntimeError('no model')))
        session.command({'version': 1, 'handsEnabled': True})
        self.run_frames(session, 1)
        self.assertFalse(session.enabled)
        self.assertIn('no model', session.error)

    def test_hands_toggle_gates_recognition(self):
        recognizer = mock.Mock()
        recognizer.recognize.return_value = None
        session = HandSession(self.args(), factory=lambda: recognizer)
        sender = self.run_frames(session)
        sender.sendto.assert_not_called()
        recognizer.recognize.assert_not_called()
        for bad in (1, 'true', None):
            session.command({'version': 1, 'handsEnabled': bad})
            self.assertFalse(session.enabled)
        session.command({'version': 1, 'handsEnabled': True})
        self.assertTrue(self.run_frames(session).sendto.called)
        session.command({'version': 1, 'handsEnabled': False})
        self.assertIsNone(session.latest)

    def test_failing_recognizer_is_rebuilt_and_recovers(self):
        broken, healthy = mock.Mock(), mock.Mock()
        broken.recognize.side_effect = RuntimeError('graph failed')
        healthy.recognize.return_value = None
        recognizers = iter([broken, healthy])
        session = HandSession(self.args(), factory=lambda: next(recognizers))
        session.command({'version': 1, 'handsEnabled': True})
        sender = self.run_frames(session, 4)
        broken.close.assert_called_once()
        self.assertTrue(session.enabled)
        self.assertTrue(sender.sendto.called)  # packets flow again from the rebuilt recognizer

    def test_repeated_unchanged_count_request_keeps_hand_results(self):
        session = HandSession(self.args())
        session.command({'version': 1, 'performerCount': 3, 'upperBodyOnly': True})
        session.latest = (None, [], {1: 0}, 1, session.epoch, time.monotonic())
        session.command({'version': 1, 'performerCount': 3, 'upperBodyOnly': True})
        self.assertIsNotNone(session.latest)
        session.command({'version': 1, 'performerCount': 2, 'upperBodyOnly': True})
        self.assertIsNone(session.latest)
        session.latest = (None, [], {1: 0}, 1, session.epoch, time.monotonic())
        session.command({'version': 1, 'performerCount': 2, 'upperBodyOnly': True, 'zones': 2})
        self.assertIsNone(session.latest)  # switching to fixed zones renumbers people

    def test_start_loads_model_early_and_failure_only_disables_hands(self):
        factory = mock.Mock()
        session = HandSession(self.args(), factory=factory)
        session.start()
        factory.assert_called_once()
        failing = HandSession(self.args(), factory=mock.Mock(side_effect=RuntimeError('missing')))
        failing.start()
        self.assertFalse(failing.enabled)


class PatchedBridgeTests(unittest.TestCase):
    def test_hands_and_camera_recovery_hooks_are_installed(self):
        import sys as _sys
        from pathlib import Path
        _sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'fruit_tracking'))
        from fruit_bridge import patched_source
        source = patched_source()
        self.assertIn('hands.process(rgb, observations, assignments, sender)', source)
        self.assertIn('hands.start()', source)
        self.assertIn('recover_read(cv2, camera, args.camera)', source)
        self.assertLess(source.index('hands.start()'), source.index('camera = cv2.VideoCapture(args.camera)'))


class CameraRecoveryTests(unittest.TestCase):
    class Camera:
        def __init__(self, reads):
            self.reads = list(reads); self.released = False
        def read(self):
            return self.reads.pop(0) if self.reads else (False, None)
        def release(self):
            self.released = True

    def test_brief_dropout_is_retried_without_reopening(self):
        from camera_recovery import recover_read
        camera = self.Camera([(False, None), (True, 'frame')])
        cv2 = mock.Mock()
        ok, frame, same = recover_read(cv2, camera, 0, sleep=lambda _: None, log=lambda _: None)
        self.assertEqual((ok, frame, same), (True, 'frame', camera))
        cv2.VideoCapture.assert_not_called()

    def test_persistent_failure_reopens_and_requires_warm_frames(self):
        from camera_recovery import recover_read
        old = self.Camera([])
        fresh = self.Camera([(True, 1), (False, None), (True, 2), (True, 3), (True, 4)])
        cv2 = SimpleNamespace(VideoCapture=lambda index: fresh)
        ok, frame, camera = recover_read(cv2, old, 0, sleep=lambda _: None, log=lambda _: None)
        self.assertTrue(old.released)
        self.assertEqual((ok, frame, camera), (True, 4, fresh))


if __name__ == '__main__':
    unittest.main()
