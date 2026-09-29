using UnityEngine;

// The watching statue. Rules, kept simple:
//  - Shooting makes you LOUD for a few seconds (GazeTarget.MakeNoise).
//  - The statue turns toward the loudest player it can see.
//  - If it keeps you in view it charges for 1.5 s (sparks swirl into its eyes, the eyes flare, a whine
//    rises, an aiming line tightens on you) and then fires a sustained laser from both eyes.
//  - The beam drags after the loudest player - a little slower than a sprint, so you can outrun it -
//    and swings over to anyone who becomes louder mid-beam, burning whatever it passes over.
//  - If nobody is loud, it slowly sweeps around.
// Eye colour says what it is doing: dim red = searching, bright red = locking on, white = firing.
public class StatueGaze : MonoBehaviour
{
    public Transform head;                    // the part that turns (the bust)
    public Transform[] eyes;                  // eye centres; beams come from here
    public Transform[] pupils;                // pivots at each eye's centre carrying the iris and pupil
    public float turnSpeed = 140f;            // degrees per second when locking on
    public float sweepSpeed = 25f;
    public float viewAngle = 40f;             // half-angle of the gaze cone
    public float range = 30f;
    public float chargeTime = 1.5f;
    public float beamTime = 1.3f;
    public float cooldown = 2f;
    public float damagePerSecond = 40f;
    public float beamTrackSpeed = 4.5f;       // m/s the beam's aim can move: a sprinting player can escape it
    public float switchMargin = 0.15f;        // how much louder someone must be to steal the beam

    enum State { Searching, Charging, Firing, Cooldown }
    State state;
    float timer, sweepYaw;
    GazeTarget target;
    Vector3 aimPoint;                         // where the beam is pointing (lags behind the target)
    Light gazeLight;
    Renderer[] irisRenderers;
    LaserEye[] lasers;
    AudioSource whine, hum, fx;
    AudioClip crack, sizzle;

    static readonly Color Dim = new Color(0.8f, 0.5f, 0.5f), Bright = new Color(1f, 0.25f, 0.2f), White = new Color(1f, 1f, 1f);

    void Start()
    {
        sweepYaw = head.eulerAngles.y;
        irisRenderers = new Renderer[eyes.Length];
        lasers = new LaserEye[eyes.Length];
        for (int i = 0; i < eyes.Length; i++)
        {
            var iris = eyes[i].Find("PupilPivot/Iris");   // the glowing part whose tint shows the statue's mood
            irisRenderers[i] = iris != null ? iris.GetComponent<Renderer>() : eyes[i].GetComponentInChildren<Renderer>();
            lasers[i] = new LaserEye(eyes[i]);
        }
        // soft cone of light showing where the statue is looking
        gazeLight = new GameObject("GazeLight").AddComponent<Light>();
        gazeLight.transform.SetParent(head, false);
        gazeLight.transform.position = EyeCentre;
        gazeLight.transform.rotation = head.rotation;
        gazeLight.type = LightType.Spot;
        gazeLight.spotAngle = viewAngle * 2f;
        gazeLight.range = range;
        gazeLight.intensity = 6f;
        gazeLight.shadows = LightShadows.Soft;

        whine = Source(false);
        whine.clip = Tone("Whine", 1.6f, (t, s) => (Mathf.Sin(2 * Mathf.PI * (200f + 700f * t * t) * s)
                                                  + 0.3f * Mathf.Sin(2 * Mathf.PI * (400f + 1400f * t * t) * s)) * (0.3f + 0.7f * t));
        hum = Source(true);   // the beam's sustained crackling buzz; exactly 1 s of whole cycles so it loops cleanly
        hum.clip = Tone("BeamHum", 1f, (t, s) => 0.6f * (2f * ((s * 110f) % 1f) - 1f) + 0.4f * Mathf.Sin(2 * Mathf.PI * 220f * s)
                                                 + (Random.value < 0.02f ? Random.Range(-1f, 1f) : 0f));
        fx = Source(false);
        crack = Tone("Crack", 0.5f, (t, s) => (Random.Range(-1f, 1f) * 0.7f + Mathf.Sin(2 * Mathf.PI * 90f * s)) * Mathf.Exp(-t * 9f));
        sizzle = Tone("Sizzle", 0.6f, (t, s) => Random.Range(-1f, 1f) * (0.5f + 0.5f * Mathf.Sin(s * 300f)) * Mathf.Exp(-t * 4f));
    }

