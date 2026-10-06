using UnityEngine;
using UnityEngine.InputSystem;
// Drives the third-person body: Speed from player movement, Shooting while left mouse held.
public class PlayerAvatarAnim : MonoBehaviour
{
    public float aimYawOffset = 40f; float lastShot=-10f; bool standPose; Vector3 standCamLocal = new Vector3(0.03f, 0.57f, 0.36f); Quaternion standChestRot; bool haveStandChest; bool running, sprinting; [HideInInspector] public bool debugSprint; float runBlend; Quaternion leftRel = Quaternion.identity; bool haveLeftRel; Vector3 camRel = new Vector3(-0.154f, 0.156f, 0.385f); bool haveCamRel = true; public Vector3 runHandPos = new Vector3(0.16f, -0.14f, 0.32f); public Vector3 runGunEuler = new Vector3(-10f, -30f, 25f); Animator anim; Vector3 last; CharacterController cc;
    void Start() { anim = GetComponentInChildren<Animator>(); cc = GetComponentInParent<CharacterController>(); last = transform.position; }
    void Update()
    {
        var p = transform.position; var v = (p - last) / Mathf.Max(Time.deltaTime, 1e-4f); v.y = 0; last = p;
        float sp = v.magnitude;
        bool run = Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;
        bool crouch = FirstPersonController.Crouching; anim.SetBool("Crouch", crouch);
        anim.SetFloat("Speed", sp < 0.2f ? 0f : (crouch ? 1.5f : (run ? 6f : 2f)), 0.15f, Time.deltaTime);
        { var lv = transform.InverseTransformDirection(v); if (sp < 0.2f) lv = Vector3.zero; anim.SetFloat("MoveX", lv.x, 0.12f, Time.deltaTime); anim.SetFloat("MoveZ", lv.z, 0.12f, Time.deltaTime); }   // 8-direction locomotion
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) lastShot = Time.time;
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) flashTimer = 0.06f;
        bool shooting = Time.time - lastShot < 0.8f; shootingNow = shooting;
        float target = !shooting ? 1f : 0f; // 5 fixed states: not shooting = gun lowered (walk/stand/run) anim.SetLayerWeight(1, Mathf.MoveTowards(anim.GetLayerWeight(1), target, Time.deltaTime * (target > 0f ? 4f : 20f)));
        anim.SetBool("Shooting", shooting);
        running = sp >= 0.2f; sprinting = debugSprint;
        float camPitch = Camera.main ? Mathf.Asin(Mathf.Clamp(-Camera.main.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg : 0f;
        standPose = running && shooting && Mathf.Abs(camPitch) > 30f; // looking down while moving: reuse the standing-shoot upper body 1:1
        anim.SetLayerWeight(3, Mathf.MoveTowards(anim.GetLayerWeight(3), (running && shooting && !standPose) ? 1f : 0f, Time.deltaTime * 8f));
        anim.SetLayerWeight(2, Mathf.MoveTowards(anim.GetLayerWeight(2), shooting ? 1f : 0f, Time.deltaTime * 12f));
    }
    public Vector3 leftHandOffset = new Vector3(0.05f, -0.03f, 0.0f); public Vector3 leftHandRot = new Vector3(0f, 0f, 90f); public bool useLeftHandRot = false;
    public float muzzleDist = 0.32f; float flashTimer; Light flashLight; Transform flashQuad;
    public float lowGunForward = 1.6f;
    public bool hideHead = true; public bool followHeadCam = true; public Vector3 fpHandPos = new Vector3(0.14f, -0.12f, 0.45f); public float eyeForward = 0f, eyeUp = 0.06f;
    // keep the lowest foot on the ground while crouched (the crouch clip floats the feet)
    public float rightFootExtraDrop = 0.03f;   // extra lowering of the right foot while crouched
    public float crouchFootDrop = 0.07f;   // raise/lower to make the crouched feet touch the ground
    float rawMinL = 999f, rawMinR = 999f, adjL, adjR, crouchW; float rawMin = 999f, ankleH = -1f, avBaseY = float.NaN, crouchOff;
    void GroundFeet()
    {
        var lf = anim.GetBoneTransform(HumanBodyBones.LeftFoot); var rf = anim.GetBoneTransform(HumanBodyBones.RightFoot);
        if (lf == null || rf == null) return;
        if (float.IsNaN(avBaseY)) avBaseY = transform.localPosition.y;
        var root = transform.root; float gy = -999f;
        foreach (var h in Physics.RaycastAll(root.position + Vector3.up * 0.2f, Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore))
            if (!h.collider.transform.IsChildOf(root) && h.point.y > gy) gy = h.point.y;
        if (gy < -900f) return;
        float minFoot = Mathf.Min(lf.position.y, rf.position.y);
        bool crouching = FirstPersonController.Crouching;
        if (!crouching && cc != null && cc.isGrounded && Mathf.Abs(crouchOff) < 0.001f) { float a = minFoot - gy; if (ankleH < 0f || a < ankleH) ankleH = Mathf.Max(a, 0.02f); }   // lowest ankle height seen standing/walking = sole on ground
        float ah = ankleH < 0f ? 0.07f : ankleH;
        // lowest foot height over the whole step cycle (independent of the current offset), so the body doesn't bob with each step
        if (!crouching) rawMin = 999f;
        else { float raw = minFoot - crouchOff; rawMin = raw < rawMin ? raw : Mathf.MoveTowards(rawMin, raw, Time.deltaTime * 0.05f); }
        float target = crouching ? Mathf.Clamp(gy + ah - crouchFootDrop - rawMin, -0.5f, 0.5f) : 0f;
        crouchOff = Mathf.MoveTowards(crouchOff, target, Time.deltaTime * 0.6f);
        var lp = transform.localPosition; lp.y = avBaseY + crouchOff; transform.localPosition = lp;
        // the crouch clip has one foot higher than the other: pull the higher foot down onto the ground with leg IK
        if (!crouching) { rawMinL = rawMinR = 999f; }
        else
        {
            float rl = lf.position.y - crouchOff, rr = rf.position.y - crouchOff;
            rawMinL = rl < rawMinL ? rl : Mathf.MoveTowards(rawMinL, rl, Time.deltaTime * 0.05f);
            rawMinR = rr < rawMinR ? rr : Mathf.MoveTowards(rawMinR, rr, Time.deltaTime * 0.05f);
        }
        crouchW = Mathf.MoveTowards(crouchW, crouching ? 1f : 0f, Time.deltaTime * 4f);
        float both = Mathf.Min(rawMinL, rawMinR);
        adjL = Mathf.MoveTowards(adjL, crouching ? Mathf.Clamp(rawMinL - both, 0f, 0.2f) : 0f, Time.deltaTime * 0.6f);
        adjR = Mathf.MoveTowards(adjR, crouching ? Mathf.Clamp(rawMinR - both, 0f, 0.2f) : 0f, Time.deltaTime * 0.6f);
        LegDown(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, lf, adjL * crouchW);
        LegDown(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, rf, (adjR + rightFootExtraDrop) * crouchW);
    }
    void LegDown(HumanBodyBones up, HumanBodyBones lo, Transform foot, float d)
    {
        if (d < 0.002f && crouchW < 0.01f) return;
        var u = anim.GetBoneTransform(up); var l = anim.GetBoneTransform(lo); if (u == null || l == null) return;
        var rot = foot.rotation;
        TwoBoneIK(u, l, foot, foot.position - Vector3.up * d, l.position + transform.forward * 0.5f);
        foot.rotation = rot;
    }
    bool tpVirtual;
    // third person: all the first-person aim/arm logic runs with the camera temporarily at the head (eye) position, then the camera goes back behind the player
    void LateUpdate()
    {
        var cm = Camera.main;
        if (followHeadCam || cm == null) { tpVirtual = false; LateUpdateInner(); return; }
        var ct = cm.transform; var p0 = ct.position; tpVirtual = true;
        var hd = anim.GetBoneTransform(HumanBodyBones.Head); if (hd) ct.position = hd.position + transform.up * eyeUp;
        LateUpdateInner();
        ct.position = p0; tpVirtual = false;
    }
    void LateUpdateInner()
    {
        GroundFeet();
        LateBody();
        bool crouchHold = FirstPersonController.Crouching && !shootingNow && haveHold;
        crouchArmW = Mathf.MoveTowards(crouchArmW, crouchHold ? 1f : 0f, Time.deltaTime * 8f);
        if (crouchArmW < 1f) { RunCarry(); RunLeftGrip(); }
        if (crouchArmW > 0f) ApplyHold(crouchArmW);
        // crouched and shooting: the standing-shoot arms, 1:1 in camera space (same gun position/angle on screen)
        crouchShootW = Mathf.MoveTowards(crouchShootW, (FirstPersonController.Crouching && shootingNow && haveShoot) ? 1f : 0f, Time.deltaTime * 12f);
        if (crouchShootW > 0f) ApplyShoot(crouchShootW);
        LegAvoid(); WallRetract(); UpdateFlash();
        if (!FirstPersonController.Crouching && shootingNow && !running && anim.GetLayerWeight(2) > 0.99f) CaptureShoot();
        // remember the normal (standing, not shooting) two-hand hold so crouching can use it 1:1
        if (!FirstPersonController.Crouching && !shootingNow && carryW >= 0.999f) CaptureHold();
    }
    // Crouched and not shooting: arms/hands/fingers take exactly the standing low-ready hold (rotations relative
    // to the body's facing, fingers relative to the hand), so the pistol is held the same way as when standing.
    static readonly HumanBodyBones[] ArmBones = {
        HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
        HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand };
    // called after the player's model was swapped for another rig
    public void Rebind()
    {
        anim = GetComponentInChildren<Animator>();
        fingerBones = null; holdRot = null; holdFinger = null; haveHold = false; shootRot = null; shootFinger = null; haveShoot = false;
        haveLeftRel = false; haveStandChest = false; ankleH = -1f; rawMin = 999f; rawMinL = rawMinR = 999f; adjL = adjR = 0f; crouchOff = 0f; crouchW = 0f;
        var lp = transform.localPosition; if (!float.IsNaN(avBaseY)) { lp.y = avBaseY; transform.localPosition = lp; }
    }
    Quaternion[] holdRot; Quaternion[] holdFinger; bool haveHold; float crouchArmW; Transform[] fingerBones;
    void CaptureHold()
    {
        if (holdRot == null || holdRot.Length != ArmBones.Length) holdRot = new Quaternion[ArmBones.Length];
        Fingers();
        if (holdFinger == null || holdFinger.Length != fingerBones.Length) holdFinger = new Quaternion[fingerBones.Length];
        var inv = Quaternion.Inverse(transform.rotation);
        for (int i = 0; i < ArmBones.Length; i++) { var t = anim.GetBoneTransform(ArmBones[i]); if (t) holdRot[i] = inv * t.rotation; }
        for (int i = 0; i < fingerBones.Length; i++) holdFinger[i] = fingerBones[i].localRotation;
        haveHold = true;
    }
    Quaternion[] shootRot; Quaternion[] shootFinger; Vector3 shootR, shootL, shootCam; bool haveShoot; float crouchShootW;
    void CaptureShoot()
    {
        var cam = Camera.main; if (!cam) return; var ct = cam.transform;
        Fingers();
        if (shootRot == null || shootRot.Length != ArmBones.Length || shootFinger == null || shootFinger.Length != fingerBones.Length) { shootRot = new Quaternion[ArmBones.Length]; shootFinger = new Quaternion[fingerBones.Length]; }
        var inv = Quaternion.Inverse(ct.rotation);
        for (int i = 0; i < ArmBones.Length; i++) { var t = anim.GetBoneTransform(ArmBones[i]); if (t) shootRot[i] = inv * t.rotation; }
        for (int i = 0; i < fingerBones.Length; i++) shootFinger[i] = fingerBones[i].localRotation;
        shootR = ct.InverseTransformPoint(anim.GetBoneTransform(HumanBodyBones.RightHand).position);
        shootL = ct.InverseTransformPoint(anim.GetBoneTransform(HumanBodyBones.LeftHand).position);
        shootCam = inv * (ct.position - anim.GetBoneTransform(HumanBodyBones.RightShoulder).position);
        haveShoot = true;
    }
    void ApplyShoot(float w)
    {
        var cam = Camera.main; if (!cam) return; var ct = cam.transform;
        for (int i = 0; i < ArmBones.Length; i++) { var t = anim.GetBoneTransform(ArmBones[i]); if (t) t.rotation = Quaternion.Slerp(t.rotation, ct.rotation * shootRot[i], w); }
        for (int i = 0; i < fingerBones.Length; i++) fingerBones[i].localRotation = Quaternion.Slerp(fingerBones[i].localRotation, shootFinger[i], w);
        var rua = anim.GetBoneTransform(HumanBodyBones.RightUpperArm); var rla = anim.GetBoneTransform(HumanBodyBones.RightLowerArm); var rh = anim.GetBoneTransform(HumanBodyBones.RightHand);
        var lua = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm); var lla = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm); var lh = anim.GetBoneTransform(HumanBodyBones.LeftHand);
        // camera sits where it does relative to the shoulder when standing and shooting, so the arms reach the same way
        var rsh = anim.GetBoneTransform(HumanBodyBones.RightShoulder);
        ct.position = Vector3.Lerp(ct.position, rsh.position + ct.rotation * shootCam, w);
        var rRot = rh.rotation; var lRot = lh.rotation;
        TwoBoneIK(rua, rla, rh, Vector3.Lerp(rh.position, ct.TransformPoint(shootR), w), rua.position - ct.up * 0.4f + ct.right * 0.25f - ct.forward * 0.1f);
        TwoBoneIK(lua, lla, lh, Vector3.Lerp(lh.position, ct.TransformPoint(shootL), w), lua.position - ct.up * 0.4f - ct.right * 0.25f - ct.forward * 0.1f);
        rh.rotation = rRot; lh.rotation = lRot;
    }
    void Fingers()
    {
        if (fingerBones != null && fingerBones.Length > 0) return;
        var list = new System.Collections.Generic.List<Transform>();
        for (int b = (int)HumanBodyBones.LeftThumbProximal; b <= (int)HumanBodyBones.RightLittleDistal; b++) { var t = anim.GetBoneTransform((HumanBodyBones)b); if (t) list.Add(t); }
        fingerBones = list.ToArray();
    }
    // The gun and hands can't pass through the player's own legs (crouched and looking down): if the pistol
    // (grip, middle or muzzle) or a hand is inside a leg, both hands are pushed out of it together.
    public float legRadius = 0.11f;
    // how far the eye sits in front of the chest: less = the arms reach further out in front of the view (the gun sits more in front)
    public static float chestClear = 0.08f;
    void LegAvoid()
    {
        var rh = anim.GetBoneTransform(HumanBodyBones.RightHand); var lh = anim.GetBoneTransform(HumanBodyBones.LeftHand);
        if (!rh || !lh) return; var gp = rh.Find("GunPivot");
        var segs = new[] {
            (anim.GetBoneTransform(HumanBodyBones.RightUpperLeg), anim.GetBoneTransform(HumanBodyBones.RightLowerLeg)),
            (anim.GetBoneTransform(HumanBodyBones.RightLowerLeg), anim.GetBoneTransform(HumanBodyBones.RightFoot)),
            (anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg), anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg)),
            (anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg), anim.GetBoneTransform(HumanBodyBones.LeftFoot)) };
        for (int pass = 0; pass < 3; pass++)
        {
            Vector3 push = Vector3.zero;
            var pts = new System.Collections.Generic.List<Vector3> { rh.position, lh.position };
            if (gp) { pts.Add(gp.position + gp.forward * muzzleDist * 0.5f); pts.Add(gp.position + gp.forward * muzzleDist); }
            foreach (var (a, b) in segs)
            {
                if (!a || !b) continue;
                foreach (var p in pts)
                {
                    var ab = b.position - a.position; float t = Mathf.Clamp01(Vector3.Dot(p - a.position, ab) / Mathf.Max(ab.sqrMagnitude, 1e-5f));
                    var closest = a.position + ab * t; var d = p - closest; float dist = d.magnitude;
                    float need = legRadius + 0.04f;
                    if (dist < need)
                    {
                        var dir = dist > 1e-4f ? d / dist : transform.forward;
                        var amt = dir * (need - dist);
                        if (amt.sqrMagnitude > push.sqrMagnitude) push = amt;
                    }
                }
            }
            if (push.sqrMagnitude < 1e-6f) break;
            var rua = anim.GetBoneTransform(HumanBodyBones.RightUpperArm); var rla = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
            var lua = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm); var lla = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            var rRot = rh.rotation; var lRot = lh.rotation;
            TwoBoneIK(rua, rla, rh, rh.position + push, rua.position - transform.up * 0.4f + transform.right * 0.25f);
            TwoBoneIK(lua, lla, lh, lh.position + push, lua.position - transform.up * 0.4f - transform.right * 0.25f);
            rh.rotation = rRot; lh.rotation = lRot;
        }
    }
    // another player's body takes this player's walking hold (gun lowered, both hands on it): arms relative to the body, fingers relative to the hand
    public bool HasHold { get { return haveHold && holdRot != null && holdFinger != null; } }
    public void ApplyHoldTo(Animator a, Quaternion bodyRot, float w)
    {
        if (!HasHold || a == null || w <= 0f) return;
        for (int i = 0; i < ArmBones.Length; i++) { var t = a.GetBoneTransform(ArmBones[i]); if (t) t.rotation = Quaternion.Slerp(t.rotation, bodyRot * holdRot[i], w); }
        int k = 0;
        for (int b = (int)HumanBodyBones.LeftThumbProximal; b <= (int)HumanBodyBones.RightLittleDistal; b++)
        {
            var t = a.GetBoneTransform((HumanBodyBones)b); if (!t) continue;
            if (k < holdFinger.Length) t.localRotation = Quaternion.Slerp(t.localRotation, holdFinger[k], w);
            k++;
        }
    }
    void ApplyHold(float w)
    {
        for (int i = 0; i < ArmBones.Length; i++) { var t = anim.GetBoneTransform(ArmBones[i]); if (t) t.rotation = Quaternion.Slerp(t.rotation, transform.rotation * holdRot[i], w); }
        for (int i = 0; i < fingerBones.Length; i++) fingerBones[i].localRotation = Quaternion.Slerp(fingerBones[i].localRotation, holdFinger[i], w);
    }
    // Hands and gun collide with walls: if the gun muzzle or a hand would pass through geometry, the arm pulls back.
    Transform playerRoot;
    float Clear(Vector3 from, Vector3 to, float pad)
    {
        var d = to - from; float len = d.magnitude; if (len < 1e-4f) return 0f;
        if (!playerRoot) { var pl = GameObject.Find("Player"); playerRoot = pl ? pl.transform : transform.root; }
        float best = len + pad;
        foreach (var h in Physics.SphereCastAll(from, 0.04f, d / len, len + pad, ~0, QueryTriggerInteraction.Ignore))
        { if (h.distance <= 0f || h.collider.transform.IsChildOf(playerRoot)) continue; if (h.distance < best) best = h.distance; }
        return Mathf.Max(0f, (len + pad) - best); // how far the end point is inside (plus padding)
    }
    void WallRetract()
    {
        var chest = anim.GetBoneTransform(HumanBodyBones.UpperChest) ?? anim.GetBoneTransform(HumanBodyBones.Chest); if (!chest) return;
        var rh = anim.GetBoneTransform(HumanBodyBones.RightHand); var gp = rh ? rh.Find("GunPivot") : null;
        var lh = anim.GetBoneTransform(HumanBodyBones.LeftHand);
        var rua = anim.GetBoneTransform(HumanBodyBones.RightUpperArm); var rla = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
        var lua = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm); var lla = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        if (!rh || !lh || !rua || !lua) return;
        Vector3 o = chest.position;
        bool leftOnGun = gp && Vector3.Distance(lh.position, rh.position) < 0.2f;
        // right hand: test the muzzle tip (gun) and the hand
        Vector3 tip = gp ? gp.position + gp.forward * muzzleDist : rh.position;
        float inside = Mathf.Max(Clear(o, tip, 0.04f), Clear(o, rh.position, 0.05f));
        // snap in instantly so the gun never pokes through for even a frame, ease back out slowly
        retractR = inside > retractR ? inside : Mathf.MoveTowards(retractR, inside, Time.deltaTime * 3f);
        for (int pass = 0; pass < 3 && retractR > 0.001f; pass++)
        {
            if (pass > 0)
            {   // re-measure after the IK moved the arm; if anything is still inside, push further
                tip = gp ? gp.position + gp.forward * muzzleDist : rh.position;
                float still = Mathf.Max(Clear(o, tip, 0.04f), Clear(o, rh.position, 0.05f));
                if (still <= 0.001f) break; retractR = still;
            }
            var back = (o - tip); back.y = 0f; back = back.sqrMagnitude > 1e-6f ? back.normalized : -transform.forward;
            var delta = back * retractR;
            var lhBefore = lh.position;
            TwoBoneIK(rua, rla, rh, rh.position + delta, rua.position - transform.up * 0.4f + transform.right * 0.25f);
            if (leftOnGun) TwoBoneIK(lua, lla, lh, lhBefore + delta, lua.position - transform.up * 0.4f - transform.right * 0.25f);
        }
        // free left hand
        if (!leftOnGun)
        {
            float li = Clear(o, lh.position, 0.05f);
            if (li > 0f) { var b = (o - lh.position).normalized; TwoBoneIK(lua, lla, lh, lh.position + b * li, lua.position - transform.up * 0.4f - transform.right * 0.25f); }
        }
    }
    float retractR;
    // Running without shooting: compact two-hand low-ready carry locked to the view (only this state).
    bool shootingNow; float carryW;
    public Vector3 carryHandPos = new Vector3(0.10f, -0.30f, 0.34f); public float carryDownPitch = 38f, carryInYaw = -12f;
    public Vector3 carryLeftOffset = new Vector3(0.05f, -0.03f, 0.0f);
    void RunCarry()
    {
        carryW = Mathf.MoveTowards(carryW, ((running || FirstPersonController.Crouching) && !shootingNow) ? 1f : 0f, Time.deltaTime * 7f);
        if (carryW <= 0f || Camera.main == null) return;
        var ct = Camera.main.transform;
        var ua = anim.GetBoneTransform(HumanBodyBones.RightUpperArm); var fr = anim.GetBoneTransform(HumanBodyBones.RightLowerArm); var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
        var la = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm); var lf = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm); var lh = anim.GetBoneTransform(HumanBodyBones.LeftHand);
        if (!ua || !fr || !hand || !la || !lf || !lh) return; var gp = hand.Find("GunPivot"); if (!gp) return;
        var yawRot = Quaternion.Euler(0f, ct.eulerAngles.y, 0f);
        float ph = Time.time * 9f; var bob = new Vector3(Mathf.Sin(ph) * 0.012f, Mathf.Abs(Mathf.Cos(ph)) * 0.018f, 0f);
        // hand position relative to the view (yaw only so looking up/down doesn't swing the gun off-screen too much)
        var basePos = ct.position + yawRot * (carryHandPos + bob);
        var rHint = ua.position - transform.up * 0.4f + transform.right * 0.3f - transform.forward * 0.15f;
        TwoBoneIK(ua, fr, hand, Vector3.Lerp(hand.position, basePos, carryW), rHint);
        var aimDir = yawRot * (Quaternion.Euler(carryDownPitch, carryInYaw, 0f) * Vector3.forward);
        var want = Quaternion.LookRotation(aimDir, yawRot * Vector3.up) * Quaternion.Inverse(gp.localRotation);
        hand.rotation = Quaternion.Slerp(hand.rotation, want, carryW);
        // support hand wraps the grip exactly like the standing two-hand hold
        var lt = hand.position - gp.right * carryLeftOffset.x + gp.up * carryLeftOffset.y + gp.forward * carryLeftOffset.z;
        var lHint = la.position - transform.up * 0.4f - transform.right * 0.3f - transform.forward * 0.15f;
        TwoBoneIK(la, lf, lh, Vector3.Lerp(lh.position, lt, carryW), lHint);
        var rel = haveLeftRel ? leftRel : Quaternion.Inverse(gp.rotation) * lh.rotation;
        lh.rotation = Quaternion.Slerp(lh.rotation, gp.rotation * rel, carryW);
    }
    // While moving (walk/run/sprint, shooting or not) the left hand holds the pistol grip with the right hand.
    float runGripW;
    void RunLeftGrip()
    {
        runGripW = Mathf.MoveTowards(runGripW, running ? anim.GetLayerWeight(2) : 0f, Time.deltaTime * 6f); // not in the lowered (gun at hip) pose
        if (standPose) { runGripW = 0f; return; } // standing-shoot pose already places the left hand
        if (runGripW <= 0f) return;
        var hand = anim.GetBoneTransform(HumanBodyBones.RightHand); var gp = hand ? hand.Find("GunPivot") : null;
        var la = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm); var lf = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm); var lh = anim.GetBoneTransform(HumanBodyBones.LeftHand);
        if (gp == null || la == null || lf == null || lh == null) return;
        var lt = hand.position - gp.right * leftHandOffset.x + gp.up * leftHandOffset.y + gp.forward * leftHandOffset.z;
        var hint = la.position - transform.up * 0.4f - transform.right * 0.25f - transform.forward * 0.1f;
        TwoBoneIK(la, lf, lh, Vector3.Lerp(lh.position, lt, runGripW), hint);
        // wrap the palm around the grip: same relative pose as the standing two-hand hold
        var rel = haveLeftRel ? leftRel : Quaternion.Inverse(hand.rotation) * lh.rotation;
        lh.rotation = Quaternion.Slerp(lh.rotation, haveLeftRel ? gp.rotation * rel : lh.rotation, runGripW);
    }
    void LateBody()
    {
        var hd = anim.GetBoneTransform(HumanBodyBones.Head);
        if (hideHead && hd) hd.localScale = Vector3.one * 0.001f;
        if ((followHeadCam || tpVirtual) && hd && Camera.main)
        {
            var ct = Camera.main.transform;
            float pitch = Mathf.Asin(Mathf.Clamp(-ct.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            var nk = anim.GetBoneTransform(HumanBodyBones.Neck);
            if (nk) nk.rotation = Quaternion.AngleAxis(pitch * 0.4f, transform.right) * nk.rotation;
            hd.rotation = Quaternion.AngleAxis(pitch * 0.6f, transform.right) * hd.rotation;
            var look = Quaternion.AngleAxis(pitch, transform.right);
            float down = Mathf.Clamp01(Mathf.Abs(pitch) / 80f);
            ct.position = hd.position + transform.forward * (eyeForward + 0.12f * down) + transform.up * (eyeUp - 0.04f * down);
            var chest = anim.GetBoneTransform(HumanBodyBones.UpperChest) ?? anim.GetBoneTransform(HumanBodyBones.Chest);
            if (chest)
            {
                float need = chestClear + 0.16f * down;
                float dd = Vector3.Dot(ct.position - chest.position, transform.forward);
                if (dd < need) ct.position += transform.forward * (need - dd);
            }
        }
        float lw = anim.GetLayerWeight(1) * (1f - anim.GetLayerWeight(2));
        var rh = anim.GetBoneTransform(HumanBodyBones.RightHand); var gpl = rh ? rh.Find("GunPivot") : null;
        if (lw > 0f && gpl != null)
        {
            var want = (transform.forward * lowGunForward - transform.up + transform.right * 0.35f).normalized;
            rh.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(gpl.forward, want), lw) * rh.rotation;
        }
        if (false && sprinting && Camera.main)
        {
            var cam0 = Camera.main.transform; var hand0 = anim.GetBoneTransform(HumanBodyBones.RightHand); var gp0 = hand0.Find("GunPivot");
            var ua0 = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
            if (gp0 && ua0)
            {
                runBlend = Mathf.MoveTowards(runBlend, 1f, Time.deltaTime * 6f);
                var bob = new Vector3(Mathf.Sin(Time.time * 9f) * 0.012f, Mathf.Abs(Mathf.Cos(Time.time * 9f)) * 0.015f, 0);
                var desired0 = cam0.TransformPoint(runHandPos + bob);
                ua0.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(hand0.position - ua0.position, desired0 - ua0.position), runBlend) * ua0.rotation;
                var wantRot = cam0.rotation * Quaternion.Euler(runGunEuler);
                hand0.rotation = Quaternion.Slerp(Quaternion.identity, wantRot * Quaternion.Inverse(gp0.rotation), runBlend) * hand0.rotation;
            }
        }
        else runBlend = 0f;
        float w = anim.GetLayerWeight(2); if (w <= 0f) return;
        var sp = anim.GetBoneTransform(HumanBodyBones.Spine); if (sp == null) return;
        if (Camera.main)
        {
            float cp = Mathf.Asin(Mathf.Clamp(-Camera.main.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg; // negative = looking up
            if (cp < -20f) sp.rotation = Quaternion.AngleAxis((cp + 20f) * 0.8f * w, transform.right) * sp.rotation;
        }
        var cam = Camera.main; var gp = anim.GetBoneTransform(HumanBodyBones.RightHand).Find("GunPivot");
        if (cam != null && gp != null)
        {
            // moving + looking down: straighten the torso to the standing-shoot posture so both arms reach the gun exactly like standing
            var chestB = anim.GetBoneTransform(HumanBodyBones.UpperChest) ?? anim.GetBoneTransform(HumanBodyBones.Chest);
            if (chestB)
            {
                if (!running && w > 0.99f) { standChestRot = Quaternion.Inverse(transform.rotation) * chestB.rotation; haveStandChest = true; }
                else if (standPose && haveStandChest) chestB.rotation = transform.rotation * standChestRot;
            }
            var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            var desired = cam.transform.TransformPoint(fpHandPos);
            var ua = anim.GetBoneTransform(HumanBodyBones.RightUpperArm); var fr = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
            var ct = cam.transform;
            var yawRot = Quaternion.Euler(0f, ct.eulerAngles.y, 0f);
            if (!(running && !standPose) && w > 0.99f && !haveCamRel) { camRel = Quaternion.Inverse(yawRot) * (ct.position - ua.position); haveCamRel = true; }
            if (haveCamRel) { ct.position = Vector3.Lerp(ct.position, ua.position + yawRot * camRel, w); desired = ct.TransformPoint(fpHandPos); }
            // moving + looking down: walk anim leans the torso forward, so anchor the view (and thus both hands) to the standing spot on the body
            if (!running && w > 0.99f) standCamLocal = transform.InverseTransformPoint(ct.position);
            else if (standPose) { ct.position = transform.TransformPoint(standCamLocal); desired = ct.TransformPoint(fpHandPos); }
            var rHint = ua.position - ct.up * 0.4f + ct.right * 0.25f - ct.forward * 0.1f;
            TwoBoneIK(ua, fr, hand, Vector3.Lerp(hand.position, desired, w), rHint);
            var target = ct.position + ct.forward * 20f;
            var want = Quaternion.LookRotation((target - gp.position).normalized, ct.up) * Quaternion.Inverse(gp.localRotation);
            hand.rotation = Quaternion.Slerp(hand.rotation, want, w);
            // left hand supports the grip (same pose in every state)
            var la = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm); var lf = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm); var lh = anim.GetBoneTransform(HumanBodyBones.LeftHand);
            bool twoHand = standPose || !(running && !sprinting);
            if (twoHand)
            {
                bool standing = !(running && !standPose) && w > 0.99f;
                var lt = hand.position - gp.right * leftHandOffset.x + gp.up * leftHandOffset.y + gp.forward * leftHandOffset.z;
                var hint = la.position - ct.up * 0.4f - ct.right * 0.25f - ct.forward * 0.1f;
                float lhw = w * (1f - anim.GetLayerWeight(3));
                TwoBoneIK(la, lf, lh, Vector3.Lerp(lh.position, lt, lhw), hint);
                if (standing && !haveLeftRel) { leftRel = Quaternion.Inverse(gp.rotation) * lh.rotation; haveLeftRel = true; }
                if (haveLeftRel) lh.rotation = Quaternion.Slerp(lh.rotation, gp.rotation * leftRel, lhw);
                // copy finger curl from standing too
            }
        }
        else sp.rotation = Quaternion.AngleAxis(aimYawOffset * w, transform.up) * sp.rotation;
    }
    static void TwoBoneIK(Transform a, Transform b, Transform c, Vector3 target, Vector3 hint)
    {
        float ab = Vector3.Distance(a.position, b.position), bc = Vector3.Distance(b.position, c.position);
        Vector3 at = target - a.position; float d = Mathf.Clamp(at.magnitude, 0.01f, ab + bc - 0.001f);
        Vector3 dir = at.normalized;
        float cosA = Mathf.Clamp((ab * ab + d * d - bc * bc) / (2f * ab * d), -1f, 1f);
        Vector3 axis = Vector3.Cross(dir, hint - a.position); if (axis.sqrMagnitude < 1e-6f) axis = Vector3.up; axis.Normalize();
        Vector3 desiredB = a.position + Quaternion.AngleAxis(Mathf.Acos(cosA) * Mathf.Rad2Deg, axis) * dir * ab;
        a.rotation = Quaternion.FromToRotation(b.position - a.position, desiredB - a.position) * a.rotation;
        b.rotation = Quaternion.FromToRotation(c.position - b.position, target - b.position) * b.rotation;
    }
    void UpdateFlash()
    {
        var rh = anim.GetBoneTransform(HumanBodyBones.RightHand); var gp = rh ? rh.Find("GunPivot") : null; if (gp == null) return;
        if (flashLight == null)
        {
            var go = new GameObject("MuzzleFlash"); flashLight = go.AddComponent<Light>(); flashLight.type = LightType.Point;
            flashLight.color = new Color(1f, 0.75f, 0.4f); flashLight.range = 6f; flashLight.intensity = 6f; flashLight.shadows = LightShadows.None;
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(q.GetComponent<Collider>()); q.transform.SetParent(go.transform, false);
            q.transform.localScale = Vector3.one * 0.12f;
            var baseMat = Res.Load<Material>("FX_Flash"); var m = baseMat != null ? new Material(baseMat) : new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));   // FX_Flash is a real asset so the web build keeps its transparent shader variant
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 2f); m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One); m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = 3100;
            var tex = new Texture2D(32, 32); for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++) { float d = Vector2.Distance(new Vector2(x, y), new Vector2(15.5f, 15.5f)) / 16f; float a = Mathf.Clamp01(1f - d); a *= a; tex.SetPixel(x, y, new Color(1f, 0.85f, 0.5f, a)); }
            tex.Apply(); m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", new Color(1f, 0.8f, 0.4f, 1f));
            q.GetComponent<Renderer>().material = m; q.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; flashQuad = q.transform;
        }
        bool on = flashTimer > 0f; flashTimer -= Time.deltaTime;
        flashLight.gameObject.SetActive(on);
        if (on)
        {
            flashLight.transform.position = gp.position + gp.forward * muzzleDist + gp.up * 0.03f;
            if (Camera.main) flashQuad.rotation = Quaternion.LookRotation(flashQuad.position - Camera.main.transform.position) * Quaternion.Euler(0, 0, Random.Range(0f, 360f));
            flashQuad.localScale = Vector3.one * Random.Range(0.1f, 0.18f);
        }
    }
}
