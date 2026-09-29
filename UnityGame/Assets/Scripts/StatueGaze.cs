using System.Collections.Generic;
using UnityEngine;

// The watching statue. Rules, kept simple:
//  - Shooting makes you LOUD for a few seconds (GazeTarget.MakeNoise).
//  - The statue turns toward the loudest player it can see.
//  - If it keeps you in view it charges (eyes brighten, rising whine, aiming line) and then fires a laser.
//  - If nobody is loud, it slowly sweeps around.
// Eye colour says what it is doing: dim red = searching, bright red = locking on, white = about to fire.
public class StatueGaze : MonoBehaviour
{
    public Transform head;                    // the part that turns (the bust)
    public Transform[] eyes;                  // eyeballs (fixed in the sockets); beams come from here
    public Transform[] pupils;                // pivots at each eye's centre carrying a pupil on the surface
    public float turnSpeed = 140f;            // degrees per second when locking on
    public float sweepSpeed = 25f;
    public float viewAngle = 40f;             // half-angle of the gaze cone
    public float range = 30f;
    public float chargeTime = 1.5f;
    public float cooldown = 2f;
    public float damage = 35f;

    enum State { Searching, Charging, Cooldown }
    State state;
    float timer, sweepYaw;
    GazeTarget target;
    Light gazeLight;
    Renderer[] eyeRenderers;
    LineRenderer[] aimLines, beams;
    AudioSource whine, zap;
    // eye tint (multiplies the eye texture) and gaze-cone colours
    static readonly Color Dim = new Color(0.8f, 0.5f, 0.5f), Bright = new Color(1f, 0.25f, 0.2f), White = new Color(1f, 1f, 1f);

    void Start()
    {
        sweepYaw = head.eulerAngles.y;
        eyeRenderers = new Renderer[eyes.Length];
        aimLines = new LineRenderer[eyes.Length];
        beams = new LineRenderer[eyes.Length];
        var lineMat = new Material(Shader.Find("Sprites/Default"));
        for (int i = 0; i < eyes.Length; i++)
        {
            var iris = eyes[i].Find("PupilPivot/Iris");   // the glowing part whose tint shows the statue's mood
            eyeRenderers[i] = iris != null ? iris.GetComponent<Renderer>() : eyes[i].GetComponentInChildren<Renderer>();
            aimLines[i] = Line(eyes[i], lineMat, 0.008f);
            beams[i] = Line(eyes[i], lineMat, 0.06f);
        }
        // soft red cone showing where the statue is looking
        gazeLight = new GameObject("GazeLight").AddComponent<Light>();
        gazeLight.transform.SetParent(head, false);
        gazeLight.transform.position = (eyes[0].position + eyes[eyes.Length - 1].position) / 2f;
        gazeLight.transform.rotation = head.rotation;
        gazeLight.type = LightType.Spot;
        gazeLight.spotAngle = viewAngle * 2f;
        gazeLight.range = range;
        gazeLight.intensity = 6f;
        gazeLight.shadows = LightShadows.Soft;

        whine = gameObject.AddComponent<AudioSource>();
        whine.clip = Tone("Whine", 1.6f, t => Mathf.Sin(2 * Mathf.PI * (300f + 900f * t * t) * t) * 0.5f);
        whine.playOnAwake = false;
        zap = gameObject.AddComponent<AudioSource>();
        zap.clip = Tone("Zap", 0.4f, t => (Mathf.Sin(2 * Mathf.PI * 1800f * t * (1 - t)) + Random.Range(-0.4f, 0.4f)) * Mathf.Exp(-t * 8f));
        zap.playOnAwake = false;
    }

    LineRenderer Line(Transform parent, Material mat, float width)
    {
        var lr = new GameObject("Line").AddComponent<LineRenderer>();
        lr.transform.SetParent(parent, false);
        lr.material = mat;
        lr.widthMultiplier = width;
        lr.positionCount = 2;
        lr.enabled = false;
        return lr;
    }

