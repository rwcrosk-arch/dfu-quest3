# DFU Quest3 VR — SESSION HANDOFF
Generated: 2026-08-31 (EST, UTC-04:00)
Updated: 2026-09-02 (KEYBOARD SAVE-SCREEN BUG SOLVED — root cause: white-on-white)

## WHERE WE ARE
DFU VR port for Meta Quest 3 (Unity 6 / OpenXR / Vulkan, Multi-pass) is in a very
good, playable state. Milestones: pause menu, melee combat (unsheathe + attack),
HUD transparency, weapon visible, full keyboard works everywhere (char-name AND
save screen). Stereo split is FIXED (TAA off). Keyboard save-screen bug is SOLVED
(see KEYBOARD SAVE-SCREEN — SOLVED). Next frontier: save/load game system (currently
NOT working — see DFU_VR_TODO.md).

## STEREO SPLIT — ROOT CAUSE FOUND (2026-09-01) — FIX IS A SETTING, NOT CODE
SYMPTOM: broken stereo in gameplay — right eye shows BLACK OUTLINES on poly edges
(cell-shade look), left eye loses the world-space VRKeyboard label overlay (blank
letters on save screen). Both eyes render the same world but through DIFFERENT
post-processing states. Opening the in-game effects settings menu fixes BOTH and it
sticks (it re-inits the real PP path).
ROOT CAUSE: TAA (AntialiasingMethod=3) per-eye temporal-history corruption under
OpenXR Multi-pass. TAA's temporal history buffer desyncs between the two eyes ->
dark ghost edges on one eye's geometry + corrupted per-eye state drops the keyboard
chars from the other eye. NOT a texture/geometry/keyboard-bake problem.
FIX (CONFIRMED ON-HEADSET): set AntialiasingMethod=0 (None) in settings.ini.
Stereo is now SOLID. This is a SETTINGS change, NOT a code change — a future restore
must apply BOTH the code state AND this setting (see RESTORE below).
NOTE: the deferred-PPv2-redeploy code (d66f84e) does NOT fix stereo and is NOT the
cause of the fix; it is harmless but not the answer. The keyboard blank-letters on
the save screen STILL requires the effects-settings trick even with TAA off — that
is a SEPARATE, still-open bug (see ACTIVE BUG).

## RESTORE / SAFE FALLBACK (how to actually get back to this working state)
1. git checkout <milestone tag> (see MILESTONE TAGS) — restores the CODE.
2. PUSH the on-device settings.ini with AntialiasingMethod=0 (TAA OFF). This is the
   stereo fix and is NOT in git. The working restore file (AA=0, verified) lives at:
   /home/ross/Distros/turboquant-home/dfu-backup-20260902/settings.ini.stereo-fixed-AA0
   To restore: adb push that file to
   /storage/emulated/0/Android/data/com.dfworkshop.dfuquest3/files/settings.ini
   then force-stop + relaunch. (settings.ini.pre-taatest is the PRE-test backup with
   AA=3 — do NOT use it for restore.)
3. Game data (arena2) is NOT in git — it lives on-device at
   /storage/emulated/0/Android/data/com.dfworkshop.dfuquest3/files/Daggerfall/arena2
   and locally at /home/ross/Distros/turboquant-home/daggerfall-data/wine-df/drive_c/Daggerfall/.
   A full uninstall wipes it; re-push from the local copy if needed.

