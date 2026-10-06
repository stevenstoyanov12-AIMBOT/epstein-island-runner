using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// Another player as seen locally.
// - Buffered interpolation: drawn ~120 ms in the past, blended between two real snapshots (no chasing, no teleport jitter).
// - Aim: camera pitch bends the spine so the body and gun point where the player actually looks.
// - Hitboxes: colliders on skeleton bones (head, torso, limbs) that follow animation and crouch.
// - Death: everyone sees the body fall with the same death animation (Resources/Death) until the player respawns.
public class RemotePlayer : MonoBehaviour
{
    public string id;
    Animator anim; Transform model; GazeTarget gaze;
    float speed, pitch, reload, reloadT; bool crouch, shoot, wasShoot, alive = true, first = true;   // reload: progress from the last message, reloadT: when it arrived
    readonly List<Transform> holdBones = new List<Transform>(); readonly List<Quaternion> holdRel = new List<Quaternion>();   // fallback hold (arms + fingers of the select-screen aiming pose, relative to the model root)
    PlayerAvatarAnim localAv; Quaternion bodyRel = Quaternion.identity; float carryW;   // walking hold: gun lowered, both hands on it (same as the local body)

    struct Snap { public float t; public Vector3 pos; public float yaw, pitch; }
    readonly List<Snap> snaps = new List<Snap>();
    float interpDelay = 0.12f, gapJit;    // drawn this far in the past; grows by itself when snapshots arrive unevenly (slow or busy sender)
    const float MaxExtrap = 0.25f;        // when the newest snapshot is already in the past, keep gliding at the last velocity for up to this long
    Vector3 shown, shownVel; bool haveShown;   // what is drawn: the interpolated target, eased so late packets never snap the body
    const float TeleportDist = 5f;        // bigger jumps snap instead of sliding
    float clockOffset; bool haveOffset;   // maps sender clock -> local clock

    Transform spine, chest, upperChest;
    readonly List<Collider> hitboxes = new List<Collider>();
    CapsuleCollider rootCol; float standH, standCY, crouchH = 1f;
    PlayableGraph deathGraph; bool deadShown; Vector3 modelLocal;

    public static RemotePlayer Create(string id, string character, Transform localRoot)
    {
        var prefab = Res.Load<GameObject>("SelectModels/" + character);
        if (prefab == null || localRoot == null) { Debug.LogWarning("RemotePlayer: cannot create '" + character + "' (prefab " + (prefab != null) + ", local player " + (localRoot != null) + ")"); return null; }
        var go = new GameObject("Remote_" + id);
        var rp = go.AddComponent<RemotePlayer>(); rp.id = id;

        var cc = localRoot.GetComponent<CharacterController>();
        var col = go.AddComponent<CapsuleCollider>();
        if (cc != null) { col.center = cc.center; col.height = cc.height; col.radius = cc.radius; } else { col.center = new Vector3(0f, 0.9f, 0f); col.height = 1.8f; col.radius = 0.3f; }
        var fpc = localRoot.GetComponent<FirstPersonController>();
        rp.rootCol = col; rp.standH = col.height; rp.standCY = col.center.y; if (fpc != null) rp.crouchH = fpc.crouchHeight;

        var inst = Instantiate(prefab, go.transform); inst.name = "Model_" + character;
        CharacterSelect.FixSleeves(inst, character);
        var av = Object.FindFirstObjectByType<PlayerAvatarAnim>();
        Animator localAnim = av != null ? av.GetComponentInChildren<Animator>(false) : null;
        if (localAnim != null)
        {
            inst.transform.localPosition = localRoot.InverseTransformPoint(localAnim.transform.position);
            inst.transform.localRotation = Quaternion.Inverse(localRoot.rotation) * localAnim.transform.rotation;
        }
        inst.transform.localScale = Vector3.one;
        var a = inst.GetComponent<Animator>(); if (a == null) a = inst.AddComponent<Animator>();
        a.enabled = true; a.applyRootMotion = false; a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        if (localAnim != null) a.runtimeAnimatorController = localAnim.runtimeAnimatorController;
        rp.anim = a; rp.model = inst.transform; rp.modelLocal = inst.transform.localPosition;
        rp.BuildFallbackHold(inst.transform);
        rp.localAv = av; if (av != null) rp.bodyRel = Quaternion.Inverse(localRoot.rotation) * av.transform.rotation;
        CharacterSelect.AttachGunTo(inst, a);

        if (a.isHuman)
        {
            rp.spine = a.GetBoneTransform(HumanBodyBones.Spine);
            rp.chest = a.GetBoneTransform(HumanBodyBones.Chest);
            rp.upperChest = a.GetBoneTransform(HumanBodyBones.UpperChest);
            rp.BuildBoneHitboxes(a, inst.layer);
        }

        var lg = localRoot.GetComponent<GazeTarget>();
        var gt = go.AddComponent<GazeTarget>(); gt.remoteProxy = true; gt.testShooting = false; if (lg != null) gt.aimOffset = lg.aimOffset; rp.gaze = gt;
        return rp;
    }

