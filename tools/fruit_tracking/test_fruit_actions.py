import json
import unittest
from fruit_actions import ActionDetector
from fruit_bridge import patched_source


def scores(jaw=0.05, left=0.05, right=0.05):
    return dict(jawOpen=jaw, eyeBlinkLeft=left, eyeBlinkRight=right)


class FaceActionsTests(unittest.TestCase):
    def setUp(self):
        self.detector = ActionDetector()
        for i in range(22):
            self.detector.update(scores(), i * .1)
        self.assertIsNotNone(self.detector.baseline)

    def hold(self, face, start, duration=.4):
        result = None
        for i in range(round(duration / .05) + 1):
            result = self.detector.update(face, start + i * .05)
        return result

    def test_mouth_dwell_one_event_until_release(self):
        self.assertEqual(self.detector.update(scores(jaw=.8), 2.2)['actionId'], 0)
        packet = self.hold(scores(jaw=.8), 2.25)
        self.assertEqual((packet['action'], packet['actionId']), ('rearUp', 1))
        self.assertEqual(self.hold(scores(jaw=.8), 2.7, 1)['actionId'], 1)
        self.detector.update(scores(), 3.75)
        self.assertEqual(self.hold(scores(jaw=.8), 3.8)['actionId'], 2)

    def test_anatomical_right_apple_left_banana(self):
        packet = self.hold(scores(right=.9), 2.2)
        self.assertEqual(packet['action'], 'apple')
        self.hold(scores(), 2.65, .5)
        packet = self.hold(scores(left=.9), 3.2)
        self.assertEqual((packet['action'], packet['actionId']), ('banana', 2))

    def test_both_eyes_ignored(self):
        self.assertEqual(self.hold(scores(left=.9, right=.9), 2.2)['actionId'], 0)

    def test_wink_must_reopen_before_next_wink(self):
        self.assertEqual(self.hold(scores(right=.9), 2.2)['action'], 'apple')
        # Holding the same wink past the cooldown does not fire again.
        self.assertEqual(self.hold(scores(right=.9), 2.65, 1)['actionId'], 1)

    def test_flicker_of_both_eyes_inside_wink_still_counts(self):
        # Performer data: wink eye closes, other eye flickers shut briefly, wink continues.
        self.detector.update(scores(right=.9), 2.2)
        for i in range(3):
            self.detector.update(scores(left=.9, right=.9), 2.25 + i * .05)
        packet = self.detector.update(scores(right=.9, left=.3), 2.4)
        self.assertEqual((packet['action'], packet['actionId']), ('apple', 1))

    def test_eyelid_openness_preferred_over_blink_scores(self):
        detector = ActionDetector()
        for i in range(22):
            detector.update(dict(scores(left=.2, right=.2), eyeOpenLeft=.62, eyeOpenRight=.6), i * .1)
        # Blendshapes say both eyes equally closed; eyelids show a clear left wink.
        for i in range(4):
            packet = detector.update(dict(scores(left=.75, right=.75), eyeOpenLeft=.01, eyeOpenRight=.4), 2.2 + i * .05)
        self.assertEqual((packet['action'], packet['actionId']), ('banana', 1))
        self.assertGreater(packet['eyeBlinkLeft'], .9)

    def test_normal_blink_with_short_asymmetry(self):
        self.detector.update(scores(right=.9), 2.2)
        self.detector.update(scores(right=.9, left=.9), 2.25)
        self.hold(scores(), 2.3)
        self.assertEqual(self.detector.action_id, 0)

    def test_wink_with_open_mouth_and_squinting_other_eye(self):
        self.hold(scores(jaw=.8), 2.2)
        # Mouth open squints both eyes; that alone must not pick.
        self.hold(scores(jaw=.8, left=.4, right=.4), 3.1)
        self.assertEqual(self.detector.action_id, 1)
        packet = self.hold(scores(jaw=.8, left=.45, right=.9), 3.6)
        self.assertEqual((packet['action'], packet['actionId']), ('apple', 2))
        self.hold(scores(jaw=.8, left=.4, right=.4), 4.1, .5)
        packet = self.hold(scores(jaw=.8, left=.9, right=.45), 4.7)
        self.assertEqual((packet['action'], packet['actionId']), ('banana', 3))

    def test_tracking_loss_requires_fresh_calibration(self):
        packet = self.detector.update(None, 3)
        self.assertFalse(packet['tracked'])
        self.assertFalse(packet['calibrated'])
        packet = self.detector.update(scores(jaw=.8), 3.1)
        self.assertFalse(packet['calibrated'])
        self.assertEqual(packet['actionId'], 0)

    def test_invalid_scores_never_emit_nan(self):
        for bad in (float('nan'), float('inf'), -1, 2, True):
            packet = self.detector.update(scores(jaw=bad), 2.2)
            self.assertFalse(packet['tracked'])
            json.dumps(packet, allow_nan=False)

    def test_calibration_interruption_and_held_action(self):
        detector = ActionDetector()
        for i in range(30):
            detector.update(scores(jaw=.8), i * .1)
        self.assertIsNone(detector.baseline)
        for i in range(10):
            detector.update(scores(), 3 + i * .1)
        detector.update(None, 4)
        detector.update(scores(), 4.1)
        self.assertEqual(len(detector.samples), 1)

    def test_source_hooks_compile_without_camera(self):
        compile(patched_source(), 'fruit_bridge', 'exec')


if __name__ == '__main__':
    unittest.main()
