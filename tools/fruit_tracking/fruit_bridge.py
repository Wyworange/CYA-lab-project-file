"""Reuse the pinned original camera bridge, adding face and hand actions to its SAME frames.

The source checkout stays unchanged. Exact hooks fail closed after starter updates.
"""
import hashlib
from pathlib import Path
import sys
from camera_recovery import recover_read
from fruit_actions import FruitSession
from zones import ZoneSession

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/hand_tracking'))
from hand_actions import HandSession  # noqa: E402  Aurora's hand grab
BRIDGE = ROOT / 'DigiPhantStarter/Tracking/bridge.py'
BRIDGE_SHA256 = '0bf97ad47beb9b63ba185085e0576f86464281aac9a042692f923b8632a7d8e9'


def patched_source():
    original = BRIDGE.read_bytes()
    if hashlib.sha256(original).hexdigest() != BRIDGE_SHA256:
        raise RuntimeError('Starter bridge changed: review fruit hooks before updating the recorded SHA256.')
    source = original.decode()
    replacements = [
        ('    args = parser.parse_args()',
         "    parser.add_argument('--fruit-actions', action='store_true')\n"
         "    parser.add_argument('--fruit-port', type=int, default=5058)\n"
         "    parser.add_argument('--face-model', type=Path, default=Path(__file__).parent / 'face_landmarker.task')\n"
         "    parser.add_argument('--hand-actions', action='store_true')\n"
         "    parser.add_argument('--hand-port', type=int, default=5059)\n"
         "    parser.add_argument('--gesture-model', type=Path, default=Path(__file__).parent / 'gesture_recognizer.task')\n"
         "    args = parser.parse_args()\n"
         "    if args.hand_actions and (not 1024 <= args.hand_port <= 65535 or args.hand_port in (args.port, args.port + 1, args.port + 2, args.fruit_port)):\n"
         "        parser.error('Hand port must be 1024..65535 and separate from pose/command/preview/face ports')\n"
         "    if args.fruit_actions and (not 1024 <= args.fruit_port <= 65535 or args.fruit_port in (args.port, args.port + 1, args.port + 2)):\n"
         "        parser.error('Fruit port must be 1024..65535 and separate from pose/command/preview ports')"),
        ('with vision.PoseLandmarker.create_from_options(options) as detector:',
         'with FruitSession(args) as fruit, hands, ZoneSession(args) as zones, vision.PoseLandmarker.create_from_options(options) as detector:'),
        # Fixed seating zones (Unity "Fixed zones"): one single-person pose per strip, slot = strip.
        ('                result = detector.detect_for_video(mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb), timestamp)',
         '                result = zones.detect(rgb, timestamp) or detector.detect_for_video(mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb), timestamp)'),
        ("                cv2.putText(frame, message, (12, 26), cv2.FONT_HERSHEY_SIMPLEX, .55, (255, 255, 255), 2)",
         "                cv2.putText(frame, message, (12, 26), cv2.FONT_HERSHEY_SIMPLEX, .55, (255, 255, 255), 2)\n"
         "                zones.draw(frame, cv2, assignments)"),
        # Load the hand model before the camera opens, so capture warm-up is never stalled.
        ('    tracker = PerformerTracker(args.people)',
         '    hands = HandSession(args)\n'
         '    hands.start()\n'
         '    tracker = PerformerTracker(args.people)'),
        ("                if not ok:\n                    raise RuntimeError('Camera stopped delivering frames.')",
         "                if not ok:\n"
         "                    ok, frame, camera = recover_read(cv2, camera, args.camera)\n"
         "                if not ok:\n"
         "                    raise RuntimeError('Camera stopped delivering frames.')"),
        ("if address == ('127.0.0.1', args.port):", "if address == ('127.0.0.1', args.port) or (args.fruit_actions and address == ('127.0.0.1', args.fruit_port)):") ,
        ("                            if isinstance(command, dict) and command.get('version') == 1:",
         "                            if isinstance(command, dict) and command.get('version') == 1:\n"
         "                                fruit.command(command)\n"
         "                                hands.command(command)\n"
         "                                zones.command(command)\n"
         "                                if address == ('127.0.0.1', args.fruit_port):\n"
         "                                    continue"),
        ('                assignments = tracker.assign([o[0] for o in observations])',
         '                assignments = zones.assign(observations) if zones.active else tracker.assign([o[0] for o in observations])\n'
         '                fruit.process(mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb), timestamp, sender, args.people,\n'
         '                              {slot: observations[i][3] for slot, i in assignments.items()})\n'
         '                hands.process(rgb, observations, assignments, sender)'),
    ]
    for anchor, replacement in replacements:
        if source.count(anchor) != 1:
            raise RuntimeError('Fruit integration hook ambiguous or missing: ' + anchor)
        source = source.replace(anchor, replacement)
    return source


def main():
    sys.path.insert(0, str(BRIDGE.parent))
    scope = dict(__name__='__main__', __file__=str(BRIDGE), FruitSession=FruitSession, HandSession=HandSession,
                 ZoneSession=ZoneSession, recover_read=recover_read)
    exec(compile(patched_source(), str(BRIDGE), 'exec'), scope)


if __name__ == '__main__':
    main()
