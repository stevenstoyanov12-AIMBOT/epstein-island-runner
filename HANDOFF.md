# Handoff — changes from 4 Oct 2026

## Character select
- Characters face the right way (25° at spawn).
- Older baked pistol animation, trimmed to 189 frames (last ~2 s of standing still gone).
- Pistol always points left; grip solver off on this screen.
- Left hand grips the gun.
- Yolo's wrist skin covered by `yolo_wrist.mat`.

## Other players
- Walk and crouch show the carry pose.
- Smoother movement: adaptive delay, extrapolation, smoothing.
- Pistol visible in their hands, with a fallback pose.
- Not tested with two real players yet.

## Web build
- AMD cards under 4 GB switch to WebGL (`index.html`).

## Museum panels (Outside scene)
- New texture `PortraitFriend.png` and material `PortraitFriend.mat`.
- Casino picture in the glass panel next to the gaming portrait, and on one panel on the back of the museum.

## Crate spawn
- `CrateSpawn.cs` is a hard lock: spawns in Awake, controls off, body pinned, avatar hidden, camera forced into the crate every frame (also `Application.onBeforeRender`); player counts as crouched; E escapes.
- Skips blocked or floating crates; faces the most open direction.
- Emergency crate if none exist. Waits while queued (`Net.Queued`).
- Not compiled yet.

## Unique crate per player
- Server gives each player the lowest free crate number 0–19 (`hello` field `sl` = slot+1).
- `Net.cs` connects at game start and passes the number to `CrateSpawn.Assign(slot)`.

## Project basics
- Unity 6 (6000.6) URP multiplayer 3D FPS around a destroyed museum + street. Local path: `C:/Users/steve/Documents/My project`. Unity MCP bridge available.
- Web build: WebGPU, WebGL 2 fallback picked in `index.html` by GPU name (`oldGpu()`). Addressables.
- Flow: Intro (`IntroLoader.cs`) → Select → crate cutscene → Outside (street) → Inside (museum interior, loads in background).
- Hosting: Cloudflare Worker `museum-server` (`museum-server/src/index.js`), R2 bucket `museum-game`, rooms as Durable Objects. URL: museum-server.steven-stoyanov12.workers.dev. Deploy: `npx wrangler deploy`. Upload: `museum-server/upload_all.bat`.
- Editor Play always starts from Intro (`Assets/Editor/PlayFromIntro.cs`).

## Gameplay
- Remote players (`Net/RemotePlayer.cs`): interpolation, carry pose, pistol, hidden with no hitboxes while in a crate (`InCrate`).
- 8-direction locomotion: Mixamo Pistol Handgun Locomotion in `Assets/Animations/PistolLocomotion/`. `HeistCharacterMixamo.controller` Locomotion = 2D Freeform Directional (MoveX/MoveZ, m/s local velocity); Crouch = CrouchWalk1 (reversed for backward). Driven by `PlayerAvatarAnim.cs` and `RemotePlayer.cs`.
- Destructible statue: `Assets/Models/Statues/DestructibleStatue.fbx` (22 Fig_## + 8 Base_## pieces), `ProgressiveStatue.cs`. Pieces knock off per bullet, 6th shot drops the rest, plinth chunk every 3rd hit, debris sinks after 7 s; synced via SimpleGun `OnBulletHit`. Test instance `DestructibleStatue_Test` in Outside at (-3, ground, 30), root X=270. User wants only "done" replies for statue changes.

## Multiplayer rooms
- 10 rooms: eu-1..4 (weur), us-1..4 (enam), asia-1..2 (apac), 10 players each. Region from continent (EU/AF/ME→eu, NA/SA→us, AS/OC→asia).
- `Lobby` Durable Object (binding LOBBY, migration v2): client hits `/lobby`, gets `{t:"room",room}` or `{t:"wait",pos}`. FIFO per region, never cross-continent, reservations expire after 20 s. Queue overlay; player stays in crate while queued.
- Net connects before character pick, sends poses only after pick.

## Loading / size (in progress)
- Goal: street ready within select + cutscene (~20–30 s), street download ~100–150 MB. Now: street ~495 MB source, interior 317 MB, shared 47 MB.
- Heaviest: MuseumScene_v2_1.fbx 27 MB, SniperGuard.fbx 23 MB, ArtVan_Wheelless 23 MB, ArtVan_Wrecked material 23 MB (suspicious), HeistCharacter 11 MB, Death.fbx 10 MB, old_stone_tile.glb 10 MB, leftover yolo_nowrist.asset 7 MB still referenced. Show user before changing visuals.
- IntroLoader preloads street (activateOnLoad false) during select + cutscene; interior downloads after (keep that).
- Compression: WebGL Brotli (decompressionFallback on; no Content-Encoding for .unityweb). Addressables LZ4 (LZMA unsupported on web). `upload_all.bat` → `brotli_bundles.js` (q11, `.brcache`) → `upload_changed.js` (md5, `.uploaded.json`). Server sends `Content-Encoding: br` + Content-Length for .bundle, `encodeBody: manual`.
- Bug: Unity 6 sizes streamed bundles from Content-Length (compressed size) → "Failed to decompress data" / "Received no data". Untested fix: fetch wrapper at top of `index.html` (template `Assets/WebGLTemplates/Fullscreen/index.html` + built copy) returns plain Response with exact length for encoded .bundle responses; bundles `no-store` in cacheControl.

## Cloud streaming
- Good GPUs: WebGPU in browser. Weak PCs (integrated / <4 GB): cloud streaming. Extend `oldGpu()`; unknown GPUs → WebGPU. Not built.
- Rejected: RunPod (no UDP), AWS (G/VT quota denied; $100 credits, org Steven-Stoyanov-Team), Vultr (no gaming GPUs on demand), DigitalOcean (Toronto only).
- Chosen: Vagon Streams (T4 Starter $0.025/min/player, $0.67/day/region storage). 30-min cap per session, cap on concurrent cloud players. Open questions to Vagon: free-plan concurrency, event capacity booking. Event: public, ~50–60 min, up to 100 players in first 20 min, US/EU/Asia.
- Also planned: WebGL potato mode (fewer shadows/post, lower render scale, half-res textures).

## To do
1. Open Unity, compile, check console.
2. Test Brotli fetch-wrapper fix: clear site data + Ctrl+F5, read console.
3. `npx wrangler deploy`, build, upload.
4. Shrink street assets to ~100–150 MB.
5. WebGL potato mode.
6. Vagon integration + launcher routing + session caps.
7. Two-player test: crates, queue, locomotion, statue.
8. Cleanup: `Diag_*.png`, `Assets/Screenshots`, `.bak` files.

User preferences: terse, direct, no unnecessary questions, short confirmations.
