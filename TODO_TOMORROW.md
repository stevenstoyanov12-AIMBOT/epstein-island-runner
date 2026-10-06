# Tomorrow

1. GPU test (Vast, one card). Add credit first, then from E:\My project:
   python cloud\vast_ctl.py up --offer 40114418 --region eu --instances 2
   python cloud\vast_ctl.py list / logs / down
2. Fetch the pistol animations (Tripo export WITH animation: Mixamo skeleton + clip).
   Wire it as the reload clip: UnityGame (ReloadTestBuilder) first, then the game (replaces ReloadPose).

Statue: head and head pieces rebuilt from a clean fine-SDF head (no mouth hole), whiter marble.
Still pending: copy it into the game + Tools > Place Venus Statues.

## Statue workflow (no more full rebuilds)
Start from the good tools/statue_gen/blend/VenusStatue.blend and make small local edits there, one at a time:
- Whiter: material colour only (Blender material + Unity remap). No mesh rebuild.
- Eyes: edit only the head region of the existing mesh, render one close-up of the face, check it, then export.
Never rerun build_venus.py from scratch for a small change.
