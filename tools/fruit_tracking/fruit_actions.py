"""Optional single-performer face actions; no camera or ML imports at module load."""
import math


# Face-mesh eyelid points (subject's own right/left eye): top, bottom, two corners.
RIGHT_EYE = (159, 145, 33, 133)
LEFT_EYE = (386, 374, 362, 263)


def eye_openness(landmarks):
    """Eyelid gap over eye width per eye; None when the mesh is incomplete."""
    if len(landmarks) <= max(RIGHT_EYE + LEFT_EYE):
        return None

    def openness(top, bottom, a, b):
        width = math.hypot(landmarks[a].x - landmarks[b].x, landmarks[a].y - landmarks[b].y)
        return abs(landmarks[top].y - landmarks[bottom].y) / width if width > 1e-6 else None

    left, right = openness(*LEFT_EYE), openness(*RIGHT_EYE)
    return None if left is None or right is None else (left, right)


class ActionDetector:
    """Neutral calibration + dwell; anatomical eye names from MediaPipe.

    Winks are judged from eyelid openness relative to the neutral face when the mesh is
    available (blendshape blink scores rise in both eyes for many performers and with
    an open mouth); otherwise openness falls back to 1 - blink score.
    """
    WINK_CLOSED, WINK_OTHER_OPEN, REARM_OPEN = .4, .45, .6
    WINK_WINDOW, WINK_TIME, WINK_FRAMES = .6, .06, 2

    def __init__(self):
        self.action_id = 0
        self.last_action = ''
        self.reset()

    def reset(self):
        self.baseline = None
        self.samples = []
        self.started = None
        self.last_seen = None
        self.pending = None
        self.pending_since = 0
        self.mouth_open = False
        self.mouth_armed = True
        self.wink_armed = True
        self.wink_frames = []
        self.cooldown_until = 0
        self.closure = None

    @staticmethod
    def _openness(scores):
        if type(scores.get('eyeOpenLeft')) is float and type(scores.get('eyeOpenRight')) is float:
            return scores['eyeOpenLeft'], scores['eyeOpenRight']
        return 1 - scores['eyeBlinkLeft'], 1 - scores['eyeBlinkRight']

    def update(self, scores, now):
        valid = (isinstance(scores, dict) and all(
            type(scores.get(k)) in (int, float) and math.isfinite(scores[k])
            and 0 <= scores[k] <= 1 for k in ('jawOpen', 'eyeBlinkLeft', 'eyeBlinkRight'))
            and all(k not in scores or (type(scores[k]) is float and math.isfinite(scores[k]) and 0 <= scores[k] < 5)
                    for k in ('eyeOpenLeft', 'eyeOpenRight')))
        if not valid:
            self.pending = None
            self.closure = None
            # An interruption cancels a partially collected neutral sample window.
            if self.baseline is None:
                self.samples = []
                self.started = None
            if self.last_seen is not None and now - self.last_seen > .5:
                self.reset()
            return self.packet(False, scores=None)
        if self.last_seen is not None and now - self.last_seen > .5:
            self.reset()
        previous = self.last_seen
        self.last_seen = now
        jaw, left, right = (scores[k] for k in ('jawOpen', 'eyeBlinkLeft', 'eyeBlinkRight'))
        open_left, open_right = self._openness(scores)
        if self.baseline is None:
            # Calibration accepts only plausibly neutral face, never a held wink/open mouth.
            if jaw > .35 or left > .35 or right > .35 or open_left <= 0 or open_right <= 0:
                self.started = None
                self.samples = []
            else:
                if self.started is None:
                    self.started = now
                self.samples.append((jaw, open_left, open_right))
                if now - self.started >= 2 and len(self.samples) >= 10:
                    self.baseline = tuple(sum(s[i] for s in self.samples) / len(self.samples) for i in range(3))
                    self.samples = []
            return self.packet(True, scores)
        jaw_on = max(.4, self.baseline[0] + .25)
        jaw_off = max(.25, self.baseline[0] + .12)
        if jaw < jaw_off:
            self.mouth_open = False
            self.mouth_armed = True
        # Openness relative to the neutral face: 1 = as open as neutral, 0 = shut.
        rel_left, rel_right = open_left / self.baseline[1], open_right / self.baseline[2]
        self.closure = (min(1., max(0., 1 - rel_left)), min(1., max(0., 1 - rel_right)))
        if rel_left > self.REARM_OPEN and rel_right > self.REARM_OPEN:
            self.wink_armed = True
        side = None
        if rel_right < self.WINK_CLOSED and rel_left >= self.WINK_OTHER_OPEN:
            side = 'apple'
        elif rel_left < self.WINK_CLOSED and rel_right >= self.WINK_OTHER_OPEN:
            side = 'banana'
        # Count one-eye frames over a short window: a wink often contains a brief flicker of
        # both eyes, which must not cancel it, while a normal blink shows at most one
        # asymmetric edge frame.
        step = min(.1, now - previous) if previous is not None else 0
        self.wink_frames = [f for f in self.wink_frames if now - f[0] <= self.WINK_WINDOW]
        if side:
            self.wink_frames.append((now, side, step))
        if side and self.wink_armed and now >= self.cooldown_until:
            mine = [f for f in self.wink_frames if f[1] == side]
            other = sum(f[2] for f in self.wink_frames if f[1] != side)
            if len(mine) >= self.WINK_FRAMES and sum(f[2] for f in mine) >= self.WINK_TIME and other < .04:
                self.fire(side, now)
                self.wink_armed = False
                self.wink_frames = []
                self.pending = None
                return self.packet(True, scores)
        candidate = 'rearUp' if self.mouth_armed and jaw >= jaw_on else None
        if candidate != self.pending:
            self.pending = candidate
            self.pending_since = now
        if candidate and now >= self.cooldown_until and now - self.pending_since >= .25:
            self.fire(candidate, now)
            self.pending = None
            self.mouth_open = True
            self.mouth_armed = False
        return self.packet(True, scores)

    def fire(self, action, now):
        self.action_id += 1
        self.last_action = action
        self.cooldown_until = now + .8

    def packet(self, tracked, scores):
        result = dict(version=1, kind='fruitActions', tracked=tracked,
                      calibrated=self.baseline is not None,
                      calibrating=self.baseline is None and self.started is not None,
                      mouthOpen=self.mouth_open if tracked else False,
                      action=self.last_action, actionId=self.action_id)
        if scores:
            result.update(jawOpen=scores['jawOpen'], eyeBlinkLeft=scores['eyeBlinkLeft'], eyeBlinkRight=scores['eyeBlinkRight'])
            # Once calibrated, report eyelid closure (what the wink decision uses) to Unity.
            if self.closure:
                result.update(eyeBlinkLeft=self.closure[0], eyeBlinkRight=self.closure[1])
        else:
            result.update(jawOpen=0., eyeBlinkLeft=0., eyeBlinkRight=0.)
        return result


