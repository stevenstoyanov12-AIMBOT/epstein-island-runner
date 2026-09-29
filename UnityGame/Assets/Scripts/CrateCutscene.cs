using UnityEngine;
using UnityEngine.InputSystem;

// 10-second cutscene: hidden in a crate in the back of a moving van, looking out through the slats.
// Street lamps sweep light through the gaps, the van rumbles, hits bumps and takes a turn, then brakes,
// the doors open and daylight floods in before fading out. Plays on start; press C to watch it again.
// Sound is synthesised at runtime, muffled as if heard from inside the crate.
public class CrateCutscene : MonoBehaviour
{
    public Camera cutsceneCamera;
    public Transform eye;                 // crouched eye position inside the crate
    public Transform van;                 // van root; lamps and door light are placed relative to it
    public Transform doorLeft, doorRight; // rear door leaves, pivoting on their hinges
    public bool playOnStart = true;
    public float duration = 10f;

    static readonly float[] LampTimes = { 1.1f, 2.7f, 4.3f, 5.8f };
    static readonly float[] BumpTimes = { 2.1f, 3.9f, 5.4f, 6.3f };
    const float BrakeStart = 7.1f, DoorsOpen = 8.5f, FadeOut = 9.1f;

    Light[] lamps;
    Light doorLight, fill;
    AudioSource engine, fx;
    AudioClip bumpClip, brakeClip, doorClip;
    Camera previousCamera;
    Light[] disabledSuns;
    Color ambientSky, ambientEquator, ambientGround;
    float time = -1f, fade;
    int nextBump;
    bool braked, doorsOpened;

    public bool Playing => time >= 0f;

    void Start()
    {
        cutsceneCamera.enabled = false;
        lamps = new Light[LampTimes.Length];
        for (int i = 0; i < lamps.Length; i++)
            lamps[i] = MakeLight($"PassingLamp{i}", LightType.Spot, new Color(1f, 0.72f, 0.42f), 70f);
        doorLight = MakeLight("DoorDaylight", LightType.Spot, new Color(0.85f, 0.9f, 1f), 100f);
        fill = MakeLight("CrateFill", LightType.Point, new Color(0.5f, 0.55f, 0.7f), 0f);
        fill.transform.position = eye.position + eye.forward * 0.2f;
        fill.range = 1.2f;
        fill.shadows = LightShadows.None;

        engine = gameObject.AddComponent<AudioSource>();
        engine.clip = SynthEngine();
        engine.loop = true;
        engine.playOnAwake = false;
        fx = gameObject.AddComponent<AudioSource>();
        fx.playOnAwake = false;
        bumpClip = SynthBump();
        brakeClip = SynthBrake();
        doorClip = SynthDoor();

        if (playOnStart) Play();
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
            l.range = 12f;
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
        SetDoors(0f);

        // it's night inside a closed van: no sun, no sky light
        disabledSuns = System.Array.FindAll(FindObjectsByType<Light>(FindObjectsSortMode.None),
            l => l.type == LightType.Directional && l.enabled);
        foreach (var s in disabledSuns) s.enabled = false;
        ambientSky = RenderSettings.ambientSkyColor;
        ambientEquator = RenderSettings.ambientEquatorColor;
        ambientGround = RenderSettings.ambientGroundColor;
        RenderSettings.ambientSkyColor = RenderSettings.ambientEquatorColor = RenderSettings.ambientGroundColor = new Color(0.01f, 0.01f, 0.015f);

        foreach (var l in lamps) l.enabled = true;
        doorLight.enabled = fill.enabled = true;
        time = 0f;
        nextBump = 0;
        braked = doorsOpened = false;
        engine.pitch = 1f;
        engine.volume = 0.55f;
        engine.Play();
    }

