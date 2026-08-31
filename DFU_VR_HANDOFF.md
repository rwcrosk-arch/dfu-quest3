# DFU Quest3 VR — SESSION HANDOFF
Generated: 2026-08-31 (EST, UTC-04:00)

## WHERE WE ARE
DFU VR port for Meta Quest 3 (Unity 6 / OpenXR / Vulkan, Multi-pass) is in a very
good, playable state. Milestones: pause menu, melee combat (unsheathe + attack),
HUD transparency, weapon visible, full keyboard works on character-name screen.
ONE open active bug on the keyboard (save screen letters blank).

## WORKING DIRECTORY
- Repo: /home/ross/Distros/turboquant-home/Projects/dfu-quest3   (branch master)
- HEAD = 43ea817 (keyboard PlayerObject anchor + world-origin lesson doc), pushed.
- Build cmd (see below). APK -> ~/dfu-builds/android/DFU.apk.
- Docs in repo: DFU_VR_ARCHITECTURE.md, DFU_WINDOW_CATALOG.md, DFU_VR_TODO.md, ATTEMPTED_FIXES.md.

## ACTIVE BUG (DO NOT attempt fix in this session — hand off)
VR KEYBOARD: letter keys BLANK on the SAVE GAME screen (reached from pause menu
during GAMEPLAY), while special keys (Shift/Space/Bksp/Enter, bottom row) DO show.
The SAME keyboard works 100% on the character-name screen (new-game MENU).

LOG EVIDENCE (latest build, save screen):
- EVERY key logs: "VRKeyboard MakeKey 'v' special=False tex=256x256 matTex=set" —
  letter AND special both get a 256x256 texture, set on material. Bake succeeds:
  "baked label 'v' (1/1 glyphs)" and "baked label 'Shift' (5/5 glyphs)".
- Anchor diag: "board=(0.03,0.59,0.87) player=null head=(0.03,1.24,-0.13) fwd=(0,0,1)"
  => board IS positioned correctly (~1m front, 0.65m low). Position is NOT the issue.
- So: textures present, position correct, yet letter-key (upper rows) render blank
  in gameplay but fine in menu. Per-row/per-context rendering issue, NOT anchoring.

FIXES ALREADY TRIED (all failed for save screen):
1. TextMesh labels -> NRE on Unity 6 Android. Replaced with BAKED Texture2D labels.
2. Camera.main anchor -> stale (world origin) in gameplay.
3. HMD pose via XROrigin -> also resolved near origin in gameplay (rig lookup issue).
4. PlayerObject anchor (PlayerMotor-gate) 43ea817 -> player=null at build time on
   save screen (PlayerMotor null then), fell back to HMD. Position now OK but bug persists.

NEXT STEPS FOR NEW SESSION (investigate the per-row rendering, NOT anchoring):
- The letter keys are the TOP 3 ROWS; special keys the BOTTOM ROW. In gameplay the
  top rows are blank, bottom shows. Suspect: occlusion/z-fight between the keyboard's
  upper rows and the save window's dark NativePanel backdrop (drawn into the world
  overlay panel in gameplay), OR a per-glyph bake UV/alpha issue that only manifests
  on the save screen. The 2D panel (VRUIOverlay) sits ~2m ahead at eye level; the
  keyboard is at eye-0.65m. Check the keyboard quad's layer/render-order vs the panel.
- Compare BakeLabelTexture output content on-device: sample the baked Texture2D
  pixels for a letter vs a special key (are letter glyphs actually in the texture?).
  If letter pixels are blank/transparent -> bake content bug in gameplay; if present
  -> occlusion/render-order bug. This single test splits the two paths.
- Also verify: does the issue persist if you disable the 2D panel / move the keyboard
  to a different world Z or layer? That isolates occlusion vs content.

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
- Device: serial 2G0YC1ZF9S0JFH, USB usb:2-2, package com.dfworkshop.dfuquest3.
  adb in ~/Android/Sdk/platform-tools. After build: adb install -r; force-stop; monkey
  launch; adb forward tcp:8720.
- Device log (binary — use grep -a): /storage/emulated/0/Android/data/com.dfworkshop.dfuquest3/files/Player.log
- On-device save: .../Saves/SAVE111/ ("GoodTutorial Character"). settings.ini:
  StartInDungeon=False, VRSkipIntroQuests=true, ColorBoostEnable=False.

## CONTROL MAP (live)
Lstick=move; Rstick X=turn Y=pitch; Rtrig=activate/click; A=sheath/unsheathe;
B=magic menu; X(hold)=jump; Y=menu cycle; grips=weapon/shield use; Ltrig=cast/recast;
Lstick click=crouch; Rstick click=toggle run; Menu=context (pause options).

## KEY FILES
- Assets/VR/Scripts/VRKeyboard.cs   (active bug; baked labels, anchor logic)
- Assets/VR/Scripts/VRUIOverlay.cs   (2D panel; authoritative head anchor pattern)
- Assets/VR/Scripts/VRRigBootstrap.cs (drives xrOrigin + yaw)
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
(SSH-key auth; no secrets).

## MILESTONE TAGS
milestone-controls-sticks-working, -fullcolor-world, -hud-transparency,
-jump-run-menuclicks, -melee-combat, -pause-menu, -raycast-menu-follow,
-weapon-visible, -keyboard-usable, -keyboard-labels, -keyboard-shift
