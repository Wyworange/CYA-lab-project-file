"""Aurora's hand-grab gestures on the shared camera frame; no camera or ML imports at load.

Ported from Aurora's bridge (AuroraJ03/Aurora-DigiPhant-HandGrab, Tracking/bridge.py):
hand_state and hand_packets are her association rules, unchanged. They run in the shared
tracking process through tools/fruit_tracking/fruit_bridge.py, so the pinned starter
bridge stays byte-identical and the camera is opened once for body, face and hands.
"""
import json
import math
import time
import uuid
from pathlib import Path

MODEL_URL = 'https://storage.googleapis.com/mediapipe-models/gesture_recognizer/gesture_recognizer/float16/latest/gesture_recognizer.task'
MODEL_SHA256 = '97952348cf6a6a4915c2ea1496b4b37ebabc50cbbf80571435643c455f2b0482'
MODEL = Path(__file__).resolve().parents[2] / 'DigiPhantStarter/Tracking/gesture_recognizer.task'
# One line per second while Hands is on: loop rate, people, recognition latency, per-slot gestures.
DIAGNOSTICS = Path(__file__).resolve().parents[2] / 'Logs/hand_tracking_diagnostics.log'


class Diagnostics:
    def __init__(self, path=DIAGNOSTICS):
        self.path = path
        self.reset(time.monotonic())

    def reset(self, now):
        self.started, self.frames, self.people, self.assigned = now, 0, [], []
        self.latency, self.hands, self.expired, self.states = [], [], 0, {}
        self.errors, self.rebuilds = [], 0

    def frame(self, now, observations, assignments, packets):
        self.frames += 1
        self.people.append(len(observations))
        self.assigned.append(len(assignments))
        for packet in packets:
            for side in ('left', 'right'):
                key = f"P{packet['performerId']}{side[0].upper()}"
                hand = packet[side]
                self.states.setdefault(key, {}).setdefault(hand['state'], []).append(hand['confidence'])
        if now - self.started >= 1:
            self.write(now)

    def write(self, now):
        def avg(values):
            return f'{sum(values) / len(values):.2f}' if values else '-'
        gestures = ' '.join(f"{key}:" + ','.join(f"{state}x{len(scores)}@{avg(scores)}" for state, scores in sorted(states.items()))
                            for key, states in sorted(self.states.items()))
        line = (f"{time.strftime('%H:%M:%S')} fps={self.frames / (now - self.started):.1f} "
                f"people={avg(self.people)} assigned={avg(self.assigned)} "
                f"recog_ms={avg([x * 1000 for x in self.latency])}/max{max(self.latency, default=0) * 1000:.0f} "
                f"hands={avg(self.hands)} expired={self.expired}"
                + (f" ERR x{len(self.errors)} rebuilt={self.rebuilds} last={self.errors[-1]!r}" if self.errors else '')
                + f" | {gestures}\n")
        try:
            self.path.parent.mkdir(parents=True, exist_ok=True)
            with open(self.path, 'a') as out:
                out.write(line)
        except OSError:
            pass
        self.reset(now)


def hand_state(category):
    """Map only the deliberate interaction gestures; every other gesture is safe/unknown.

    'victory' (both hands ✌️) asks the elephant to sit on the log (integration addition).
    """
    if category == 'Victory':
        return 'victory'
    if category == 'Open_Palm':
        return 'open'
    if category == 'Closed_Fist':
        return 'fist'
    return 'unknown'


def hand_packets(result, observations, assignments, sequence, max_wrist_distance=.16):
    """Associate hand results with pose wrists, not image order or another person's hand.

    Pose landmarks 15/16 define anatomical left/right. This keeps the protocol correct
    for the unmirrored preview and rejects hands that cannot be assigned unambiguously.
    """
    packets = {slot: {'version': 1, 'performerId': slot, 'sequence': sequence,
                      'left': {'state': 'unknown', 'confidence': 0.0},
                      'right': {'state': 'unknown', 'confidence': 0.0}}
               for slot in assignments}
    if result is None:
        return list(packets.values())
    for hand_landmarks, gestures in zip(result.hand_landmarks, result.gestures):
        if not hand_landmarks or not gestures:
            continue
        wrist = hand_landmarks[0]
        candidates = []
        for slot, index in assignments.items():
            pose = observations[index][3]
            for side, landmark_index in (('left', 15), ('right', 16)):
                point = pose[landmark_index]
                quality = min(point.visibility, point.presence)
                if quality >= .5:
                    candidates.append((math.dist((wrist.x, wrist.y), (point.x, point.y)), slot, side))
        candidates.sort()
        if not candidates or candidates[0][0] > max_wrist_distance:
            continue
        # A close runner-up could mean a crossing body/hand; do not guess P1's hand.
        if len(candidates) > 1 and candidates[1][0] - candidates[0][0] < .025:
            continue
        _, slot, side = candidates[0]
        category = gestures[0]
        packets[slot][side] = {'state': hand_state(category.category_name),
                               'confidence': float(category.score)}
    return list(packets.values())


