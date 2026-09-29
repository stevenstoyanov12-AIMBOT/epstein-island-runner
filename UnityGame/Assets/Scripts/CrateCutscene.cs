using UnityEngine;
using UnityEngine.InputSystem;

// 13-second night cutscene from inside a crate stacked in the front corner of a van's cargo area, looking
// out through a missing slat at the other crates and, through the rear-door windows, the street, its lamps
// and a car's headlights far behind. A failing ceiling lamp flickers over the cargo. Then headlights flare,
// tyres screech and the van is hit side-on: it slides sideways across the road and spins, leaning hard; the
// rear doors burst open, the crates by the doors break apart and the rest of the cargo is thrown about.
// Plays on start; press C to watch it again. Sound (engine, bumps, screech, crash) is synthesised at runtime.
public class CrateCutscene : MonoBehaviour
{
    public Camera cutsceneCamera;
    public Transform eye;                 // eye inside the player's crate; shake and glances are applied on top
    public Transform van;                 // van root; lights are placed relative to it
    public Light moon;                    // night light for the street, enabled only during the cutscene
    public NightStreet street;
    public Transform distantCar;          // a pair of headlights far behind the van, keeping pace in the dark
    public Rigidbody[] cargo;             // loose crates that tumble in the crash
    public Rigidbody[] breakable;         // crates that burst into planks on impact
    public Material debrisMaterial;
    public Transform doorLeft, doorRight; // rear door leaves, pivoting on their hinges
    public Rigidbody vanBody;             // kinematic body the crash moves (the street stays put)
    public bool playOnStart = true;
    public float duration = 13f;
    public float cruiseSpeed = 11f;       // m/s the street slides by

    static readonly float[] LampTimes = { 1.1f, 2.5f, 3.9f, 5.3f, 6.7f, 8.0f };
    static readonly float[] BumpTimes = { 2.1f, 3.9f, 5.6f, 7.4f };
    const float Headlights = 8.9f, Screech = 9.3f, Impact = 9.8f, FadeOut = 11.6f;
    static readonly Color NightSky = new Color(0.02f, 0.03f, 0.06f);

    Light[] lamps;
    Light ceiling, oncoming, fill;
    AudioSource engine, fx;
    AudioClip bumpClip, screechClip, crashClip;
    Camera previousCamera;
    Light[] disabledSuns;
    Color ambientSky, ambientEquator, ambientGround, fogColor;
    bool fog, crashed, screeched;
    float fogDensity, time = -1f, fade;
    Vector3 distantStart;
    FogMode fogMode;
    Vector3[] cargoPos;
    Quaternion[] cargoRot;
    int nextBump;
    Vector3 vanStartPos;
    Quaternion vanStartRot;
    readonly System.Collections.Generic.List<GameObject> debris = new System.Collections.Generic.List<GameObject>();
    static readonly Vector3 SpinPivot = new Vector3(0f, 0f, -1.8f);   // middle of the cargo area, van-local

    public bool Playing => time >= 0f;

    void Start()
    {
        cutsceneCamera.enabled = false;
        cutsceneCamera.clearFlags = CameraClearFlags.SolidColor;
        cutsceneCamera.backgroundColor = NightSky;
        lamps = new Light[LampTimes.Length];
        for (int i = 0; i < lamps.Length; i++)
            lamps[i] = MakeLight($"PassingLamp{i}", LightType.Spot, new Color(1f, 0.72f, 0.42f), 70f);
        oncoming = MakeLight("OncomingHeadlights", LightType.Spot, new Color(0.92f, 0.95f, 1f), 45f);
        ceiling = MakeLight("CeilingLamp", LightType.Point, new Color(0.75f, 0.85f, 0.7f), 0f);
        ceiling.transform.localPosition = new Vector3(0f, 1.9f, -2.8f);
        ceiling.range = 5f;
        ceiling.shadows = LightShadows.Soft;
        fill = MakeLight("CrateFill", LightType.Point, new Color(0.5f, 0.55f, 0.7f), 0f);
        // a soft light on the crates in front, so the stencilled lettering reads in the dark
        fill.transform.position = eye.position + eye.forward * 1.1f + Vector3.up * 0.3f;
        fill.range = 3.5f;
        fill.shadows = LightShadows.None;

        engine = Source(true);
        engine.clip = SynthEngine();
        fx = Source(false);
        bumpClip = SynthBump();
        screechClip = SynthScreech();
        crashClip = SynthCrash();
        if (distantCar != null) distantStart = distantCar.localPosition;

        cargoPos = new Vector3[cargo.Length];
        cargoRot = new Quaternion[cargo.Length];
        for (int i = 0; i < cargo.Length; i++)
        {
            cargoPos[i] = cargo[i].transform.localPosition;
            cargoRot[i] = cargo[i].transform.localRotation;
        }
        if (moon != null) moon.enabled = false;
        vanStartPos = van.position;
        vanStartRot = van.rotation;
        if (playOnStart) Play();
    }

