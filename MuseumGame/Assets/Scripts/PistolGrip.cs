using System.Collections.Generic;
using UnityEngine;

// Two-handed pistol grip, solved per character from its own hand bones:
//  - the gun is seated in the right palm: backstrap in the web of the hand, grip along the knuckle line, trigger at index-finger height
//  - middle/ring/little fingers curl around the front strap until they touch the grip (collision against the gun's shape)
//  - the index finger reaches the trigger (CCD IK), the right thumb lies along the opposite side of the frame
//  - the left hand cups the right hand on the thumb-side panel (two-bone arm IK) with its fingers wrapped over the right fingers
// Runs after the animation and the other pose scripts (execution order 1000); the select screen calls Apply() itself before rendering.
[DefaultExecutionOrder(1000)]
public class PistolGrip : MonoBehaviour
{
    // ---- gun landmarks, in SilencedPistol.obj mesh units: u along the bore (+ = muzzle), w downward (+ = toward the butt), x across ----
    static readonly Vector3 C = new Vector3(3.26f, 62.08f, 2.17f);
    static readonly Vector3 Bm = new Vector3(0.002f, 0.711f, 0.704f).normalized;   // bore, toward the muzzle
    static readonly Vector3 Dm = new Vector3(0f, -0.704f, 0.711f).normalized;      // down, toward the butt
    const float Xc = 3.15f, HalfW = 3.3f;
    public static float CantDeg = 20f;                                          // grip centre plane and half thickness
    static Vector3 M(float u, float w, float x) { var p = C + Bm * u + Dm * w; p.x = x; return p; }
    static Vector3 UWX(Vector3 p) { var d = p - C; return new Vector3(Vector3.Dot(d, Bm), Vector3.Dot(d, Dm), p.x); }
    // the solid parts of the gun as boxes in (u, w, x): grip, trigger guard, slide
    static readonly Vector3[] BoxMin = { new Vector3(-62f, 8f, -0.1f), new Vector3(-47f, 8f, 0.3f), new Vector3(-62f, -5f, 0.2f) };
    static readonly Vector3[] BoxMax = { new Vector3(-47f, 31f, 6.4f), new Vector3(-34f, 16f, 6.0f), new Vector3(-21f, 8f, 6.1f) };

    public bool rightOnly;
    public bool manualSolve;   // the owner calls Solve() itself, in the right pose
    public bool fixedHands;    // select screen: hands/arms stay exactly as the animation has them; only the gun is seated and the fingers close around it
    public bool forceLeft;
    public bool aimAlongArm;   // select screen: the gun points along the arm instead of where the old attach put it   // select screen: always put the left hand on the gun, however far the pose has it   // select screen: the left hand comes from the animation itself

    public static PistolGrip Ensure(GameObject root)
    {
        if (root == null) return null;
        var g = root.GetComponent<PistolGrip>(); if (g == null) g = root.AddComponent<PistolGrip>();
        return g;
    }

    Animator anim; Transform hand, lhand, lUpper, lLower, rUpper, rLower, lShoulder, gun;
    float s;                                       // metres per mesh unit
    readonly List<Transform> rBones = new List<Transform>(); readonly List<Quaternion> rLocal = new List<Quaternion>();
    readonly List<Transform> lBones = new List<Transform>(); readonly List<Quaternion> lLocal = new List<Quaternion>();
    Vector3 lPosG; Quaternion lRotG; bool solved, haveLeft, aimDriven; Quaternion handFix = Quaternion.identity, lastHandLocal; bool haveLast;
    readonly List<Transform> rFingerJoints = new List<Transform>();   // right finger joints, for the left hand to wrap around

    void Start() { if (!manualSolve) Solve(); }
    void LateUpdate() { Apply(); }

    Transform B(HumanBodyBones b) { return anim != null ? anim.GetBoneTransform(b) : null; }