## KEYBOARD SAVE-SCREEN BLANK LETTERS — SOLVED (2026-09-02) — WHITE-ON-WHITE
SYMPTOM (historical): letter keys BLANK on the SAVE GAME screen (pause menu ->
Save during GAMEPLAY); special keys (Shift/Space/Bksp/Enter) showed fine. The SAME
keyboard worked 100% on the character-name screen. The "effects-menu trick" (open
effects settings, close) made letters appear AND stick.
ROOT CAUSE: the glyphs were rendering THE WHOLE TIME — as WHITE ink on a key
background that gameplay's PPv2 post-processing (auto-exposure/bloom) blows out
past clipping to WHITE. White glyph on white background = invisible. The special
keys' background was a DARKER grey (0.3,0.3,0.4) so it survived brightening and
stayed visible; the alphanum keys' lighter grey (0.5,0.5,0.6) clipped to white.
The char-name screen had no PP brightening, so white-on-light-grey worked there.
The effects-menu trick "fixed" it because opening/re-rendering any PP-adjacent UI
re-evaluated exposure state (same mechanism family as the stereo TAA fix).
HOW IT WAS FOUND (the diagnostic chain that worked):
1. Always-on-top shader (VRKeyboardAlwaysOnTop: Overlay queue + ZTest Always +
   ZWrite Off) -> did NOT fix. Ruled out occlusion/z-order (the handoff's prior
   occlusion theory was WRONG — with ZTest Always, an occluded quad is impossible).
2. Pixel-sampling baked textures on-device -> glyph ink PROVABLY present in the
   CPU textures (20/36 letters had pure-white ink at sample points). Content fine.
3. Cache-clear + full re-bake in gameplay -> no change. Cache invalidation ruled out.
4. SOLID MAGENTA isolator (letters got a magenta texture, no bake/font/cache) ->
   magenta VISIBLE on save screen. Quads/shader/draw-order/anchoring ALL proven good.
5. MAGENTA BORDER test (bake + 12px magenta border stamped pre-upload) -> border
   AND "blank" bg visible; Ross spotted the bg was WHITE in gameplay vs light-grey
   in menu => white glyph on clipped-to-white background. Root cause confirmed.
THE FIX (in code, commit milestone-keyboard-savename-fixed):
- Alphanum key bg darkened to the same grey as special keys: Color(0.3,0.3,0.4)
  for ALL keys, both screens (VRKeyboard.cs MakeKey + RefreshLetterLabels).
- Kept the VRKeyboardAlwaysOnTop shader (harmless, still guarantees draw-after).
- Magenta/border/cache-clear/pixel-sample diagnostics removed.
LESSON: "blank" key labels in a post-processed scene — suspect tone/bloom clipping
an off-white element into the background before assuming occlusion, bake bugs, or
render-state corruption. A solid neon test texture (magenta) on the SAME quad is
the fastest possible split between "not drawn" and "drawn without contrast".

## WORKING DIRECTORY
- Repo: /home/ross/Distros/turboquant-home/Projects/dfu-quest3   (branch master)
- Milestone tag milestone-keyboard-savename-fixed (2026-09-02: keyboard letters
  fixed on save screen — white-on-white root cause; see that section above).
- Build cmd (see below). APK -> ~/dfu-builds/android/DFU.apk.
- Docs in repo: DFU_VR_ARCHITECTURE.md, DFU_WINDOW_CATALOG.md, DFU_VR_TODO.md,
  ATTEMPTED_FIXES.md, README.md (rewritten for the fork).

## RECURRING LESSON (world-origin trap) — documented in DFU_VR_ARCHITECTURE.md
- NEVER trust Camera.main or raw HMD->XROrigin pose as the sole anchor in the GAMEPLAY
  scene. DFU spawns far from origin (StartCell 109/158); Camera.main is stale near
  origin, and FindFirstObjectByType<XROrigin>() can return a rig VRRigBootstrap isn't
  driving. Poses convert to ~(0,1.2,0).
- Authoritative head-in-world in gameplay = Player object: PlayerObject.pos + up*1.5f,
  yaw = playerT.eulerAngles.y. Gate on PlayerMotor != null (only real player).
- gameplay = PlayerObject + eye-height + yaw; menu = raw HMD/camera. Never Camera.main
  alone in gameplay. Hit 4x (panel, ray, keyboard x2).

