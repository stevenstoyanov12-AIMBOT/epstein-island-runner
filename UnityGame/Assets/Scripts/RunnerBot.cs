using UnityEngine;

// An animated Mixamo character (tools/blender_character/mixamo_combine.py: Walk, Run, Shoot) that roams the
// garden for the watching statue to hunt. It walks between random spots, sometimes sprints, and every few
// seconds shoots on the move: the Shoot clip plays on the upper body only, over the walk or run on the legs,
// so it can walk-and-shoot and run-and-shoot. Shooting makes it loud (GazeTarget), so the statue's eyes
// lock on and its lasers follow it; once loud, it panics and sprints away.
[RequireComponent(typeof(GazeTarget))]
public class RunnerBot : MonoBehaviour
{
    public Vector3 areaCentre = Vector3.zero;
    public float areaRadius = 9f;
    public float walkSpeed = 1.4f;
    public float runSpeed = 4.2f;
    public Vector2 shotInterval = new Vector2(2.5f, 5f);

    GazeTarget target;
    Animation anim;
    string walkClip, runClip, shootClip;
    float shootTime;                          // seconds left of the current burst
    Transform hand;
    Vector3 destination;
    bool running;
    float nextShot, flash;
    LineRenderer muzzle;

    void Start()
    {
        target = GetComponent<GazeTarget>();
        target.testShooting = false;
        anim = GetComponentInChildren<Animation>();
        if (anim != null)
            foreach (AnimationState s in anim)
            {
                s.wrapMode = WrapMode.Loop;
                if (s.name.Contains("Walk")) walkClip = s.name;
                if (s.name.Contains("Run")) runClip = s.name;
                if (s.name.Contains("Shoot")) shootClip = s.name;
            }
        if (anim != null && shootClip != null)
        {
            // shooting only moves the body from the spine up; the legs keep walking or running underneath
            var shoot = anim[shootClip];
            shoot.layer = 1;
            var spine = FindDeep(transform, "mixamorig:Spine");
            if (spine != null) shoot.AddMixingTransform(spine, true);
        }
        hand = FindDeep(transform, "mixamorig:RightHand");
        PickDestination();
        nextShot = Time.time + Random.Range(shotInterval.x, shotInterval.y);

        muzzle = new GameObject("MuzzleFlash").AddComponent<LineRenderer>();
        muzzle.transform.SetParent(transform, false);
        muzzle.material = new Material(Shader.Find("Sprites/Default"));
        muzzle.startColor = new Color(1f, 0.9f, 0.5f, 1f);
        muzzle.endColor = new Color(1f, 0.5f, 0.1f, 0f);
        muzzle.widthMultiplier = 0.03f;
        muzzle.positionCount = 2;
        muzzle.enabled = false;
    }

    static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t)
        {
            var f = FindDeep(c, name);
            if (f != null) return f;
        }
        return null;
    }

    void PickDestination()
    {
        var p = Random.insideUnitCircle * areaRadius;
        destination = areaCentre + new Vector3(p.x, 0f, p.y);
        destination.y = transform.position.y;
        running = Random.value < 0.35f;
    }

    void Update()
    {
        if (!target.Alive)
        {
            if (anim != null) anim.Stop();
            return;
        }
        // panic: once loud, sprint away to somewhere new
        if (target.Noise > 0.6f && !running)
        {
            PickDestination();
            running = true;
        }

        var to = destination - transform.position;
        to.y = 0f;
        if (to.magnitude < 0.4f)
        {
            PickDestination();
            return;
        }
        float speed = running ? runSpeed : walkSpeed;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), 360f * Time.deltaTime);
        transform.position += transform.forward * speed * Time.deltaTime;

        var clip = running ? runClip : walkClip;
        if (anim != null && clip != null && !anim.IsPlaying(clip)) anim.CrossFade(clip, 0.2f);

        if (Time.time >= nextShot)
        {
            shootTime = 1.2f;                  // a short burst: raise the gun, fire a few rounds
            nextShot = Time.time + Random.Range(shotInterval.x, shotInterval.y);
        }
        if (shootTime > 0f)
        {
            shootTime -= Time.deltaTime;
            if (anim != null && shootClip != null) anim.Blend(shootClip, 1f, 0.15f);
            // rounds go off during the burst: each one is a flash and more noise
            if (Mathf.Repeat(shootTime, 0.3f) < Time.deltaTime && shootTime < 1f)
            {
                target.MakeNoise();
                flash = 0.06f;
            }
        }
        else if (anim != null && shootClip != null)
            anim.Blend(shootClip, 0f, 0.25f);   // lower the gun
        // a quick muzzle flash when it fires, so you can see who made the noise
        flash -= Time.deltaTime;
        muzzle.enabled = flash > 0f;
        if (muzzle.enabled)
        {
            var from = hand != null ? hand.position : transform.position + transform.up * 1.2f + transform.forward * 0.3f;
            muzzle.SetPosition(0, from);
            muzzle.SetPosition(1, from + transform.forward * 0.6f);
        }
    }
}
