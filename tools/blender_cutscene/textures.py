"""Textures for the crate and van cargo area.

wood_albedo.png / wood_normal.png   pine boards: grain, knots, edge wear (tiles along the grain)
stencil.png                         "BATON CORPORATION" spray stencil with bridges and grunge (alpha)
metal_albedo.png / metal_normal.png painted steel with scratches and rust for the van walls
"""
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "statue_gen"))
from sdf import fbm  # noqa: E402

FONT = "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf"


def normal_map(height, strength):
    gy, gx = np.gradient(height)
    n = np.dstack([-gx * strength, gy * strength, np.ones_like(height)])
    n /= np.linalg.norm(n, axis=2, keepdims=True)
    return ((n * 0.5 + 0.5) * 255).astype(np.uint8)


def noise2(w, h, sx, sy, octaves, seed, tile_x=True):
    """2D fbm, seamless along x by sampling on a circle."""
    x = np.arange(w) / w
    y = np.arange(h) / h
    X, Y = np.meshgrid(x, y)
    if tile_x:
        a = 2 * np.pi * X
        p = np.stack([np.cos(a) * sx / (2 * np.pi), np.sin(a) * sx / (2 * np.pi), Y * sy], -1)
    else:
        p = np.stack([X * sx, Y * sy, np.zeros_like(X)], -1)
    return fbm(p.reshape(-1, 3), octaves, seed).reshape(h, w)


def wood(out, w=2048, h=512):
    """Grain runs along u (the board length). Several boards' worth of variation stacked in v."""
    warp = noise2(w, h, 2, 3, 4, 1)
    streaks = noise2(w, h, 3, 90, 3, 2)                       # long fibres running along the board
    phase = np.arange(h)[:, None] / h * 70 + 5.0 * warp + 1.5 * streaks
    rings = np.abs(np.sin(phase * np.pi)) ** 0.35            # growth rings: soft bands, sharp dark lines
    fine = noise2(w, h, 40, 400, 2, 3)
    grain = 0.25 + 0.5 * rings + 0.15 * streaks + 0.1 * fine
    # knots: small dark ovals stretched along the grain
    rng = np.random.default_rng(4)
    yy, xx = np.mgrid[0:h, 0:w]
    knot = np.zeros((h, w))
    for _ in range(5):
        cx, cy, r = rng.uniform(0, w), rng.uniform(0, h), rng.uniform(7, 14)
        dx = np.minimum(np.abs(xx - cx), w - np.abs(xx - cx))
        d = np.sqrt((dx / 2.4) ** 2 + (yy - cy) ** 2)
        knot = np.maximum(knot, np.exp(-(d / r) ** 2) * 0.8 + 0.2 * np.exp(-(d / (2.2 * r)) ** 2))
    tone = noise2(w, h, 4, 3, 3, 5)
    light = np.array([0.74, 0.47, 0.24])
    dark = np.array([0.42, 0.23, 0.10])
    col = dark + (light - dark) * np.clip(grain + 0.15 * tone, 0, 1)[..., None]
    col = col * (1 - 0.6 * knot[..., None])
    grime = np.clip(noise2(w, h, 6, 5, 4, 7) * 1.8 - 0.3, 0, 1)
    col = col * (1 - 0.3 * grime[..., None])
    Image.fromarray((np.clip(col, 0, 1) * 255).astype(np.uint8)).save(os.path.join(out, "wood_albedo.png"))
    height = 0.6 * grain + 0.25 * fine - 0.4 * knot
    Image.fromarray(normal_map(height, 6.0)).save(os.path.join(out, "wood_normal.png"))


