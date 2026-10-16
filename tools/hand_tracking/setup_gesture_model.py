"""Download the official MediaPipe gesture model used by Aurora's hand grab; verify bytes first."""
import hashlib
import urllib.request

from hand_actions import MODEL, MODEL_SHA256 as SHA256, MODEL_URL as URL


def main():
    if MODEL.exists():
        if hashlib.sha256(MODEL.read_bytes()).hexdigest() != SHA256:
            raise SystemExit('Existing gesture model differs; preserve and reconcile it manually.')
        print('Existing gesture model checksum verified.')
        return
    MODEL.parent.mkdir(parents=True, exist_ok=True)
    temporary = MODEL.with_suffix('.download')
    if temporary.exists():
        raise SystemExit('Existing download found; inspect it before retrying.')
    urllib.request.urlretrieve(URL, temporary)
    if hashlib.sha256(temporary.read_bytes()).hexdigest() != SHA256:
        raise SystemExit('Downloaded model checksum differs; model left in .download for inspection.')
    temporary.replace(MODEL)
    print('Official gesture model downloaded and verified.')


if __name__ == '__main__':
    main()