    public void Solve()
    {
        solved = false;
        anim = GetComponentInChildren<Animator>(true); if (anim == null || !anim.isHuman) return;
        hand = B(HumanBodyBones.RightHand); lhand = B(HumanBodyBones.LeftHand);
        lUpper = B(HumanBodyBones.LeftUpperArm); lLower = B(HumanBodyBones.LeftLowerArm);
        rUpper = B(HumanBodyBones.RightUpperArm); rLower = B(HumanBodyBones.RightLowerArm); lShoulder = B(HumanBodyBones.LeftShoulder);
        var gp = hand != null ? hand.Find("GunPivot") : null; if (gp == null) return;
        var mf = gp.GetComponentInChildren<MeshFilter>(true); if (mf == null) return;
        gun = mf.transform; s = Mathf.Abs(gun.lossyScale.x); if (s < 1e-7f) return;

        // keep the left arm as it is while solving; restored at the end
        var keep = new List<(Transform, Quaternion, Vector3)>();
        foreach (var t in anim.GetComponentsInChildren<Transform>(true)) if (!t.IsChildOf(gp)) keep.Add((t, t.localRotation, t.localPosition));

        Vector3 f, k, n; Frame(true, out f, out k, out n);
        var oldRel = Quaternion.Inverse(hand.rotation) * gun.rotation;
        PlaceGun(f, k, n);
        // where no aiming script steers the hand (select screen, other players), turn the wrist so the gun keeps pointing where the animation had it
        handFix = oldRel * Quaternion.Inverse(Quaternion.Inverse(hand.rotation) * gun.rotation);
        aimDriven = GetComponentInParent<PlayerAvatarAnim>() != null;
        AlignPivot(gp);
        rBones.Clear(); rLocal.Clear(); rFingerJoints.Clear();
        float thumbSide = ThumbSideSign(n);
        foreach (var fi in new[] { 2, 3, 4 }) CurlFinger(true, fi, f, n, null);
        IndexToTrigger(true, M(-40.5f, 11.5f, Xc));
        ThumbTo(true, M(-46f, 9.5f, Xc + thumbSide * (HalfW + 0.009f / s)));
        foreach (var fi in new[] { 1, 2, 3, 4 }) { var c = Chain(true, fi); if (c != null) rFingerJoints.AddRange(c); }

        haveLeft = lhand != null && lUpper != null && lLower != null && (fixedHands ? SolveLeftFixed(thumbSide) : SolveLeft(thumbSide));

        // restore everything except the right fingers (those are re-applied every frame anyway)
        foreach (var (t, r, lp) in keep) if (t != null && !rBones.Contains(t) && !(fixedHands && lBones.Contains(t))) { t.localRotation = r; t.localPosition = lp; }
        solved = true; haveLast = false;
        Apply();
    }

