using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Something the statue can hear and shoot: put it on each player.
// Call MakeNoise() whenever the player fires. Noise fades to nothing over `noiseFade` seconds.
// For testing, `testShooting` lets the fly camera "fire" with the left mouse button (while the mouse is captured).
public class GazeTarget : MonoBehaviour
{
    public static readonly List<GazeTarget> All = new List<GazeTarget>();

    public float noiseFade = 3f;
    public float maxHealth = 100f;
    public bool testShooting = true;
    public Vector3 aimOffset = Vector3.zero;

    public float Noise { get; private set; }
    public float Health { get; private set; }
    public bool Alive => Health > 0f;
    public Vector3 AimPoint => transform.TransformPoint(aimOffset);

    float hitFlash, respawnTimer;
    Vector3 spawnPos;
    Quaternion spawnRot;

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    void Start()
    {
        Health = maxHealth;
        spawnPos = transform.position;
        spawnRot = transform.rotation;
        if (GetComponent<Collider>() == null)
        {
            var c = gameObject.AddComponent<SphereCollider>();   // so the statue's line of sight can hit us
            c.radius = 0.3f;
        }
    }

    public void MakeNoise() => Noise = 1f;

    public void Hit(float damage)
    {
        if (!Alive) return;
        Health -= damage;
        hitFlash = 1f;
        if (Health <= 0f) respawnTimer = 2f;
    }

    void Update()
    {
        Noise = Mathf.Max(0f, Noise - Time.deltaTime / noiseFade);
        hitFlash = Mathf.Max(0f, hitFlash - Time.deltaTime * 2f);
        if (!Alive)
        {
            respawnTimer -= Time.deltaTime;
            if (respawnTimer <= 0f)
            {
                transform.SetPositionAndRotation(spawnPos, spawnRot);
                Health = maxHealth;
                Noise = 0f;
            }
            return;
        }
        if (testShooting && Mouse.current != null && Cursor.lockState == CursorLockMode.Locked
            && Mouse.current.leftButton.wasPressedThisFrame)
            MakeNoise();
    }

    void OnGUI()
    {
        if (!testShooting) return;   // HUD only for the local test player
        GUI.color = Color.white;
        GUI.Label(new Rect(20, Screen.height - 80, 400, 25), $"HEALTH  {Mathf.CeilToInt(Mathf.Max(Health, 0))}");
        // noise meter: how loud you are right now
        GUI.Label(new Rect(20, Screen.height - 55, 80, 25), "NOISE");
        GUI.color = Color.gray;
        GUI.DrawTexture(new Rect(80, Screen.height - 50, 200, 12), Texture2D.whiteTexture);
        GUI.color = Color.Lerp(Color.yellow, Color.red, Noise);
        GUI.DrawTexture(new Rect(80, Screen.height - 50, 200 * Noise, 12), Texture2D.whiteTexture);
        if (hitFlash > 0f)
        {
            GUI.color = new Color(1f, 0f, 0f, 0.4f * hitFlash);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        }
        if (!Alive)
        {
            GUI.color = Color.white;
            GUI.Label(new Rect(Screen.width / 2 - 60, Screen.height / 2, 200, 30), "SPOTTED. Respawning...");
        }
        else
        {
            GUI.color = new Color(1f, 1f, 1f, 0.7f);
            GUI.Label(new Rect(20, 20, 600, 25), "Click to shoot (makes noise). Stay out of the statue's red gaze.");
        }
    }
}