    // until the local body has a captured walking hold, other players still carry the pistol in front of them (arms + fingers of the select-screen aiming pose)
    void BuildFallbackHold(Transform root)
    {
        var c = CharacterSelect.Baked.Get(null); if (c == null || c.count == 0) return;
        var map = new Dictionary<string, Transform>(); foreach (var t in root.GetComponentsInChildren<Transform>(true)) map[t.name] = t;
        for (int i = 0; i < c.names.Length; i++)
        {
            string n = c.names[i];
            if (!(n.Contains("Shoulder") || n.Contains("Arm") || n.Contains("Hand"))) continue;
            Transform t; if (!map.TryGetValue(n, out t)) continue;
            holdBones.Add(t); holdRel.Add(c.q[0][i]);
        }
    }
    void ApplyFallbackHold(float w)
    {
        if (model == null) return;
        var rr = model.rotation;
        for (int i = 0; i < holdBones.Count; i++) if (holdBones[i] != null) holdBones[i].rotation = Quaternion.Slerp(holdBones[i].rotation, rr * holdRel[i], w);
    }

    // ---------- hitboxes ----------
    void BuildBoneHitboxes(Animator a, int layer)
    {
        var torsoTop = Bone(a, HumanBodyBones.Neck, HumanBodyBones.Head);
        var torsoMid = Bone(a, HumanBodyBones.Chest, HumanBodyBones.Spine);
        HeadSphere(a, layer);
        Capsule(torsoMid, torsoTop, "HB_Chest", 0.17f, layer);
        Capsule(a.GetBoneTransform(HumanBodyBones.Hips), torsoMid, "HB_Hips", 0.16f, layer);
        Capsule(a.GetBoneTransform(HumanBodyBones.LeftUpperLeg), a.GetBoneTransform(HumanBodyBones.LeftLowerLeg), "HB_Limb", 0.085f, layer);
        Capsule(a.GetBoneTransform(HumanBodyBones.RightUpperLeg), a.GetBoneTransform(HumanBodyBones.RightLowerLeg), "HB_Limb", 0.085f, layer);
        Capsule(a.GetBoneTransform(HumanBodyBones.LeftLowerLeg), a.GetBoneTransform(HumanBodyBones.LeftFoot), "HB_Limb", 0.065f, layer);
        Capsule(a.GetBoneTransform(HumanBodyBones.RightLowerLeg), a.GetBoneTransform(HumanBodyBones.RightFoot), "HB_Limb", 0.065f, layer);
        Capsule(a.GetBoneTransform(HumanBodyBones.LeftUpperArm), a.GetBoneTransform(HumanBodyBones.LeftLowerArm), "HB_Limb", 0.055f, layer);
        Capsule(a.GetBoneTransform(HumanBodyBones.RightUpperArm), a.GetBoneTransform(HumanBodyBones.RightLowerArm), "HB_Limb", 0.055f, layer);
        Capsule(a.GetBoneTransform(HumanBodyBones.LeftLowerArm), a.GetBoneTransform(HumanBodyBones.LeftHand), "HB_Limb", 0.045f, layer);
        Capsule(a.GetBoneTransform(HumanBodyBones.RightLowerArm), a.GetBoneTransform(HumanBodyBones.RightHand), "HB_Limb", 0.045f, layer);
    }