def stencil(out, w=2048, h=1024):
    """Spray-painted stencil lettering; black paint in alpha with overspray and wear."""
    big = ImageFont.truetype(FONT, 330)
    mid = ImageFont.truetype(FONT, 170)
    small = ImageFont.truetype(FONT, 70)
    mask = Image.new("L", (w, h), 0)
    d = ImageDraw.Draw(mask)

    def centred(text, font, y, spacing=0):
        x0, y0, x1, y1 = d.textbbox((0, 0), text, font=font)
        width = x1 - x0 + spacing * (len(text) - 1)
        x = (w - width) / 2
        for ch in text:
            d.text((x, y), ch, font=font, fill=255)
            cx0, _, cx1, _ = d.textbbox((0, 0), ch, font=font)
            x += cx1 - cx0 + spacing

    centred("BATON", big, 90, spacing=24)
    centred("CORPORATION", mid, 470, spacing=10)
    centred("HANDLE WITH CARE  -  THIS SIDE UP", small, 760, spacing=6)
    d.rectangle([220, 700, w - 220, 718], fill=255)     # stencilled rule
    # stencil bridges: thin vertical gaps through the letters, like a real cut stencil
    m = np.array(mask).astype(float) / 255
    bridges = np.zeros_like(m)
    for x in range(0, w, 97):
        bridges[:, x:x + 9] = 1
    m = m * (1 - bridges * (np.array(mask.filter(ImageFilter.MinFilter(25))) > 0))
    # overspray halo and worn, patchy paint
    halo = np.array(Image.fromarray((m * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(6))) / 255
    wear = noise2(w, h, 14, 7, 4, 11, tile_x=False)
    speck = noise2(w, h, 160, 80, 2, 12, tile_x=False)
    alpha = np.clip(m * (0.95 - np.clip(wear * 1.6 - 0.2, 0, 1) * 0.8) + 0.25 * halo, 0, 1)
    alpha = alpha * (speck > -0.55)
    rgba = np.zeros((h, w, 4), np.uint8)
    rgba[..., :3] = 18
    rgba[..., 3] = (alpha * 255).astype(np.uint8)
    Image.fromarray(rgba, "RGBA").save(os.path.join(out, "stencil.png"))


def metal(out, w=1024, h=1024):
    base = np.array([0.55, 0.56, 0.58])
    tone = noise2(w, h, 5, 5, 4, 21)
    scratches = np.zeros((h, w))
    img = Image.fromarray((scratches * 255).astype(np.uint8))
    dr = ImageDraw.Draw(img)
    rng = np.random.default_rng(8)
    for _ in range(260):
        x, y = rng.uniform(0, w), rng.uniform(0, h)
        a, L = rng.uniform(-0.4, 0.4), rng.uniform(20, 120)
        dr.line([(x, y), (x + L * np.cos(a), y + L * np.sin(a))], fill=int(rng.uniform(60, 200)), width=1)
    scratches = np.array(img) / 255
    rust = np.clip(noise2(w, h, 8, 8, 4, 22) * 2.2 - 0.9, 0, 1)
    col = base * (0.85 + 0.2 * tone[..., None])
    col = col + 0.25 * scratches[..., None]
    col = col * (1 - rust[..., None]) + np.array([0.38, 0.2, 0.1]) * rust[..., None]
    Image.fromarray((np.clip(col, 0, 1) * 255).astype(np.uint8)).save(os.path.join(out, "metal_albedo.png"))
    Image.fromarray(normal_map(0.4 * tone - 0.6 * scratches + 0.5 * rust, 3.0)).save(os.path.join(out, "metal_normal.png"))


def facade(out, w=512, h=1024):
    """Night-time building front: dark brick with a grid of windows, some lit (emission map)."""
    rng = np.random.default_rng(31)
    brick = 0.12 + 0.05 * noise2(w, h, 20, 40, 3, 32, tile_x=False)
    col = np.dstack([brick * 1.1, brick * 0.85, brick * 0.75])
    emit = np.zeros((h, w, 3))
    cols, rows = 4, 8
    cw, ch = w / cols, h / rows
    for r in range(rows):
        for c in range(cols):
            x0, y0 = int(c * cw + cw * 0.22), int(r * ch + ch * 0.2)
            x1, y1 = int((c + 1) * cw - cw * 0.22), int((r + 1) * ch - ch * 0.25)
            col[y0 - 4:y1 + 4, x0 - 4:x1 + 4] = 0.2          # window frame
            col[y0:y1, x0:x1] = 0.03                          # dark glass
            if rng.random() < 0.35:
                warm = np.array([1.0, 0.72, 0.38]) if rng.random() < 0.75 else np.array([0.55, 0.7, 1.0])
                glow = warm * rng.uniform(0.5, 1.0)
                emit[y0:y1, x0:x1] = glow
                col[y0:y1, x0:x1] = glow * 0.6
                if rng.random() < 0.5:                        # half-drawn blind
                    split = y0 + int((y1 - y0) * rng.uniform(0.2, 0.6))
                    emit[y0:split, x0:x1] *= 0.25
    Image.fromarray((np.clip(col, 0, 1) * 255).astype(np.uint8)).save(os.path.join(out, "facade_albedo.png"))
    Image.fromarray((np.clip(emit, 0, 1) * 255).astype(np.uint8)).save(os.path.join(out, "facade_emission.png"))


def asphalt(out, w=512, h=1024):
    """Road surface along v: worn asphalt with a dashed centre line and faint tyre tracks."""
    base = 0.09 + 0.03 * noise2(w, h, 60, 120, 3, 41, tile_x=False)
    x = np.arange(w) / w
    tracks = np.exp(-((x[None, :] - 0.3) / 0.05) ** 2) + np.exp(-((x[None, :] - 0.7) / 0.05) ** 2)
    base = base - 0.02 * tracks
    col = np.dstack([base, base, base * 1.05])
    y = np.arange(h) / h
    dash = ((y * 4) % 1 < 0.5)[:, None] & (np.abs(x - 0.5) < 0.012)[None, :]
    col[dash] = [0.75, 0.68, 0.4]
    Image.fromarray((np.clip(col, 0, 1) * 255).astype(np.uint8)).save(os.path.join(out, "asphalt.png"))


if __name__ == "__main__":
    out = sys.argv[1] if len(sys.argv) > 1 else "textures"
    os.makedirs(out, exist_ok=True)
    wood(out)
    stencil(out)
    metal(out)
    facade(out)
    asphalt(out)
