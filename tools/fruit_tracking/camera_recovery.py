"""Tolerate brief macOS capture dropouts instead of ending the whole tracking session.

Pattern from Aurora's bridge (read retries, then reopen with a warm-up), applied as a hook
so the pinned starter bridge stays byte-identical.
"""
import time


def recover_read(cv2, camera, index, retries=3, warmup_reads=12, warmup_successes=3,
                 delay=.05, sleep=time.sleep, log=print):
    """Return (ok, frame, camera); camera may be a reopened handle."""
    for _ in range(retries):
        sleep(delay)
        ok, frame = camera.read()
        if ok and frame is not None:
            return True, frame, camera
    log('Camera read failed; reopening the camera.')
    camera.release()
    camera = cv2.VideoCapture(index)
    successes, frame = 0, None
    for _ in range(warmup_reads):
        ok, latest = camera.read()
        successes = successes + 1 if ok and latest is not None else 0
        if successes:
            frame = latest
        if successes >= warmup_successes:
            log('Camera recovered.')
            return True, frame, camera
        sleep(delay)
    return False, None, camera
