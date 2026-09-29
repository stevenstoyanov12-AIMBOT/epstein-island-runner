"""Procedural textures for the autumn tree.

bark_albedo.png / bark_normal.png / bark_height.png  (seamless around the trunk, 1024 x 2048)
leaves_atlas.png / leaves_normal.png                 (2 x 2 maple leaves with veins, alpha cut-out)
"""
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "statue_gen"))
from sdf import fbm  # noqa: E402


def normal_map(height, strength):
    gy, gx = np.gradient(height)
    n = np.dstack([-gx * strength, gy * strength, np.ones_like(height)])
    n /= np.linalg.norm(n, axis=2, keepdims=True)
    return ((n * 0.5 + 0.5) * 255).astype(np.uint8)


def save(arr, path):
    Image.fromarray(arr).save(path)
    print(path)


# --- bark ------------------------------------------------------------------------

def bark(out, w=1024, h=2048):
    """Deep vertical fissures with plates of rough bark and patches of lichen."""
    u = (np.arange(w) + 0.5) / w
    v = (np.arange(h) + 0.5) / h
    U, V = np.meshgrid(u, v)
    a = 2 * np.pi * U
    # sample 3D noise on a cylinder so the texture wraps around the trunk without a seam
    R = 1.1
    def cyl(freq_r, freq_v, oct_, seed):
        p = np.stack([R * np.cos(a) * freq_r, R * np.sin(a) * freq_r, V * freq_v], -1).reshape(-1, 3)
        return fbm(p, oct_, seed).reshape(h, w)

    warp = cyl(1.2, 2.0, 3, 1)
    plates = cyl(2.0, 3.0, 4, 2)
    ridges = 1 - np.abs(np.sin(a * 6 + 4.0 * warp + 3.0 * plates))            # vertical fissure pattern
    plate = np.clip((ridges - 0.22) / 0.35, 0, 1)
    plate = plate * plate * (3 - 2 * plate)                                     # flat-topped bark plates
    cracks = np.clip(1 - np.abs(cyl(1.5, 40, 2, 3)) * 12, 0, 1) * np.clip(cyl(3, 6, 2, 6) * 3, 0, 1)  # sparse breaks across plates
    grain = cyl(18, 30, 3, 4)
    rough = cyl(8, 12, 3, 5)
    height = plate * (0.8 + 0.2 * rough) - 0.45 * cracks * plate + 0.06 * grain
    # make it tile along the length: crossfade with a copy shifted by half the height
    t = np.abs(np.linspace(-1, 1, h))[:, None]
    height = height * (1 - t) + np.roll(height, h // 2, 0) * t
    height = (height - height.min()) / np.ptp(height)

    lichen = np.clip(cyl(4, 5, 3, 7) * 3.0 - 1.4, 0, 1) * np.clip(height * 1.4 - 0.2, 0, 1)
    dark = np.array([0.06, 0.04, 0.03])
    mid = np.array([0.22, 0.15, 0.10])
    light = np.array([0.40, 0.32, 0.25])
    moss = np.array([0.42, 0.46, 0.33])
    hh = height[..., None]
    col = np.where(hh < 0.5, dark + (mid - dark) * (hh / 0.5), mid + (light - mid) * ((hh - 0.5) / 0.5))
    col = col * (0.9 + 0.2 * grain[..., None])
    col = col * (1 - 0.6 * lichen[..., None]) + moss * 0.6 * lichen[..., None]
    save((np.clip(col, 0, 1) ** (1 / 2.2) * 255).astype(np.uint8), os.path.join(out, "bark_albedo.png"))
    save((height * 255).astype(np.uint8), os.path.join(out, "bark_height.png"))
    save(normal_map(height, 60.0), os.path.join(out, "bark_normal.png"))


# --- leaves ----------------------------------------------------------------------

PALETTES = [  # (base, edge, vein) per atlas cell
    ((0.80, 0.14, 0.06), (0.45, 0.05, 0.03), (0.95, 0.55, 0.30)),   # red maple
    ((0.93, 0.42, 0.06), (0.62, 0.18, 0.04), (1.00, 0.75, 0.40)),   # orange
    ((0.96, 0.70, 0.10), (0.80, 0.36, 0.05), (1.00, 0.90, 0.55)),   # amber / gold
    ((0.88, 0.78, 0.20), (0.55, 0.50, 0.10), (0.98, 0.95, 0.60)),   # yellow, a little green left
]


def maple_outline(cx, cy, size, rng):
    """Five-lobed maple outline with serrated edges, stem pointing down."""
    pts = []
    lobes = [(-90, 1.0), (-38, 0.86), (-142, 0.86), (12, 0.55), (-192, 0.55)]  # degrees, length
    for deg in np.linspace(-270, 90, 900):
        r = 0.18
        for ang, ln in lobes:
            dd = (deg - ang + 180) % 360 - 180
            r = max(r, ln * np.exp(-(dd / 17.0) ** 2) ** 0.55)
        r *= 1 + 0.04 * np.sin(np.radians(deg) * 38) + 0.02 * rng.normal()
        # notch where the stem joins
        r *= 1 - 0.5 * np.exp(-(((deg - 90 + 180) % 360 - 180) / 14.0) ** 2)
        pts.append((cx + size * r * np.cos(np.radians(deg)), cy + size * r * np.sin(np.radians(deg))))
    return pts, lobes


def leaves(out, cell=1024):
    rng = np.random.default_rng(5)
    S = 2  # supersample
    img = Image.new("RGBA", (2 * cell * S, 2 * cell * S), (0, 0, 0, 0))
    height = Image.new("L", img.size, 0)
    dh = ImageDraw.Draw(height)
    for k, (base, edge, vein) in enumerate(PALETTES):
        ox, oy = (k % 2) * cell * S, (k // 2) * cell * S
        cx, cy = ox + cell * S * 0.5, oy + cell * S * 0.56
        size = cell * S * 0.47
        outline, lobes = maple_outline(cx, cy, size, rng)
        mask = Image.new("L", img.size, 0)
        ImageDraw.Draw(mask).polygon(outline, fill=255)

        # colour: base in the middle fading to the edge colour, with blotches
        yy, xx = np.mgrid[0:cell * S, 0:cell * S]
        d = np.sqrt((xx - cell * S * 0.5) ** 2 + (yy - cell * S * 0.56) ** 2) / size
        p = np.stack([xx / 180.0 + k * 10, yy / 180.0, np.zeros_like(xx, float)], -1).reshape(-1, 3)
        blot = fbm(p, 4, 11 + k).reshape(cell * S, cell * S)
        f = np.clip(d * 0.9 + 0.35 * blot, 0, 1)[..., None]
        col = np.array(base) * (1 - f) + np.array(edge) * f
        spots = np.clip(fbm(p * 4, 2, 21 + k).reshape(cell * S, cell * S) * 3 - 2.2, 0, 1)[..., None]
        col = col * (1 - 0.5 * spots)
        rgb = (np.clip(col, 0, 1) * 255).astype(np.uint8)  # palettes are already sRGB
        tile = Image.fromarray(rgb, "RGB").convert("RGBA")
        full = Image.new("RGBA", img.size, (0, 0, 0, 0))
        full.paste(tile, (ox, oy))
        img.paste(full, (0, 0), mask)

        # veins: primary to each lobe tip, secondary branching off them
        dv = ImageDraw.Draw(img)
        vc = tuple(int(c * 255) for c in vein) + (255,)
        base_pt = (cx, cy + size * 0.05)
        for ang, ln in lobes:
            tip = (cx + size * ln * 0.92 * np.cos(np.radians(ang)), cy + size * ln * 0.92 * np.sin(np.radians(ang)))
            dv.line([base_pt, tip], fill=vc, width=int(7 * S * ln))
            dh.line([base_pt, tip], fill=255, width=int(7 * S * ln))
            for t in np.linspace(0.25, 0.8, 5):
                px = base_pt[0] + (tip[0] - base_pt[0]) * t
                py = base_pt[1] + (tip[1] - base_pt[1]) * t
                for side in (-1, 1):
                    sa = np.radians(ang + side * 42)
                    L = size * ln * 0.28 * (1 - t * 0.6)
                    end = (px + L * np.cos(sa), py + L * np.sin(sa))
                    dv.line([(px, py), end], fill=vc[:3] + (170,), width=int(3 * S))
                    dh.line([(px, py), end], fill=150, width=int(3 * S))
        # stem
        stem_end = (cx + size * 0.05, cy + size * 0.55)
        dv.line([base_pt, stem_end], fill=tuple(int(c * 0.7) for c in vc[:3]) + (255,), width=int(9 * S))
        # re-apply the outline alpha so veins don't spill past the edge (stem stays)
        a = np.array(img)[..., 3]
        m = np.array(mask)
        stem = np.zeros_like(m)
        sd = Image.new("L", img.size, 0)
        ImageDraw.Draw(sd).line([base_pt, stem_end], fill=255, width=int(9 * S))
        stem = np.array(sd)
        region = np.zeros_like(m, bool)
        region[oy:oy + cell * S, ox:ox + cell * S] = True
        arr = np.array(img)
        arr[..., 3] = np.where(region, np.maximum(np.minimum(a, m), stem), arr[..., 3])
        img = Image.fromarray(arr, "RGBA")

    img = img.resize((2 * cell, 2 * cell), Image.LANCZOS)
    img.save(os.path.join(out, "leaves_atlas.png"))
    print(os.path.join(out, "leaves_atlas.png"))
    hgt = np.array(height.filter(ImageFilter.GaussianBlur(3 * S)).resize((2 * cell, 2 * cell), Image.LANCZOS)) / 255.0
    save(normal_map(hgt, -8.0), os.path.join(out, "leaves_normal.png"))


def grass(out, w=256, h=512):
    """Grass blade strip: u picks a blade tint, v runs from root (dark) to tip (pale, a bit dry)."""
    u = np.linspace(0, 1, w)[None, :, None]
    v = np.linspace(1, 0, h)[:, None, None]           # image top = blade tip
    root = np.array([0.10, 0.20, 0.06])
    mid = np.array([0.28, 0.45, 0.14]) * (0.85 + 0.3 * u)
    tip = np.array([0.62, 0.62, 0.30]) * (0.9 + 0.2 * u)
    col = np.where(v < 0.6, root + (mid - root) * (v / 0.6), mid + (tip - mid) * ((v - 0.6) / 0.4))
    streak = fbm(np.stack(np.meshgrid(np.arange(w) / 6.0, np.arange(h) / 60.0, [0.0]), -1).reshape(-1, 3), 2, 31)
    col = col * (0.9 + 0.12 * streak.reshape(h, w)[..., None])
    save((np.clip(col, 0, 1) * 255).astype(np.uint8), os.path.join(out, "grass.png"))


if __name__ == "__main__":
    out = sys.argv[1] if len(sys.argv) > 1 else "textures"
    os.makedirs(out, exist_ok=True)
    bark(out)
    leaves(out)
    grass(out)
