"""Generate a seamless lunar-regolith texture set for the ground (no external assets).

Outputs (PNG, tileable in both directions):
  MoonRegolith_Albedo.png  grey regolith with craters, bright rims and ray ejecta
  MoonRegolith_Normal.png  tangent-space normal map from the same height field

Usage: python tools/environment/make_moon_texture.py [out_dir] [--size 1024] [--seed 7]
"""
import argparse
from pathlib import Path

import cv2
import numpy as np


def fractal_noise(size, rng, beta=2.2):
    """Periodic 1/f^beta noise via FFT (tiles seamlessly)."""
    freq = np.fft.fftfreq(size)
    fx, fy = np.meshgrid(freq, freq)
    radius = np.sqrt(fx * fx + fy * fy)
    radius[0, 0] = 1
    spectrum = (rng.normal(size=(size, size)) + 1j * rng.normal(size=(size, size))) / radius ** (beta / 2)
    spectrum[0, 0] = 0
    field = np.real(np.fft.ifft2(spectrum))
    return (field - field.mean()) / (field.std() + 1e-9)


def craters(size, rng, count):
    """Height and brightness from craters; distances wrap around so the edges tile."""
    height = np.zeros((size, size), np.float32)
    bright = np.zeros((size, size), np.float32)
    y, x = np.mgrid[0:size, 0:size].astype(np.float32)
    # Power-law radii: many small craters, a few large ones.
    radii = np.clip(size * .006 * (1 - rng.random(count)) ** -.55, 3, size * .12)
    for radius in sorted(radii, reverse=True):
        cx, cy = rng.random(2) * size
        dx = (x - cx + size / 2) % size - size / 2
        dy = (y - cy + size / 2) % size - size / 2
        r = np.sqrt(dx * dx + dy * dy) / radius
        bowl = np.where(r < 1, -(1 - r * r), 0) * radius * .35
        rim = np.exp(-((r - 1.05) / .18) ** 2) * radius * .12
        height += bowl + rim
        # Fresh-looking craters: bright rim and faint radial ejecta rays outside.
        if rng.random() < .35:
            angle = np.arctan2(dy, dx)
            rays = np.clip(np.cos(angle * rng.integers(6, 14) + rng.random() * 6.28), 0, 1) ** 6
            bright += np.where((r > 1) & (r < 3), rays * np.exp(-(r - 1)) * .5, 0)
        bright += np.exp(-((r - 1.02) / .12) ** 2) * .35
        bright -= np.where(r < .9, (1 - r) * .15, 0)
    return height, bright


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('out', nargs='?', default='Assets/StudentWork/Environment/Moon')
    parser.add_argument('--size', type=int, default=1024)
    parser.add_argument('--seed', type=int, default=7)
    args = parser.parse_args()
    rng = np.random.default_rng(args.seed)
    size = args.size

    crater_height, crater_bright = craters(size, rng, count=420)
    regolith = fractal_noise(size, rng, 2.4) * 1.5 + fractal_noise(size, rng, 1.2) * .35
    height = crater_height + regolith * 2.0

    # Albedo: neutral grey regolith, broad mare-like dark patches, crater brightness, fine grain.
    mare = fractal_noise(size, rng, 3.4)  # steep spectrum = smooth, still periodic
    grain = fractal_noise(size, rng, .6)
    albedo = .47 + .035 * np.tanh(mare) + .06 * crater_bright + .025 * grain
    albedo = np.clip(albedo, .18, .82)
    tint = np.stack([albedo * .985, albedo * 1.0, albedo * 1.02], -1)  # cool lunar grey (BGR)

    # Normal map from the wrapped height field (OpenGL convention, +Y up as Unity expects).
    gx = (np.roll(height, -1, 1) - np.roll(height, 1, 1)) * .5
    gy = (np.roll(height, -1, 0) - np.roll(height, 1, 0)) * .5
    strength = .35
    nx, ny, nz = -gx * strength, gy * strength, np.ones_like(height)
    length = np.sqrt(nx * nx + ny * ny + nz * nz)
    normal = np.stack([nz / length, ny / length, nx / length], -1)  # BGR order for OpenCV
    normal = (normal * .5 + .5)

    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    cv2.imwrite(str(out / 'MoonRegolith_Albedo.png'), np.round(tint * 255).astype(np.uint8))
    cv2.imwrite(str(out / 'MoonRegolith_Normal.png'), np.round(normal * 255).astype(np.uint8))
    print('wrote', out / 'MoonRegolith_Albedo.png', out / 'MoonRegolith_Normal.png')


if __name__ == '__main__':
    main()