    Vector3 EyeCentre => (eyes[0].position + eyes[eyes.Length - 1].position) / 2f;

    AudioSource Source(bool loop)
    {
        var a = gameObject.AddComponent<AudioSource>();
        a.loop = loop;
        a.playOnAwake = false;
        a.spatialBlend = 0.6f;
        return a;
    }

    void LateUpdate()
    {
        // The pupils glide over the eyes toward wherever the statue is looking (they stay within the lids);
        // with nothing to look at they circle slowly, each a little out of step with the other.
        for (int i = 0; i < pupils.Length; i++)
        {
            Quaternion look;
            bool focused = state == State.Firing || (target != null && state != State.Cooldown);
            if (focused)
            {
                var point = state == State.Firing ? aimPoint : target.AimPoint;
                var dir = pupils[i].parent.InverseTransformDirection(point - pupils[i].position);
                look = Quaternion.RotateTowards(Quaternion.identity, Quaternion.LookRotation(dir), 8f);
            }
            else
            {
                float a = Time.time * 1.3f + i * 0.5f;
                look = Quaternion.Euler(Mathf.Sin(a) * 7f, Mathf.Cos(a) * 7f, 0f);
            }
            pupils[i].localRotation = Quaternion.Slerp(pupils[i].localRotation, look, Time.deltaTime * 12f);
        }
        LaserEye.UpdateScorches();
    }

    void Update()
    {
        switch (state)
        {
            case State.Searching:
                target = LoudestVisible();
                if (target != null)
                {
                    if (TurnToward(target.AimPoint, turnSpeed) && CanSee(target))
                    {
                        state = State.Charging;
                        timer = 0f;
                        whine.Play();
                    }
                }
                else
                {
                    sweepYaw += sweepSpeed * Time.deltaTime;
                    head.rotation = Quaternion.RotateTowards(head.rotation, Quaternion.Euler(0f, sweepYaw, 0f), sweepSpeed * 2f * Time.deltaTime);
                }
                Tint(Dim, target != null ? Bright : Dim);
                foreach (var l in lasers) l.Idle(target != null ? 0.3f : 0f);
                break;

            case State.Charging:
                timer += Time.deltaTime;
                if (target == null || !target.Alive || !CanSee(target))
                {
                    // lost you: the charge fizzles out
                    whine.Stop();
                    foreach (var l in lasers) l.SetIdle();
                    state = State.Searching;
                    break;
                }
                TurnToward(target.AimPoint, turnSpeed);
                float k = Mathf.Clamp01(timer / chargeTime);
                Tint(Color.Lerp(Bright, White, k * k), Color.Lerp(Bright, White, k));
                for (int i = 0; i < lasers.Length; i++) lasers[i].Charge(k, target.AimPoint);
                if (timer >= chargeTime) StartBeam();
                break;

            case State.Firing:
                timer += Time.deltaTime;
                UpdateBeam();
                if (timer >= beamTime) EndBeam();
                break;

            case State.Cooldown:
                timer += Time.deltaTime;
                // the eyes cool from white-hot back to a dim ember
                float heat = Mathf.Exp(-timer * 2.5f);
                Tint(Color.Lerp(Dim, White, heat), Dim);
                foreach (var l in lasers) l.Idle(heat);
                if (timer >= cooldown)
                {
                    state = State.Searching;
                    sweepYaw = head.eulerAngles.y;
                }
                break;
        }
    }

    void StartBeam()
    {
        whine.Stop();
        fx.PlayOneShot(crack, 1f);
        hum.volume = 0.5f;
        hum.pitch = 1f;
        hum.Play();
        aimPoint = target.AimPoint;
        state = State.Firing;
        timer = 0f;
    }

