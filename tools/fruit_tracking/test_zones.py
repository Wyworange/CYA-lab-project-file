import sys
import unittest
from types import SimpleNamespace
from unittest import mock

import numpy as np

from fruit_bridge import patched_source
from zones import ZoneSession, zone_bounds


def pose(x):
    """33 landmarks at crop-relative x (0..1), fully visible."""
    return [SimpleNamespace(x=x, y=.5, z=0., visibility=1., presence=1.) for _ in range(33)]


class FakeDetector:
    def __init__(self, result):
        self.result, self.images, self.closed = result, [], False

    def detect_for_video(self, image, timestamp):
        self.images.append(image)
        return self.result

    def close(self):
        self.closed = True


class ZoneTests(unittest.TestCase):
    def setUp(self):
        mp = SimpleNamespace(Image=lambda **kw: kw['data'], ImageFormat=SimpleNamespace(SRGB=1))
        self.modules = mock.patch.dict(sys.modules, {'mediapipe': mp})
        self.modules.start()
        self.addCleanup(self.modules.stop)

    def session(self, results):
        detectors = iter([FakeDetector(r) for r in results])
        session = ZoneSession(SimpleNamespace(model='pose.task'), factory=lambda: next(detectors))
        session.command({'version': 1, 'zones': len(results)})
        return session

    def test_strips_tile_the_frame_exactly(self):
        self.assertEqual(zone_bounds(640, 3), [(0, 213), (213, 426), (426, 640)])

    def test_each_strip_maps_back_and_owns_its_slot(self):
        # Left strip empty, middle and right each see one person at their own crop centre.
        session = self.session([SimpleNamespace(pose_landmarks=[]),
                                SimpleNamespace(pose_landmarks=[pose(.5)]),
                                SimpleNamespace(pose_landmarks=[pose(.5)])])
        rgb = np.zeros((480, 600, 3), np.uint8)
        result = session.detect(rgb, 7)
        self.assertEqual([round(p[0].x, 3) for p in result.pose_landmarks], [.5, .833])
        self.assertEqual([d.images[0].shape[1] for d in session.detectors], [200, 200, 200])
        observations = [(None, None, None, landmarks) for landmarks in result.pose_landmarks]
        self.assertEqual(session.assign(observations), {2: 0, 3: 1})

    def test_off_falls_back_to_stock_tracker(self):
        session = ZoneSession(SimpleNamespace(model='pose.task'), factory=mock.Mock())
        self.assertIsNone(session.detect(np.zeros((4, 4, 3), np.uint8), 1))
        for bad in (True, 5, -1, '3', 3.0):
            session.command({'version': 1, 'zones': bad})
            self.assertFalse(session.active)

    def test_failure_disables_zones_only(self):
        session = ZoneSession(SimpleNamespace(model='pose.task'), factory=mock.Mock(side_effect=RuntimeError('no model')))
        session.command({'version': 1, 'zones': 3})
        self.assertIsNone(session.detect(np.zeros((4, 9, 3), np.uint8), 1))
        self.assertFalse(session.active)
        self.assertIn('no model', session.error)

    def test_failure_retries_after_backoff(self):
        session = ZoneSession(SimpleNamespace(model='pose.task'), factory=mock.Mock(side_effect=RuntimeError('glitch')))
        session.command({'version': 1, 'zones': 2})
        session.detect(np.zeros((4, 8, 3), np.uint8), 1)
        self.assertFalse(session.active)
        session.error_time -= 3  # backoff elapsed
        self.assertTrue(session.active)

    def test_body_centred_outside_strip_is_ignored(self):
        # Strip 1 sees a neighbour whose shoulders are past its right edge; strip 2 is centred.
        leaning = pose(.5)
        leaning[11] = SimpleNamespace(x=1.1, y=.5, z=0., visibility=1., presence=1.)
        leaning[12] = SimpleNamespace(x=.95, y=.5, z=0., visibility=1., presence=1.)
        session = self.session([SimpleNamespace(pose_landmarks=[leaning]), SimpleNamespace(pose_landmarks=[pose(.5)])])
        result = session.detect(np.zeros((10, 200, 3), np.uint8), 1)
        self.assertEqual(len(result.pose_landmarks), 1)
        observations = [(None, None, None, landmarks) for landmarks in result.pose_landmarks]
        self.assertEqual(session.assign(observations), {2: 0})

    def test_z_scaled_like_x(self):
        person = pose(.5)
        person[0] = SimpleNamespace(x=.5, y=.3, z=-.4, visibility=1., presence=1.)
        session = self.session([SimpleNamespace(pose_landmarks=[person]), SimpleNamespace(pose_landmarks=[])])
        result = session.detect(np.zeros((10, 200, 3), np.uint8), 1)
        self.assertAlmostEqual(result.pose_landmarks[0][0].z, -.2)

    def test_changing_zone_count_closes_detectors(self):
        session = self.session([SimpleNamespace(pose_landmarks=[])] * 2)
        session.detect(np.zeros((4, 8, 3), np.uint8), 1)
        old = list(session.detectors)
        session.command({'version': 1, 'zones': 3})
        self.assertTrue(all(d.closed for d in old))
        self.assertEqual(session.detectors, [])

    def test_draws_strict_boundaries_and_labels(self):
        session = self.session([SimpleNamespace(pose_landmarks=[])] * 3)
        cv2 = mock.Mock()
        session.draw(np.zeros((240, 300, 3), np.uint8), cv2, {1: 0})
        boundaries = sorted({call.args[1][0] for call in cv2.line.call_args_list})
        self.assertEqual(boundaries, [100, 200])
        labels = [call.args[1] for call in cv2.putText.call_args_list]
        self.assertIn('P1', labels)
        self.assertIn('P2 empty', labels)

    def test_bridge_hooks_installed(self):
        source = patched_source()
        self.assertIn('zones.detect(rgb, timestamp) or detector.detect_for_video', source)
        self.assertIn('zones.assign(observations) if zones.active else tracker.assign', source)
        self.assertIn('zones.draw(frame, cv2, assignments)', source)
        self.assertIn('zones.command(command)', source)


if __name__ == '__main__':
    unittest.main()