def match_faces(faces, slots):
    """{slot: face index}: each face goes to the nearest pose nose, within ~0.6 shoulder widths.

    Face-mesh landmark 1 is the nose tip; pose landmarks 0/11/12 are nose and shoulders, all in
    the same normalised image coordinates. Each face and each slot is used at most once.
    """
    pairs = []
    for index, face in enumerate(faces):
        if len(face) < 2:
            continue
        nose = face[1]
        for slot, pose in slots.items():
            width = max(math.hypot(pose[11].x - pose[12].x, pose[11].y - pose[12].y), .03)
            distance = math.hypot(nose.x - pose[0].x, nose.y - pose[0].y)
            if distance < .6 * width:
                pairs.append((distance, slot, index))
    result, used = {}, set()
    for _, slot, index in sorted(pairs):
        if slot not in result and index not in used:
            result[slot] = index
            used.add(index)
    return result


def face_scores(result, index):
    scores = {c.category_name: c.score for c in result.face_blendshapes[index]
              if c.category_name in ('jawOpen', 'eyeBlinkLeft', 'eyeBlinkRight')}
    eyes = eye_openness(result.face_landmarks[index])
    if eyes:
        scores.update(eyeOpenLeft=float(eyes[0]), eyeOpenRight=float(eyes[1]))
    return scores


def nearest_face(faces, nose, taken, limit=.08):
    """Index of the unclaimed face whose nose tip is closest to nose, within limit."""
    best = None
    for index, face in enumerate(faces):
        if index in taken or len(face) < 2:
            continue
        distance = math.hypot(face[1].x - nose[0], face[1].y - nose[1])
        if distance < limit and (best is None or distance < best[0]):
            best = (distance, index)
    return best and best[1]


