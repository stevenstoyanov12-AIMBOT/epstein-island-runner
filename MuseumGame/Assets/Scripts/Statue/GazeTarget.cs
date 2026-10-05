using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// A player: can be heard/lasered by the statues and shot by other players.
// Noise: firing makes you loud (fades over `noiseFade` s). Each statue lasers the loudest player near it. No HUD meter.
// Health: 20 half-hearts worth (100 hp) shown as 10 Minecraft-style hearts.
//   pistol body shot 15-16 (dies in 7, sometimes 6), headshot 34 (dies in 3).
public class GazeTarget : MonoBehaviour
{
    public static readonly List<GazeTarget> All = new List<GazeTarget>();

    public float noiseFade = 3f;
    public float maxHealth = 100f;
    public bool testShooting = true;          // local player: shooting makes noise + draws the HUD
    public Vector3 aimOffset = Vector3.zero;
    public float bodyDamageMin = 15f, bodyDamageMax = 16.8f, headDamage = 34f;
    public float headZone = 0.32f;            // top of the body (metres) that counts as the head

    public float Noise { get; private set; }
    public float Health { get; private set; }
    public bool remoteProxy, proxyAlive = true;   // another player's avatar: aim/noise target for statues + snipers, never takes local damage
    public bool Alive => remoteProxy ? proxyAlive : Health > 0f;
    public Vector3 AimPoint => transform.TransformPoint(aimOffset);

    float hitFlash, respawnTimer, heartShake, regenDelay;
    Vector3 spawnPos;
    Quaternion spawnRot;
    Animator anim;

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    void Start()
    {
        Health = maxHealth;
        spawnPos = transform.position;
        spawnRot = transform.rotation;
        anim = GetComponentInChildren<Animator>();
        if (GetComponent<Collider>() == null)
        {
            var c = gameObject.AddComponent<SphereCollider>();
            c.radius = 0.3f;
        }
    }

    public void MakeNoise() => Noise = 1f;

    public void Hit(float damage)
    {
        if (remoteProxy) return;
        if (!Alive || damage <= 0f) return;
        Health = Mathf.Max(0f, Health - damage);
        hitFlash = 1f; heartShake = 0.4f; regenDelay = 6f;
        if (Health <= 0f) Die();
    }

    // Pistol hit from SimpleGun (SendMessageUpwards)
    void OnBulletHit(RaycastHit hit)
    {
        bool head = IsHead(hit.point);
        Hit(head ? headDamage : Random.Range(bodyDamageMin, bodyDamageMax));
    }

    bool IsHead(Vector3 p)
    {
        if (anim != null && anim.isHuman)
        {
            var h = anim.GetBoneTransform(HumanBodyBones.Head);
            if (h != null) return p.y > h.position.y - 0.12f;
        }
        var cc = GetComponent<CharacterController>();
        if (cc != null)
        {
            float top = transform.position.y + cc.center.y + cc.height * 0.5f;
            return p.y > top - headZone;
        }
        return false;
    }

    void Die()
    {
        respawnTimer = 5f;
        var fpc = GetComponent<FirstPersonController>();
        if (fpc) fpc.enabled = false;
        var pd = GetComponent<PlayerDeath>(); if (pd == null) pd = gameObject.AddComponent<PlayerDeath>(); pd.Die();   // play the death animation
    }

    void Respawn()
    {
        Health = maxHealth; Noise = 0f;
        var pd = GetComponent<PlayerDeath>(); if (pd) pd.Revive();
        var fpc = GetComponent<FirstPersonController>();
        if (fpc) { fpc.enabled = true; fpc.Teleport(spawnPos, spawnRot.eulerAngles.y); }   // CharacterController-safe move
        else transform.SetPositionAndRotation(spawnPos, spawnRot);
    }

    void Update()
    {
        Noise = Mathf.Max(0f, Noise - Time.deltaTime / noiseFade);
        hitFlash = Mathf.Max(0f, hitFlash - Time.deltaTime * 2f);
        heartShake = Mathf.Max(0f, heartShake - Time.deltaTime);
        if (!Alive)
        {
            respawnTimer -= Time.deltaTime;
            if (respawnTimer <= 0f) Respawn();
            return;
        }
        // slow natural regeneration after 6 s without damage (half a heart every 2 s)
        regenDelay -= Time.deltaTime;
        if (regenDelay <= 0f && Health < maxHealth) Health = Mathf.Min(maxHealth, Health + 2.5f * Time.deltaTime);
        if (testShooting && !CrateSpawn.Hidden && Mouse.current != null && Cursor.lockState == CursorLockMode.Locked
            && Mouse.current.leftButton.wasPressedThisFrame)
            MakeNoise();
    }

