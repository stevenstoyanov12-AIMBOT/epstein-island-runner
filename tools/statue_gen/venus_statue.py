"""Signed-distance model of a life-size marble Venus (Milo-style) on a block plinth.

Units are metres, +Y up, the figure faces -Z. The plinth top is y = 0 and its foot is y = -PLINTH_H.
Figure: bare torso in contrapposto (weight on her right leg, left knee forward), both arms broken off,
heavy drapery from a rolled edge low on the hips down to a rough rock base, hair in waves with a bun.
"""
import numpy as np

from sdf import capsule, ellipsoid, fbm, length, rot_xyz, round_cone, smax, smin, sphere, torus_y

SCALE = 1.14          # the figure is modelled at 1.75 m and scaled up to just over 2 m
PLINTH_H = 0.80
PLINTH_HALF = (0.44, 0.40)   # x, z half sizes of the plinth body


def box(p, c, half):
    q = np.abs(p - np.asarray(c)) - np.asarray(half)
    return length(np.maximum(q, 0.0)) + np.minimum(q.max(1), 0.0)


def rot_z(p, pivot, deg):
    a = np.radians(deg)
    q = p - np.asarray(pivot)
    c, s = np.cos(a), np.sin(a)
    if np.ndim(a):
        c, s = c[:, None], s[:, None]
    x = q[:, 0:1] * c - q[:, 1:2] * s
    y = q[:, 0:1] * s + q[:, 1:2] * c
    return np.concatenate([x, y, q[:, 2:3]], 1) + np.asarray(pivot)


