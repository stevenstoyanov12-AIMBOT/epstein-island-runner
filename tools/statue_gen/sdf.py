"""Vectorised signed-distance-field helpers (numpy). Points are (N, 3) arrays."""
import numpy as np


def length(v):
    return np.sqrt(np.einsum("ij,ij->i", v, v))


def smin(a, b, k):
    h = np.clip(0.5 + 0.5 * (b - a) / k, 0.0, 1.0)
    return b * (1 - h) + a * h - k * h * (1 - h)


def smax(a, b, k):
    return -smin(-a, -b, k)


def sphere(p, c, r):
    return length(p - np.asarray(c)) - r


def ellipsoid(p, c, r):
    q = (p - np.asarray(c)) / np.asarray(r)
    k0 = length(q)
    k1 = length(q / np.asarray(r))
    return k0 * (k0 - 1.0) / np.maximum(k1, 1e-9)


def capsule(p, a, b, r):
    a = np.asarray(a, dtype=np.float64)
    b = np.asarray(b, dtype=np.float64)
    pa = p - a
    ba = b - a
    h = np.clip(pa @ ba / (ba @ ba), 0.0, 1.0)
    return length(pa - h[:, None] * ba) - r


def round_cone(p, a, b, r1, r2):
    """Capsule whose radius tapers linearly from r1 at a to r2 at b (approximate)."""
    a = np.asarray(a, dtype=np.float64)
    b = np.asarray(b, dtype=np.float64)
    pa = p - a
    ba = b - a
    h = np.clip(pa @ ba / (ba @ ba), 0.0, 1.0)
    return length(pa - h[:, None] * ba) - (r1 + (r2 - r1) * h)


def cylinder_y(p, c, r, half_h):
    q = p - np.asarray(c)
    dx = np.sqrt(q[:, 0] ** 2 + q[:, 2] ** 2) - r
    dy = np.abs(q[:, 1]) - half_h
    outside = np.sqrt(np.maximum(dx, 0) ** 2 + np.maximum(dy, 0) ** 2)
    return outside + np.minimum(np.maximum(dx, dy), 0.0)


def torus_y(p, c, R, r):
    q = p - np.asarray(c)
    xz = np.sqrt(q[:, 0] ** 2 + q[:, 2] ** 2) - R
    return np.sqrt(xz ** 2 + q[:, 1] ** 2) - r


def rot_xyz(pitch, yaw, roll):
    """Rotation matrix (degrees). Applied as world = R @ local."""
    x, y, z = np.radians([pitch, yaw, roll])
    rx = np.array([[1, 0, 0], [0, np.cos(x), -np.sin(x)], [0, np.sin(x), np.cos(x)]])
    ry = np.array([[np.cos(y), 0, np.sin(y)], [0, 1, 0], [-np.sin(y), 0, np.cos(y)]])
    rz = np.array([[np.cos(z), -np.sin(z), 0], [np.sin(z), np.cos(z), 0], [0, 0, 1]])
    return ry @ rx @ rz


# --- value noise -----------------------------------------------------------

def _hash(ix, iy, iz, seed):
    n = (ix * 73856093) ^ (iy * 19349663) ^ (iz * 83492791) ^ (seed * 2654435761)
    n = n.astype(np.uint64) & np.uint64(0xFFFFFFFF)
    n = (n ^ (n >> np.uint64(13))) * np.uint64(1274126177)
    n = n & np.uint64(0xFFFFFFFF)
    n = n ^ (n >> np.uint64(16))
    return (n & np.uint64(0xFFFF)).astype(np.float64) / 65535.0


def noise(p, seed=0):
    """Smooth value noise in [-1, 1]."""
    fl = np.floor(p)
    f = p - fl
    i = fl.astype(np.int64)
    u = f * f * (3 - 2 * f)
    out = 0.0
    for dx in (0, 1):
        wx = u[:, 0] if dx else 1 - u[:, 0]
        for dy in (0, 1):
            wy = u[:, 1] if dy else 1 - u[:, 1]
            for dz in (0, 1):
                wz = u[:, 2] if dz else 1 - u[:, 2]
                out = out + wx * wy * wz * _hash(i[:, 0] + dx, i[:, 1] + dy, i[:, 2] + dz, seed)
    return out * 2.0 - 1.0


def fbm(p, octaves=3, seed=0):
    total, amp, norm = 0.0, 1.0, 0.0
    for o in range(octaves):
        total = total + amp * noise(p * (2.0 ** o), seed + o * 17)
        norm += amp
        amp *= 0.5
    return total / norm
