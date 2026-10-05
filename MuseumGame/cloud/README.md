# Cloud streaming (weak PCs)

Weak PCs (`oldGpu()` true) play a stream from a Vast.ai card instead of running the game. Strong PCs are unchanged.

```
browser --HTTPS/WSS--> Worker (/cloud/claim, /cloud/signal)   signalling only: SDP + ICE
browser --UDP/TCP 3478 direct port--> card: coturn --localhost--> Unity instance (one per player)
card supervisor --POST /cloud/report every 10 s--> Worker
```

## Why there's a TURN server on the card (and not plain direct UDP)

These are the three things that ruled out the plan as written:

- The game page is HTTPS, so the browser can't open `ws://<card-ip>:8000`. A `wss://` connection to a bare IP needs a TLS certificate on every card. So signalling goes through the Worker instead, which has TLS already.
- Unity's WebRTC package can't pin its UDP ports. Vast also remaps every direct port to a random external port. So the media ports can't be listed in advance.
- coturn runs on the card itself and listens on one port (3478) for both UDP and TCP. That's the only direct port players use. Inside the card, coturn relays to the Unity instances over the container network, which adds no measurable latency.

There's no third-party relay: the only external service is Google's public STUN server. The TURN password is created on the card at boot and only ever leaves it for the Worker's memory.

## Wiring (one time)

`museum-server/src/index.js`:

```js
import { CloudBroker, handleCloud } from "./cloud.js";
export { CloudBroker };
// first thing in fetch():
if (url.pathname.startsWith("/cloud/") || url.pathname === "/stream.html") return handleCloud(request, env);
```

`wrangler.toml`:

```toml
[[durable_objects.bindings]]
name = "CLOUD"
class_name = "CloudBroker"

[[migrations]]
tag = "v3"
new_classes = ["CloudBroker"]
```

```
npx wrangler secret put CLOUD_SECRET      # any long random string; the same value goes in the Vast env
npx wrangler deploy
```

`index.html` (template + built copy), just before the WebGPU/WebGL choice:

```js
if (oldGpu() && !/[?&]nocloud=1/.test(location.search)) { location.replace("/stream.html"); return; }
```

`stream.html` handles claiming a slot, the queue ("number N in the queue"), and the "Play in the browser instead" button (which goes to `/?nocloud=1`, the WebGL fallback). It also falls back automatically when there are no cards in your region.

`Net.cs`: a cloud instance must not join a room until a player is attached, otherwise idle instances take room seats. Where `Net` opens its socket:

```csharp
if (CloudArgs.IsCloud && !CloudArgs.PlayerAttached) { CloudArgs.PlayerAttachedChanged += on => { if (on) Connect(); }; return; }
```

`CloudArgs.Room` (from `-room`) is there if you want to force a room. Otherwise the instance goes through the Lobby like any player. The Lobby puts it by the card's location, so EU cards land in EU rooms.

## Build the image

1. Unity Hub: add **Linux Build Support (IL2CPP + Mono)** to 6000.6.
2. Unity: **Build > Cloud Streaming (Linux)**. The first run installs `com.unity.webrtc`; run it again after the import. The build goes to `CloudBuild/`. The active Addressables profile needs *local* load paths for this build. **Build > Back To WebGL** switches back afterwards.
3. Docker, run from `MuseumGame/`:

```
docker build -t <you>/museum-cloud:latest -f cloud/Dockerfile .
docker push <you>/museum-cloud:latest
```

## Vast template

- Image: `<you>/museum-cloud:latest`
- Docker options: `-p 3478:3478/udp -p 3478:3478/tcp`
- Env: `CLOUD_KEY=<CLOUD_SECRET value>`, `CLOUD_REGION=eu` or `us`, optionally `INSTANCES=6` (max 8) and `FPS=60`.
- Launch mode: entrypoint (the image's own CMD), not Jupyter/SSH.

Only `CLOUD_KEY` goes on the card. No Cloudflare API tokens.

Finding cards: `python cloud/vast_find.py`, with `VAST_API_KEY` set on your PC. If a region shows nothing, try `--relaxed` (drops the datacenter filter). If it's still empty, that region needs an outside TURN server after all.

## Test day

1. Rent the cards with the template. Check `https://museum-server.../cloud/status?key=<CLOUD_SECRET>`: every card should be listed, with `host: true` on each slot.
2. On the Iris Xe laptop (home Wi-Fi) and the RTX 3050 PC, open `/stream.html`. Press **F2** for RTT, fps, kbps and packet loss.
3. Players per card: set `INSTANCES` to 1, 2, 4, 6 and 8 on one card and fill every slot. Watch `gpu.util` and `gpu.enc` in `/cloud/status` and the F2 fps. Keep the highest count that holds 60 fps (or a steady 30) with encoder use under about 85%. Then set `CAP` in `cloud.js` to cards × that number.
4. If the stream connects but there's no picture, set `-noflip` in `supervisor.py` (in case it's upside down). If it shows `failed`, the 3478 UDP mapping is wrong. TCP is the fallback.

Logs on a card: `/app/logs/slotN.log` (Unity) and `/app/logs/turn.log`.

## Limits

- Each session is 30 minutes, with a warning at 25. When it ends, the instance quits and the supervisor starts a fresh one.
- A card that hasn't reported for 30 s counts as dead. Its players are moved to another card automatically.
- There are no Asia cards yet, so Asia players get the browser version.
- Up to 40 cloud players at once (`CAP` in `cloud.js`). Never more than 8 instances per card (the GeForce NVENC session limit).
