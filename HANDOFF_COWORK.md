# Handoff: museum heist game (Steve), Oct 7 2026

This is the full state of the project as of this chat, written so another assistant can pick it up cold. The last section is a step-by-step for putting the destructible Venus statue into the game, which is the task in hand.

## 1. Who and how

- Steve builds a Unity 6 (6000.6.0f1) URP multiplayer FPS, a "museum heist". It runs in the browser on WebGPU, falls back to WebGL, and weak PCs can get a cloud GPU stream.
- Style: terse. For game fixes he wants just "done" plus the commands. He likes copy-paste cmd blocks (Windows cmd, not PowerShell). Don't ask unnecessary questions.
- He says "itarate", "statue thingy", "unity game", "the game"; see the paths below for what each means.

## 2. Paths and how files move

| What | Where |
|---|---|
| The real game project ("the game") | `E:\My project` (Unity) |
| Sandbox project ("unity game") for iterating on the statue | `C:\Users\steve\epstein-island-runner\UnityGame` |
| Old stale copy, ignore | `C:\Users\steve\Documents\My project` |
| Git bridge clone on his PC | `%USERPROFILE%\Documents\heist-sync` (= `C:\Users\steve\Documents\heist-sync`) |
| GitHub repo / branch | `stevenstoyanov12-AIMBOT/epstein-island-runner`, branch `claude/practical-shannon-sg54y3` |
| Game files inside the repo | `MuseumGame/` (mirror of `E:\My project`, scripts synced Oct 7) |
| Sandbox files inside the repo | `UnityGame/` |
| Server (Cloudflare Worker) in repo | `MuseumGame/museum-server/` (on his PC: `E:\My project\museum-server`) |
| Cloud GPU stuff in repo | `MuseumGame/cloud/` (on his PC: `E:\My project\cloud`) |

Flow: the assistant pushes to the branch, then Steve runs `git pull` in heist-sync and copies files to E: or the sandbox with `copy /y`/`robocopy`. To send his files back: `robocopy "E:\My project\Assets\Scripts" MuseumGame\Assets\Scripts /E /XF *.meta`, then commit and push from heist-sync.

Gotchas we hit:
- heist-sync is on C: and the game on E:, so `cd` needs `/d` (`cd /d %USERPROFILE%\Documents\heist-sync`). Without it, commands run in the wrong folder.
- Repo copies of game scripts can go stale. Before editing a game script, make sure the repo has his current version (he synced all of `Assets/Scripts` on Oct 7, commit `56f882d`).
- Game `.cs` files mix LF and CRLF. Keep each file's existing line endings when editing.
- Nothing compiles C# in the assistant's sandbox; Steve compiles in Unity and sends console errors.

## 3. Game systems (what exists now)