    // f: wrist -> middle knuckle, k: index knuckle -> little knuckle, n: palm normal (out of the palm)
    void Frame(bool right, out Vector3 f, out Vector3 k, out Vector3 n)
    {
        var h = right ? hand : lhand;
        var i1 = B(right ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal);
        var m1 = B(right ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
        var p1 = B(right ? HumanBodyBones.RightLittleProximal : HumanBodyBones.LeftLittleProximal);
        var t3 = B(right ? HumanBodyBones.RightThumbDistal : HumanBodyBones.LeftThumbDistal);
        f = (m1.position - h.position).normalized;
        k = p1.position - i1.position; k = (k - f * Vector3.Dot(k, f)).normalized;
        n = Vector3.Cross(f, k).normalized;
        if (t3 != null && Vector3.Dot(t3.position - h.position, n) < 0f) n = -n;
    }

    float ThumbSideSign(Vector3 n)
    {
        // the palm sits on one side panel; the thumb-side panel is the other one. Returns the mesh-x sign of the thumb side.
        var xw = gun.TransformDirection(Vector3.right);
        return Vector3.Dot(xw, n) > 0f ? 1f : -1f;   // n points from the palm into the gun, i.e. toward the thumb side
    }

    void PlaceGun(Vector3 f, Vector3 k, Vector3 n)
    {
        // gun "down" runs along the knuckle line; the muzzle points the way the fingers do
        var fw = Vector3.Cross(n, k); if (Vector3.Dot(fw, f) < 0f) fw = -fw;
        gun.rotation = Quaternion.LookRotation(fw, -k) * Quaternion.Inverse(Quaternion.LookRotation(Bm, -Dm));
        var h = hand.position;
        var i1 = B(HumanBodyBones.RightIndexProximal).position; var m1 = B(HumanBodyBones.RightMiddleProximal).position;
        float fk = Vector3.Dot(m1 - h, f), kI = Vector3.Dot(i1 - h, k), nP = Vector3.Dot(m1 - h, n);
        // front strap just behind the knuckles, trigger at index-knuckle height, grip pressed into the palm
        var target = h + f * (fk - 0.006f - 7f * s) + k * (kI + 3f * s) + n * (nP + 0.012f + HalfW * s);
        gun.position += target - gun.TransformPoint(M(-54f, 14f, Xc));
        // a real hand wraps the grip at an angle: turn the gun about its grip axis so the backstrap sits in the web/heel of the palm
        var centre = gun.TransformPoint(M(-54f, 18f, Xc)); var back0 = gun.TransformPoint(M(-61f, 18f, Xc));
        gun.RotateAround(centre, k, CantDeg);
        // check the direction empirically: the backstrap must have moved into the palm (against n)
        if (Vector3.Dot(gun.TransformPoint(M(-61f, 18f, Xc)) - back0, n) > 0f) gun.RotateAround(centre, k, -2f * CantDeg);
    }

    // the aiming code points GunPivot.forward at the crosshair and puts the muzzle flash at GunPivot + forward * muzzleDist:
    // line the pivot up with the barrel as it now sits in the hand, without moving the gun itself
    void AlignPivot(Transform gp)
    {
        var wp = gun.position; var wr = gun.rotation;
        var bore = gun.TransformDirection(Bm).normalized; var upW = -gun.TransformDirection(Dm).normalized;
        var muzzle = gun.TransformPoint(C + Bm * 31f);
        gp.rotation = Quaternion.LookRotation(bore, upW);
        gp.position = muzzle - bore * 0.24f;
        gun.SetPositionAndRotation(wp, wr);
    }

    // ---- fingers ----
    Transform[] Chain(bool right, int finger)
    {
        HumanBodyBones b0;
        switch (finger)
        {
            case 0: b0 = right ? HumanBodyBones.RightThumbProximal : HumanBodyBones.LeftThumbProximal; break;
            case 1: b0 = right ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal; break;
            case 2: b0 = right ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal; break;
            case 3: b0 = right ? HumanBodyBones.RightRingProximal : HumanBodyBones.LeftRingProximal; break;
            default: b0 = right ? HumanBodyBones.RightLittleProximal : HumanBodyBones.LeftLittleProximal; break;
        }
        var j0 = B(b0); var j1 = B(b0 + 1); var j2 = B(b0 + 2);
        if (j0 == null || j1 == null || j2 == null) return null;
        return new[] { j0, j1, j2 };
    }
    static Vector3 Tip(Transform[] c)
    {
        var j2 = c[2];
        if (j2.childCount > 0) return j2.GetChild(0).position;
        return j2.position + (j2.position - c[1].position) * 0.8f;
    }
    void Record(bool right, Transform[] c)
    {
        var lb = right ? rBones : lBones; var ll = right ? rLocal : lLocal;
        foreach (var j in c) { int i = lb.IndexOf(j); if (i >= 0) ll[i] = j.localRotation; else { lb.Add(j); ll.Add(j.localRotation); } }
    }

    // signed distance (metres) from a world point to the gun's solid parts, plus optional extra spheres (the right fingers, for the left hand)
    float Sdf(Vector3 world, List<Transform> extra, float extraR)
    {
        var p = UWX(gun.InverseTransformPoint(world));
        float best = float.MaxValue;
        for (int i = 0; i < BoxMin.Length; i++)
        {
            var c = (BoxMin[i] + BoxMax[i]) * 0.5f; var e = (BoxMax[i] - BoxMin[i]) * 0.5f;
            var q = new Vector3(Mathf.Abs(p.x - c.x) - e.x, Mathf.Abs(p.y - c.y) - e.y, Mathf.Abs(p.z - c.z) - e.z);
            float d = new Vector3(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f), Mathf.Max(q.z, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, Mathf.Max(q.y, q.z)), 0f);
            best = Mathf.Min(best, d * s);
        }
        if (extra != null) foreach (var t in extra) if (t != null) best = Mathf.Min(best, Vector3.Distance(world, t.position) - extraR);
        return best;
    }
    bool Hits(Transform[] c, float r, List<Transform> extra, float extraR)
    {
        var tip = Tip(c);
        var pts = new[] { c[1].position, c[2].position, tip, (c[0].position + c[1].position) * 0.5f, (c[1].position + c[2].position) * 0.5f, (c[2].position + tip) * 0.5f };
        foreach (var p in pts) if (Sdf(p, extra, extraR) < r) return true;
        return false;
    }

