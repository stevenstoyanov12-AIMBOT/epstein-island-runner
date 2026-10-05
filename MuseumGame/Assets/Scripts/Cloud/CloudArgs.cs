using System;
using UnityEngine;

// Command line / environment for a cloud streaming instance (Linux build on a Vast card).
//   -slot <n>  -card <id>  [-room <room>]  [-fps <30..60>]
//   env CLOUD_SIGNAL = wss://museum-server.../cloud/signal, CLOUD_KEY = shared secret (Vast env var, never baked in)
// Always compiled so other scripts (Net) can ask CloudArgs.IsCloud without the WebRTC package.
public static class CloudArgs
{
    public static bool IsCloud { get; private set; }
    public static int Slot { get; private set; } = -1;
    public static string Card { get; private set; } = "";
    public static string Room { get; private set; } = "";      // optional: force a Lobby room
    public static int Fps { get; private set; } = 60;
    public static string Signal { get; private set; } = "";
    public static string Key { get; private set; } = "";

    // True while a browser player is attached. Net should not join a room before this (an idle instance
    // would hold a room seat) and should leave when it goes false again.
    public static bool PlayerAttached { get; private set; }
    public static event Action<bool> PlayerAttachedChanged;

    public static void SetPlayerAttached(bool on)
    {
        if (PlayerAttached == on) return;
        PlayerAttached = on;
        PlayerAttachedChanged?.Invoke(on);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Parse()
    {
        PlayerAttached = false;
        PlayerAttachedChanged = null;
#if CLOUD_STREAMING
        var a = Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++)
        {
            switch (a[i])
            {
                case "-slot": if (int.TryParse(a[i + 1], out var s)) Slot = s; break;
                case "-card": Card = a[i + 1]; break;
                case "-room": Room = a[i + 1]; break;
                case "-fps": if (int.TryParse(a[i + 1], out var f)) Fps = Mathf.Clamp(f, 30, 60); break;
            }
        }
        Signal = Environment.GetEnvironmentVariable("CLOUD_SIGNAL") ?? "";
        Key = Environment.GetEnvironmentVariable("CLOUD_KEY") ?? "";
        IsCloud = Slot >= 0 && Signal.Length > 0;
#endif
    }
}