class HandSession:
    """Rate-limited gesture recognition on the tracking loop's own thread; rebuilt after repeated errors.

    It used to run in a worker thread beside the pose and face models. With fixed zones (three pose
    landmarkers plus the face landmarker on the main thread) the worker's recognize() hung for good
    in a live run (log: results stopped at 15:45:19, every frame "expired"), so hands went silent
    until restart. Running it inline at a lower rate on a downscaled frame avoids concurrent
    MediaPipe graphs; hand landmarks are normalised, so the downscale does not affect association.
    """
    RATE, MAX_AGE, MAX_WIDTH = 5, .40, 640

    def __init__(self, args, factory=None):
        self.args = args
        self.factory = factory or self.create_recognizer
        self.recognizer = None
        self.latest = None
        self.sequence = 0
        self.epoch = 0
        self.last_submit = 0.
        self.session = uuid.uuid4().hex
        self.error = ''
        # Off until Unity's "Hands" toggle sends handsEnabled; only then are hands recognised.
        self.active = False
        self.identity = None  # (performerCount, upperBodyOnly, zones) last seen from Unity
        self.diagnostics = None
        self.failures = 0
        self.last_rebuild = 0.

    @property
    def available(self):
        return bool(getattr(self.args, 'hand_actions', False)) and not self.error

    @property
    def enabled(self):
        return self.available and self.active

    def __enter__(self):
        return self

    def start(self):
        """Load the model eagerly, before the camera opens; failure disables hands only."""
        if self.available:
            try:
                self.ensure_recognizer()
            except Exception as error:
                self.fail(error)

    def ensure_recognizer(self):
        if self.recognizer is None:
            self.recognizer = self.factory()

    def fail(self, error):
        # Fail the optional hand task only; body tracking and the camera keep running.
        self.error = 'Hand gestures unavailable: ' + str(error)
        print(self.error)
        self.close()

    def __exit__(self, *exc):
        self.close()

    def create_recognizer(self):
        import hashlib
        model = getattr(self.args, 'gesture_model', None) or MODEL
        if not model.is_file():
            raise RuntimeError('Gesture model missing: run tools/hand_tracking/setup_gesture_model.py.')
        if hashlib.sha256(model.read_bytes()).hexdigest() != MODEL_SHA256:
            raise RuntimeError('Gesture model checksum mismatch; restore the verified model.')
        from mediapipe.tasks import python
        from mediapipe.tasks.python import vision
        return vision.GestureRecognizer.create_from_options(vision.GestureRecognizerOptions(
            base_options=python.BaseOptions(model_asset_path=str(model)),
            running_mode=vision.RunningMode.IMAGE,
            # Bystanders' hands must not use up the detections before pose association.
            num_hands=8, min_hand_detection_confidence=.5,
            min_hand_presence_confidence=.5, min_tracking_confidence=.5))

    def command(self, command):
        """Count, mode or identity changes make associations stale.

        Unity repeats performerCount/upperBodyOnly every 0.5 s; only an actual change (or an
        explicit reset) invalidates, otherwise every repeat would drop a hand frame.
        """
        if not isinstance(command, dict):
            return
        active = command.get('handsEnabled')
        if type(active) is bool and active != self.active:
            self.active = active
            self.latest = None
            self.epoch += 1
        identity = (command.get('performerCount', self.identity and self.identity[0]),
                    command.get('upperBodyOnly', self.identity and self.identity[1]),
                    command.get('zones', self.identity and self.identity[2]))
        changed = self.identity is not None and identity != self.identity
        if 'performerCount' in command or 'upperBodyOnly' in command or 'zones' in command:
            self.identity = identity
        if command.get('reset') is True or changed:
            self.latest = None
            self.epoch += 1

    def downscale(self, rgb):
        shape = getattr(rgb, 'shape', None)
        if not shape or shape[1] <= self.MAX_WIDTH:
            return rgb
        import cv2
        height = round(shape[0] * self.MAX_WIDTH / shape[1])
        return cv2.resize(rgb, (self.MAX_WIDTH, height), interpolation=cv2.INTER_AREA)

    def process(self, rgb, observations, assignments, sender):
        if not self.enabled:
            return
        try:
            self.ensure_recognizer()
            now = time.monotonic()
            if self.diagnostics is None:
                self.diagnostics = Diagnostics()
            if now - self.last_submit >= 1 / self.RATE:
                import mediapipe as mp
                self.last_submit = now
                self.sequence += 1
                try:
                    started = time.monotonic()
                    result = self.recognizer.recognize(mp.Image(image_format=mp.ImageFormat.SRGB, data=self.downscale(rgb)))
                    self.diagnostics.latency.append(time.monotonic() - started)
                    self.diagnostics.hands.append(len(result.hand_landmarks) if result is not None else 0)
                    self.latest = (result, list(observations), dict(assignments), self.sequence, self.epoch, now)
                    self.failures = 0
                except Exception as error:
                    # A MediaPipe graph that has failed keeps failing (live log: no results after
                    # 16:02:43, every frame expired). Rebuild the recognizer, at most once a second.
                    self.failures += 1
                    self.diagnostics.errors.append(str(error)[:160])
                    print(f'Hand gesture frame ignored: {error}')
                    if self.failures >= 2 and now - self.last_rebuild >= 1:
                        self.last_rebuild = now
                        self.failures = 0
                        self.diagnostics.rebuilds += 1
                        self.close()
                        self.ensure_recognizer()
            packets = []
            if self.latest is not None:
                result, hand_observations, hand_assignments, sequence, epoch, submitted = self.latest
                # Never relabel a newer pose frame with stale hand landmarks.
                if epoch == self.epoch and now - submitted <= self.MAX_AGE:
                    packets = hand_packets(result, hand_observations, hand_assignments, sequence)
                else:
                    self.diagnostics.expired += 1
            self.diagnostics.frame(now, observations, assignments, packets)
        except Exception as error:
            self.fail(error)
            return
        for packet in packets:
            packet['session'] = self.session
            try:
                sender.sendto(json.dumps(packet, allow_nan=False).encode(), ('127.0.0.1', self.args.hand_port))
            except OSError:
                # Optional hand transport must not interrupt body/face/preview packets.
                pass

    def close(self):
        recognizer, self.recognizer = self.recognizer, None
        if recognizer is not None:
            try:
                recognizer.close()
            except Exception:
                pass
