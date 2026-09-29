"""Star-filled night sky as a 360-degree equirectangular panorama (for Unity's Skybox/Panoramic shader).

Stars are scattered uniformly over the sphere with a power-law brightness spread and real star colours,
denser along a Milky Way band that has a soft glow and dark dust lanes. Near the horizon the sky picks up
a faint warm city glow. A moon is drawn where the cutscene's moonlight comes from, and a colourful
Half-Life style nebula glows high in the sky behind the van.

Panorama layout (Unity's): u = 0.5 looks along +Z, u increases toward +X; v = 0 is straight up.

usage: python3 night_sky.py OUT.png [--width 4096]
"""
import argparse
import os
import sys

import numpy as np
from PIL import Image
from scipy import ndimage

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "statue_gen"))
from sdf import fbm  # noqa: E402

# direction toward the moon: opposite to the moonlight, which shines along Euler(32, 200, 0)'s forward
MOON_DIR = np.array([0.290, 0.530, 0.797])
MOON_DIR /= np.linalg.norm(MOON_DIR)
GALACTIC_POLE = np.array([0.35, 0.55, -0.76])     # tilts the Milky Way across the sky
GALACTIC_POLE /= np.linalg.norm(GALACTIC_POLE)
GALACTIC_CORE = np.array([0.55, 0.25, 0.8])       # brightest, dustiest part of the band
NEBULA_DIR = np.array([-0.45, 0.55, 0.70])        # high in the sky behind the van (Unity +Z), off to the left
NEBULA_DIR /= np.linalg.norm(NEBULA_DIR)


def directions(w, h):
    u = (np.arange(w) + 0.5) / w
    v = (np.arange(h) + 0.5) / h
    lon = (u - 0.5) * 2 * np.pi
    lat = (0.5 - v) * np.pi
    LON, LAT = np.meshgrid(lon, lat)
    return np.stack([np.cos(LAT) * np.sin(LON), np.sin(LAT), np.cos(LAT) * np.cos(LON)], -1), LAT


def to_pixel(d, w, h):
    lon = np.arctan2(d[:, 0], d[:, 2])
    lat = np.arcsin(np.clip(d[:, 1], -1, 1))
    return (lon / (2 * np.pi) + 0.5) * w, (0.5 - lat / np.pi) * h


def galactic(d):
    """Galactic latitude b and a unit-ish longitude term (1 at the core, -1 opposite)."""
    b = np.arcsin(np.clip(d @ GALACTIC_POLE, -1, 1))
    core = GALACTIC_CORE - GALACTIC_POLE * (GALACTIC_CORE @ GALACTIC_POLE)
    core /= np.linalg.norm(core)
    flat = d - np.outer(d @ GALACTIC_POLE, GALACTIC_POLE)
    flat /= np.maximum(np.linalg.norm(flat, axis=-1, keepdims=True), 1e-9)
    return b, flat @ core


def sky_background(w, h):
    """Gradient, city glow and the Milky Way's diffuse light, computed at quarter resolution."""
    sw, sh = w // 4, h // 4
    d, lat = directions(sw, sh)
    flat = d.reshape(-1, 3)
    up = np.clip(lat, 0, None)
    zenith = np.array([0.004, 0.006, 0.016])
    horizon = np.array([0.018, 0.024, 0.045])
    t = np.exp(-up / 0.35)[..., None]
    col = zenith + (horizon - zenith) * t
    col = col + np.array([0.045, 0.028, 0.014]) * np.exp(-up / 0.07)[..., None]   # warm city glow
    col = np.where(lat[..., None] < 0, np.array([0.006, 0.007, 0.01]), col)          # below the horizon

    b, along = galactic(flat)
    b, along = b.reshape(sh, sw), along.reshape(sh, sw)
    width = 0.16 + 0.06 * (along + 1)                                   # broader toward the core
    band = np.exp(-(b / width) ** 2) * (0.45 + 0.55 * np.clip(along, 0, 1) ** 1.5)
    clouds = fbm(flat * 4.0, 5, 3).reshape(sh, sw) * 0.5 + 0.5
    fine = fbm(flat * 14.0, 4, 4).reshape(sh, sw) * 0.5 + 0.5
    glow = band * (0.45 + 0.8 * clouds * fine)
    # dust lanes: dark filaments hugging the band's centre line
    lanes = np.clip(1 - np.abs(fbm(flat * 7.0, 4, 5).reshape(sh, sw)) * 4, 0, 1) ** 2
    dust = np.exp(-(b / (width * 0.45)) ** 2) * lanes * (0.4 + 0.6 * np.clip(along, 0, 1))
    glow = glow * (1 - 0.85 * dust)
    tint = np.array([0.78, 0.8, 1.0]) + np.array([0.25, 0.12, -0.15]) * np.clip(along, 0, 1)[..., None]
    col = col + glow[..., None] * tint * 0.085 * (lat[..., None] > -0.02)
    big = np.stack([ndimage.zoom(col[..., c], 4, order=3) for c in range(3)], -1)
    return big[:h, :w]


