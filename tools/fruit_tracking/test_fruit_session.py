import json
from pathlib import Path
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

from fruit_actions import FruitSession
from fruit_bridge import BRIDGE, HandSession, ZoneSession, patched_source


class FaceSessionTests(unittest.TestCase):
    def args(self, **extra):
        return SimpleNamespace(fruit_actions=extra.get('available', True), fruit_port=5058,
                               face_model=Path('/missing/face.task'))

    def setUp(self):
        self.sender = Mock()
        self.face = Mock()
        self.face.detect_for_video.return_value = SimpleNamespace(face_landmarks=[], face_blendshapes=[])
        self.factory = Mock(return_value=self.face)

    def packet(self):
        return json.loads(self.sender.sendto.call_args.args[0])

    def test_off_never_initializes_model_and_reports_disabled(self):
        with FruitSession(self.args(), self.factory) as session:
            session.process(object(), 10, self.sender, 1)
            self.factory.assert_not_called()
            self.assertFalse(self.packet()['enabled'])
            self.assertFalse(self.packet()['tracked'])

    def test_on_off_reuses_frame_and_releases_face(self):
        image = object()
        with FruitSession(self.args(), self.factory) as session:
            session.command(dict(version=1, fruitEnabled=True))
            self.factory.assert_not_called()
            session.process(image, 10, self.sender, 1)
            self.face.detect_for_video.assert_called_once_with(image, 10)
            session.command(dict(version=1, fruitEnabled=True))
            session.process(image, 11, self.sender, 1)
            self.factory.assert_called_once()
            session.command(dict(version=1, fruitEnabled=False))
            self.face.close.assert_called_once()
            session.process(image, 12, self.sender, 1)
            self.assertFalse(self.packet()['enabled'])
            self.assertFalse(self.packet()['calibrated'])

    def test_unavailable_capability_and_strict_commands(self):
        for available in (True, False):
            session = FruitSession(self.args(available=available), self.factory)
            for bad in (None, [], dict(version=True, fruitEnabled=True),
                        dict(version=2, fruitEnabled=True), dict(version=1, fruitEnabled=1),
                        dict(version=1, fruitEnabled='true')):
                session.command(bad)
                self.assertFalse(session.enabled)
            if not available:
                session.command(dict(version=1, fruitEnabled=True))
                session.process(object(), 1, self.sender, 1)
                self.assertFalse(session.enabled)
                self.sender.assert_not_called()

    def test_missing_or_corrupt_model_does_not_propagate(self):
        import tempfile
        with tempfile.TemporaryDirectory() as directory:
            corrupt = Path(directory) / 'face.task'
            corrupt.write_bytes(b'invalid model')
            for path in (Path(directory) / 'missing.task', corrupt):
                args = self.args()
                args.face_model = path
                session = FruitSession(args)
                session.command(dict(version=1, fruitEnabled=True))
                session.process(object(), 1, self.sender, 1)
                self.assertTrue(self.packet()['enabled'])
                self.assertFalse(self.packet()['tracked'])
                self.assertIn('Face model', self.packet()['error'])
                self.assertIsNone(session.face)

    def test_inference_failure_latches_until_toggle_and_body_can_continue(self):
        self.face.detect_for_video.side_effect = ValueError('inference failed')
        session = FruitSession(self.args(), self.factory)
        session.command(dict(version=1, fruitEnabled=True))
        session.process(object(), 1, self.sender, 1)
        session.process(object(), 2, self.sender, 1)
        self.factory.assert_called_once()
        self.assertIn('inference failed', self.packet()['error'])
        session.command(dict(version=1, fruitEnabled=False))
        session.command(dict(version=1, fruitEnabled=True))
        session.process(object(), 3, self.sender, 1)
        self.assertEqual(self.factory.call_count, 2)

    def test_single_person_requirement_does_not_initialize(self):
        session = FruitSession(self.args(), self.factory)
        session.command(dict(version=1, fruitEnabled=True))
        session.process(object(), 1, self.sender, 3)
        self.factory.assert_not_called()
        self.assertIn('1 person', self.packet()['error'])
        session.process(object(), 2, self.sender, 1)
        self.factory.assert_called_once()
        self.assertEqual(self.packet()['error'], '')

    @staticmethod
    def pose(nose_x):
        """Pose landmarks for a performer whose nose is at nose_x; shoulders 0.2 apart."""
        point = lambda x, y: SimpleNamespace(x=x, y=y, visibility=1., presence=1.)
        landmarks = [point(nose_x, .5) for _ in range(33)]
        landmarks[0] = point(nose_x, .3)
        landmarks[11], landmarks[12] = point(nose_x + .1, .5), point(nose_x - .1, .5)
        return landmarks

    @staticmethod
    def faces(*items):
        """items: (nose_x, jaw) per detected face, in detector order."""
        point = lambda x, y: SimpleNamespace(x=x, y=y)
        landmarks, shapes = [], []
        for nose_x, jaw in items:
            mesh = [point(nose_x, .3) for _ in range(400)]
            landmarks.append(mesh)
            shapes.append([SimpleNamespace(category_name=n, score=v) for n, v in
                           (('jawOpen', jaw), ('eyeBlinkLeft', .05), ('eyeBlinkRight', .05))])
        return SimpleNamespace(face_landmarks=landmarks, face_blendshapes=shapes)

    def test_claim_reports_each_persons_mouth_by_slot(self):
        slots = {1: self.pose(.2), 2: self.pose(.5), 3: self.pose(.8)}
        self.face.detect_for_video.return_value = self.faces((.8, .1), (.5, .9))  # order differs from slots
        session = FruitSession(self.args(), self.factory)
        session.command(dict(version=1, fruitEnabled=True, claimFace=True))
        session.process(object(), 1, self.sender, 3, slots)
        packet = self.packet()
        self.assertEqual(packet['error'], '')
        self.assertTrue(packet['claiming'])
        self.assertEqual(packet['slotJaw'], [dict(slot=2, jaw=.9), dict(slot=3, jaw=.1)])

    def test_claimed_face_slot_works_with_other_people_in_frame(self):
        slots = {1: self.pose(.2), 2: self.pose(.5)}
        session = FruitSession(self.args(), self.factory)
        session.command(dict(version=1, fruitEnabled=True, faceSlot=2))
        # Only the face at slot 2's head is used; a face at nobody's head is ignored.
        self.face.detect_for_video.return_value = self.faces((.95, .0), (.5, .0))
        for i in range(25):
            session.process(object(), i, self.sender, 2, slots)
        self.assertTrue(self.packet()['tracked'])
        self.assertEqual(self.packet()['faceSlot'], 2)
        # The Face person leaves the frame: untracked, even though another face is visible.
        self.face.detect_for_video.return_value = self.faces((.95, .0))
        session.process(object(), 30, self.sender, 2, {1: self.pose(.2)})
        self.assertFalse(self.packet()['tracked'])

    def test_face_kept_through_brief_identity_dropout(self):
        slots = {1: self.pose(.2), 2: self.pose(.5)}
        session = FruitSession(self.args(), self.factory)
        session.command(dict(version=1, fruitEnabled=True, faceSlot=2))
        self.face.detect_for_video.return_value = self.faces((.2, .0), (.5, .0))
        for i in range(25):
            session.process(object(), i, self.sender, 2, slots)
        # Ambiguous pose frame: no slots at all, but the same face is still at the last nose.
        session.process(object(), 26, self.sender, 2, {})
        self.assertTrue(self.packet()['tracked'])
        session.face_nose_time -= 1  # beyond the grace period
        session.process(object(), 27, self.sender, 2, {})
        self.assertFalse(self.packet()['tracked'])

    def test_face_slot_command_is_strict_and_resets_calibration(self):
        session = FruitSession(self.args(), self.factory)
        for bad in (True, 5, -1, '2', 2.0):
            session.command(dict(version=1, faceSlot=bad))
            self.assertIsNone(session.face_slot)
        session.detector.baseline = (0, 1, 1)
        session.command(dict(version=1, faceSlot=2))
        self.assertEqual(session.face_slot, 2)
        self.assertIsNone(session.detector.baseline)
        session.command(dict(version=1, faceSlot=0))
        self.assertIsNone(session.face_slot)

    def test_face_not_near_any_head_is_not_assigned(self):
        from fruit_actions import match_faces
        slots = {1: self.pose(.2), 2: self.pose(.6)}
        faces = self.faces((.2, 0), (.4, 0)).face_landmarks
        self.assertEqual(match_faces(faces, slots), {1: 0})

    def test_shared_camera_hook_continues_pose_on_face_failure(self):
        """Run the real wrapper loop with fake I/O: missing face leaves body intact."""
        rgb = object()
        frame = SimpleNamespace(shape=(240, 320, 3))
        camera = Mock()
        camera.isOpened.return_value = True
        camera.read.side_effect = [(True, frame), (False, None)]
        sender = Mock()
        sender.recvfrom.side_effect = [(b'{"version":1,"fruitEnabled":true}', ('127.0.0.1', 5058)),
                                      BlockingIOError(), BlockingIOError()]
        pose = Mock()
        pose.detect_for_video.return_value = SimpleNamespace(pose_landmarks=[])
        pose.__enter__ = Mock(return_value=pose)
        pose.__exit__ = Mock(return_value=False)
        vision = SimpleNamespace(PoseLandmarkerOptions=lambda **kw: kw,
                                 RunningMode=SimpleNamespace(VIDEO=1),
                                 PoseLandmarker=SimpleNamespace(create_from_options=lambda options: pose))
        py = SimpleNamespace(BaseOptions=lambda **kw: kw)
        mp = SimpleNamespace(Image=lambda **kw: kw['data'], ImageFormat=SimpleNamespace(SRGB=1))
        cv = Mock()
        cv.VideoCapture.return_value = camera
        cv.cvtColor.return_value = rgb
        # Camera recovery has its own tests; here a failed read must still end the loop.
        scope = dict(__name__='test_hook', __file__=str(BRIDGE), FruitSession=FruitSession, HandSession=HandSession, ZoneSession=ZoneSession,
                     recover_read=lambda cv2, camera, index: (False, None, camera))
        exec(compile(patched_source(), str(BRIDGE), 'exec'), scope)
        scope['encode_preview'] = lambda *args: None
        modules = {'cv2': cv, 'mediapipe': mp, 'mediapipe.tasks': SimpleNamespace(python=py),
                   'mediapipe.tasks.python': SimpleNamespace(vision=vision)}
        args = ['fruit_bridge.py', '--no-window', '--people', '1', '--fruit-actions',
                '--face-model', '/missing/face.task']
        with patch.dict('sys.modules', modules), patch('sys.argv', args), patch('socket.socket', return_value=sender):
            with self.assertRaisesRegex(RuntimeError, 'Camera stopped'):
                scope['main']()
        cv.VideoCapture.assert_called_once_with(0)
        pose.detect_for_video.assert_called_once()
        self.assertIs(pose.detect_for_video.call_args.args[0], rgb)
        packets = [(json.loads(call.args[0]), call.args[1]) for call in sender.sendto.call_args_list]
        face_packet = next(packet for packet, port in packets if port[1] == 5058)
        body_packet = next(packet for packet, port in packets if port[1] == 5055 and 'performerCount' in packet)
        self.assertIn('Face model missing', face_packet['error'])
        self.assertEqual(body_packet, dict(version=1, performerCount=1, upperBodyOnly=False, people=[]))
        camera.release.assert_called_once()


if __name__ == '__main__':
    unittest.main()