    AudioSource Source(bool loop)
    {
        var s = gameObject.AddComponent<AudioSource>();
        s.loop = loop;
        s.playOnAwake = false;
        return s;
    }

    Light MakeLight(string name, LightType type, Color color, float angle)
    {
        var go = new GameObject(name);
        go.transform.SetParent(van, false);
        var l = go.AddComponent<Light>();
        l.type = type;
        l.color = color;
        if (type == LightType.Spot)
        {
            l.spotAngle = angle;
            l.range = 14f;
            l.shadows = LightShadows.Soft;
        }
        l.intensity = 0f;
        l.enabled = false;
        return l;
    }

    public void Play()
    {
        previousCamera = Camera.main != cutsceneCamera ? Camera.main : null;
        if (previousCamera != null)
        {
            previousCamera.enabled = false;
            SetFlyCamera(previousCamera, false);
        }
        cutsceneCamera.enabled = true;

        // night inside a closed van: no sun, almost no sky light, haze outside, a moon over the street
        disabledSuns = System.Array.FindAll(FindObjectsByType<Light>(FindObjectsSortMode.None),
            l => l.type == LightType.Directional && l.enabled);
        foreach (var s in disabledSuns) s.enabled = false;
        ambientSky = RenderSettings.ambientSkyColor;
        ambientEquator = RenderSettings.ambientEquatorColor;
        ambientGround = RenderSettings.ambientGroundColor;
        RenderSettings.ambientSkyColor = RenderSettings.ambientEquatorColor = RenderSettings.ambientGroundColor = new Color(0.01f, 0.012f, 0.02f);
        fog = RenderSettings.fog;
        fogColor = RenderSettings.fogColor;
        fogDensity = RenderSettings.fogDensity;
        fogMode = RenderSettings.fogMode;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = NightSky;
        RenderSettings.fogDensity = 0.016f;
        if (moon != null) moon.enabled = true;
        if (street != null) street.speed = cruiseSpeed;

        // reset the van, doors and cargo; clear the wreckage of the last run
        van.SetPositionAndRotation(vanStartPos, vanStartRot);
        SetDoors(0f);
        foreach (var d in debris) Destroy(d);
        debris.Clear();
        foreach (var b in breakable) b.gameObject.SetActive(true);
        for (int i = 0; i < cargo.Length; i++)
        {
            cargo[i].isKinematic = true;
            cargo[i].transform.localPosition = cargoPos[i];
            cargo[i].transform.localRotation = cargoRot[i];
        }

        foreach (var l in lamps) l.enabled = true;
        oncoming.enabled = ceiling.enabled = fill.enabled = true;
        time = 0f;
        nextBump = 0;
        crashed = screeched = false;
        engine.volume = 0.55f;
        engine.pitch = 1f;
        engine.Play();
    }

    void Stop()
    {
        time = -1f;
        engine.Stop();
        foreach (var l in lamps) l.enabled = false;
        oncoming.enabled = ceiling.enabled = fill.enabled = false;
        foreach (var s in disabledSuns) s.enabled = true;
        RenderSettings.ambientSkyColor = ambientSky;
        RenderSettings.ambientEquatorColor = ambientEquator;
        RenderSettings.ambientGroundColor = ambientGround;
        RenderSettings.fog = fog;
        RenderSettings.fogColor = fogColor;
        RenderSettings.fogDensity = fogDensity;
        RenderSettings.fogMode = fogMode;
        if (moon != null) moon.enabled = false;
        if (street != null) street.speed = 0f;
        cutsceneCamera.enabled = false;
        if (previousCamera != null)
        {
            previousCamera.enabled = true;
            SetFlyCamera(previousCamera, true);
        }
        fade = 1f;  // fade back in from black over the game view
    }