def stars(w, h, rng):
    """Splat stars into a linear-light image; bright stars get a soft glow."""
    n_field, n_band = 45000, 30000
    d = rng.normal(size=(n_field + n_band * 3, 3))
    d /= np.linalg.norm(d, axis=1, keepdims=True)
    b, _ = galactic(d)
    keep = np.concatenate([np.ones(n_field, bool), rng.random(n_band * 3) < np.exp(-(b[n_field:] / 0.2) ** 2)])
    d = d[keep]
    # a cluster of young, hot blue stars born inside the nebula
    cl = NEBULA_DIR + rng.normal(0, 0.035, (1500, 3))
    d = np.vstack([d, cl / np.linalg.norm(cl, axis=1, keepdims=True)])
    d = d[d[:, 1] > -0.05]                                   # none below the horizon
    n = len(d)
    # power-law brightness: most stars faint, a handful brilliant
    mag = rng.pareto(1.4, n) + 1.0
    lum = np.clip(0.01 * mag ** 2.0, 0, 8)
    # fade toward the horizon where the air is thick and the city glow washes them out
    alt = np.arcsin(d[:, 1])
    lum *= np.clip(alt / 0.25, 0.15, 1) ** 1.5
    temps = np.array([[0.7, 0.8, 1.0], [0.85, 0.9, 1.0], [1.0, 1.0, 1.0], [1.0, 0.94, 0.8], [1.0, 0.8, 0.6]])
    colour = temps[rng.choice(5, n, p=[0.12, 0.25, 0.33, 0.2, 0.1])]

    px, py = to_pixel(d, w, h)
    img = np.zeros((h, w, 3))
    x0, y0 = np.floor(px - 0.5).astype(int), np.floor(py - 0.5).astype(int)
    fx, fy = px - 0.5 - x0, py - 0.5 - y0
    for dx, wx in ((0, 1 - fx), (1, fx)):
        for dy, wy in ((0, 1 - fy), (1, fy)):
            xi, yi = (x0 + dx) % w, np.clip(y0 + dy, 0, h - 1)
            for c in range(3):
                np.add.at(img[..., c], (yi, xi), lum * colour[:, c] * wx * wy)
    core = np.stack([ndimage.gaussian_filter(img[..., c], 0.55, mode="wrap") for c in range(3)], -1) * 2.2
    bright = np.where((lum > 3.0)[:, None], 1.0, 0.0)   # only a few hundred of the brightest glow
    halo_src = np.zeros((h, w, 3))
    for c in range(3):
        np.add.at(halo_src[..., c], (np.clip(py.astype(int), 0, h - 1), px.astype(int) % w), lum * colour[:, c] * bright[:, 0])
    halo = np.stack([ndimage.gaussian_filter(halo_src[..., c], 2.2, mode="wrap") for c in range(3)], -1) * 1.5
    return core + halo