    // straighten the finger along f, then close it around the hinge axis until it touches the gun
    void CurlFinger(bool right, int finger, Vector3 f, Vector3 n, List<Transform> extra)
    {
        var c = Chain(right, finger); if (c == null) return;
        for (int i = 0; i < 3; i++)
        {
            var nxt = i < 2 ? c[i + 1].position : Tip(c);
            c[i].rotation = Quaternion.FromToRotation(nxt - c[i].position, f) * c[i].rotation;
        }
        var axis = Vector3.Cross(f, n).normalized;
        var b0 = c[0].localRotation; var b1 = c[1].localRotation; var b2 = c[2].localRotation;
        // which way closes the hand?
        var before = Tip(c); c[0].rotation = Quaternion.AngleAxis(10f, axis) * c[0].rotation;
        if (Vector3.Dot(Tip(c) - before, n) < 0f) axis = -axis;
        c[0].localRotation = b0;
        const float r = 0.0085f; float extraR = 0.0105f;
        float a0 = 0f, a1 = 0f, a2 = 0f;
        System.Action set = () =>
        {
            c[0].localRotation = b0; c[1].localRotation = b1; c[2].localRotation = b2;
            c[0].rotation = Quaternion.AngleAxis(a0, axis) * c[0].rotation;
            c[1].rotation = Quaternion.AngleAxis(a1, axis) * c[1].rotation;
            c[2].rotation = Quaternion.AngleAxis(a2, axis) * c[2].rotation;
        };
        // 1) whole finger closes together, 2) the outer two joints keep wrapping, 3) the fingertip
        for (float t = 0f; t <= 95f; t += 2f) { float p0 = a0, p1 = a1, p2 = a2; a0 = t; a1 = Mathf.Min(t * 1.1f, 100f); a2 = Mathf.Min(t * 0.8f, 80f); set(); if (Hits(c, r, extra, extraR)) { a0 = p0; a1 = p1; a2 = p2; break; } }
        for (float t = 2f; a1 < 105f; t += 2f) { float p1 = a1, p2 = a2; a1 = Mathf.Min(a1 + 2f, 105f); a2 = Mathf.Min(a2 + 1.6f, 85f); set(); if (Hits(c, r, extra, extraR)) { a1 = p1; a2 = p2; break; } }
        for (; a2 < 85f;) { float p2 = a2; a2 += 2f; set(); if (Hits(c, r, extra, extraR)) { a2 = p2; break; } }
        set();
        Record(right, c);
    }

    // CCD: bend the chain so the pad of the last bone reaches the target (mesh space point)
    void Reach(Transform[] c, Vector3 targetWorld, float padAlong, int iters, float maxStep)
    {
        for (int it = 0; it < iters; it++)
        {
            for (int i = 2; i >= 0; i--)
            {
                var tip = Tip(c); var pad = c[2].position + (tip - c[2].position) * padAlong;
                var v1 = pad - c[i].position; var v2 = targetWorld - c[i].position;
                if (v1.sqrMagnitude < 1e-10f || v2.sqrMagnitude < 1e-10f) continue;
                var q = Quaternion.FromToRotation(v1, v2);
                q = Quaternion.RotateTowards(Quaternion.identity, q, maxStep);
                c[i].rotation = q * c[i].rotation;
            }
        }
    }
    void IndexToTrigger(bool right, Vector3 meshTarget)
    {
        var c = Chain(right, 1); if (c == null) return;
        Reach(c, gun.TransformPoint(meshTarget), 0.55f, 40, 8f);
        Record(right, c);
    }
    void ThumbTo(bool right, Vector3 meshTarget)
    {
        var c = Chain(right, 0); if (c == null) return;
        Reach(c, gun.TransformPoint(meshTarget), 0.9f, 80, 4f);
        Record(right, c);
    }