## BUILD / DEVICE
- Unity: ~/Unity/Hub/Editor/6000.0.82f1/Editor/Unity. Build errors=14 (pre-existing).
- Build: cd ~/Projects/dfu-quest3 && $UNITY -batchmode -quit -nographics -buildTarget
  Android -projectPath . -executeMethod BuildDFU.BuildAndroidDev -logFile ~/dfu-x.log
- PITFALL (2026-09-02): the Meta XR SDK's HandReadiness tool does a remote JSON fetch
  at editor startup that HANGS behind the GFW, stalling the build. Fix: seed the disk
  cache at /tmp/Meta/remote_content/hrt_prompt.json (a valid {"version":1,...} JSON) so
  the fetch short-circuits. If a build hangs at "Unloading 286 Unused Serialized files"
  with HandReadiness in the log, re-seed that file and rebuild.
- Device: serial 2G0YC1ZF9S0JFH, USB usb:2-2, package com.dfworkshop.dfuquest3.
  adb in ~/Android/Sdk/platform-tools. After build: adb install -r; force-stop; monkey
  launch; adb forward tcp:8720.
- Device log (binary — use grep -a): /storage/emulated/0/Android/data/com.dfworkshop.dfuquest3/files/Player.log
- On-device save: .../Saves/SAVE111/ ("GoodTutorial Character"). settings.ini:
  StartInDungeon=True, VRSkipIntroQuests=true, ColorBoostEnable=False,
  AntialiasingMethod=0 (TAA OFF — the stereo fix, see STEREO SPLIT above).

## CONTROL MAP (live)
Lstick=move; Rstick X=turn Y=pitch; Rtrig=activate/click; A=sheath/unsheathe;
B=magic menu; X(hold)=jump; Y=menu cycle; grips=weapon/shield use; Ltrig=cast/recast;
Lstick click=crouch; Rstick click=toggle run; Menu=context (pause options).

## KEY FILES
- Assets/VR/Scripts/VRKeyboard.cs   (active bug; baked labels, anchor logic)
- Assets/VR/Scripts/VRUIOverlay.cs   (2D panel; authoritative head anchor pattern)
- Assets/VR/Scripts/VRRigBootstrap.cs (drives xrOrigin + yaw; deferred PPv2 redeploy)
- Assets/VR/Scripts/VRActionInjector.cs, VRTriggerBridge.cs, VRWeaponRenderer.cs
- Assets/Scripts/Game/UserInterface/DaggerfallFont.cs, TextBox.cs
- ProjectSettings/GraphicsSettings.asset (shader AlwaysIncluded fix: fileID 4800000)

## TODO AFTER KEYBOARD (see DFU_VR_TODO.md)
loot(corpses), 6DOF hands (L/R weapons+shields), 3D weapon AssetBundle modpack,
VRAM leak (UserInterfaceRenderTarget.CheckTargetTexture ~5MB/s), Save/Load verify,
blocking, suppress 2D weapon sprite, spell-hit verify, bow, HUD clickables, perf,
ray depth-awareness. panelrewire marked resolved/rare.

## GIT DISCIPLINE
Commit+push every good state; git tag -a milestone-<name>; --force only if push
rejected. Identity ross@turboquant.local. Remote git@github.com:rwcrosk-arch/dfu-quest3.git
(SSH-key auth; no secrets). NOTE: the remote URL in .git/config uses an x-access-token
(ghp_...) — do not commit or echo that token.

## MILESTONE TAGS
milestone-controls-sticks-working, -fullcolor-world, -hud-transparency,
-jump-run-menuclicks, -melee-combat, -pause-menu, -raycast-menu-follow,
-weapon-visible, -keyboard-usable, -keyboard-labels, -keyboard-shift,
milestone-stereo-taa-fixed (2026-09-01: TAA off fixes stereo),
milestone-keyboard-savename-fixed (2026-09-02: save-screen keyboard letters fixed —
white-on-white root cause; darker key bg for all keys)