def nebula(img, w, h):
    """Half-Life style emission nebula: soft, glowing, stretched clouds of vivid gas - magenta and violet
    with teal veils at the edges and a hot gold-white heart - a few bright filaments and broad dark dust
    lanes, fading out along an irregular edge. About 70 degrees across."""
    d, _ = directions(w, h)
    ang = np.arccos(np.clip(d @ NEBULA_DIR, -1, 1))
    reach = np.radians(38)
    region = ang < reach
    dd = d[region]
    tilt = np.radians(28)                     # the cloud's long axis runs diagonally across the sky
    e1 = np.cross(NEBULA_DIR, [0, 1, 0])
    e1 /= np.linalg.norm(e1)
    e2 = np.cross(e1, NEBULA_DIR)
    e1, e2 = e1 * np.cos(tilt) + e2 * np.sin(tilt), -e1 * np.sin(tilt) + e2 * np.cos(tilt)
    x = (dd @ e1) / np.sin(reach)
    y = (dd @ e2) / np.sin(reach) * 1.8      # squashed across its width: an elongated cloud
    p = np.stack([x * 1.4, y * 1.4, np.full_like(x, 0.37)], -1)

    # gentle domain warping: large billows rather than marbled turbulence
    q = np.stack([fbm(p * 0.7, 4, 31), fbm(p * 0.7 + [5.2, 1.3, 0], 4, 32), np.zeros_like(x)], -1)
    r = np.stack([fbm(p + 1.4 * q + [1.7, 9.2, 0], 4, 33), fbm(p + 1.4 * q + [8.3, 2.8, 0], 4, 34), np.zeros_like(x)], -1)
    billow = fbm(p * 0.9 + 1.2 * r, 5, 35) * 0.5 + 0.5

    rad = np.sqrt(x ** 2 + y ** 2)
    edge = rad * (1 + 0.5 * fbm(p * 0.8 + q, 3, 36))
    fade = np.clip((reach - ang[region]) / (reach * 0.35), 0, 1)
    fade = fade * fade * (3 - 2 * fade)          # no hard cut-off where the drawn region ends
    body = np.clip(1 - edge, 0, 1) ** 1.3 * fade
    heart = np.exp(-(rad / 0.28) ** 2)

    # soft glowing gas
    glow = (billow ** 1.8) * body
    # a handful of bright, soft filaments where the warped noise folds
    fil = (1 - np.abs(fbm(p * 1.6 + 1.6 * r, 4, 37))) ** 5 * body
    # outer veil of teal, strongest toward the cloud's edge
    veil = (fbm(p * 1.1 + 2.0 * q + [3, 7, 0], 4, 38) * 0.5 + 0.5) ** 2 * np.clip(1 - np.abs(edge - 0.65) / 0.35, 0, 1) * fade

    violet = np.array([0.4, 0.05, 1.0])
    magenta = np.array([1.0, 0.08, 0.55])
    teal = np.array([0.0, 0.85, 0.85])
    gold = np.array([1.0, 0.78, 0.42])
    mix = np.clip(q[:, 0] * 1.4 + 0.5, 0, 1)[:, None]
    gas = violet * (1 - mix) + magenta * mix

    light = gas * glow[:, None] * 1.1
    light += (gas * 0.8 + 0.2) * fil[:, None] * 0.8
    light += teal * veil[:, None] * 0.6
    light += gold * (heart * (0.6 + 0.6 * billow) * fade)[:, None] * 1.0
    # broad dust lanes silhouetted against the glow, dimming the stars behind them too
    lanes = np.clip((1 - np.abs(fbm(p * 0.9 + 1.3 * r + [4, 4, 0], 4, 39))) ** 4 * 1.8 - 0.55, 0, 1) * body
    img[region] = img[region] * (1 - 0.6 * lanes[:, None]) + light * (1 - 0.8 * lanes[:, None])
    return img


def moon(img, w, h):
    """A full moon with maria and craters, plus a faint glow around it."""
    d, _ = directions(w, h)
    cosang = d @ MOON_DIR
    radius = np.radians(2.2)
    ang = np.arccos(np.clip(cosang, -1, 1))
    mask = ang < radius * 1.02
    near = ang < radius * 12
    # local surface coordinates on the disc
    e1 = np.cross(MOON_DIR, [0, 1, 0])
    e1 /= np.linalg.norm(e1)
    e2 = np.cross(e1, MOON_DIR)
    sx, sy = (d @ e1) / np.sin(radius), (d @ e2) / np.sin(radius)
    p = np.stack([sx * 2.2, sy * 2.2, np.zeros_like(sx)], -1)
    maria = fbm(p[mask].reshape(-1, 3), 4, 21)
    craters = fbm(p[mask].reshape(-1, 3) * 5, 3, 22)
    r2 = np.clip(sx[mask] ** 2 + sy[mask] ** 2, 0, 1)
    limb = (1 - r2) ** 0.25
    shade = (0.9 - 0.45 * np.clip(maria * 1.6, 0, 1) + 0.15 * craters) * limb
    edge = np.clip((radius * 1.02 - ang[mask]) / (radius * 0.04), 0, 1)
    lit = np.array([1.0, 0.97, 0.9]) * 1.1
    img[mask] = img[mask] * (1 - edge[:, None]) + (shade[:, None] * lit) * edge[:, None]
    g = np.exp(-((ang[near] - radius) / (radius * 1.5)).clip(0) ** 2) * 0.06
    img[near] += (g * (ang[near] > radius))[:, None] * np.array([0.8, 0.85, 1.0])
    return img


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("out")
    ap.add_argument("--width", type=int, default=4096)
    args = ap.parse_args()
    w, h = args.width, args.width // 2
    rng = np.random.default_rng(42)
    img = sky_background(w, h) + stars(w, h, rng)
    img = nebula(img, w, h)
    img = moon(img, w, h)
    # gentle tone curve, sRGB encode, and dither so the dark gradients don't band
    img = img / (1 + img * 0.35)
    srgb = np.where(img <= 0.0031308, img * 12.92, 1.055 * np.power(np.clip(img, 0, None), 1 / 2.4) - 0.055)
    srgb = srgb + (rng.random(srgb.shape) - 0.5) / 255
    Image.fromarray((np.clip(srgb, 0, 1) * 255).astype(np.uint8)).save(args.out, optimize=True)
    print(args.out, os.path.getsize(args.out) // 1024, "KB")


if __name__ == "__main__":
    main()