    // ---------- Minecraft-style hearts ----------
    static Texture2D heartFull, heartHalf, heartEmpty;
    static readonly string[] HeartPix = {
        ".XX...XX.",
        "XrrX.XrrX",
        "XrwrXrrrX",
        "XrrrrrrrX",
        "XrrrrrrrX",
        ".XrrrrrX.",
        "..XrrrX..",
        "...XrX...",
        "....X....",
    };
    static Texture2D MakeHeart(int mode) // 0 empty, 1 half, 2 full
    {
        var t = new Texture2D(9, 9, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        var outline = new Color(0.08f, 0.02f, 0.02f, 1f);
        var red = new Color(0.86f, 0.08f, 0.08f, 1f); var dark = new Color(0.55f, 0.03f, 0.03f, 1f);
        var shine = new Color(1f, 0.85f, 0.85f, 1f); var empty = new Color(0.22f, 0.07f, 0.07f, 0.9f);
        for (int y = 0; y < 9; y++)
            for (int x = 0; x < 9; x++)
            {
                char ch = HeartPix[8 - y][x]; Color c = new Color(0, 0, 0, 0);
                if (ch == 'X') c = outline;
                else if (ch == 'r' || ch == 'w')
                {
                    bool filled = mode == 2 || (mode == 1 && x <= 4);
                    c = !filled ? empty : (ch == 'w' ? shine : (y <= 3 ? dark : red));
                }
                t.SetPixel(x, y, c);
            }
        t.Apply(); return t;
    }

    // hearts from the icon in Resources/HeartIcon: full = the icon, half = left half lit, empty = dark silhouette
    static bool IconHearts()
    {
        var src = Res.Load<Texture2D>("HeartIcon"); if (src == null) return false;
        var px = src.GetPixels(); int w = src.width, h = src.height;
        Texture2D Make(int mode)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var o = new Color[px.Length];
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i]; int x = i % w;
                bool lit = mode == 2 || (mode == 1 && x < w / 2);
                if (!lit) { float g = (c.r + c.g + c.b) / 3f * 0.35f; c = new Color(g * 0.9f, g * 0.6f, g * 0.6f, c.a * 0.85f); }
                o[i] = c;
            }
            t.SetPixels(o); t.Apply(); return t;
        }
        heartEmpty = Make(0); heartHalf = Make(1); heartFull = Make(2);
        return true;
    }

    void OnGUI()
    {
        if (!testShooting) return;   // HUD only for the local player
        if (heartFull == null) { if (!IconHearts()) { heartEmpty = MakeHeart(0); heartHalf = MakeHeart(1); heartFull = MakeHeart(2); } }
        float size = Mathf.Round(Screen.height / 40f); float gap = size * 0.12f;
        float x0 = 20f, y0 = Screen.height - size - 24f;
        int halves = Mathf.CeilToInt(Mathf.Max(Health, 0f) / maxHealth * 20f);
        bool low = halves <= 4;
        GUI.color = Color.white;
        for (int i = 0; i < 10; i++)
        {
            int v = halves - i * 2;
            var tex = v >= 2 ? heartFull : (v == 1 ? heartHalf : heartEmpty);
            float jy = (heartShake > 0f || (low && Alive)) ? Mathf.Round(Mathf.Sin(Time.time * 40f + i * 1.7f) * size * 0.1f) : 0f;
            float hw = heartFull != null ? size * heartFull.width / (float)heartFull.height : size;
            GUI.DrawTexture(new Rect(x0 + i * (hw + gap), y0 + jy, hw, size), tex);
        }
        if (hitFlash > 0f)
        {
            GUI.color = new Color(1f, 0f, 0f, 0.35f * hitFlash);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        }
        if (!Alive)
        {
            GUI.color = new Color(0.4f, 0f, 0f, 0.2f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            var st = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Screen.height / 14f), alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            GUI.Label(new Rect(0, Screen.height * 0.35f, Screen.width, Screen.height * 0.15f), "YOU DIED", st);
        }
        else
        {
            GUI.color = new Color(1f, 1f, 1f, 0.7f);
            GUI.Label(new Rect(20, 20, 600, 25), "Click to shoot (makes noise). Stay out of the statue's red gaze.");
        }
    }
}
