"""Download official pretrained face model; verify actual bytes before using it."""
import hashlib
from pathlib import Path
import urllib.request

URL = 'https://storage.googleapis.com/mediapipe-models/face_landmarker/face_landmarker/float16/latest/face_landmarker.task'
SHA256 = '64184e229b263107bc2b804c6625db1341ff2bb731874b0bcc2fe6544e0bc9ff'
MODEL = Path(__file__).resolve().parents[2] / 'DigiPhantStarter/Tracking/face_landmarker.task'


def main():
    if MODEL.exists():
        if hashlib.sha256(MODEL.read_bytes()).hexdigest() != SHA256:
            raise SystemExit('Existing face model differs; preserve and reconcile it manually.')
        print('Existing face model checksum verified.')
        return
    MODEL.parent.mkdir(parents=True, exist_ok=True)
    temporary = MODEL.with_suffix('.download')
    if temporary.exists():
        raise SystemExit('Existing download found; inspect it before retrying.')
    urllib.request.urlretrieve(URL, temporary)
    if hashlib.sha256(temporary.read_bytes()).hexdigest() != SHA256:
        raise SystemExit('Downloaded model checksum differs; model left in .download for inspection.')
    temporary.replace(MODEL)
    print('Official face model downloaded and verified.')


if __name__ == '__main__':
    main()
