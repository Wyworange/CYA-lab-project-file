"""Fixed seating zones: one pose per vertical strip of the shared camera frame.

With three performers in one frame the stock multi-person pose model rarely sees everyone at
once (field logs: never 3 together, often 1), so identities swapped. Here the unmirrored frame
is split into equal strips, left to right; each strip has its own single-person landmarker and
its person is always that strip's slot (P1 = left). Landmarks are mapped back to full-frame
coordinates, so the body, face and hand code downstream is unchanged. No camera or ML imports
at module load.
"""
import time
from types import SimpleNamespace

RETRY_SECONDS = 2.0


class Landmark:
    __slots__ = ('x', 'y', 'z', 'visibility', 'presence')

    def __init__(self, x, y, z, visibility, presence):
        self.x, self.y, self.z, self.visibility, self.presence = x, y, z, visibility, presence


def zone_bounds(width, count):
    """Pixel [x0, x1) of each strip; the strips tile the frame exactly."""
    return [(width * i // count, width * (i + 1) // count) for i in range(count)]


def centred_in_strip(landmarks):
    """The body's centre (shoulder midpoint, else nose) is inside the crop; landmarks are crop-relative."""
    if len(landmarks) > 12 and min(landmarks[11].visibility, landmarks[12].visibility) >= .5:
        x = (landmarks[11].x + landmarks[12].x) / 2
    elif landmarks and landmarks[0].visibility >= .5:
        x = landmarks[0].x
    else:
        return False
    return 0 <= x < 1


class ZoneSession:
    def __init__(self, args, factory=None):
        self.args = args
        self.factory = factory or self.create_detector
        self.count = 0
        self.detectors = []
        self.zone_of = {}
        self.detected = 0
        self.error = ''
        self.error_time = 0.

    @property
    def active(self):
        # A failure falls back to the stock tracker briefly, then zones are retried.
        if self.error and time.monotonic() - self.error_time >= RETRY_SECONDS:
            self.error = ''
        return self.count > 0 and not self.error

    def __enter__(self):
        return self

    def __exit__(self, *exc):
        self.close()

    def close(self):
        for detector in self.detectors:
            try:
                detector.close()
            except Exception:
                pass
        self.detectors = []

    def create_detector(self):
        from mediapipe.tasks import python
        from mediapipe.tasks.python import vision
        return vision.PoseLandmarker.create_from_options(vision.PoseLandmarkerOptions(
            base_options=python.BaseOptions(model_asset_path=str(self.args.model)),
            running_mode=vision.RunningMode.VIDEO, num_poses=1,
            min_pose_detection_confidence=.5, min_pose_presence_confidence=.5,
            min_tracking_confidence=.5))

    def command(self, command):
        """{"zones": N}: N strips (0 = off, the original multi-person tracker)."""
        if not isinstance(command, dict):
            return
        count = command.get('zones')
        if type(count) is int and 0 <= count <= 4 and count != self.count:
            self.close()
            self.count = count
            self.error = ''

    def detect(self, rgb, timestamp):
        """Pose result for the whole frame, or None to fall back to the stock tracker."""
        if not self.active:
            return None
        try:
            import mediapipe as mp
            import numpy as np
            height, width = rgb.shape[:2]
            while len(self.detectors) < self.count:
                self.detectors.append(self.factory())
            poses, self.zone_of = [], {}
            for index, (x0, x1) in enumerate(zone_bounds(width, self.count)):
                crop = np.ascontiguousarray(rgb[:, x0:x1])
                result = self.detectors[index].detect_for_video(mp.Image(image_format=mp.ImageFormat.SRGB, data=crop), timestamp)
                scale = (x1 - x0) / width
                for landmarks in result.pose_landmarks[:1]:
                    # A neighbour leaning in, or someone straddling a line, must not own this strip.
                    if not centred_in_strip(landmarks):
                        continue
                    mapped = [Landmark((x0 + p.x * (x1 - x0)) / width, p.y, p.z * scale, p.visibility, p.presence) for p in landmarks]
                    self.zone_of[id(mapped)] = index + 1
                    poses.append(mapped)
            self.detected = len(poses)
            return SimpleNamespace(pose_landmarks=poses)
        except Exception as error:
            # Fall back to the stock tracker rather than stopping body tracking.
            self.error = 'Fixed zones unavailable: ' + str(error)
            self.error_time = time.monotonic()
            print(self.error + f' (stock tracker for {RETRY_SECONDS:.0f} s, then retry)')
            self.close()
            return None

    def assign(self, observations):
        """{slot: observation index}: a person's slot is their strip, never a guess."""
        assignments = {}
        for index, observation in enumerate(observations):
            slot = self.zone_of.get(id(observation[3]))
            if slot is not None and slot not in assignments:
                assignments[slot] = index
        return assignments

    def draw(self, frame, cv2, assignments):
        """Strict strip boundaries and per-zone labels on the preview (unmirrored, P1 = left)."""
        if not self.active:
            return
        height, width = frame.shape[:2]
        thickness = max(2, round(width / 320))
        for index, (x0, x1) in enumerate(zone_bounds(width, self.count)):
            if index:
                cv2.line(frame, (x0, 0), (x0, height - 1), (20, 20, 20), thickness + 4)
                cv2.line(frame, (x0, 0), (x0, height - 1), (255, 255, 255), thickness)
            found = index + 1 in assignments
            label = f'P{index + 1}' + ('' if found else ' empty')
            color = (90, 220, 90) if found else (60, 60, 230)
            cv2.rectangle(frame, (x0 + 4, height - 34), (x1 - 4, height - 6), (20, 20, 20), -1)
            cv2.putText(frame, label, (x0 + 10, height - 13), cv2.FONT_HERSHEY_SIMPLEX, .6, color, 2, cv2.LINE_AA)
        # Replace the stock "waiting for N people" text, which describes the other tracker.
        cv2.rectangle(frame, (0, 0), (width, 36), (20, 20, 20), -1)
        cv2.putText(frame, f'Fixed zones: {len(assignments)}/{self.count} detected - stay inside your zone',
                    (12, 26), cv2.FONT_HERSHEY_SIMPLEX, .55, (255, 255, 255), 2, cv2.LINE_AA)
