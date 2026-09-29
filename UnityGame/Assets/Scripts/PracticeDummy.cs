using UnityEngine;

// A practice target for the watching statue: paces back and forth and "fires" every few seconds, which
// makes it loud, so you can stand aside and watch the statue hunt it.
[RequireComponent(typeof(GazeTarget))]
public class PracticeDummy : MonoBehaviour
{
    public float pace = 3f;                   // how far it walks either side of its start
    public float speed = 1.2f;
    public Vector2 shotInterval = new Vector2(3f, 6f);

    GazeTarget target;
    Vector3 home;
    float nextShot;
    Renderer body;

    void Start()
    {
        target = GetComponent<GazeTarget>();
        target.testShooting = false;
        home = transform.position;
        body = GetComponentInChildren<Renderer>();
        nextShot = Time.time + Random.Range(shotInterval.x, shotInterval.y);
    }

    void Update()
    {
        if (!target.Alive) return;
        transform.position = home + transform.right * Mathf.Sin(Time.time * speed / pace + home.x) * pace;
        if (Time.time >= nextShot)
        {
            target.MakeNoise();
            nextShot = Time.time + Random.Range(shotInterval.x, shotInterval.y);
        }
        // glows red while it is loud, so you can see who the statue is after
        if (body != null) body.material.color = Color.Lerp(new Color(0.8f, 0.8f, 0.85f), new Color(1f, 0.2f, 0.15f), target.Noise);
    }
}