    void Stop()
    {
        time = -1f;
        engine.Stop();
        foreach (var l in lamps) l.enabled = false;
        doorLight.enabled = fill.enabled = false;
        foreach (var s in disabledSuns) s.enabled = true;
        RenderSettings.ambientSkyColor = ambientSky;
        RenderSettings.ambientEquatorColor = ambientEquator;
        RenderSettings.ambientGroundColor = ambientGround;
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
        fade = t < 1f ? 1f - t : t > FadeOut ? Mathf.Clamp01((t - FadeOut) / (duration - FadeOut - 0.1f)) : 0f;

        // --- camera: breathing, engine rumble, bumps, the turn, braking, and a nervous glance
        var offset = new Vector3(0f, Mathf.Sin(t * 1.6f) * 0.006f, 0f);
        float rumble = t < BrakeStart ? 1f : Mathf.Clamp01(1f - (t - BrakeStart) / 1.2f) * 0.8f + 0.2f;
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
            fx.PlayOneShot(bumpClip, 0.9f);
            nextBump++;
        }
        float turn = Mathf.Clamp01((t - 3f) / 2.2f);
        roll += Mathf.Sin(turn * Mathf.PI) * 4.5f;                       // leaning through a left turn
        offset.x += Mathf.Sin(turn * Mathf.PI) * 0.03f;
        if (t > BrakeStart)
        {
            float k = t - BrakeStart;
            pitch += Mathf.Sin(Mathf.Clamp01(k / 1.1f) * Mathf.PI) * 5f;  // thrown forward, then settle
            offset.z -= Mathf.Sin(Mathf.Clamp01(k / 1.1f) * Mathf.PI) * 0.05f;
            engine.pitch = Mathf.Lerp(1f, 0.55f, k / 1.2f);
            engine.volume = Mathf.Lerp(0.55f, 0.2f, k / 1.2f);
            if (!braked) { fx.PlayOneShot(brakeClip, 0.5f); braked = true; }
        }
        float glance = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 3.4f) / 0.6f)) * -22f
                       + Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 5f) / 0.8f)) * 30f
                       + Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 6.6f) / 0.8f)) * -8f;
        if (t > DoorsOpen) glance *= Mathf.Clamp01(1f - (t - DoorsOpen) / 0.4f);  // snap to the doors
        var ct = cutsceneCamera.transform;
        ct.position = eye.TransformPoint(offset);
        ct.rotation = eye.rotation * Quaternion.Euler(pitch - 4f, glance, roll);

        // --- street lamps sweeping past the rear windows, one after another
        for (int i = 0; i < lamps.Length; i++)
        {
            float k = (t - LampTimes[i]) / 1.3f;                        // 0..1 while this lamp passes
            var l = lamps[i];
            bool active = k > 0f && k < 1f && t < BrakeStart + 0.6f;
            l.intensity = active ? Mathf.Sin(k * Mathf.PI) * 60f : 0f;
            // high enough that the beam lines up with the rear-door windows on its way to the crate
            l.transform.localPosition = new Vector3(Mathf.Lerp(-3.5f, 3.5f, k), 3.4f, 3.5f);
            l.transform.LookAt(eye.position + Vector3.up * 0.2f);
        }

        // --- doors open: a clunk, then daylight pours in through the slats
        doorLight.transform.localPosition = new Vector3(0f, 1.6f, 2.5f);
        doorLight.transform.LookAt(eye.position);
        if (t > DoorsOpen)
        {
            if (!doorsOpened) { fx.PlayOneShot(doorClip, 1f); doorsOpened = true; }
            doorLight.intensity = Mathf.SmoothStep(0f, 140f, (t - DoorsOpen - 0.15f) / 0.5f);
            SetDoors(Mathf.SmoothStep(0f, 1f, (t - DoorsOpen - 0.1f) / 0.9f));
        }
        else
            doorLight.intensity = 0f;
        fill.intensity = 0.06f + (t > DoorsOpen ? 0.4f : 0f);
    }

    // 0 = closed, 1 = swung open outward (the leaves' hinges are at the van's rear corners)
    void SetDoors(float open)
    {
        if (doorLeft != null) doorLeft.localRotation = Quaternion.Euler(0f, 105f * open, 0f);
        if (doorRight != null) doorRight.localRotation = Quaternion.Euler(0f, -105f * open, 0f);
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
            // letterbox bars
            float bar = Screen.height * 0.11f;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, bar), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0, Screen.height - bar, Screen.width, bar), Texture2D.whiteTexture);
        }
        if (fade > 0f)
        {
            GUI.color = new Color(0f, 0f, 0f, fade);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        }
    }

    // --- synthesised, muffled sound --------------------------------------------------------

    const int Rate = 44100;

    static AudioClip Clip(string name, float[] data)
    {
        // muffle: two passes of a one-pole low-pass, as heard through the crate and van walls
        for (int pass = 0; pass < 2; pass++)
        {
            float y = 0f;
            for (int i = 0; i < data.Length; i++) { y += (data[i] - y) * 0.18f; data[i] = y; }
        }
        float peak = 0.0001f;
        foreach (var s in data) peak = Mathf.Max(peak, Mathf.Abs(s));
        for (int i = 0; i < data.Length; i++) data[i] = data[i] / peak * 0.9f;
        var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    static AudioClip SynthEngine()
    {
        // exactly 2 s so every partial below completes whole cycles and the loop is seamless
        var d = new float[Rate * 2];
        var rng = new System.Random(1);
        float noise = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = (float)i / Rate;
            noise += ((float)rng.NextDouble() * 2f - 1f - noise) * 0.02f;
            float firing = Mathf.Sin(2 * Mathf.PI * 42f * t) + 0.6f * Mathf.Sin(2 * Mathf.PI * 84f * t)
                           + 0.3f * Mathf.Sin(2 * Mathf.PI * 126f * t);
            float wobble = 1f + 0.15f * Mathf.Sin(2 * Mathf.PI * 3f * t);
            d[i] = firing * 0.5f * wobble + noise * 3f;
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
            // the crate's boards rattling: short noisy clicks
            float rattle = 0f;
            for (int c = 0; c < 6; c++)
            {
                float ct = t - 0.03f - c * 0.06f;
                if (ct > 0f) rattle += ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-ct * 90f) * 0.6f;
            }
            d[i] = thud + rattle;
        }
        return Clip("Bump", d);
    }

    static AudioClip SynthBrake()
    {
        var d = new float[(int)(Rate * 1.3f)];
        for (int i = 0; i < d.Length; i++)
        {
            float t = (float)i / Rate;
            float env = Mathf.Clamp01(t * 6f) * Mathf.Clamp01((1.3f - t) * 1.5f);
            d[i] = Mathf.Sin(2 * Mathf.PI * (1700f + 60f * Mathf.Sin(t * 30f)) * t) * env * 0.5f;
        }
        return Clip("Brake", d);
    }

    static AudioClip SynthDoor()
    {
        var d = new float[(int)(Rate * 1.2f)];
        var rng = new System.Random(3);
        for (int i = 0; i < d.Length; i++)
        {
            float t = (float)i / Rate;
            float latch = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 60f);
            float ring = (Mathf.Sin(2 * Mathf.PI * 180f * t) + 0.5f * Mathf.Sin(2 * Mathf.PI * 412f * t)) * Mathf.Exp(-t * 5f);
            float swing = t > 0.25f ? ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-(t - 0.25f) * 3f) * 0.3f : 0f;
            d[i] = latch + ring * 0.6f + swing;
        }
        return Clip("Doors", d);
    }
}
