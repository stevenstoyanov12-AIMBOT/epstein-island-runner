"""Signed-distance model of a life-size veiled marble statue with bullet damage.

Units are metres, +Y up, the figure faces -Z and stands on a plinth whose top is y = 0.
Layers, inside out: body -> gown (to the floor) -> mantle (over the arms) -> veil (over the head).
"""
import numpy as np

from sdf import (capsule, ellipsoid, fbm, length, rot_xyz, round_cone, smax, smin,
                 sphere, torus_y)


def box(p, c, half):
    q = np.abs(p - np.asarray(c)) - np.asarray(half)
    return length(np.maximum(q, 0.0)) + np.minimum(q.max(1), 0.0)


def ring_folds(p, axis_xz, top_y, seed, n1, n2, warp_amt):
    """Vertical drapery folds hanging from `top_y`, around a vertical axis. Returns (ridge, depth below top)."""
    rel_x = p[:, 0] - axis_xz[0]
    rel_z = p[:, 2] - axis_xz[1]
    theta = np.arctan2(rel_x, -rel_z)
    s = np.clip(top_y - p[:, 1], 0.0, None)
    warp = warp_amt * fbm(p * np.array([4.0, 1.5, 4.0]), 3, seed) + 1.5 * s
    phase = seed * 1.618
    r1 = np.abs(np.sin(theta * n1 + warp + phase))
    r2 = np.abs(np.sin(theta * n2 + 1.3 + 1.6 * warp + 2 * phase))
    # vary fold strength around the figure so the drapery is not uniform
    strength = 0.55 + 0.45 * fbm(np.stack([theta * 1.3, s * 2.0, np.zeros_like(s)], 1), 2, seed + 7)
    return (0.7 * r1 + 0.3 * r2) * strength, s, theta


