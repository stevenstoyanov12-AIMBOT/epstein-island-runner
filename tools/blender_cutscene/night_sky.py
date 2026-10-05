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
    """Gradient, city glow and the Milky Way's diffuse light (computed at half resolution): mottled star
    clouds, warm toward the galactic core and bluer at the band's edges, broken by broad soft dust clouds."""
    sw, sh = w // 2, h // 2
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
    corewards = np.clip(along, 0, 1)
    width = 0.15 + 0.07 * (along + 1)                                   # broader toward the core
    band = np.exp(-(b / width) ** 2) * (0.35 + 0.65 * corewards ** 1.5)
    # star clouds: patchy, clumpy brightness at several scales
    warp = fbm(flat * 2.0, 3, 6).reshape(sh, sw)
    big = fbm(flat * 5.0 + warp.reshape(-1, 1) * 0.6, 5, 3).reshape(sh, sw) * 0.5 + 0.5
    mid = fbm(flat * 16.0, 4, 4).reshape(sh, sw) * 0.5 + 0.5
    grain = fbm(flat * 60.0, 2, 7).reshape(sh, sw) * 0.5 + 0.5
    clouds = np.clip(big ** 2.2 * 1.8, 0, 1.4) * (0.55 + 0.6 * mid) * (0.8 + 0.4 * grain)
    glow = band * (0.2 + 1.2 * clouds)
    # dust: broad, soft dark clouds along the middle of the band with ragged edges, not thin lines
    dust_field = fbm(flat * 3.2 + warp.reshape(-1, 1) * 0.8, 5, 5).reshape(sh, sw) + 0.25 * (mid - 0.5)
    dust = np.clip((dust_field - 0.02) / 0.35, 0, 1)
    dust = dust * dust * (3 - 2 * dust)
    dust *= np.exp(-(b / (width * 0.55)) ** 2) * (0.5 + 0.5 * corewards)
    glow = glow * (1 - 0.9 * dust)
    # colour: warm gold toward the core, blue-white out at the band's edges
    edge = np.clip(np.abs(b) / width, 0, 1)[..., None]
    warm = np.array([1.0, 0.82, 0.6])
    cool = np.array([0.65, 0.78, 1.0])
    tint = warm * (corewards[..., None] * (1 - edge)) + cool * (1 - corewards[..., None] * (1 - edge))
    col = col + glow[..., None] * tint * 0.14 * (lat[..., None] > -0.02)
    big_img = np.stack([ndimage.zoom(col[..., c], 2, order=3) for c in range(3)], -1)
    return big_img[:h, :w]