class VenusStatue:
    bounds_min = np.array([-0.50, -PLINTH_H - 0.01, -0.46])
    bounds_max = np.array([0.50, 2.08, 0.46])

    def __init__(self, seed=3):
        self.seed = seed
        # head pose (figure space): turned to her left, tilted down, rolled toward the free leg
        self.head_c = np.array([0.015, 1.625, -0.035])
        self.head_R = rot_xyz(10.0, -16.0, -7.0)

    # -- figure space helpers -------------------------------------------------------

    def pose(p):
        """Contrapposto warp: hips tilt one way, shoulders the other, the spine sways between."""
        y = p[:, 1]
        t = np.clip((y - 0.85) / 0.6, 0.0, 1.0)
        tilt = 5.0 * (1 - t) - 4.5 * t                 # hip roll -> opposite shoulder roll
        sway = 0.025 * np.sin(np.clip((y - 0.8) / 0.75, 0, 1) * np.pi)
        q = rot_z(p, (0.0, 0.98, 0.0), -tilt * (y > 0.6))
        q[:, 0] -= sway * (y > 0.6)
        return q
    pose = staticmethod(pose)

    # -- anatomy ---------------------------------------------------------------------

    def face(self, q):
        """Head in head-local space, looking toward -Z: classical oval face, straight Greek nose,
        almond eyes under a soft brow, full lips, hair parted in the middle and waved back over the ears into a bun."""
        x, y, z = q[:, 0], q[:, 1], q[:, 2]
        d = ellipsoid(q, (0, 0.022, 0.012), (0.071, 0.092, 0.095))                            # cranium
        d = smin(d, ellipsoid(q, (0, -0.028, -0.036), (0.053, 0.074, 0.062)), 0.035)          # face oval
        for s in (-1, 1):
            d = smin(d, capsule(q, (s * 0.054, -0.028, 0.002), (s * 0.008, -0.090, -0.062), 0.012), 0.03)  # jaw line
            d = smin(d, ellipsoid(q, (s * 0.038, -0.022, -0.070), (0.020, 0.018, 0.016)), 0.025)  # cheekbones
        d = smin(d, ellipsoid(q, (0, -0.091, -0.068), (0.0135, 0.012, 0.0125)), 0.018)         # chin
        d = smin(d, ellipsoid(q, (0, 0.040, -0.072), (0.050, 0.035, 0.030)), 0.03)             # forehead
        d = smin(d, ellipsoid(q, (0, -0.056, -0.072), (0.026, 0.022, 0.016)), 0.025)           # muzzle under the lips
        # eyes: socket under the brow, a full eyeball with a carved iris ring and drilled pupil, thin almond lids
        for s in (-1, 1):
            ex = s * 0.0295
            ec = np.array([ex, -0.0005, -0.0715])                                                # eyeball centre
            d = smax(d, -ellipsoid(q, (ex, 0.0015, -0.100), (0.0225, 0.0145, 0.017)), 0.008)      # socket
            ball = sphere(q, ec, 0.0195)
            # iris ring and pupil, looking slightly down and toward the free leg
            look = np.array([-0.08, -0.12, -1.0]); look /= np.linalg.norm(look)
            rel = q - ec
            along = rel @ look
            rho = length(rel - along[:, None] * look)
            # broad shapes that survive the game mesh: a recessed iris disc and a drilled pupil
            disc = np.maximum(rho - 0.0060, 0.0182 - along)                                      # 1.3 mm deep
            ball = smax(ball, -disc, 0.0012)
            ball = smax(ball, -sphere(q, ec + look * 0.0212, 0.0030), 0.0008)                    # pupil
            d = smin(d, ball, 0.0015)
            # almond opening: upper arc taller and peaking toward the nose, lower arc flatter
            dx = (x - ex) * s                                                                    # + toward the temple
            w = 0.0150
            t = np.clip(1 - ((dx + 0.0015) / w) ** 2, 0, None)
            yc = -0.0008 + 0.0012 * (dx / w)                                                     # outer corner a touch higher
            above = (y - yc) - 0.0064 * t ** 0.8                                                 # > 0 above the opening
            below = (yc - y) - 0.0046 * t ** 0.9                                                 # > 0 below it
            outside = np.maximum(above, below)
            lid = sphere(q, ec, 0.0207)                                                          # lids wrap the ball
            lid = smax(lid, -outside, 0.0012)
            lid = smax(lid, ellipsoid(q, (ex, 0.0, -0.086), (0.0215, 0.0165, 0.016)), 0.002)    # keep them round the eye
            d = smin(d, lid, 0.0025)
            # upper lid crease and the soft fold under the lower lid
            crease = np.abs(ellipsoid(q, (ex + s * 0.001, 0.0005, -0.0875), (0.0185, 0.0118, 0.0125))) - 0.0007
            crease = smax(crease, -(y - yc - 0.0075), 0.001)
            d = smax(d, -crease, 0.0012)
            d = smin(d, ellipsoid(q, (ex + s * 0.002, -0.0105, -0.0905), (0.0150, 0.0040, 0.0050)), 0.006)  # soft under-eye
            d = smax(d, -sphere(q, (ex - s * 0.0155, -0.0012, -0.0905), 0.0016), 0.0012)         # tear duct corner
        d = smin(d, capsule(q, (-0.045, 0.0165, -0.088), (0.045, 0.0165, -0.088), 0.006), 0.016)  # soft brow
        # nose: one straight line with the forehead, narrow bridge, rounded tip, wings, flat base
        nose = round_cone(q, (0, 0.016, -0.0935), (0, -0.0355, -0.1215), 0.0066, 0.0095)
        for s in (-1, 1):
            nose = smin(nose, ellipsoid(q, (s * 0.0108, -0.0375, -0.1065), (0.0068, 0.0058, 0.0068)), 0.004)
        nose = smax(nose, -(q[:, 1] + 0.0425), 0.003)
        d = smin(d, nose, 0.006)
        for s in (-1, 1):
            d = smax(d, -sphere(q, (s * 0.0075, -0.0425, -0.1055), 0.0028), 0.002)              # nostrils
        # mouth: cupid's bow upper lip, fuller lower lip, parting line, deep corners
        d = smax(d, -capsule(q, (0, -0.046, -0.1065), (0, -0.054, -0.1035), 0.0022), 0.003)    # philtrum
        for s in (-1, 1):
            d = smin(d, ellipsoid(q, (s * 0.0062, -0.0582, -0.0975), (0.0085, 0.0036, 0.0050)), 0.003)  # upper lip
        d = smin(d, ellipsoid(q, (0, -0.0662, -0.0955), (0.0115, 0.0045, 0.0058)), 0.004)      # lower lip
        d = smax(d, -capsule(q, (-0.0155, -0.0622, -0.0935), (0.0155, -0.0622, -0.0935), 0.0009), 0.0012)
        for s in (-1, 1):
            d = smax(d, -sphere(q, (s * 0.0165, -0.062, -0.092), 0.0018), 0.003)
        d = smax(d, -ellipsoid(q, (0, -0.077, -0.096), (0.012, 0.004, 0.004)), 0.006)           # under-lip dip

        # hair: thick waved mass, centre parting, swept back over the ears to a bun
        hair = ellipsoid(q, (0, 0.028, 0.016), (0.084, 0.104, 0.108))
        hair = smin(hair, ellipsoid(q, (0, -0.010, 0.020), (0.082, 0.060, 0.090)), 0.03)       # over the ears
        side = np.abs(x) + 1e-4
        sweep = np.arctan2(y - 0.03, side)                                                     # angle from the head centre
        wave = np.sin(sweep * 13 + 6.0 * np.sin(z * 40 + 1.0))
        calm = np.clip(side / 0.012, 0, 1)                                                      # no spikes at the parting
        hair = hair - 0.006 * calm * (1 - np.abs(wave)) ** 2 + 0.002
        hair = hair - 0.0006 * calm * np.abs(np.sin(sweep * 30 + z * 50))                               # strands
        hair = smax(hair, -ellipsoid(q, (0, -0.032, -0.112), (0.061, 0.098, 0.074)), 0.006)     # face window
        hair = smax(hair, -(y + 0.050 - 0.55 * np.maximum(z, 0)), 0.01)                         # hem over ears to nape
        hair = smax(hair, -capsule(q, (0, 0.125, -0.07), (0, 0.11, 0.06), 0.0028), 0.003)       # parting
        bun = ellipsoid(q, (0, 0.040, 0.108), (0.044, 0.037, 0.035))
        ang = np.arctan2(y - 0.040, x)
        rr = length(q - np.array([0, 0.040, 0.108]))
        bun = bun - 0.004 * (1 - np.abs(np.sin(ang * 3 + rr * 160))) ** 2
        hair = smin(hair, bun, 0.01)
        return smin(d, hair, 0.006)

    def head(self, p):
        q = (p - self.head_c) @ self.head_R
        d = ellipsoid(q, (0, 0.0, 0.0), (0.095, 0.125, 0.135))   # cheap bound away from the head
        near = length(q) < 0.2
        if near.any():
            d[near] = self.face(q[near])
        return d

    def torso(self, p):
        d = ellipsoid(p, (0, 1.29, 0.004), (0.152, 0.17, 0.103))                               # ribcage
        d = smin(d, ellipsoid(p, (0, 1.10, -0.004), (0.128, 0.13, 0.092)), 0.07)              # abdomen
        d = smin(d, ellipsoid(p, (0, 0.95, 0.012), (0.175, 0.13, 0.118)), 0.08)               # pelvis
        for s in (-1, 1):
            d = smin(d, ellipsoid(p, (s * 0.085, 0.89, 0.075), (0.088, 0.10, 0.068)), 0.05)   # buttocks
            d = smin(d, capsule(p, (0, 1.40, 0.010), (s * 0.165, 1.40, 0.010), 0.05), 0.09)   # shoulder girdle
            d = smin(d, sphere(p, (s * 0.178, 1.395, 0.005), 0.058), 0.04)                   # deltoid
            d = smin(d, ellipsoid(p, (s * 0.074, 1.262, -0.080), (0.057, 0.053, 0.046)), 0.035)  # breast
            d = smin(d, capsule(p, (s * 0.02, 1.445, -0.050), (s * 0.140, 1.455, -0.030), 0.011), 0.02)  # clavicle
            d = smin(d, ellipsoid(p, (s * 0.080, 1.34, 0.072), (0.068, 0.085, 0.030)), 0.04)  # scapula
            d = smin(d, ellipsoid(p, (s * 0.10, 1.08, -0.03), (0.03, 0.08, 0.05)), 0.04)     # obliques
        d = smin(d, capsule(p, (0, 1.43, 0.0), (0, 1.57, -0.018), 0.047), 0.05)               # neck
        for s in (-1, 1):
            d = smin(d, capsule(p, (s * 0.035, 1.55, -0.02), (s * 0.014, 1.45, -0.045), 0.010), 0.03)  # neck tendons
        d = smax(d, -capsule(p, (0, 1.42, 0.115), (0, 1.00, 0.10), 0.006), 0.012)             # spine
        d = smax(d, -capsule(p, (0, 1.20, -0.107), (0, 1.08, -0.097), 0.0035), 0.010)         # linea alba
        d = smax(d, -sphere(p, (0, 1.045, -0.100), 0.0075), 0.006)                            # navel
        return d

    def arms(self, p):
        """Broken arm stumps with rough fracture faces."""
        rough = 0.004 * fbm(p * 90.0, 2, self.seed + 21)
        # her right arm: upper arm angled down and forward, snapped a hand's width below the shoulder
        a, b = np.array([-0.17, 1.40, 0.0]), np.array([-0.245, 1.17, -0.06])
        r = round_cone(p, a, b, 0.052, 0.043)
        n = (b - a) / np.linalg.norm(b - a)
        cut = (p - (a + n * 0.12)) @ n + rough + 0.015 * (p[:, 2] + 0.03) * 10
        right = smax(r, cut, 0.004)
        # her left arm: raised slightly away from the body, broken off near the shoulder
        a, b = np.array([0.17, 1.40, 0.0]), np.array([0.27, 1.25, -0.02])
        r = round_cone(p, a, b, 0.052, 0.045)
        n = (b - a) / np.linalg.norm(b - a)
        cut = (p - (a + n * 0.055)) @ n + rough - 0.01 * (p[:, 1] - 1.33) * 10
        left = smax(r, cut, 0.004)
        return np.minimum(right, left)

    def legs(self, p):
        d = np.full(len(p), 10.0)
        # weight leg (her right, -X): nearly straight
        hip, knee, ank = (-0.085, 0.90, 0.01), (-0.075, 0.50, -0.005), (-0.065, 0.08, 0.02)
        d = smin(d, round_cone(p, hip, knee, 0.088, 0.056), 0.03)
        d = smin(d, round_cone(p, knee, ank, 0.056, 0.036), 0.03)
        # free leg (her left, +X): knee forward and across, foot raised on a step
        hip, knee, ank = (0.085, 0.88, 0.0), (0.075, 0.52, -0.17), (0.14, 0.17, -0.10)
        d = smin(d, round_cone(p, hip, knee, 0.088, 0.056), 0.03)
        d = smin(d, round_cone(p, knee, ank, 0.056, 0.036), 0.03)
        return d

    def drapery(self, p, torso):
        y = p[:, 1]
        legs = self.legs(p)
        # cloth envelope: wraps the hips and legs and flares into a pool at the base
        t = np.clip((0.95 - y) / 0.95, 0, 1)
        cone = (length(np.stack([(p[:, 0] - 0.01) / (0.15 + 0.10 * t ** 2.2),
                                 (p[:, 2] + 0.02 * t) / (0.11 + 0.09 * t ** 2.2), 0 * y], 1)) - 1.0) * 0.13
        pool = np.clip((0.12 - y) / 0.12, 0, 1) ** 2 * 0.06
        env = smin(smin(legs, cone, 0.09), torso, 0.05) - 0.018 - pool
        # top edge: low across the front and her right hip, rising to the knot over her left hip
        theta = np.arctan2(p[:, 0], -p[:, 2])       # 0 = front, +pi/2 = her left
        top = 0.90 + 0.07 * np.sin(theta) - 0.025 * np.cos(theta) + 0.05 * np.clip(-np.cos(theta), 0, 1) \
            + 0.006 * np.sin(theta * 7)
        h = y - top
        # folds: deep catenaries between the legs, vertical runs down the weight leg, diagonals to the knot
        s = np.clip(top - y, 0, None)
        warp = 1.8 * fbm(p * np.array([4.0, 1.2, 4.0]), 3, self.seed) + 1.2 * s
        r1 = np.abs(np.sin(theta * 6.0 + warp))
        r2 = np.abs(np.sin(theta * 13.0 + 2.6 * warp + 0.7))
        diag = np.abs(np.sin((y * 9.0 - theta * 2.2) * 2.0 + 1.3 * warp))
        knee = np.array([0.075, 0.52, -0.17])
        taut = np.clip(length(p - knee) / 0.14, 0, 1)          # cloth stretched flat over the knee
        free_side = np.clip(0.5 + p[:, 0] * 4, 0, 1)
        amp = (0.004 + 0.032 * np.clip(s / 0.8, 0, 1) ** 1.2) * (0.35 + 0.65 * taut)
        ridge = (0.55 * r1 + 0.25 * r2) * (1 - 0.4 * free_side) + 0.45 * diag * free_side
        cloth = env - amp * ridge
        cloth = cloth - 0.0012 * fbm(p * 70.0, 2, self.seed + 4)           # crumple
        cloth = smax(cloth, h, 0.01)
        # rolled edge: a thick tube along the top hem lying on the envelope
        tube = np.sqrt((env + 0.010) ** 2 + (h + 0.012) ** 2) - 0.017 - 0.003 * np.sin(theta * 11 + warp)
        tube = smax(tube, -h - 0.08, 0.01)
        cloth = smin(cloth, tube, 0.012)
        # knot of gathered cloth over her left hip and a fall of cloth hanging from it
        knot = ellipsoid(p, (0.15, 0.95, -0.03), (0.035, 0.04, 0.035)) - 0.004 * np.abs(np.sin(p[:, 1] * 140))
        cloth = smin(cloth, knot, 0.025)
        return smax(cloth, -y + 0.02, 0.01)

    def rock(self, p):
        """Rough rock slab the figure stands on, with a step under the free foot."""
        d = box(p, (0, 0.035, -0.01), (0.27, 0.035, 0.24)) - 0.012
        d = smin(d, box(p, (0.13, 0.09, -0.13), (0.085, 0.04, 0.08)) - 0.01, 0.015)
        return d + 0.006 * fbm(p * 25.0, 3, self.seed + 11)

    def figure(self, p):
        """Everything above the plinth, in statue space (metres)."""
        f = p / SCALE
        f[:, 1] -= 0.06 / SCALE                      # stands on the rock slab
        q = self.pose(f)
        torso = self.torso(q)
        body = smin(torso, self.arms(q), 0.02)
        body = smin(body, self.head(q), 0.02)
        cloth = self.drapery(f, torso)
        d = np.minimum(body, cloth)
        d = d - 0.00025 * fbm(f * 60.0, 2, self.seed + 30)         # faint weathering, not pitting
        d = d * SCALE
        return smin(d, self.rock(p), 0.012)

    def plinth(self, p):
        w, dz = PLINTH_HALF
        d = box(p, (0, -PLINTH_H / 2, 0), (w, PLINTH_H / 2, dz)) - 0.006
        d = smin(d, box(p, (0, -0.035, 0), (w + 0.035, 0.035, dz + 0.035)) - 0.006, 0.004)          # cap
        d = smin(d, box(p, (0, -PLINTH_H + 0.05, 0), (w + 0.04, 0.05, dz + 0.04)) - 0.006, 0.004)   # foot
        for y0 in (-0.085, -PLINTH_H + 0.115):
            groove = smax(box(p, (0, y0, 0), (w + 0.1, 0.006, dz + 0.1)),
                          -box(p, (0, y0, 0), (w - 0.012, 0.1, dz - 0.012)), 0.002)
            d = smax(d, -groove, 0.003)                                                              # grooves
        rng = np.random.default_rng(self.seed + 5)
        for _ in range(9):                                                                         # edge chips
            c = np.array([rng.choice([-1, 1]) * (w + 0.03), rng.uniform(-PLINTH_H, 0), rng.choice([-1, 1]) * (dz + 0.03)])
            c[rng.integers(0, 2) * 2] *= rng.uniform(0.2, 1.0)
            d = smax(d, -(sphere(p, c, rng.uniform(0.02, 0.045)) + 0.006 * fbm(p * 60, 2, 7)), 0.003)
        return d + 0.0008 * fbm(p * 50.0, 2, self.seed + 12)

    def __call__(self, p):
        out = np.full(len(p), 1.0)
        up = p[:, 1] > -0.02
        if up.any():
            out[up] = self.figure(p[up])
        dn = p[:, 1] < 0.02
        if dn.any():
            out[dn] = np.minimum(out[dn], self.plinth(p[dn]))
        return out
