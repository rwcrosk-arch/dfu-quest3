# DFU Quest3 VR — SESSION HANDOFF
Generated: 2026-08-31 (EST, UTC-04:00)
Updated: 2026-09-04 (BETA: settings-first boot flow + save/load everywhere; ready for
playtest solicitation)

## WHERE WE ARE
DFU VR port for Meta Quest 3 (Unity 6 / OpenXR / Vulkan, Multi-pass) is in **BETA**:
boot -> DFU settings wizard -> in-world New Game / Load Game menu -> new game (intro
videos) or load game; in-gameplay save/load/switch-char; melee combat; full keyboard;
stereo fixed (TAA off). See BOOT FLOW v2 below. Ready for playtest solicitation.
Known cosmetic: Start-menu mirror artifact (documented below, low priority).

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

## SAVE/LOAD — WORKING EVERYWHERE (2026-09-04) — milestone-saveload-startmenu
SYMPTOM (historical): saves persisted on disk but the START MENU's save list was always
empty and switch-char dead; loading from the menu hung on "please wait" forever.
ROOT CAUSES (two stacked, both menu-context crashes):
1. No SaveLoadManager in the startup scene (true upstream too). GameManager.Instance's
   get_SaveLoadManager -> FindObjectOfType -> null -> THROWS, killing
   DaggerfallUnitySaveGameWindow.OnPush before EnumerateSaves() — list never populated.
2. SaveLoadManager.Load's coroutine resolves PlayerDeath/PlayerMotor/PlayerEntity via
   GameManager.PlayerObject, which THROWS with no live player (startup menu). Even past
   that, SerializablePlayer is null in a worldless scene -> silent yield break. Hence
   the eternal "please wait".
FIXES (all in code, tagged milestone-saveload-startmenu):
- VRStartGameBridge.Autostart: guard-spawn a SaveLoadManager in the startup scene
  (self-registering singleton -> GameManager.Instance.SaveLoadManager resolves in menus).
- DaggerfallUnitySaveGameWindow.OnPush: exception-safe PlayerEntity access; in menus
  (no live player) fall back to FindMostRecentSave()'s characterName + rebuild list.
- SaveLoadManager.Load(key): menu-context detection (PlayerMotor null) -> stash the key
  (pendingMenuLoadKey static) + SceneManager.LoadScene(1); new static
  CompletePendingMenuLoad() re-enters the normal Load path once a live player exists.
- VRStartGameBridge game-scene branch: completes the deferred load when
  HasPendingMenuLoad && GameManagerReady().
RESULTING FLOW (separate-menu env, verified on-device 2026-09-04):
DFU start menu -> Load Game -> [deferral: game scene boots, brief please-wait] ->
new-game menu appears in gamemode -> Load Game again -> SAVE APPLIES (player now
exists; log: "restored faction state from save"). The two-step is inherent: the
startup scene has no player; scene 1 does. In-gameplay save/load/switch-char work
directly (no hop).
Also fixed along the way (from menu context): saves ARE written to
files/Saves/SAVE<n> — never were in a temp folder; the menu crash merely hid them.

## START-MENU MIRROR ARTIFACT — KNOWN COSMETIC (2026-09-04, low priority)
On the Start menu (separate-menu env), the background shows a mirror-like reflection
of the menu and the controller ray: one clean reflected copy below the panel, then an
"infinite mirror" regress of the ray, shifting with HMD movement. The menu itself is
perfect and usable. Evidence rules: NOT occlusion (ZTest Always shader didn't change
it), NOT bake/cache (pixel sampling proved ink; cache-clear no-op), NOT the UI RT clear
(hardcoded + context-aware clear both no-op), NOT behind-the-panel (a real black quad
4cm behind the panel changed nothing but covered gameplay — reverted). Head-parallax +
recursion signature points to a stereo/render-path feedback (panel displaying a target
the eye cameras also write, or per-eye IMGUI timing), NOT any UI-texture issue.
SAFE PARTIAL MITIGATIONS ALREADY IN (kept, harmless): try/finally RenderTexture.active
guards in DaggerfallUI.OnGUI + VRKeyboard bake (prevent the active-RT leak class).
NEXT PROBE IF WANTED (5-line test): hide ray + reticle while DaggerfallStartWindow is
top (SetActive false in VRUIOverlay when top.GetType().Name=="DaggerfallStartWindow");
if the infinite regress follows the ray, it's world-object capture in the leaked path.
LOW PRIORITY: cosmetic, menu-only, non-blocking.