    void UpdateBeam()
    {
        // follow the noisiest player: anyone clearly louder than the current target steals the beam
        var loudest = LoudestVisible();
        if (loudest != null && loudest != target && (target == null || !target.Alive || loudest.Noise > target.Noise + switchMargin))
            target = loudest;
        if (target != null && target.Alive)
            aimPoint = Vector3.MoveTowards(aimPoint, target.AimPoint, beamTrackSpeed * Time.deltaTime);
        TurnToward(aimPoint, turnSpeed);

        float fade = Mathf.Clamp01((beamTime - timer) / 0.25f);   // thins out over the last quarter second
        hum.volume = 0.5f * fade;
        hum.pitch = 1f + 0.05f * Mathf.Sin(timer * 20f);
        Tint(White, Color.Lerp(Bright, White, 0.5f));
        gazeLight.intensity = 6f + 6f * fade;

        bool hitAnyone = false;
        for (int i = 0; i < lasers.Length; i++)
        {
            var from = eyes[i].position;
            var dir = (aimPoint - from).normalized;
            Vector3 end = from + dir * range, normal = -dir;
            bool hitSomething = false;
            if (Physics.Raycast(from + dir * 0.15f, dir, out var hit, range))
            {
                end = hit.point;
                normal = hit.normal;
                hitSomething = true;
                var victim = hit.collider.GetComponentInParent<GazeTarget>();
                if (victim != null && victim.Alive && fade > 0.3f && i == 0)   // damage once, not once per eye
                {
                    victim.Hit(damagePerSecond * Time.deltaTime);
                    hitAnyone = true;
                }
            }
            lasers[i].Beam(end, normal, hitSomething, timer, fade);
        }
        if (hitAnyone && !fx.isPlaying) fx.PlayOneShot(sizzle, 0.6f);
    }

    void EndBeam()
    {
        hum.Stop();
        foreach (var l in lasers) l.SetIdle();
        gazeLight.intensity = 6f;
        state = State.Cooldown;
        timer = 0f;
    }

    // iris tint (multiplies the iris texture) and gaze-cone colour
    void Tint(Color iris, Color cone)
    {
        foreach (var r in irisRenderers)
            if (r != null) r.material.SetColor("_BaseColor", iris);
        gazeLight.color = cone;
    }

    GazeTarget LoudestVisible()
    {
        GazeTarget best = null;
        foreach (var t in GazeTarget.All)
            if (t.Noise > 0.05f && t.Alive && (best == null || t.Noise > best.Noise) && CanSee(t, true))
                best = t;
        return best;
    }

    // Line of sight from the eyes. `anyDirection` ignores the view cone (it can hear shots behind it).
    bool CanSee(GazeTarget t, bool anyDirection = false)
    {
        var eye = EyeCentre;
        var to = t.AimPoint - eye;
        if (to.magnitude > range) return false;
        if (!anyDirection && Vector3.Angle(head.forward, to) > viewAngle) return false;
        if (Physics.Raycast(eye + to.normalized * 0.3f, to.normalized, out var hit, to.magnitude - 0.3f))
            return hit.transform.GetComponentInParent<GazeTarget>() == t;
        return true;
    }

    bool TurnToward(Vector3 point, float speed)
    {
        var flat = point - head.position;
        flat.y = 0f;
        if (flat.sqrMagnitude < 0.01f) return true;
        var want = Quaternion.LookRotation(flat);
        head.rotation = Quaternion.RotateTowards(head.rotation, want, speed * Time.deltaTime);
        return Quaternion.Angle(head.rotation, want) < 3f;
    }

    // f(t, s): t = 0..1 through the clip, s = seconds
    static AudioClip Tone(string name, float seconds, System.Func<float, float, float> f)
    {
        const int rate = 44100;
        var d = new float[(int)(rate * seconds)];
        float peak = 0.0001f;
        for (int i = 0; i < d.Length; i++)
        {
            float s = i / (float)rate;
            d[i] = f(s / seconds, s);
            peak = Mathf.Max(peak, Mathf.Abs(d[i]));
        }
        for (int i = 0; i < d.Length; i++) d[i] = d[i] / peak * 0.6f;
        var clip = AudioClip.Create(name, d.Length, 1, rate, false);
        clip.SetData(d, 0);
        return clip;
    }
}
