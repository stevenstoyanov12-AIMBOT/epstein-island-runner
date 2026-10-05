# Museum Shooter – Session Handoff (for a new chat)

## Project basics
- Unity 6 (6000.6.0f1), URP. Project: `C:/Users/steve/Documents/My project`, scene `Assets/game.unity`.
- Blender 5.1 file: `C:/Users/steve/Documents/My project/AuctionHall.blend` (collections: AuctionHall, HallProps, Fractured, PillarDetailed, GlassShatter).
- All Unity work goes through the `mcp__remote-devices__unityMCP__*` tools (load via ToolSearch); Blender through `mcp__remote-devices__Blender__execute_blender_code`. Never click in Unity directly.
- Reply style: after a change, just say "Done." Only speak if asked something or if something important went wrong.
- Everything is saved (Unity scene + assets + Blender file) as of end of session.

## Hard-won technical lessons
1. Screenshots taken during Play mode are stale. Always `manage_editor stop` first, then screenshot with `camera: "DiagnosticCamera"` or `"Main Camera"` + view_position/view_target.
2. Anything changed during Play mode is lost. Always check `Application.isPlaying` and stop before editing; scripts start with `if(Application.isPlaying) return "playing";`.
3. The `Museum` root has scale 100 – never parent new things under it or put things under a non-uniformly scaled parent (causes skew). `AuctionHall` root is scaled (0.95,1,0.59); props live in `AuctionHall_Props` (unscaled) to avoid squashing.
4. FBX children under nested empties import with leftover rotation (90,0,0) and scale 0.01. Fix by resetting children's localPosition/Rotation/Scale to identity (done in code for shards/spots).
5. `??` does not work with Unity objects – use explicit null checks.
6. CodeDom (C# 6): no local functions, use `Func<>` lambdas; `Random` is ambiguous (use UnityEngine.Random explicitly or avoid).
7. Runtime-created materials (Shader.Find at runtime) went magenta – all FX materials now live in `Assets/Resources/` and load with `Resources.Load` (FX_Dust, FX_Grit, FX_Hole, FX_Wine, FX_GlassCrack, FX_Hidden, FX_GlassShard, SoftDot.png).
8. `manage_scene save` sometimes times out first try – just retry.
9. `AssetDatabase.DeleteAsset` / file writes need `safety_checks:false`.

## Museum work done earlier this session (outside the hall)
- Removed debris/fixed vestibule (UpperVestibule_HQ cropped meshes in `Assets/Museum/VestibuleCropped/`), arch hole between vestibule and gallery made natural, gallery back-faces visible (two-sided materials `*_2S`), stair gaps filled (StairFill, LandingCorners, ArchFloor, ArchFloor2, DoorFloorFill), GalleryDoor refitted, church door fitted to opening (`ChurchDoor_Fit`, tilted −1.5°).
- Street entrance: stone tile path (EntrancePath, old_stone_tile.glb, 7 rows × 3), angel columns along it, NeonLeaf on right front pillar, BreakthroughElegance statue on left pillar (pillar 1/3 height).
- Two framed painting walls replacing shop windows (`farmed_painting.png`, PaintingWall/FarmedPainting 1 & 2).
- Dining room visibility zone widened so it's seen from the gallery.
- Player: `FirstPersonController.cs` – WASD + 360 mouse look, camera pushed out of walls (spherecast), radius 0.35. MeshColliders on all visible meshes.

## The Auction Hall (the big new room – inspired by Uncharted 4 auction scene)
- Location: inside the museum building's left side; 17 m opening cut in the left façade (x≈−29, z −7..10.6). Interior 6 m slab removed there. Hall spans x −28.7..7.6, z −18.1..21.5, 26 m tall.
- Built in Blender, exported `Assets/Museum/AuctionHall.fbx`: floor, walls, ceiling beams, cornices, balconies at 9.5 m with balustrades + brackets, 12 arched windows (6 per side), 4 crystal chandeliers.
- Props (in `AuctionHall_Props`): 8 fluted columns (4 per row, evenly spaced), 8 detailed bases, 8 glass display cases, 6 auction desks (3 at far end + 3 CoverDesk near entrance), 24 wine barrels (3x size, 6 stacks), rope stanchions.
- Player spawn: just inside the hall entrance (−26.5, 1.05, 1.7) facing +x.
- Gun: DrumGun.glb (Fortnite drum/tommy gun, CC-BY – credit fortniteman69 if released) on Main Camera; `SimpleGun.cs` hitscan, hold left click, no visible bullets.

## Destruction systems (scripts in `Assets/Scripts/`)
- `SimpleGun.cs` – raycast, sends `OnBulletHit`; black decal + small dust on normal surfaces; marble (Hall_Stone) gets `StoneImpactFX`; glass cases skip the decal.
- `StoneImpactFX.cs` – jagged marble fragments with physics (fade 6–10 s via `FragmentFade`), quick dust puff + grit (~200 ms, per user request).
- `DestructibleColumn.cs` – columns swap to 56 pre-fractured chunks + unbreakable core; nearest chunk falls per hit, max 6 shots per area.
- `ShardCrater.cs` + Blender `DetailedPillar.fbx` – each base has 3 cracked-stone spots (F/L/R sides, back solid). Voronoi shards flush in the face, crater pit + rubble behind, realistic angular hairline cracks appear on first hit. Only a bullet that actually hits a shard breaks it (precise hitbox), max 6 shots per spot.
- `WineBarrel.cs` – shots 1–3 wine streams pour from each hole; shot 4 wine explosion + barrel bursts into 12 pieces. (Known: barrel stacked on top stays floating when bottom one explodes – not yet fixed.)
- `GlassCase.cs` + Blender `GlassShards.fbx` – shots 1–3 spiderweb cracks at impact (clipped to the pane, brass frame hits ignored) + glass sparkles; shot 4 all panes shatter into 321 spiderweb shards rippling outward from impact.
- `BreakableProp.cs` – old generic 1–3 chip / 4 break (no longer on cases).
- `TippableCover.cs` – press E near a desk (within 3 m, looking at it) to kick it onto its side as cover, away from the player.
- Rules the user set: columns progressive 1–6 then capped; barrels/cases progressive 1–3, break on 4.

## Lighting (moon-blue, user loved it)
- `HallLighting` object: 12 blue spot "MoonShaft" lights through the windows (intensity 600), 4 warm chandelier point lights (250), realtime box-projected reflection probe.
- Trilight ambient (cool blue), blue exp² fog density 0.009, dim blue directional light.
- Global Volume (`Assets/HallVolume.asset` or existing profile): Bloom (1.4, blue tint), Color Adjustments (exposure 0.9, contrast 18, sat −20), White Balance (−12), Vignette (0.32 dark blue), ACES tonemapping. Floor smoothness 0.92.

## Textures made
- `Marble_Albedo.png` (veined marble, on Hall_Stone), `Crater_Albedo.png`, `Crater_Normal.png`.

## Next up (user's plan)
1. Balcony access: open a door (user will send a screenshot with the door circled) and seamlessly teleport players to the balcony (the side without moonlit windows) when they walk through – no fade, same facing, they shouldn't notice.
2. Possible: make desks breakable, make stacked barrels fall when the one below breaks, multiplayer sync (only "which chunk broke" is networked), more cover props.