## BOOT FLOW v2 — SETTINGS-FIRST, VERIFIED (2026-09-04) — BETA MILESTONE
FINAL FLOW (all verified on-device 2026-09-04, milestone-beta-bootflow):
App boot -> startup scene -> DFU Settings wizard (Options page; SkipToStartWindow
REMOVED so it no longer fights the wizard) -> [Done] -> LaunchGame stage loads scene 1
-> SGB TitleMenu -> InitGame -> New Game / Load Game menu IN GAMEPLAY LAND (menu over
the live world) -> new game (with intro videos) or load game.
WHY IT'S GOOD: settings-first (options + future mod checklist page), the gameplay-land
menu is the native DFU stack (stable, videos work), and the mirroring artifact has not
reappeared in this flow so far (watch it; the RT leak guards are active here for the
first time with a gameplay-land menu).
FIX THAT MADE THE WIZARD BOOTABLE: DaggerfallUnitySetupGameWizard.CreateBackdrop builds
a real 3D city block (CUSTAA06.RMB) INTO the current scene — upstream runs the wizard
only with the world live, so MeshReplacement's GameManager lookups (PlayerGPS) THROW in
our startup scene, killing ShowOptionsPanel. CreateBackdrop is now try/catch: on failure
it logs and continues with the plain dark background (backdrop is cosmetic).
SkipToStartWindow.cs DELETED (git rm) — its push would have raced the wizard in the
startup scene and double-pushed the Start window in scene 1; the wizard + native
InitGame now cover both duties. settings.ini ShowOptionsAtStart must be True (it is).
NOTE for restore: if the wizard ever regresses, SkipToStartWindow can be restored from
git history (deleted at milestone-beta-bootflow); the previous separate-menu flow is
milestone-saveload-startmenu.

## SAVE/LOAD NATIVE BOOT VARIANT — EXPLORED, STASHED (2026-09-03)
An alternative boot flow was built and verified working end-to-end (save/load from the
title menu over the live world, videos played, no extra hop): settings.ini
ShowOptionsAtStart=False -> SceneControl forwards to scene 1 natively; SkipToStartWindow
removed; VRSceneSetup made DontDestroyOnLoad (fixes a boot race: it was destroyed by the
startup->game transition before OpenXR activated -> rig never built -> black screen).
Ross PREFERRED the separate start menu for stability/extensibility (options/mods screens)
and it was reverted. The stash 'menu-load deferral + native boot + RT leak guards'
(git stash list) holds: native boot pieces + those fixes. Cherry-pick recipe:
git checkout stash@{N} -- SaveLoadManager.cs DaggerfallUnitySaveGameWindow.cs
  VRStartGameBridge.cs VRKeyboard.cs DaggerfallUI.cs VRSceneSetup.cs
(skip SkipToStartWindow deletion + keep ShowOptionsAtStart=True for separate-menu flow).
NOTE: settings.ini ShowOptionsAtStart is NOT in git — set True for separate menu,
False for native boot. Current on-device: True (separate menu).

## WORKING DIRECTORY
- Repo: /home/ross/Distros/turboquant-home/Projects/dfu-quest3   (branch master)
- Milestone tags: milestone-keyboard-savename-fixed (2026-09-02), 
  milestone-saveload-startmenu (2026-09-04: save/load everywhere incl. menu,
  separate start menu kept; stash holds the native-boot variant).
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
white-on-white root cause; darker key bg for all keys),
milestone-saveload-startmenu (2026-09-04: save/load works everywhere incl. menu —
SaveLoadManager guard-spawn + OnPush guard + menu-load deferral; separate start menu
kept; native-boot variant stashed — see SAVE/LOAD NATIVE BOOT),
milestone-beta-bootflow (2026-09-04: BETA — settings-first boot, in-world menu,
SkipToStartWindow removed, wizard backdrop try/catch; ready for playtesters)