def stars(w, h, rng):
    """Splat stars into a linear-light image; bright stars get a soft glow."""
    n_field, n_band = 45000, 140000
    d = rng.normal(size=(n_field + n_band * 3, 3))
    d /= np.linalg.norm(d, axis=1, keepdims=True)
    b, _ = galactic(d)
    keep = np.concatenate([np.ones(n_field, bool), rng.random(n_band * 3) < np.exp(-(b[n_field:] / 0.17) ** 2)])
    in_band = np.concatenate([np.zeros(n_field, bool), np.ones(n_band * 3, bool)])[keep]
    d = d[keep]
    # a cluster of young, hot blue stars born inside the nebula
    cl = NEBULA_DIR + rng.normal(0, 0.035, (1500, 3))
    d = np.vstack([d, cl / np.linalg.norm(cl, axis=1, keepdims=True)])
    in_band = np.concatenate([in_band, np.zeros(len(cl), bool)])
    above = d[:, 1] > -0.05                                  # none below the horizon
    d, in_band = d[above], in_band[above]
    n = len(d)
    # power-law brightness: most stars faint, a handful brilliant
    mag = rng.pareto(1.4, n) + 1.0
    lum = np.clip(0.01 * mag ** 2.0, 0, 8)
    lum[in_band] *= 0.45                                     # the band's stars: a fine, faint grain
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
    """Half-Life style emission nebula, about 70 degrees across and stretched diagonally over the sky:
    deep violet outer gas, hot pink and orange inner layers and a white-gold core with a young star cluster;
    long wispy streaks along its length, bright ionisation fronts at the edges of the glowing gas, cyan
    veils, a few dark dust knots near the core and broad dust lanes."""
    rng = np.random.default_rng(9)
    d, _ = directions(w, h)
    ang = np.arccos(np.clip(d @ NEBULA_DIR, -1, 1))
    reach = np.radians(38)
    region = ang < reach
    dd = d[region]
    tilt = np.radians(28)
    e1 = np.cross(NEBULA_DIR, [0, 1, 0])
    e1 /= np.linalg.norm(e1)
    e2 = np.cross(e1, NEBULA_DIR)
    e1, e2 = e1 * np.cos(tilt) + e2 * np.sin(tilt), -e1 * np.sin(tilt) + e2 * np.cos(tilt)
    x = (dd @ e1) / np.sin(reach)
    y = (dd @ e2) / np.sin(reach) * 1.8
    z0 = np.full_like(x, 0.37)
    p = np.stack([x * 1.4, y * 1.4, z0], -1)
    streak = np.stack([x * 0.9, y * 3.4, z0], -1)            # stretched along the cloud's long axis

    q = np.stack([fbm(p * 0.7, 4, 31), fbm(p * 0.7 + [5.2, 1.3, 0], 4, 32), np.zeros_like(x)], -1)
    r = np.stack([fbm(p + 1.4 * q + [1.7, 9.2, 0], 4, 33), fbm(p + 1.4 * q + [8.3, 2.8, 0], 4, 34), np.zeros_like(x)], -1)
    billow = fbm(p * 0.9 + 1.2 * r, 6, 35) * 0.5 + 0.5

    rad = np.sqrt(x ** 2 + y ** 2)
    edge = rad * (1 + 0.5 * fbm(p * 0.8 + q, 3, 36))
    fade = np.clip((reach - ang[region]) / (reach * 0.35), 0, 1)
    fade = fade * fade * (3 - 2 * fade)
    body = np.clip(1 - edge, 0, 1) ** 1.3 * fade
    puffs = fbm(p * 2.2 + 2.0 * r, 5, 43) * 0.5 + 0.5
    density = (billow ** 1.6) * (0.55 + 0.9 * puffs ** 2) * body

    # long wisps and fine streaks running along the cloud
    wisp = (1 - np.abs(fbm(streak * 1.2 + 2.6 * r + 1.5 * q, 5, 40))) ** 4 * body
    fine = (1 - np.abs(fbm(streak * 3.5 + 3.5 * r, 4, 41))) ** 9 * body
    # ionisation fronts: bright rims where the glowing gas thins out
    front = np.exp(-((density - 0.32) / 0.07) ** 2) * body
    veil = (fbm(p * 1.1 + 2.0 * q + [3, 7, 0], 4, 38) * 0.5 + 0.5) ** 2 * np.clip(1 - np.abs(edge - 0.62) / 0.5, 0, 1) ** 2 * fade

    # temperature: hot in the middle, cool at the edges, broken up by the gas
    temp = np.clip(0.95 - rad * 1.7 + 0.35 * q[:, 0] + 0.25 * (billow - 0.5), 0, 1)[:, None]
    deep = np.array([0.22, 0.08, 0.85])
    pink = np.array([1.0, 0.12, 0.5])
    orange = np.array([1.0, 0.5, 0.18])
    white = np.array([1.0, 0.92, 0.78])
    cyan = np.array([0.05, 0.9, 1.0])
    col = np.where(temp < 0.4, deep + (pink - deep) * (temp / 0.4),
                   np.where(temp < 0.75, pink + (orange - pink) * ((temp - 0.4) / 0.35),
                            orange + (white - orange) * ((temp - 0.75) / 0.25)))

    heart = np.exp(-(rad / 0.13) ** 2)
    bloom = np.exp(-(rad / 0.6) ** 2) * fade
    light = col * density[:, None] * 1.7
    light += col * wisp[:, None] * 0.4 + (col * 0.5 + 0.5) * fine[:, None] * 0.25
    light += (col * 0.6 + 0.4) * front[:, None] * 0.55
    light += cyan * veil[:, None] * 0.55
    light += white * (heart * (0.8 + 0.5 * billow))[:, None] * 0.6 + np.array([1.0, 0.3, 0.6]) * bloom[:, None] * 0.1

    # dark dust: a few dense knots near the core (with lit rims) and broad lanes across the cloud
    knots = np.zeros_like(x)
    for _ in range(3):
        cx, cy = rng.uniform(-0.4, 0.4), rng.uniform(-0.2, 0.2)
        size = rng.uniform(0.05, 0.1)
        # ragged, irregular blobs rather than neat ovals
        k = np.sqrt((x - cx) ** 2 + ((y - cy) / 1.6) ** 2) / size * (1 + 0.7 * fbm(p * 5 + [cx * 9, cy * 9, 0], 3, 42))
        knots = np.maximum(knots, np.clip(1.1 - k, 0, 1) ** 1.5 * 0.7)
    lanes = np.clip((1 - np.abs(fbm(p * 0.9 + 1.3 * r + [4, 4, 0], 4, 39))) ** 4 * 1.8 - 0.6, 0, 1) * body
    dark = np.clip(np.maximum(knots, lanes * 0.8), 0, 1)
    light = light * (1 - 0.65 * dark[:, None])
    img[region] = img[region] * (1 - 0.65 * dark[:, None]) + light
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
