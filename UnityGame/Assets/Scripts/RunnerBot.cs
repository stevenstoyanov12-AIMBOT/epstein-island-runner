using UnityEngine;

// An animated mannequin (tools/blender_character/make_runner.py) that roams the garden for the watching
// statue to hunt: it walks between random spots, sometimes breaks into a sprint, and "fires" every few
// seconds, which makes it loud (GazeTarget) so the statue's eyes lock on and its lasers follow it.
// When the statue is charging at it, it panics and sprints for cover.
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
    string walkClip, runClip;
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
            }
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
            target.MakeNoise();
            nextShot = Time.time + Random.Range(shotInterval.x, shotInterval.y);
            flash = 0.08f;
        }
        // a quick muzzle flash when it fires, so you can see who made the noise
        flash -= Time.deltaTime;
        muzzle.enabled = flash > 0f;
        if (muzzle.enabled)
        {
            var hand = transform.position + transform.up * 1.2f + transform.right * 0.25f + transform.forward * 0.3f;
            muzzle.SetPosition(0, hand);
            muzzle.SetPosition(1, hand + transform.forward * 0.6f);
        }
    }
}