class VeiledStatue:
    bounds_min = np.array([-0.46, -0.16, -0.42])
    bounds_max = np.array([0.46, 1.86, 0.40])

    def __init__(self, seed=1, head_yaw=18.0, head_pitch=16.0, head_roll=8.0, damage=True):
        self.seed = seed
        self.head_c = np.array([0.0, 1.625, -0.02])
        self.head_R = rot_xyz(head_pitch, head_yaw, head_roll)
        self.crown = self.head_c + self.head_R @ np.array([0.0, 0.11, 0.01])
        self.damage = damage
        self.hits = []
        self.chunks = []

    # -- anatomy ------------------------------------------------------------------

    def face(self, q):
        """Detailed head in head-local space. Face looks toward -Z, eyes closed."""
        d = ellipsoid(q, (0, 0.022, 0.012), (0.074, 0.094, 0.094))                          # cranium
        d = smin(d, ellipsoid(q, (0, -0.032, -0.032), (0.050, 0.070, 0.068)), 0.03)          # face mass
        d = smin(d, ellipsoid(q, (0, -0.092, -0.070), (0.020, 0.018, 0.020)), 0.025)         # chin
        for s in (-1, 1):
            d = smin(d, ellipsoid(q, (s * 0.037, -0.028, -0.072), (0.019, 0.022, 0.018)), 0.03)   # cheeks
            d = smin(d, ellipsoid(q, (s * 0.043, -0.068, -0.040), (0.015, 0.028, 0.030)), 0.035)  # jaw
        d = smin(d, capsule(q, (-0.036, 0.014, -0.080), (0.036, 0.014, -0.080), 0.008), 0.02)    # brow
        for s in (-1, 1):
            d = smax(d, -ellipsoid(q, (s * 0.029, 0.000, -0.100), (0.017, 0.010, 0.012)), 0.012)  # socket
            d = smin(d, ellipsoid(q, (s * 0.029, -0.004, -0.087), (0.014, 0.008, 0.008)), 0.008)  # lid
        d = smin(d, round_cone(q, (0, 0.006, -0.088), (0, -0.038, -0.114), 0.006, 0.009), 0.014)  # nose
        for s in (-1, 1):
            d = smin(d, sphere(q, (s * 0.0095, -0.041, -0.100), 0.0065), 0.008)
            d = smin(d, round_cone(q, (s * 0.017, -0.060, -0.093), (0.0, -0.058, -0.099), 0.0025, 0.0045), 0.007)
            d = smin(d, round_cone(q, (s * 0.014, -0.069, -0.092), (0.0, -0.071, -0.097), 0.003, 0.005), 0.008)
        d = smax(d, -capsule(q, (-0.017, -0.0645, -0.104), (0.017, -0.0645, -0.104), 0.0012), 0.0015)
        # braided band and waved hair, visible through the veil
        band = torus_y(q, (0, 0.045, 0.012), 0.074, 0.013)
        band = band - 0.0025 * np.abs(np.sin(np.arctan2(q[:, 0], q[:, 2]) * 22 + q[:, 1] * 40))
        d = smin(d, band, 0.02)
        hair = ellipsoid(q, (0, 0.030, 0.020), (0.082, 0.096, 0.100))
        hair = hair - 0.002 * np.abs(np.sin(q[:, 1] * 120 + np.arctan2(q[:, 0], -q[:, 2]) * 4))
        hair = smax(hair, -(q[:, 2] + 0.045 - 0.35 * np.maximum(q[:, 1] - 0.01, 0)), 0.01)
        return smin(d, hair, 0.02)

    def head(self, p):
        q = (p - self.head_c) @ self.head_R
        d = ellipsoid(q, (0, -0.005, -0.01), (0.085, 0.12, 0.11))  # cheap bound away from the head
        near = length(q) < 0.17
        if near.any():
            d[near] = self.face(q[near])
        return d, q

    def arms(self, p):
        """Arms folded across the waist, hidden under the mantle."""
        d = np.full(len(p), 10.0)
        for s in (-1, 1):
            sh, el = (s * 0.165, 1.305, 0.015), (s * 0.19, 1.09, 0.0)
            wr = (-s * 0.04, 1.06, -0.14)
            d = smin(d, round_cone(p, sh, el, 0.05, 0.04), 0.02)
            d = smin(d, round_cone(p, el, wr, 0.04, 0.032), 0.02)
        return d

    def torso(self, p):
        d = ellipsoid(p, (0, 1.28, 0.005), (0.175, 0.19, 0.115))                              # chest
        for s in (-1, 1):
            d = smin(d, capsule(p, (0, 1.37, 0.015), (s * 0.165, 1.315, 0.015), 0.052), 0.10)  # sloped shoulders
        d = smin(d, capsule(p, (0, 1.34, 0.015), (0, 1.54, -0.012), 0.046), 0.06)            # neck
        for s in (-1, 1):
            d = smin(d, ellipsoid(p, (s * 0.066, 1.255, -0.075), (0.062, 0.058, 0.045)), 0.05)  # breast
        d = smin(d, ellipsoid(p, (0, 1.08, 0.0), (0.15, 0.16, 0.11)), 0.08)                   # waist
        d = smin(d, ellipsoid(p, (0.01, 0.92, 0.01), (0.19, 0.14, 0.13)), 0.08)               # hips
        return d

    def skirt(self, p):
        """Long robe falling to the floor; the left knee is bent and presses the cloth forward."""
        y = p[:, 1]
        t = np.clip((0.95 - y) / 0.95, 0.0, 1.0)
        pool = np.clip((0.07 - y) / 0.07, 0.0, 1.0) ** 2 * 0.05
        rx = 0.18 + 0.09 * t ** 1.4 + pool
        rz = 0.13 + 0.10 * t ** 1.4 + pool
        d = (np.sqrt((p[:, 0] / rx) ** 2 + ((p[:, 2] + 0.02 * t) / rz) ** 2) - 1.0) * np.minimum(rx, rz)
        d = smax(d, -y, 0.01)
        d = smax(d, y - 1.0, 0.05)
        knee = ellipsoid(p, (0.09, 0.52, -0.10), (0.09, 0.14, 0.10))
        shin = ellipsoid(p, (0.09, 0.28, -0.07), (0.08, 0.22, 0.10))
        return smin(d, smin(knee, shin, 0.05), 0.08)

    # -- full field -----------------------------------------------------------------

    def undamaged(self, p):
        head, q = self.head(p)
        torso = self.torso(p)
        body = smin(head, torso, 0.025)

        # gown: follows the torso, then the skirt to the floor, with deep vertical folds
        gown = smin(torso - 0.006, self.skirt(p), 0.06)
        ridge, s, _ = ring_folds(p, (0.0, 0.0), 1.2, self.seed + 1, 5.0, 12.0, 2.4)
        amp = 0.004 + 0.034 * np.clip(s / 1.1, 0, 1) ** 1.3
        gown = gown - amp * ridge
        gown = smax(gown, -p[:, 1], 0.004)

        # mantle: wraps the shoulders and folded arms, hem swoops lower at the back
        mantle_env = smin(smin(torso, self.arms(p), 0.08), self.skirt(p) + 0.01, 0.12) - 0.018
        ridge, s, theta = ring_folds(p, (0.0, -0.02), 1.42, self.seed + 2, 4.0, 9.0, 2.6)
        mantle = mantle_env - (0.004 + 0.028 * np.clip(s / 0.7, 0, 1)) * ridge
        hem = 0.72 + 0.10 * np.cos(theta) + 0.05 * np.sin(2 * theta + 0.6) + 0.02 * np.sin(5 * theta)
        mantle = smax(mantle, hem - p[:, 1], 0.006)
        mantle = smax(mantle, p[:, 1] - 1.47, 0.03)

        # veil: stays close to the face so the features read through, drapes to the chest
        fc = np.array([0.0, -0.035, -0.09])
        face_w = np.clip((length(q - fc) - 0.06) / 0.07, 0.0, 1.0)
        head_env = ellipsoid(q, (0, -0.005, -0.005), (0.088, 0.118, 0.110))
        head_env = smin(head_env, ellipsoid(q, (0, -0.045, -0.07), (0.05, 0.06, 0.06)), 0.03)
        veil_env = smin(head_env, np.minimum(torso, mantle_env) - 0.006, 0.075)
        base = body + (veil_env - body) * face_w
        ridge, s, theta = ring_folds(p, self.crown[[0, 2]], self.crown[1], self.seed + 3, 9.0, 19.0, 1.6)
        amp = (0.0008 + 0.02 * np.clip((s - 0.05) / 0.45, 0, 1) ** 1.2) * (0.15 + 0.85 * face_w)
        veil = base - 0.003 - amp * ridge
        veil_hem = 1.06 + 0.07 * np.cos(theta) + 0.03 * np.sin(theta * 3 + 0.4) + 0.015 * np.sin(theta * 7 + 1.0)
        veil = smax(veil, veil_hem - p[:, 1], 0.004)

        d = np.minimum(np.minimum(body, gown), np.minimum(mantle, veil))
        plinth = box(p, (0, -0.075, 0), (0.37, 0.075, 0.33)) - 0.008
        plinth = smax(plinth, -box(p, (0, -0.075, 0), (0.40, 0.012, 0.36)), 0.004)  # groove
        return np.minimum(d, plinth)

    def plan_damage(self, rng, count=26):
        """Bullet impacts on the front of the statue (sparing the face) plus two broken chunks."""
        g = np.stack(np.meshgrid(np.linspace(-0.4, 0.4, 80), np.linspace(0.0, 1.78, 180),
                                 np.linspace(-0.40, 0.05, 45), indexing="ij"), -1).reshape(-1, 3)
        d = self.undamaged(g)
        surf = g[np.abs(d) < 0.006]
        face_c = self.head_c + self.head_R @ np.array([0.0, -0.035, -0.09])
        surf = surf[length(surf - face_c) > 0.09]
        rng.shuffle(surf)
        picked = []
        for pt in surf:
            if len(picked) >= count:
                break
            if all(np.linalg.norm(pt - o) > 0.07 for o in picked):
                picked.append(pt)
        picked = np.array(picked)
        e = 1e-3
        n = np.stack([self.undamaged(picked + o * e) - self.undamaged(picked - o * e) for o in np.eye(3)], -1)
        n /= np.linalg.norm(n, axis=1, keepdims=True)
        for pt, nn in zip(picked, n):
            pt = pt - nn * self.undamaged(pt[None])[0]
            t1 = np.cross(nn, [0.0, 1.0, 0.0])
            if np.linalg.norm(t1) < 1e-3:
                t1 = np.cross(nn, [1.0, 0.0, 0.0])
            t1 /= np.linalg.norm(t1)
            t2 = np.cross(nn, t1)
            r = rng.uniform(0.007, 0.014)
            cracks = []
            for _ in range(rng.integers(2, 5)):
                a = rng.uniform(0, 2 * np.pi)
                pts = [pt + (np.cos(a) * t1 + np.sin(a) * t2) * r * 0.8]
                for _ in range(3):
                    a += rng.normal(0, 0.45)
                    pts.append(pts[-1] + (np.cos(a) * t1 + np.sin(a) * t2) * rng.uniform(0.01, 0.025))
                cracks.append(pts)
            frags = [pt + (np.cos(b) * t1 + np.sin(b) * t2) * rng.uniform(1.4, 2.4) * r
                     for b in rng.uniform(0, 2 * np.pi, rng.integers(2, 6))]
            self.hits.append(dict(p=pt, n=nn, r=r, cracks=cracks, frags=frags,
                                  frag_r=rng.uniform(0.003, 0.005, len(frags))))
        self.chunks.append((np.array([0.26, 0.74, -0.14]), 0.07))   # mantle hem shot away
        self.chunks.append((np.array([-0.37, 0.0, -0.33]), 0.09))   # plinth corner

    def __call__(self, p):
        d = self.undamaged(p)
        if not self.damage:
            return d
        for c, r in self.chunks:
            near = length(p - c) < r + 0.05
            if near.any():
                rough = sphere(p[near], c, r) + 0.015 * fbm(p[near] * 40.0, 3, self.seed + 9)
                d[near] = smax(d[near], -rough, 0.002)
        for h in self.hits:
            near = length(p - h["p"]) < 0.1
            if not near.any():
                continue
            pp, dd, r = p[near], d[near], h["r"]
            jag = 0.25 * r * fbm(pp * 400.0, 2, self.seed + 3)
            core = sphere(pp, h["p"] + h["n"] * 0.2 * r, r * 0.75) + jag
            spall = sphere(pp, h["p"] + h["n"] * (2.6 * r - 0.003), 2.6 * r) + jag
            dd = smax(dd, -np.minimum(core, spall), 0.0015)
            for line in h["cracks"]:
                for a, b in zip(line[:-1], line[1:]):
                    dd = np.maximum(dd, -capsule(pp, a, b, 0.0016))
            for f, fr in zip(h["frags"], h["frag_r"]):
                dd = smax(dd, -sphere(pp, f, fr), 0.001)
            d[near] = dd
        return d
