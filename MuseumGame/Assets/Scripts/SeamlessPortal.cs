using UnityEngine;
// See-through doorway: shows the destination live through the door and moves the player there seamlessly on crossing.
[DefaultExecutionOrder(1100)]
public class SeamlessPortal : MonoBehaviour
{
    public Vector3 doorCenter = new Vector3(18f, 1.56f, 0.49f); // center of the opening
    public Vector2 doorSize = new Vector2(1.74f, 3.28f);        // width (along z), height
    public Vector3 sideNormal = Vector3.right;                   // points toward the side the player comes from
    public Vector3 srcRef = new Vector3(18f, 0.92f, 0.49f);      // player pivot at the door
    public Vector3 dstRef = new Vector3(-26f, 10.8f, 17.8f);     // matching player pivot at the destination
    public float yawDelta = 180f; public bool controlsHall = true; public bool earlyTrigger = true;
    public Renderer hideDoor;
    public Collider[] passThrough; static int sqFrame = -1; static bool sqAny; bool ignoring; bool pendingCross; Vector3 prevPos; bool prevInit; float origStep = -1f, origRadius = -1f, shrinkR = 0.1f;
    Transform player; Camera main, pcam; RenderTexture rt; Transform quad; static float globalCooldown; float lastSide, lastPs, cooldown, nearSaved = 0.1f; bool nearTight; Behaviour oldTeleport;
    // other narrow doorways (DoorSqueeze) ask for the slim capsule through here so every door shares one verdict per frame
    public static void RequestSqueeze() { if (Time.frameCount != sqFrame) { sqFrame = Time.frameCount; sqAny = false; } sqAny = true; }
    Quaternion Rot => Quaternion.Euler(0, yawDelta, 0);
    // After the body goes through, the (third-person) camera is still behind it on the near side.
    // Keep rendering from the near side (looking through the portal) until the camera itself crosses.
    public static SeamlessPortal camPortal; static float camSince;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics() { camPortal = null; camSince = 0; globalCooldown = 0; sqFrame = -1; sqAny = false; blurs = null; blurFrame = -1; nearestPortal = 1e9f; }
    static int blurFrame = -1; static float nearestPortal = 1e9f;
    static System.Collections.Generic.List<UnityEngine.Rendering.Universal.MotionBlur> blurs; static System.Collections.Generic.List<float> blurBase;
    static void ApplyBlur(float d)
    {
        if (blurs == null)
        {
            blurs = new System.Collections.Generic.List<UnityEngine.Rendering.Universal.MotionBlur>(); blurBase = new System.Collections.Generic.List<float>();
            foreach (var v in FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            { if (!v.sharedProfile) continue; UnityEngine.Rendering.Universal.MotionBlur mb; if (v.profile.TryGet(out mb)) { blurs.Add(mb); blurBase.Add(mb.intensity.value); } }
        }
        float f = Mathf.Clamp01((d - 2.5f) / 4f);
        for (int i = 0; i < blurs.Count; i++) if (blurs[i] != null) blurs[i].intensity.value = blurBase[i] * f;
    }
    public Vector3 InvMap(Vector3 p) => srcRef + Quaternion.Inverse(Rot) * (p - dstRef);
    public Quaternion InvRot(Quaternion r) => Quaternion.Inverse(Rot) * r;
    public static bool Remap(ref Vector3 origin, ref Vector3 target, ref Quaternion rot)
    {
        var P = camPortal; if (P == null) return false;
        Vector3 t = P.InvMap(target);
        if (Time.time - camSince > 2f || P.Side(t) <= 0.02f) { camPortal = null; return false; }
        origin = P.InvMap(origin); target = t; rot = P.InvRot(rot); return true;
    }
    Vector3 MapPoint(Vector3 p) => dstRef + Rot * (p - srcRef);

    void Start()
    {
        var p = GameObject.Find("Player"); if (p) player = p.transform; main = Camera.main;
        if (hideDoor) hideDoor.enabled = false;
        var gd = controlsHall ? GameObject.Find("GalleryDoor") : null; if (gd) { oldTeleport = gd.GetComponent("DoorTeleport") as Behaviour; if (oldTeleport) oldTeleport.enabled = false; }
        var cg = new GameObject("PortalCam"); pcam = cg.AddComponent<Camera>(); pcam.CopyFrom(main); pcam.enabled = false;
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(q.GetComponent<Collider>()); quad = q.transform; quad.name = "PortalSurface";
        quad.position = doorCenter; quad.rotation = Quaternion.LookRotation(-sideNormal); quad.localScale = new Vector3(doorSize.x, doorSize.y, 1);
        q.GetComponent<Renderer>().material = new Material(Shader.Find("Custom/PortalView")); q.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (player) { lastSide = Side(main.transform.position); lastPs = Side(player.position); }
    }
    public float Side(Vector3 p) => Vector3.Dot(p - doorCenter, sideNormal);
    bool InOpening(Vector3 p)
    {
        var l = Quaternion.Inverse(quad.rotation) * (p - doorCenter);
        return Mathf.Abs(l.x) < doorSize.x * 0.5f + 0.02f && l.y > -doorSize.y * 0.5f - 1.5f && l.y < doorSize.y * 0.5f + 0.5f;
    }
    void LateUpdate()
    {
        if (!player || !main) return;
        if (rt == null || rt.width != main.pixelWidth || rt.height != main.pixelHeight)
        {
            if (rt) rt.Release(); rt = new RenderTexture(main.pixelWidth, main.pixelHeight, 24, RenderTextureFormat.DefaultHDR); rt.filterMode = FilterMode.Point; pcam.targetTexture = rt; // HDR: bloom/tonemap match the real view
            quad.GetComponent<Renderer>().material.SetTexture("_MainTex", rt);
        }
        // motion blur reads the portal quad's (near) depth, so it over-blurs the view through the door, then snaps sharp after crossing.
        // Fade blur out near any portal; both sides of a crossing are near a portal, so it stays continuous.
        if (Time.frameCount != blurFrame) { ApplyBlur(nearestPortal); blurFrame = Time.frameCount; nearestPortal = 1e9f; }
        nearestPortal = Mathf.Min(nearestPortal, Vector3.Distance(main.transform.position, doorCenter));
        var pcc = player.GetComponent<CharacterController>();
        bool near = InOpening(player.position) && Mathf.Abs(Side(player.position)) < 1.2f;
        if (pcc && passThrough != null && passThrough.Length > 0 && near != ignoring) { foreach (var c in passThrough) if (c) Physics.IgnoreCollision(pcc, c, near); ignoring = near; }
        // squeeze through narrow doorframes: while close to the opening make the capsule slim (restored right after), so the player no longer jams on the jambs when approaching off-centre
        if (pcc)
        {
            if (origRadius < 0f) origRadius = pcc.radius;
            var lp = Quaternion.Inverse(quad.rotation) * (player.position - doorCenter); float sd = Side(player.position);
            bool squeeze = Mathf.Abs(lp.x) < doorSize.x * 0.5f + origRadius + 0.2f && sd > -1.0f && sd < 1.6f && lp.y > -doorSize.y * 0.5f - 1.5f && lp.y < doorSize.y * 0.5f + 0.8f;
            if (Time.frameCount != sqFrame) { sqFrame = Time.frameCount; sqAny = false; } if (squeeze) sqAny = true;   // both doors share one verdict per frame, otherwise they fight over the radius
            float want = sqAny ? Mathf.Min(shrinkR, origRadius) : origRadius;
            if (!Mathf.Approximately(pcc.radius, want)) pcc.radius = want;
            if (origStep < 0f) origStep = pcc.stepOffset; float wantStep = sqAny ? Mathf.Min(0.1f, origStep) : origStep;   // a slim capsule climbs the door sills better with a low step
            if (!Mathf.Approximately(pcc.stepOffset, wantStep)) pcc.stepOffset = wantStep;
        }
        // teleport when the camera crosses the door plane inside the opening
        float s = Side(main.transform.position);
        float ps = Side(player.position);
        // teleport exactly when the body crosses the door plane (camera is pulled into the head by then)
        if (!prevInit || (player.position - prevPos).sqrMagnitude > 9f) { prevPos = player.position; prevInit = true; lastPs = ps; pendingCross = false; }   // spawn / teleport jump: no crossing this frame
        if (lastPs > 0f && ps <= 0f)
        { float tt = lastPs / Mathf.Max(1e-5f, lastPs - ps); if (InOpening(Vector3.Lerp(prevPos, player.position, tt)) || InOpening(player.position)) pendingCross = true; }   // swept test: fast steps can't skip the plane
        if (ps > 0.05f) pendingCross = false;
        bool crossed = pendingCross && Time.time > globalCooldown;
        prevPos = player.position;
        // the camera can be ahead of the body (pushed in by walls): whichever crosses first triggers
        const float camEps = 0.04f; // near-plane margin: never let the near plane touch the portal surface
        bool camCrossed = lastSide > camEps && s <= camEps && InOpening(main.transform.position);
        // solid (painted) doorway: trigger when the body touches it; destination side is open, so no step is needed
        float moveIn = lastPs - ps; float rad = pcc ? pcc.radius : 0.35f;
        bool touch = false && earlyTrigger && ps < rad + 0.08f && ps > -0.5f && moveIn > 0.0005f && moveIn < 0.5f && InOpening(player.position);
        lastPs = ps;
        if (crossed) pendingCross = false;
        if ((crossed || camCrossed || touch) && Time.time > globalCooldown) { bool camThrough = s <= camEps; Teleport(camThrough); globalCooldown = Time.time + 0.3f; s = Side(main.transform.position); lastPs = Side(player.position); camPortal = camThrough ? null : this; camSince = Time.time; if (HallVisibility.I) HallVisibility.I.Tick(); }
        lastSide = s;
        bool visible = Vector3.Distance(main.transform.position, doorCenter) < 40f && s > 0;
        quad.gameObject.SetActive(visible); 
        if (!visible) return;
        pcam.fieldOfView = main.fieldOfView; pcam.aspect = main.aspect;
        pcam.transform.SetPositionAndRotation(MapPoint(main.transform.position), Rot * main.transform.rotation);
        // clip everything in front of the virtual door so walls behind it don't block the view
        var n = Rot * (-sideNormal); var cpos = MapPoint(doorCenter);
        var plane = new Plane(n, cpos);
        var camSpacePos = pcam.worldToCameraMatrix.MultiplyPoint(cpos); var camSpaceN = pcam.worldToCameraMatrix.MultiplyVector(n);
        float d = -Vector3.Dot(camSpacePos, camSpaceN);
        pcam.projectionMatrix = main.projectionMatrix;
        if (Mathf.Abs(d) > 0.05f) pcam.projectionMatrix = pcam.CalculateObliqueMatrix(new Vector4(camSpaceN.x, camSpaceN.y, camSpaceN.z, d));
        var hid = new System.Collections.Generic.List<Renderer>();
        foreach (var r in HallVisibility.Overlaps) if (r && r.enabled) { r.enabled = false; hid.Add(r); }
        if (controlsHall) HallVisibility.PortalRender(true);
        pcam.Render();
        if (controlsHall) HallVisibility.PortalRender(false);
        foreach (var r in hid) r.enabled = true;
    }
    void Teleport(bool moveCam)
    {
        var cc = player.GetComponent<CharacterController>(); if (cc) cc.enabled = false;
        Vector3 camPos = main.transform.position; Quaternion camRot = main.transform.rotation; // camera is a child of the player
        player.position = MapPoint(player.position);
        var fpc = player.GetComponent("FirstPersonController");
        if (fpc)
        {
            var f = fpc.GetType().GetField("yaw", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (f != null) f.SetValue(fpc, (float)f.GetValue(fpc) + yawDelta);
        }
        player.rotation = Rot * player.rotation;
        if (cc) cc.enabled = true;
        // camera already through: move it with the body (same frame, identical view). Otherwise it stays on the near side (Remap) until it crosses
        // the camera jumps across the world this frame: reset motion-vector/TAA history so motion blur doesn't smear the handover
        var acd = main.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>(); if (acd) acd.resetHistory = true;
        if (moveCam) main.transform.SetPositionAndRotation(MapPoint(camPos), Rot * camRot);
        else main.transform.SetPositionAndRotation(camPos, camRot);
    }
}