### Multiplayer (Cloudflare)
- Worker `museum-server` at `https://museum-server.steven-stoyanov12.workers.dev`. It also serves the game build from the R2 bucket `museum-game` (`upload_all.bat` uploads `Build/WebGPU`).
- Durable Objects (all SQLite-backed): `Lobby` (one; hands out rooms per region, queue when full), `Room` (one per room, max 10 players, uses the WebSocket Hibernation API), `CloudBroker` (one; GPU slots).
- Rooms: 4 EU, 4 US, 2 Asia, 10 players each = 100 max; players after that wait in the queue. Region comes from Cloudflare's `cf.continent` (NA/SA → us, AS/OC → asia, everything else → eu). Steve confirmed this layout.
- Client: `Assets/Scripts/Net/Net.cs`. Sends its pose 30 times a second (`t:"s"`), plus `shot`, `hit`, `act` (tip desk, barrel burst), and host-driven `st`/`sg`/`sf`/`bp` (statue gaze AI, sniper guard, barrel physics). The host is the player with the lowest id (`Net.IsHost`).
- Hits: the server only accepts damage 0-40 per hit and drops a second hit from the same player within 60 ms (it was 90 ms, the same as the gun's fire delay, which could drop real hits; fixed Oct 7, commit `84f010c`).

### Late-join world state (new Oct 7, commits `fe5f81c`, `791f9a8`)
- Before: the room stored the last 800 shots and replayed them to late joiners (one storage write per shot).
- Now: each breakable implements `IWorldState` (`Assets/Scripts/Net/WorldState.cs`): `Save()` returns a short state string, `Load(string)` snaps the object to that state with no effects. Key = `Net.PathOf(transform)` taken in `Awake` + `"#" + type name`. Register at the end of `Start`.
- Only the shooter reports: `SimpleGun` sets `WorldState.Local = true` around its local `ApplyHit`; breakables call `WorldState.Changed(key, this)` after their state changes. Local actions/host physics use `force: true` (desk tip by the kicker, fallen/burst barrels by the host).
- Message `{t:"ws", c:key, s:state}` (field `s` added to `Net.Msg`). The Room keeps the latest per key in memory, saves it every 5 s via a DO alarm (`storage "world"`), does NOT relay it to live players, and sends all of them to a new joiner. When the room empties, the state is wiped.
- Covered: BreakableProp, GlassCase (crack points or broken), WineBarrel (hits, fallen pose, burst), DestructibleColumn (gone chunks + per-area shots), DestructiblePlinth, ShardCrater, ProgressiveStatue, VenusStatueBreak (hits + loose pieces), TippableCover (final pose).
- Deployed together with a WebGL build by Steve on Oct 7. The server and game build must be deployed together.

### Gameplay additions this session
- Fortnite-style damage numbers: `Scripts/DamageNumbers.cs`, called from `RemotePlayer.OnBulletHit`. White body, yellow headshot (body 15-16.8, head 34).
- Remote death animation everyone sees (`RemotePlayer`).
- Reload: 20-round mag, R to reload, 1.7 s (`SimpleGun`), procedural pose `ReloadPose.cs` seen by others via `Msg.rl`. A real reload animation from Tripo is coming "in a few days"; the Tripo files sent so far had no animation in them (checked the GLB JSON).

### Cloud GPU streaming for weak PCs (Vast.ai)
- `Assets/WebGLTemplates/Fullscreen/index.html` picks the path: weak GPU (GTX, Intel HD/UHD/Iris, old AMD, low-VRAM Radeon) → `/stream.html` cloud stream; good GPU + WebGPU → WebGPU; otherwise WebGL. `?nocloud=1` skips the stream.
- Broker `museum-server/src/cloud.js`: cap 40 cloud players, 30-minute sessions, regions eu/us (no Asia cards, Asia plays in the browser). Oct 7: each player now goes to the NEAREST card with a free slot (great-circle distance from Cloudflare's `cf.latitude/longitude` for the player and for the card's heartbeat), then the next nearest, then the queue. Cards within 300 km of the nearest count as equal and the busiest is filled first.
- The broker uses classic WebSockets (not hibernation), so it bills run time while any card is connected (~460 GB-s/hour, negligible). Steve wants to switch it to hibernation later, for a multi-day game.
- Cards: Docker image `aimbot66/museum-cloud:latest` (`cloud/Dockerfile`, ubuntu 22.04, Xvfb + Vulkan + coturn + `supervisor.py`). Only port 3478 udp/tcp, Vast maps it (`VAST_UDP_PORT_3478`). Media goes straight from card to browser; Cloudflare only does signalling.
- Rule from Steve: never put Cloudflare tokens or other secrets on Vast machines. Only `CLOUD_KEY` (shared secret = Worker secret `CLOUD_SECRET`) goes to the card.
- Tools on his PC (`E:\My project`): `python cloud\vast_find.py` (offers), `python cloud\vast_ctl.py up --offer <ID> --region eu --instances 2`, `list`, `logs <id>`, `down`. Needs `vastai tfa login --method-type totp -c <code>` first. `cloud\.env` holds `VAST_API_KEY` and `CLOUD_KEY`.
- Status: Steve added Vast credit. The Worker secret matches (the status URL `/cloud/status?key=<CLOUD_KEY>` returned `{"active":0,"cap":40,...}`). The one-card stream test has NOT been run yet. The Docker image is an older build (no damage numbers/reload/world state); Steve will rebuild it after his next game changes (Unity: Build > Cloud Streaming (Linux), then Build > Back To WebGL; then `docker build -t aimbot66/museum-cloud:latest -f cloud/Dockerfile .` and `docker push`).

### Cloudflare pricing (checked against the docs page Steve pasted)
- Free: 100k DO requests/day (incoming WebSocket messages count 20:1), 13,000 GB-s/day, resets 00:00 UTC; over the limit, operations fail. 100 players × ~31 msgs/s ≈ 155 requests/s → about 11 minutes of 100 players per day.
- Workers Paid ($5/month, NOT the "Pro" website plan): 1M DO requests/month (~1h45m of 100 players), then $0.15/M; 400k GB-s/month. Two 30-minute events a month cost just the $5. Steve plans to buy it.
- Steve wants 30 updates/s kept (lower is choppy) and declined the "skip unchanged" idea.

## 4. The Venus statue

### Model
- Built in Blender (bpy 5.0.1 Python module) from an SDF: `tools/statue_gen/venus_statue.py` (shape) and `tools/statue_gen/build_venus.py` (sample, Voronoi fracture, decimate, export). Outputs: `UnityGame/Assets/Models/Statues/VenusStatue.fbx`, `tools/statue_gen/blend/VenusStatue.blend`, previews.
- FBX contents (one root `VenusStatue`): `Intact` (seamless shell shown until the first real break, ~208k tris), `Fig_00`..`Fig_33` (figure pieces), `Base_00`..`Base_09` (plinth pieces, never break). Materials `Marble` and `MarbleBroken` (fracture faces).
- The plinth foot is 0.8 m below the model origin; the top of the head is ~2.06 m above it (about 2.9 m total). It faces +Z in Unity.
- Oct 6-7 fixes, done locally on the existing .blend (Steve's rule: iterate small changes on the .blend, never full rebuilds, they waste tokens and add new bugs): the head was swapped for a clean fine-sampled SDF head (no mouth hole), the head pieces Fig_00/Fig_01 were rebuilt from it, and the head faces were flipped (they were inverted, so Unity culled the face). The marble is pure white with a faint emission (0.10) so it doesn't turn beige under the warm lamps.
- The game copy `MuseumGame/Assets/Models/Statues/VenusStatue.fbx` is byte-identical to the sandbox one (latest, face fixed).

### Sandbox (UnityGame) for iteration
- Tools > Build Statue Scene builds the Statue Garden: `StatueSceneBuilder.cs` → `VenusSceneBuilder.PlaceStatue(...)` (+ a white key spotlight, remapped white materials) with `DestructibleStatue.cs` (v4), `StatueImpactFX.cs`, `StatueShooter.cs` (click to shoot, R respawns). Copy command for a new FBX: `copy /y UnityGame\Assets\Models\Statues\VenusStatue.fbx "C:\Users\steve\epstein-island-runner\UnityGame\Assets\Models\Statues\"`, then Ctrl+R and Tools > Build Statue Scene.

### In the game: scripts
- `MuseumGame/Assets/Scripts/Statue/VenusStatueBreak.cs`: the game's version of the break logic. Hits arrive via `SimpleGun`'s `OnBulletHit` (local shots and replayed remote shots, so every client breaks it the same way). The first 2 figure hits only chip it (`StoneImpactFX`); after that, pieces within 0.18 m of the hit fall; the 6th figure hit drops the whole figure; pieces with no support from below fall too (real contact measurement); plinth hits never count. Falling pieces get `VenusDebris` (heavy-stone limits on speed/spin) and sink away after 10 s. Implements `IWorldState` (state = `hits:loose piece indices`) so late joiners see it already broken.
- `MuseumGame/Assets/Editor/PlaceVenusStatues.cs`: menu **Tools > Place Venus Statues** and **Tools > Remove Venus Statues**. It:
  1. sets up the FBX importer: Read/Write on (needed for the convex MeshColliders in web builds), no animation, remaps `Marble` → `Assets/Materials/VenusMarble.mat` (white, smoothness 0.55, emission 0.10) and `MarbleBroken` → `VenusMarbleBroken.mat` (white, 0.12, emission 0.14);
  2. removes any previous `VenusStatues` root in the open scene;
  3. takes the bounds of all renderers in the OPEN scene, shrinks them by 10% on X/Z, and tries up to 4000 random points;
  4. for each, raycasts down and keeps the spot only if: the ground is flat (normal·up ≥ 0.97); the hit collider is a floor at least 3 × 3 m (not a prop top); it's ≥ 10 m from the other statues; all four corners at ±0.5 m are level within 5 cm; and a 1.4 × 2.9 × 1.4 m box above it is empty;
  5. instantiates the FBX prefab under a root `VenusStatues`, named `VenusStatue_1..4`, at `hit.point + 0.8 m up`, random Y rotation, and adds `VenusStatueBreak`;
  6. marks the scene dirty and logs `Placed N Venus statues in <scene> (save the scene to keep them)`, plus a warning if it found fewer than 4 spots.
- The statues are saved INTO the scene, so every player has them in the same places, and `Net.PathOf` keys match on every client. Re-running the tool reshuffles them (new positions → new keys; fine as long as everyone gets the same build).

## 5. Step by step: put 4 Venus statues in the game

Everything is already pushed (latest commit `d932122` on `claude/practical-shannon-sg54y3`).

1. Copy the files (Windows cmd):
   ```
   cd /d %USERPROFILE%\Documents\heist-sync
   git pull
   mkdir "E:\My project\Assets\Models\Statues" 2>nul
   copy /y MuseumGame\Assets\Models\Statues\VenusStatue.fbx "E:\My project\Assets\Models\Statues\"
   copy /y MuseumGame\Assets\Editor\PlaceVenusStatues.cs "E:\My project\Assets\Editor\"
   copy /y MuseumGame\Assets\Scripts\Statue\VenusStatueBreak.cs "E:\My project\Assets\Scripts\Statue\"
   ```
   (`VenusStatueBreak.cs` and `Net/WorldState.cs` were already copied to E: on Oct 7; copying again is harmless.)
2. Open `E:\My project` in Unity and wait for the import/compile. Check the Console: no red errors. If the FBX shows magenta or beige, the importer remap hasn't run yet; it runs on step 4.
3. Open the scene the statues should go in. The game has `Outside` (street) and `Inside` (museum); the tool only looks at the scene that is open/active. Outside has the large flat ground it needs; Inside may not have 3 × 3 m clear floor spots with 2.9 m headroom.
4. Click Tools > Place Venus Statues. Check the Console for `Placed 4 Venus statues in Outside`. If it says fewer, the open space is too tight: reduce `MinApart` (10 m) or the 3 m floor rule in `PlaceVenusStatues.cs`, or run it in another scene.
5. Look at them in the Scene view (`VenusStatues` root in the Hierarchy). If one is in a bad spot (blocking a path, inside a building), run the tool again to reshuffle, or move/delete that one by hand.
6. Ctrl+S to save the scene. Without this, the statues are gone.
7. Quick test in Play mode: shoot one. The first 2 hits chip it; from the 3rd, pieces fall; the 6th drops the figure; the plinth never breaks.
8. Build WebGL as usual, then:
   ```
   cd /d "E:\My project\museum-server"
   upload_all.bat
   npx wrangler deploy
   ```
9. Multiplayer check: two browser windows. Shoot a statue in one, and the other sees the same pieces fall (shots replay). Then join with a third window: the statue is already broken (world state).
10. Later: rebuild the cloud Docker image so stream players get the statues too (section 3, cloud).

Things to watch:
- Performance: each statue is ~208k triangles intact plus 44 pieces with convex colliders. Four statues is fine on desktop GPUs; on weak devices watch the frame rate. If it drops, the fix is a lower-poly export (decimate the .blend), not fewer features.
- `VenusStatueBreak` uses `StoneImpactFX.Play(hit, marble)`, which exists in the game (`Scripts/StoneImpactFX.cs`).
- If the face looks missing (back of the head visible), that's backface culling on inverted normals; the current FBX is fixed, so it would mean an old FBX got copied.

## 6. Open to-dos (also in `TODO_TOMORROW.md`)

1. Put the 4 Venus statues in the game (section 5).
2. GPU stream test with one Vast card (renew 2FA, `vast_find`, `vast_ctl up`, wait for `/cloud/status` to show the card, test `/stream.html` on his PC, then the Iris Xe laptop, then `vast_ctl down`).
3. Buy Workers Paid ($5) before any big event.
4. Rebuild and push the cloud Docker image after his next game changes.
5. Optional: a bot script that fakes 100 players connecting (load test; do it on the Paid plan, it would eat the free allowance in minutes).
6. When Tripo sends a real animated export: wire it as the reload clip (sandbox first, then the game, replacing `ReloadPose`).
7. Later: switch the cloud broker to WebSocket hibernation (for a multi-day game).
