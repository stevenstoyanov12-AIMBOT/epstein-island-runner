using UnityEngine;

// Showcase loop: idle -> walk -> run -> shoot, pacing back and forth along a line.
public class HeistCharacterDemo : MonoBehaviour
{
    public float walkSpeed = 1.6f, runSpeed = 4.8f, halfLength = 8f;
    Animator anim; public GameObject gun; Vector3 start; float dir = 1, t; int phase;
    readonly float[] dur = { 3f, 5f, 4f, 3f, 3f };

    void Start() { anim = GetComponentInChildren<Animator>(); start = transform.position; }

    void Update()
    {
        t += Time.deltaTime;
        if (t > dur[phase]) { t = 0; phase = (phase + 1) % dur.Length; }
        float speed = phase == 1 ? walkSpeed : phase == 2 ? runSpeed : 0f;
        anim.SetFloat("Speed", phase == 1 ? 2f : phase == 2 ? 6f : 0f, 0.2f, Time.deltaTime);
        anim.SetBool("Shooting", phase >= 3); anim.SetBool("Pistol", phase == 4); if (gun) gun.SetActive(phase != 4);
        Vector3 fwd = transform.right * dir;
        transform.position += fwd * speed * Time.deltaTime;
        float off = Vector3.Dot(transform.position - start, transform.right);
        if (Mathf.Abs(off) > halfLength && Mathf.Sign(off) == dir) dir = -dir;
        var look = Quaternion.LookRotation(fwd == Vector3.zero ? transform.forward : (phase >= 3 ? Vector3.back : fwd));
        if (transform.childCount > 0) { var model = transform.GetChild(0); model.rotation = Quaternion.Slerp(model.rotation, look, Time.deltaTime * 6f); }
    }
}