    // ---- left (support) hand ----
    bool SolveLeft(float thumbSide)
    {
        lBones.Clear(); lLocal.Clear();
        Vector3 f, k, n; Frame(false, out f, out k, out n);
        var xw = gun.TransformDirection(Vector3.right).normalized;
        var tsd = xw * thumbSide;                                    // world direction from the gun out to its thumb-side panel
        var down = gun.TransformDirection(Dm).normalized; var fwd = gun.TransformDirection(Bm).normalized;
        var nL = -tsd;                                               // left palm faces the gun from the thumb side
        var fL = fwd * 0.45f + down * 0.89f; fL = (fL - nL * Vector3.Dot(fL, nL)).normalized;   // fingers forward-down, wrapping under the guard
        var delta = Quaternion.LookRotation(fL, nL) * Quaternion.Inverse(Quaternion.LookRotation(f, n));
        var h = lhand.position; var m1 = B(HumanBodyBones.LeftMiddleProximal).position;
        var palm = h + f * Vector3.Dot(m1 - h, f) * 0.5f + n * 0.012f;
        // the heel of the left palm fills the thumb-side panel, over the right fingertips
        var g = gun.TransformPoint(M(-51f, 18f, Xc)) + tsd * (HalfW * s + 0.017f + 0.012f);
        var tRot = delta * lhand.rotation; var tPos = g - delta * (palm - h);
        lPosG = gun.InverseTransformPoint(tPos); lRotG = Quaternion.Inverse(gun.rotation) * tRot;
        // pose the hand there (temporarily) to close its fingers around the gun and the right hand
        lhand.SetPositionAndRotation(tPos, tRot);
        Frame(false, out f, out k, out n);
        foreach (var fi in new[] { 1, 2, 3, 4 }) CurlFinger(false, fi, f, n, rFingerJoints);
        // thumbs-forward: the left thumb lies along the frame under the slide, pointing at the target
        ThumbTo(false, M(-27f, 10f, Xc + thumbSide * (HalfW + 0.012f / s)));
        return true;
    }

    // left hand stays where the animation put it; its fingers close around the gun / the right fingers, the thumb lies along the frame
    bool SolveLeftFixed(float thumbSide)
    {
        lBones.Clear(); lLocal.Clear();
        Vector3 f, k, n; Frame(false, out f, out k, out n);
        foreach (var fi in new[] { 1, 2, 3, 4 }) CurlFinger(false, fi, f, n, rFingerJoints);
        ThumbTo(false, M(-27f, 10f, Xc + thumbSide * (HalfW + 0.012f / s)));
        return true;
    }