    void Update()
    {
        if (!Playing)
        {
            fade = Mathf.MoveTowards(fade, 0f, Time.deltaTime * 1.6f);
            if (Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame) Play();
            return;
        }
        time += Time.deltaTime;
        if (time >= duration)
        {
            Stop();
            return;
        }
        float t = time;
        fade = t < 1f ? 1f - t : t > FadeOut ? Mathf.Clamp01((t - FadeOut) / (duration - FadeOut - 0.2f)) : 0f;
        float sinceCrash = t - Impact;
        bool afterCrash = sinceCrash > 0f;

        // --- camera: breathing, rumble, bumps, a gentle turn and glances at the other crates
        var offset = new Vector3(0f, Mathf.Sin(t * 1.6f) * 0.004f, 0f);
        float rumble = afterCrash ? 0f : 1f;
        offset += new Vector3(Mathf.PerlinNoise(t * 9f, 0f) - 0.5f, Mathf.PerlinNoise(0f, t * 11f) - 0.5f, 0f) * 0.006f * rumble;
        float pitch = 0f, roll = (Mathf.PerlinNoise(t * 3f, 5f) - 0.5f) * 1.2f * rumble;
        foreach (float b in BumpTimes)
        {
            float k = t - b;
            if (k > 0f && k < 0.6f)
            {
                float jolt = Mathf.Exp(-k * 9f) * Mathf.Sin(k * 38f);
                offset.y -= jolt * 0.035f;
                pitch += jolt * 3f;
            }
        }
        if (nextBump < BumpTimes.Length && t >= BumpTimes[nextBump])
        {
            fx.PlayOneShot(bumpClip, 0.8f);
            nextBump++;
        }
        float lean = Mathf.Sin(Mathf.Clamp01((t - 3f) / 2.2f) * Mathf.PI);
        roll += lean * 4f;
        offset.x += lean * 0.025f;
        // slow drift from the player's crate toward the window and back, like a handheld camera
        float glance = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 2.5f) / 2.5f)) * 8f
                       + Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 6.0f) / 2.0f)) * -8f;
        // headlights flare in the rear window: snap to look at them
        if (t > Headlights && !afterCrash) glance = Mathf.Lerp(glance, 12f, Mathf.Clamp01((t - Headlights) / 0.3f));

        // --- the crash: a violent jolt sideways, then the camera settles askew, still trembling
        if (afterCrash)
        {
            float slam = Mathf.Exp(-sinceCrash * 5f);
            float tilt = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(sinceCrash / 0.4f));
            offset.x -= 0.12f * tilt;
            offset += Random.insideUnitSphere * 0.08f * slam;
            roll += -14f * tilt + Mathf.Sin(sinceCrash * 30f) * 9f * slam;
            pitch += Mathf.Sin(sinceCrash * 23f) * 6f * slam;
            glance += Mathf.Sin(sinceCrash * 17f) * 10f * slam;
        }
        var ct = cutsceneCamera.transform;
        ct.position = eye.TransformPoint(offset);
        ct.rotation = eye.rotation * Quaternion.Euler(pitch - 3f, glance, roll);

        // --- street keeps rolling by until the crash stops everything dead
        if (street != null) street.speed = afterCrash ? cruiseSpeed * Mathf.Exp(-sinceCrash * 6f) : cruiseSpeed;

        // --- street lamps sweeping past the rear window
        for (int i = 0; i < lamps.Length; i++)
        {
            float k = (t - LampTimes[i]) / 1.3f;
            var l = lamps[i];
            l.intensity = k > 0f && k < 1f ? Mathf.Sin(k * Mathf.PI) * 45f : 0f;
            l.transform.localPosition = new Vector3(Mathf.Lerp(-3.5f, 3.5f, k), 2.5f, 3.5f);
            l.transform.LookAt(van.TransformPoint(new Vector3(0f, 1f, -1.6f)));
        }

        // --- oncoming headlights, growing and swinging in from the left, then gone at the impact
        float approach = Mathf.Clamp01((t - Headlights) / (Impact - Headlights));
        oncoming.intensity = t > Headlights && !afterCrash ? Mathf.Lerp(10f, 160f, approach * approach) : 0f;
        oncoming.transform.localPosition = new Vector3(Mathf.Lerp(-7f, -1.5f, approach), 1.4f, Mathf.Lerp(9f, 2.5f, approach));
        oncoming.transform.LookAt(eye.position);
        if (t > Screech && !screeched) { fx.PlayOneShot(screechClip, 0.9f); screeched = true; }

        // --- the failing ceiling lamp: buzzes and stutters, blows out in the crash, then sparks
        float flicker = Mathf.PerlinNoise(t * 7f, 1f) > 0.33f ? 1f : 0.15f;
        if (Mathf.PerlinNoise(t * 23f, 2f) > 0.82f) flicker = 0f;
        ceiling.intensity = afterCrash
            ? (sinceCrash < 0.15f ? 6f : (Random.value > 0.93f && sinceCrash < 1.4f ? 2.5f : 0f))
            : 0.9f * flicker;

        if (!crashed && afterCrash)
        {
            crashed = true;
            fx.PlayOneShot(crashClip, 1f);
            engine.Stop();
            // the crates break loose; the ones by the doors burst apart
            foreach (var rb in cargo)
            {
                rb.isKinematic = false;
                rb.AddForce(van.TransformDirection(new Vector3(-2.5f, 2f, Random.Range(0.5f, 2.5f))), ForceMode.VelocityChange);
                rb.AddTorque(Random.insideUnitSphere * 4f, ForceMode.VelocityChange);
            }
            foreach (var rb in breakable) Shatter(rb);
        }

        // --- a car far behind, headlights on, drifting between lanes
        if (distantCar != null)
            distantCar.localPosition = distantStart + new Vector3(Mathf.Sin(t * 0.35f) * 1.6f, 0f, Mathf.Sin(t * 0.5f) * 5f);

        fill.intensity = afterCrash ? 0.05f : 0.6f;
    }

    // The crash moves the van body: shoved sideways across the road, the back swinging round, leaning hard
    // and dropping back. Done in FixedUpdate on a kinematic rigidbody so the loose cargo reacts to it.
    void FixedUpdate()
    {
        if (!Playing || vanBody == null) return;
        float s = time - Impact;
        if (s <= 0f) return;
        float slide = 3.4f * (1f - Mathf.Exp(-s * 2.2f));
        float yaw = 42f * (1f - Mathf.Exp(-s * 1.8f));
        float lean = 22f * Mathf.Sin(Mathf.Clamp01(s / 1.1f) * Mathf.PI) + 3f * Mathf.Clamp01(s - 1.1f);
        var rot = vanStartRot * Quaternion.Euler(0f, yaw, -lean);
        var pivot = vanStartPos + vanStartRot * (SpinPivot + new Vector3(slide, 0f, 0f));
        vanBody.MovePosition(pivot - rot * SpinPivot);
        vanBody.MoveRotation(rot);
        // the rear doors burst open, swing wide and bounce off their stops
        float open = Mathf.Clamp01(1f - Mathf.Exp(-s * 7f)) + 0.12f * Mathf.Sin(s * 9f) * Mathf.Exp(-s * 2f);
        SetDoors(open);
    }

    // 0 = closed, 1 = swung wide open outward (the leaves' hinges are at the van's rear corners)
    void SetDoors(float open)
    {
        if (doorLeft != null) doorLeft.localRotation = Quaternion.Euler(0f, 115f * open, 0f);
        if (doorRight != null) doorRight.localRotation = Quaternion.Euler(0f, -115f * open, 0f);
    }

    // Swap a crate for a burst of loose planks flying apart.
    void Shatter(Rigidbody crate)
    {
        var c = crate.transform;
        var centre = c.TransformPoint(new Vector3(0f, 0.6f, 0f));
        crate.gameObject.SetActive(false);
        for (int i = 0; i < 16; i++)
        {
            var plank = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plank.name = "Plank";
            plank.GetComponent<MeshRenderer>().sharedMaterial = debrisMaterial;
            plank.transform.position = centre + Random.insideUnitSphere * 0.45f;
            plank.transform.rotation = Random.rotation;
            plank.transform.localScale = new Vector3(Random.Range(0.4f, 1.1f), 0.022f, Random.Range(0.09f, 0.17f));
            var rb = plank.AddComponent<Rigidbody>();
            rb.mass = 3f;
            rb.linearVelocity = (plank.transform.position - centre).normalized * Random.Range(2f, 5f)
                                + van.TransformDirection(new Vector3(-1.5f, 1.5f, 1.5f));
            rb.angularVelocity = Random.insideUnitSphere * 12f;
            debris.Add(plank);
        }
    }

    static void SetFlyCamera(Camera cam, bool on)
    {
        var fly = cam.GetComponent<FlyCamera>();
        if (fly != null) fly.enabled = on;
    }

    void OnGUI()
    {
        if (Playing)
        {
            float bar = Screen.height * 0.07f;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, bar), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0, Screen.height - bar, Screen.width, bar), Texture2D.whiteTexture);
            // white flash of the impact
            float since = time - Impact;
            if (since > 0f && since < 0.25f)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.6f * (1f - since / 0.25f));
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            }
        }
        if (fade > 0f)
        {
            GUI.color = new Color(0f, 0f, 0f, fade);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        }
    }

    // --- synthesised sound --------------------------------------------------------------

    const int Rate = 44100;

    static AudioClip Clip(string name, float[] data, float muffle = 0.18f, float gain = 0.9f)
    {
        // muffle: two passes of a one-pole low-pass, as heard through the crate and van walls
        for (int pass = 0; pass < 2; pass++)
        {
            float y = 0f;
            for (int i = 0; i < data.Length; i++) { y += (data[i] - y) * muffle; data[i] = y; }
        }
        float peak = 0.0001f;
        foreach (var s in data) peak = Mathf.Max(peak, Mathf.Abs(s));
        for (int i = 0; i < data.Length; i++) data[i] = data[i] / peak * gain;
        var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    static float Noise(System.Random r) => (float)r.NextDouble() * 2f - 1f;

    static AudioClip SynthEngine()
    {
        // exactly 2 s so every partial completes whole cycles and the loop is seamless
        var d = new float[Rate * 2];
        var rng = new System.Random(1);
        float n = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = (float)i / Rate;
            n += (Noise(rng) - n) * 0.02f;
            float firing = Mathf.Sin(2 * Mathf.PI * 42f * t) + 0.6f * Mathf.Sin(2 * Mathf.PI * 84f * t)
                           + 0.3f * Mathf.Sin(2 * Mathf.PI * 126f * t);
            d[i] = firing * 0.5f * (1f + 0.15f * Mathf.Sin(2 * Mathf.PI * 3f * t)) + n * 3f;
        }
        return Clip("Engine", d);
    }

    static AudioClip SynthBump()
    {
        var d = new float[(int)(Rate * 0.7f)];
        var rng = new System.Random(2);
        for (int i = 0; i < d.Length; i++)
        {
            float t = (float)i / Rate;
            float thud = Mathf.Sin(2 * Mathf.PI * 55f * t) * Mathf.Exp(-t * 14f);
            float rattle = 0f;  // the crate's boards rattling
            for (int c = 0; c < 6; c++)
            {
                float ct = t - 0.03f - c * 0.06f;
                if (ct > 0f) rattle += Noise(rng) * Mathf.Exp(-ct * 90f) * 0.6f;
            }
            d[i] = thud + rattle;
        }
        return Clip("Bump", d);
    }

    static AudioClip SynthScreech()
    {
        // tyres skidding: squealing band of noise wobbling in pitch, rising
        var d = new float[(int)(Rate * 0.6f)];
        var rng = new System.Random(4);
        float phase = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = (float)i / Rate;
            phase += 2 * Mathf.PI * (1400f + 300f * t + 90f * Mathf.Sin(t * 60f)) / Rate;
            d[i] = (Mathf.Sin(phase) * 0.7f + Noise(rng) * 0.3f) * Mathf.Clamp01(t * 8f);
        }
        return Clip("Screech", d, 0.45f, 0.7f);
    }

    static AudioClip SynthCrash()
    {
        // the side impact: a deep boom, crunching metal, glass shattering and wood splintering
        var d = new float[(int)(Rate * 2.4f)];
        var rng = new System.Random(6);
        var glass = new float[40];
        for (int g = 0; g < glass.Length; g++) glass[g] = 0.05f + (float)rng.NextDouble() * 0.9f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = (float)i / Rate;
            float boom = Mathf.Sin(2 * Mathf.PI * Mathf.Lerp(70f, 32f, Mathf.Clamp01(t * 2f)) * t) * Mathf.Exp(-t * 3f) * 1.6f;
            float crunch = Noise(rng) * Mathf.Exp(-t * 4f) * (0.8f + 0.4f * Mathf.Sin(t * 170f));
            float metal = (Mathf.Sin(2 * Mathf.PI * 236f * t) + Mathf.Sin(2 * Mathf.PI * 377f * t) * 0.7f
                           + Mathf.Sin(2 * Mathf.PI * 611f * t) * 0.5f) * Mathf.Exp(-t * 2.2f) * 0.35f;
            float shards = 0f;
            foreach (float g in glass)
            {
                float gt = t - g;
                if (gt > 0f && gt < 0.08f) shards += Mathf.Sin(2 * Mathf.PI * (3000f + g * 4000f) * gt) * Mathf.Exp(-gt * 70f) * 0.4f;
            }
            float splinter = t > 0.1f && t < 0.9f ? Noise(rng) * Mathf.Exp(-(t - 0.1f) * 5f) * (Noise(rng) > 0.6f ? 1f : 0.1f) * 0.8f : 0f;
            d[i] = boom + crunch + metal + shards + splinter;
        }
        return Clip("Crash", d, 0.55f, 1f);
    }
}
