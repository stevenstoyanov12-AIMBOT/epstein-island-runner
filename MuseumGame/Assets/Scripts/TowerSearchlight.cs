using UnityEngine;

// Sweeping searchlight: aims at a point that wanders across the ground in a smooth figure pattern.
public class TowerSearchlight : MonoBehaviour
{
    public Vector3 center;          // area to sweep (world)
    public float radiusX = 35f, radiusZ = 30f, speed = 0.18f, phase;
    Light spot; Transform beam;

    void Start()
    {
        spot = GetComponent<Light>();
        beam = transform.Find("Beam");
    }

    void Update()
    {
        float t = Time.time * speed + phase;
        var target = center + new Vector3(Mathf.Sin(t) * radiusX, 0, Mathf.Sin(t * 1.7f + 1.1f) * radiusZ);
        target += new Vector3(Mathf.PerlinNoise(t * 2f, phase) - 0.5f, 0, Mathf.PerlinNoise(phase, t * 2f) - 0.5f) * 6f;
        var want = Quaternion.LookRotation(target - transform.position);
        transform.rotation = Quaternion.Slerp(transform.rotation, want, Time.deltaTime * 3f);
        if (beam) beam.localScale = new Vector3(beam.localScale.x, beam.localScale.y, Vector3.Distance(transform.position, target));
    }
}