    public void Apply()
    {
        if (!solved || gun == null) return;
        if (fixedHands)
        {
            for (int i = 0; i < rBones.Count; i++) if (rBones[i] != null) rBones[i].localRotation = rLocal[i];
            for (int i = 0; i < lBones.Count; i++) if (lBones[i] != null) lBones[i].localRotation = lLocal[i];
            return;
        }
        if (!aimDriven)
        {
            // only once per animated pose: if nothing re-posed the hand since last frame, it is already turned
            if (!(haveLast && hand.localRotation == lastHandLocal))
            {
                if (aimAlongArm && rUpper != null)
                {
                    // point the barrel straight down the line of the arm (shoulder -> hand), as an aiming pose does
                    var bore = gun.TransformDirection(Bm); var want = hand.position - rUpper.position;
                    if (want.sqrMagnitude > 1e-6f) hand.rotation = Quaternion.FromToRotation(bore, want) * hand.rotation;
                }
                else hand.rotation = hand.rotation * handFix;
            }
            lastHandLocal = hand.localRotation; haveLast = true;
        }
        for (int i = 0; i < rBones.Count; i++) if (rBones[i] != null) rBones[i].localRotation = rLocal[i];
        // your own first-person view: the left forearm would cover the screen, so the game keeps placing that hand itself
        if (!haveLeft || aimDriven || rightOnly) return;
        var tPos = gun.TransformPoint(lPosG); var tRot = gun.rotation * lRotG;
        // only when the left hand is already up near the gun (two-handed poses); one-handed poses (running, carrying) are left alone
        float d = Vector3.Distance(lhand.position, tPos);
        float w = forceLeft ? 1f : 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.32f, 0.5f, d));
        if (w <= 0f) return;
        // if the left arm can't reach: first roll the left shoulder forward (like a real two-handed stance)...
        float reach = (Vector3.Distance(lUpper.position, lLower.position) + Vector3.Distance(lLower.position, lhand.position)) * 0.97f;
        float need = Vector3.Distance(lUpper.position, tPos) - reach;
        if (need > 0f && lShoulder != null)
        {
            var v1 = lUpper.position - lShoulder.position; var v2 = tPos - lShoulder.position;
            float ang = Mathf.Min(18f, Mathf.Asin(Mathf.Clamp01(need / Mathf.Max(v1.magnitude, 0.01f))) * Mathf.Rad2Deg) * w;
            lShoulder.rotation = Quaternion.RotateTowards(Quaternion.identity, Quaternion.FromToRotation(v1, v2), ang) * lShoulder.rotation;
            need = Vector3.Distance(lUpper.position, tPos) - reach;
        }
        // ...then bring the right hand (and the gun) back toward the body, a few centimetres at most
        if (need > 0f && rUpper != null && rLower != null)
        {
            var back = (lUpper.position - tPos).normalized * Mathf.Min(need, 0.04f) * w;
            var hr = hand.rotation;
            TwoBoneIK(rUpper, rLower, hand, hand.position + back, rLower.position + (rLower.position - (rUpper.position + hand.position) * 0.5f));
            hand.rotation = hr;
            tPos = gun.TransformPoint(lPosG); tRot = gun.rotation * lRotG;
        }
        // elbow down and slightly out to the side (not up across the face)
        var side = (lUpper.position - hand.position); side.y = 0f; side = side.sqrMagnitude > 1e-6f ? side.normalized : Vector3.zero;
        var elbowHint = (lUpper.position + tPos) * 0.5f + Vector3.down * 0.35f + side * 0.12f;
        TwoBoneIK(lUpper, lLower, lhand, Vector3.Lerp(lhand.position, tPos, w), elbowHint);
        lhand.rotation = Quaternion.Slerp(lhand.rotation, tRot, w);
        for (int i = 0; i < lBones.Count; i++) if (lBones[i] != null) lBones[i].localRotation = Quaternion.Slerp(lBones[i].localRotation, lLocal[i], w);
    }

    static void TwoBoneIK(Transform a, Transform b, Transform c, Vector3 target, Vector3 hint)
    {
        float la = Vector3.Distance(a.position, b.position), lb = Vector3.Distance(b.position, c.position);
        var cRot = c.rotation;
        var at = target - a.position; float dt = Mathf.Clamp(at.magnitude, 0.001f, (la + lb) * 0.999f);
        var dir = at.normalized;
        // elbow angle
        float cosB = Mathf.Clamp((la * la + lb * lb - dt * dt) / (2f * la * lb), -1f, 1f);
        float cosA = Mathf.Clamp((la * la + dt * dt - lb * lb) / (2f * la * dt), -1f, 1f);
        var planeN = Vector3.Cross(dir, hint - a.position); if (planeN.sqrMagnitude < 1e-8f) planeN = Vector3.Cross(dir, Vector3.up);
        planeN.Normalize();
        var elbowDir = Quaternion.AngleAxis(Mathf.Acos(cosA) * Mathf.Rad2Deg, planeN) * dir;
        // make sure the elbow bends toward the hint
        if (Vector3.Dot(elbowDir - dir * Vector3.Dot(elbowDir, dir), hint - a.position) < 0f) elbowDir = Quaternion.AngleAxis(-Mathf.Acos(cosA) * Mathf.Rad2Deg, planeN) * dir;
        var elbow = a.position + elbowDir * la;
        a.rotation = Quaternion.FromToRotation(b.position - a.position, elbow - a.position) * a.rotation;
        b.rotation = Quaternion.FromToRotation(c.position - b.position, target - b.position) * b.rotation;
        c.rotation = cRot;
    }
}