class FruitSession:
    """Face actions. Without roles, only a lone performer may drive them (original rule).

    With roles (Unity's gesture-claim flow), the face belonging to the claimed Face slot is used
    even when other people are in frame; during claiming every person's mouth opening is reported.
    """
    FACE_GRACE = .3

    def __init__(self, args, factory=None):
        self.args = args
        self.detector = ActionDetector()
        self.face = None
        self.sequence = 0
        self.enabled = False
        self.error = ''
        self.factory = factory or self.create_face
        self.face_slot = None
        self.claiming = False
        # Last nose of the Face person: bridges brief identity/pose dropouts (ambiguous frames).
        self.face_nose = None
        self.face_nose_time = -1000.

    def __enter__(self):
        # Camera/Pose work immediately; the face task is strictly lazy and initially off.
        return self

    def __exit__(self, *exc):
        self.close_face()

    def close_face(self):
        if self.face:
            try:
                self.face.close()
            except Exception:
                pass
            self.face = None

    def create_face(self):
        import hashlib
        from setup_face_model import SHA256
        model = self.args.face_model
        if not model.is_file():
            raise RuntimeError('Face model missing: run tools/fruit_tracking/setup_face_model.py.')
        if hashlib.sha256(model.read_bytes()).hexdigest() != SHA256:
            raise RuntimeError('Face model checksum mismatch; restore the verified face model.')
        from mediapipe.tasks import python
        from mediapipe.tasks.python import vision
        return vision.FaceLandmarker.create_from_options(vision.FaceLandmarkerOptions(
            base_options=python.BaseOptions(model_asset_path=str(model)),
            running_mode=vision.RunningMode.VIDEO, num_faces=4,
            output_face_blendshapes=True,
            min_face_detection_confidence=.6, min_face_presence_confidence=.6,
            min_tracking_confidence=.6))

    def command(self, command):
        """The caller validates localhost sender port; strict JSON value types here."""
        if not self.args.fruit_actions or not isinstance(command, dict):
            return
        if type(command.get('version')) is not int or command['version'] != 1:
            return
        enabled = command.get('fruitEnabled')
        if type(enabled) is bool and enabled != self.enabled:
            self.enabled = enabled
            self.close_face()
            self.error = ''
            self.detector.reset()
        if command.get('fruitCalibrate') is True and self.enabled:
            self.detector.reset()
        slot = command.get('faceSlot')
        if type(slot) is int and 0 <= slot <= 4:
            slot = slot or None
            if slot != self.face_slot:
                # A different person needs their own neutral face.
                self.face_slot = slot
                self.face_nose = None
                self.detector.reset()
        claiming = command.get('claimFace')
        if type(claiming) is bool:
            self.claiming = claiming

    def process(self, image, timestamp, sender, count, slots=None):
        """slots: {slot: 33 pose landmarks} of the assigned performers (None from old callers)."""
        if not self.args.fruit_actions:
            return
        import json
        import time
        scores = None
        slot_jaw = []
        status_error = self.error
        roles = self.face_slot is not None or self.claiming
        if self.enabled and count != 1 and not roles:
            self.detector.reset()
            status_error = 'Facial control requires 1 person.'
        elif self.enabled and not self.error:
            try:
                if self.face is None:
                    self.face = self.factory()
                result = self.face.detect_for_video(image, timestamp)
                faces = len(result.face_landmarks) if len(result.face_landmarks) == len(result.face_blendshapes) else 0
                if roles:
                    owners = match_faces(result.face_landmarks[:faces], slots or {})
                    if self.claiming:
                        for slot, index in sorted(owners.items()):
                            jaw = face_scores(result, index).get('jawOpen')
                            if jaw is not None:
                                slot_jaw.append(dict(slot=slot, jaw=float(jaw)))
                    else:
                        index = owners.get(self.face_slot)
                        now = time.monotonic()
                        if index is None and self.face_nose and now - self.face_nose_time <= self.FACE_GRACE:
                            index = nearest_face(result.face_landmarks[:faces], self.face_nose, set(owners.values()))
                        if index is not None:
                            nose = result.face_landmarks[index][1]
                            self.face_nose, self.face_nose_time = (nose.x, nose.y), now
                            scores = face_scores(result, index)
                # Never give an arbitrary face ownership with another person present.
                elif faces == 1:
                    scores = face_scores(result, 0)
            except Exception as error:
                # Fail the optional task, not the body task or camera/preview stream.
                self.error = 'Facial tracking unavailable: ' + str(error)
                status_error = self.error
                self.close_face()
                self.detector.reset()
        packet = self.detector.update(scores, time.monotonic())
        self.sequence += 1
        packet['sequence'] = self.sequence
        packet['enabled'] = self.enabled
        packet['error'] = status_error
        packet['faceSlot'] = self.face_slot or 0
        packet['claiming'] = self.claiming
        packet['slotJaw'] = slot_jaw
        try:
            sender.sendto(json.dumps(packet, allow_nan=False).encode(), ('127.0.0.1', self.args.fruit_port))
        except OSError:
            # Optional face status transport must not interrupt body/preview packets.
            pass