    static Transform Bone(Animator a, HumanBodyBones b, HumanBodyBones fallback) { var t = a.GetBoneTransform(b); return t != null ? t : a.GetBoneTransform(fallback); }

    void HeadSphere(Animator a, int layer)
    {
        var head = a.GetBoneTransform(HumanBodyBones.Head); if (head == null) return;
        var go = new GameObject("HB_Head"); go.layer = layer; go.transform.SetParent(head, false);
        go.transform.position = head.position + Vector3.up * 0.09f;
        float s = Mathf.Max(go.transform.lossyScale.x, 1e-4f);
        var c = go.AddComponent<SphereCollider>(); c.radius = 0.125f / s;
        hitboxes.Add(c);
    }

    void Capsule(Transform b0, Transform b1, string name, float r, int layer)
    {
        if (b0 == null || b1 == null) return;
        Vector3 p0 = b0.position, p1 = b1.position; float len = Vector3.Distance(p0, p1); if (len < 0.01f) return;
        var go = new GameObject(name); go.layer = layer; go.transform.SetParent(b0, false);
        go.transform.SetPositionAndRotation((p0 + p1) * 0.5f, Quaternion.FromToRotation(Vector3.up, (p1 - p0) / len));
        float s = Mathf.Max(go.transform.lossyScale.x, 1e-4f);
        var c = go.AddComponent<CapsuleCollider>(); c.direction = 1; c.radius = r / s; c.height = (len + r * 2f) / s;
        hitboxes.Add(c);
    }

    // ---------- network input ----------
    public void Apply(Net.Msg m)
    {
        speed = m.sp; crouch = m.cr == 1; shoot = m.sh == 1; alive = m.al == 1; reload = m.rl; reloadT = Time.time;
        if (gaze != null) { gaze.proxyAlive = alive; if (shoot && !wasShoot) gaze.MakeNoise(); wasShoot = shoot; }

        // timestamp on the local clock: use sender time when available so packets that arrive bunched together stay spaced out
        float t;
        if (m.ts > 0f)
        {
            float est = Time.time - m.ts;
            if (!haveOffset || est < clockOffset) { clockOffset = est; haveOffset = true; }
            else clockOffset = Mathf.Lerp(clockOffset, est, 0.02f); // slow drift so a laggy period doesn't stick forever
            t = m.ts + clockOffset;
        }
        else t = Time.time;

        if (snaps.Count > 0 && t <= snaps[snaps.Count - 1].t) return; // out of order / duplicate
        if (snaps.Count > 0) { float gap = Mathf.Min(t - snaps[snaps.Count - 1].t, 1f); gapJit = Mathf.Max(gap, gapJit * 0.97f); interpDelay = Mathf.Lerp(interpDelay, Mathf.Clamp(0.07f + gapJit * 1.4f, 0.12f, 0.35f), 0.1f); }
        snaps.Add(new Snap { t = t, pos = new Vector3(m.x, m.y, m.z), yaw = m.r, pitch = m.p });
        if (snaps.Count > 40) snaps.RemoveAt(0);

        if (first) { transform.SetPositionAndRotation(snaps[0].pos, Quaternion.Euler(0f, m.r, 0f)); pitch = m.p; first = false; }
    }