    void LateUpdate()
    {
        // The eyeballs never turn (so nothing but glowing iris ever shows in the sockets); the pupils glide
        // over them instead: fixed on the target when it has one, otherwise circling all the way round,
        // each a little out of step with the other.
        for (int i = 0; i < pupils.Length; i++)
        {
            Quaternion look;
            if (target != null && state != State.Cooldown)
            {
                var dir = pupils[i].parent.InverseTransformDirection(target.AimPoint - pupils[i].position);
                look = Quaternion.RotateTowards(Quaternion.identity, Quaternion.LookRotation(dir), 8f);   // stays inside the eyelids
            }
            else
            {
                float a = Time.time * 1.3f + i * 0.5f;
                look = Quaternion.Euler(Mathf.Sin(a) * 7f, Mathf.Cos(a) * 7f, 0f);
            }
            pupils[i].localRotation = Quaternion.Slerp(pupils[i].localRotation, look, Time.deltaTime * 12f);
        }
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
                SetEyes(Dim, target != null ? Bright : Dim, 0f);
                break;

            case State.Charging:
                timer += Time.deltaTime;
                if (target == null || !CanSee(target))
                {
                    // lost you: back to searching
                    whine.Stop();
                    state = State.Searching;
                    break;
                }
                TurnToward(target.AimPoint, turnSpeed);
                float k = timer / chargeTime;
                SetEyes(Color.Lerp(Bright, White, k * k), Color.Lerp(Bright, White, k), k);
                if (timer >= chargeTime) Fire();
                break;

            case State.Cooldown:
                timer += Time.deltaTime;
                foreach (var b in beams) b.widthMultiplier = Mathf.Lerp(0.06f, 0f, timer / 0.25f);
                if (timer > 0.25f) foreach (var b in beams) b.enabled = false;
                SetEyes(Dim, Dim, 0f);
                if (timer >= cooldown)
                {
                    state = State.Searching;
                    sweepYaw = head.eulerAngles.y;
                }
                break;
        }
    }

    void Fire()
    {
        whine.Stop();
        zap.Play();
        for (int i = 0; i < eyes.Length; i++)
        {
            var from = eyes[i].position;
            var to = target.AimPoint;
            if (Physics.Raycast(from + (to - from).normalized * 0.3f, (to - from).normalized, out var hit, range))
                to = hit.point;
            beams[i].enabled = true;
            beams[i].widthMultiplier = 0.06f;
            beams[i].startColor = beams[i].endColor = White;
            beams[i].SetPositions(new[] { from, to });
            aimLines[i].enabled = false;
        }
        if (CanSee(target)) target.Hit(damage);
        state = State.Cooldown;
        timer = 0f;
    }

    // eye glow, gaze-cone colour, and the aiming line strength (0 = off)
    void SetEyes(Color eye, Color cone, float aim)
    {
        foreach (var r in eyeRenderers)
            if (r != null) r.material.SetColor("_BaseColor", eye);
        gazeLight.color = cone;
        for (int i = 0; i < eyes.Length; i++)
        {
            aimLines[i].enabled = aim > 0f && target != null;
            if (!aimLines[i].enabled) continue;
            var c = new Color(1f, 0.2f, 0.1f, 0.2f + 0.6f * aim);
            aimLines[i].startColor = aimLines[i].endColor = c;
            aimLines[i].SetPositions(new[] { eyes[i].position, target.AimPoint });
        }
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
        var eye = (eyes[0].position + eyes[eyes.Length - 1].position) / 2f;
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

    static AudioClip Tone(string name, float seconds, System.Func<float, float> f)
    {
        const int rate = 44100;
        var d = new float[(int)(rate * seconds)];
        for (int i = 0; i < d.Length; i++) d[i] = f(i / (float)rate / seconds) * 0.6f;
        var clip = AudioClip.Create(name, d.Length, 1, rate, false);
        clip.SetData(d, 0);
        return clip;
    }
}