    // ---------- crates: anyone standing inside one is hidden ----------
    static System.Collections.Generic.List<Bounds> crateBoxes; static float crateScanT = -10f;
    static bool InCrate(Vector3 p)
    {
        if (crateBoxes == null || (crateBoxes.Count == 0 && Time.time - crateScanT > 2f))
        {
            crateScanT = Time.time; crateBoxes = new System.Collections.Generic.List<Bounds>();
            var root = GameObject.Find("CoverCrates");
            if (root) foreach (Transform c in root.transform)
            {
                if (!c.name.StartsWith("Crate_")) continue;
                var bc = c.GetComponentInChildren<BoxCollider>(true); if (bc == null) continue;
                var ls = bc.transform.lossyScale; var size = Vector3.Scale(bc.size, new Vector3(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z)));
                crateBoxes.Add(new Bounds(bc.transform.TransformPoint(bc.center), size));
            }
        }
        foreach (var b in crateBoxes)
        {
            float half = Mathf.Min(b.extents.x, b.extents.z);   // inner square of the (possibly rotated) crate
            if (Mathf.Abs(p.x - b.center.x) < half && Mathf.Abs(p.z - b.center.z) < half && p.y > b.min.y - 0.2f && p.y < b.max.y + 0.2f) return true;
        }
        return false;
    }

    // ---------- per frame ----------
    void Update()
    {
        if (first || snaps.Count == 0) return;

        float rt = Time.time - interpDelay;
        while (snaps.Count > 2 && snaps[1].t <= rt) snaps.RemoveAt(0);

        Vector3 pos; float yaw;
        if (snaps.Count >= 2 && rt >= snaps[0].t)
        {
            var s0 = snaps[0]; var s1 = snaps[1];
            float span = Mathf.Max(s1.t - s0.t, 1e-4f);
            bool jump = (s1.pos - s0.pos).sqrMagnitude > TeleportDist * TeleportDist;
            if (rt <= s1.t || jump)
            {
                float k = Mathf.Clamp01((rt - s0.t) / span);
                if (jump) k = k < 0.5f ? 0f : 1f;
                pos = Vector3.Lerp(s0.pos, s1.pos, k);
                yaw = Mathf.LerpAngle(s0.yaw, s1.yaw, k);
                pitch = Mathf.LerpAngle(s0.pitch, s1.pitch, k);
            }
            else
            {
                // newer data is late: carry on at the last velocity instead of freezing and then jumping when it arrives
                var vel = (s1.pos - s0.pos) / span; if (vel.sqrMagnitude > 100f) vel = Vector3.zero;
                pos = s1.pos + vel * Mathf.Min(rt - s1.t, MaxExtrap); yaw = s1.yaw; pitch = s1.pitch;
            }
        }
        else { var s = snaps[0]; pos = s.pos; yaw = s.yaw; pitch = s.pitch; }
        // ease the drawn body toward the target so corrections (late packet, extrapolation ending) glide; real teleports still snap
        if (!haveShown || (pos - shown).sqrMagnitude > TeleportDist * TeleportDist) { shown = pos; shownVel = Vector3.zero; haveShown = true; }
        else shown = Vector3.SmoothDamp(shown, pos, ref shownVel, 0.045f, Mathf.Infinity, Time.deltaTime);
        transform.SetPositionAndRotation(shown, Quaternion.Euler(0f, yaw, 0f));

        if (!alive && !deadShown) StartDeath();
        else if (alive && deadShown) EndDeath();
        bool show = (alive || deadShown) && !InCrate(shown);   // a player hiding in a crate is never drawn, whatever his client reports
        if (model != null && model.gameObject.activeSelf != show) model.gameObject.SetActive(show);

        // fallback root capsule (only used when the model has no humanoid bones) shrinks when crouching
        float h = crouch ? Mathf.Min(standH, crouchH) : standH;
        rootCol.height = Mathf.Lerp(rootCol.height, h, 1f - Mathf.Exp(-12f * Time.deltaTime));
        rootCol.center = new Vector3(rootCol.center.x, standCY - (standH - rootCol.height) * 0.5f, rootCol.center.z);
        rootCol.enabled = alive && show && hitboxes.Count == 0;
        for (int i = 0; i < hitboxes.Count; i++) if (hitboxes[i] != null) hitboxes[i].enabled = alive && show;   // a dead body can't be shot

        if (anim != null && anim.runtimeAnimatorController != null && alive)
        {
            anim.SetBool("Crouch", crouch);
            anim.SetFloat("Speed", speed < 0.2f ? 0f : (crouch ? 1.5f : (speed > 4.5f ? 6f : 2f)), 0.15f, Time.deltaTime);
            { var lv = transform.InverseTransformDirection(shownVel); lv.y = 0f; if (speed < 0.2f) lv = Vector3.zero; anim.SetFloat("MoveX", lv.x, 0.12f, Time.deltaTime); anim.SetFloat("MoveZ", lv.z, 0.12f, Time.deltaTime); }   // strafe / backpedal from the actual movement
            anim.SetBool("Shooting", shoot);
            carryW = Mathf.MoveTowards(carryW, ((speed >= 0.2f || crouch) && !shoot) ? 1f : 0f, Time.deltaTime * 7f);
            if (anim.layerCount > 2) anim.SetLayerWeight(2, Mathf.MoveTowards(anim.GetLayerWeight(2), shoot ? 1f : 0f, Time.deltaTime * 12f));
        }
    }

    // after the Animator has posed the skeleton: bend the spine by the camera pitch so body + gun aim where the player looks
    void LateUpdate()
    {
        if (deadShown) { KeepBodyOnFloor(); return; }
        if (first || !alive) return;
        float p = Mathf.Clamp(pitch, -60f, 60f);
        Vector3 axis = transform.right;
        int n = (spine != null ? 1 : 0) + (chest != null ? 1 : 0) + (upperChest != null ? 1 : 0);
        if (n == 0) return;
        float part = p / n;
        if (spine != null) spine.rotation = Quaternion.AngleAxis(part, axis) * spine.rotation;
        if (chest != null) chest.rotation = Quaternion.AngleAxis(part, axis) * chest.rotation;
        if (upperChest != null) upperChest.rotation = Quaternion.AngleAxis(part, axis) * upperChest.rotation;
        // arms + fingers take the walking hold last, so the lowered gun doesn't swing up and down with the look pitch
        if (carryW > 0f)
        {
            if (localAv != null && localAv.HasHold) localAv.ApplyHoldTo(anim, transform.rotation * bodyRel, carryW);
            else ApplyFallbackHold(carryW);
        }
        // reload: run the same motion locally between messages (~30 per second), so it plays smoothly
        if (reload > 0f) ReloadPose.Apply(anim, transform, Mathf.Min(0.999f, reload + (Time.time - reloadT) / SimpleGun.ReloadTime));
    }

    // ---------- death, seen by everyone ----------
    void StartDeath()
    {
        deadShown = true;
        if (anim == null) return;
        AnimationClip clip = null;
        foreach (var c in Res.LoadAll<AnimationClip>("Death")) { if (c.name == "mixamo.com") clip = c; if (clip == null) clip = c; }
        if (clip == null) return;
        deathGraph = PlayableGraph.Create("RemoteDeath_" + id);
        deathGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        var o = AnimationPlayableOutput.Create(deathGraph, "death", anim);
        o.SetSourcePlayable(AnimationClipPlayable.Create(deathGraph, clip));
        deathGraph.Play();
    }

    void EndDeath()
    {
        deadShown = false;
        if (deathGraph.IsValid()) deathGraph.Destroy();
        if (model != null) model.localPosition = modelLocal;
        if (anim != null) anim.Rebind();   // back to the normal locomotion controller
    }

    // the death clip's hip height is for a different rig: keep the collapsing body on the floor, never under it
    void KeepBodyOnFloor()
    {
        if (anim == null || model == null || !anim.isHuman) return;
        float lo = float.MaxValue;
        foreach (var hb in new[] { HumanBodyBones.Hips, HumanBodyBones.Head, HumanBodyBones.Chest, HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
                                   HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm })
        { var bt = anim.GetBoneTransform(hb); if (bt) lo = Mathf.Min(lo, bt.position.y); }
        float floor = transform.position.y + 0.04f;
        if (lo < floor) model.position += Vector3.up * (floor - lo);
    }

    void OnDestroy() { if (deathGraph.IsValid()) deathGraph.Destroy(); }

    // SimpleGun: hit.collider.SendMessageUpwards("OnBulletHit", hit)
    void OnBulletHit(RaycastHit hit)
    {
        if (!alive || Net.I == null) return;
        bool head;
        if (hitboxes.Count > 0) head = hit.collider != null && hit.collider.name == "HB_Head";
        else head = hit.point.y > rootCol.bounds.max.y - 0.32f;
        var fpc = Object.FindFirstObjectByType<FirstPersonController>();
        var g = fpc != null ? fpc.GetComponent<GazeTarget>() : null;
        float body = g != null ? Random.Range(g.bodyDamageMin, g.bodyDamageMax) : Random.Range(15f, 16.8f);
        float hd = g != null ? g.headDamage : 34f;
        float dmg = head ? hd : body;
        DamageNumbers.Show(hit.point, dmg, head);   // Fortnite-style number right where the bullet landed
        Net.I.SendHit(id, dmg);
    }
}
