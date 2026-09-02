# DFU Quest3 VR — Attempted Fixes That Didn't Work

Purpose: a living log of fixes we tried that FAILED or REGRESSED, so future models/sessions
don't re-invent the wheel. Each entry: what we tried, why it failed, the commit/revert that
removed it, and the lesson. Append new failures here. Do NOT delete entries — mark superseded.

Repo: rwcrosk-arch/dfu-quest3. Device: Quest 3, package com.dfworkshop.dfuquest3.
Build: ~/Unity/Hub/Editor/6000.0.82f1/Editor/Unity -batchmode -quit -nographics -buildTarget
Android -projectPath ~/Projects/dfu-quest3 -executeMethod BuildDFU.BuildAndroidDev
APK: ~/dfu-builds/android/DFU.apk. Device log: .../files/Player.log (mirror to ~/dfu-current.log).

================================================================================
## THE CENTRAL PROBLEM (still open as of stable-gameplay-loads / da6703c)
The VR UI panel (world-space quad showing DFU's OnGUI UI RenderTexture) is INVISIBLE in
GAMEPLAY. Root cause chain: the XROrigin sits at world origin (rigParent=none) while the
player body is at (12.75, 0.96, 41.80) in the dungeon. The game camera is a child of the rig,
so it's at origin too — 40m from the player. The UI panel anchors to the camera/HMD, so it's
40m away and invisible. The rig-follow fix (3e2e6a8) drives rig position+yaw from the player
every LateUpdate, which SHOULD put the camera at the player — but the panel is STILL absent
in gameplay. This is the active frontier.

================================================================================
## 1. Anchor UI panel to GameManager.PlayerObject (no eye-height offset)
- What: VRUIOverlay anchored the panel to PlayerObject.transform directly.
- Why failed: In char-creation the PlayerObject sits at FEET level, so the new-game UI
  appeared at the player's feet ("a little low, almost right at player feet").
- Reverted: commit 461817f (reverted by d24ac63).
- Lesson: PlayerObject.position is feet-level; must add eye-height offset. Also PlayerObject
  is a DIFFERENT object in char-creation vs gameplay (StartNewCharacter creates a fresh one).

## 2. Anchor UI panel to HMD head pose (InputDevices HeadMounted devicePosition)
- What: VRUIOverlay read the HMD pose via legacy InputDevices and anchored the panel to it.
- Why failed: InputDevices HMD devicePosition reads in XR TRACKING SPACE (relative to the
  XROrigin), not world space. In menu/char-creation tracking origin == world origin, so it
  worked there. In gameplay the XROrigin is stuck at world origin, so the HMD pose reads
  ~origin — 40m from the player. Panel invisible in gameplay.
- Reverted: commit 0017d2a (kept as the menu-working baseline, then superseded).
- Lesson: HMD devicePosition is tracking-space, NOT world-space. Only valid when the rig is
  at world origin (menu/char-creation).

## 3. Scene-gate the anchor: PlayerObject in gameplay, HMD in menu (HasGameCamera gate)
- What: Used presence of the PlayerMouseLook game camera (HasGameCamera) to pick PlayerObject
  (gameplay) vs HMD pose (menu).
- Why failed: HasGameCamera()/PlayerMouseLook is TRUE during char-creation too (a game camera
  exists there), so it wrongly used PlayerObject in char-creation -> panel at feet.
- Reverted: commit 27a797b (reverted by 5a966eb).
- Lesson: camera presence is NOT a reliable gameplay-vs-charcreation discriminator. Use
  StateManager.GameInProgress instead (false in menu AND char-creation).

## 4. Make rig follow player position-only every LateUpdate (commit 1c642a7)
- What: VRRigBootstrap.LateUpdate set xrOrigin.position = player.position every frame
  (world-space follow, NO rotation copy). VRUIOverlay anchored to camera world position.
- Why failed: BROKE THE STICKS. It deleted the SetParent block that coupled the rig's rotation
  to the player. That coupling is what made right-stick-X yaw turn the view (rig, as a child
  of the player, rotated with it). Position-only follow left the rig at identity rotation, so
  yaw rotated the player but NOT the view -> right-stick X dead, left stick wrong axis.
  Right-stick Y survived (pitch rotates VRHeadPitch, a node under the rig, not the player).
- Reverted: commit 49c92d4.
- Lesson: if you drive the rig from the player, you MUST copy the player's YAW too (not just
  position), or you break the yaw-coupling the sticks depend on.

## 5. Anchor to PlayerObject + 1.5m eye height, gated by StateManager.GameInProgress (commit 5e9e5e0)
- What: VRUIOverlay anchored to PlayerObject + 1.5m in gameplay (GameInProgress gate), HMD
  pose in menu. Head-gaze ray also pointed at player facing in gameplay.
- Why failed: gameplay FROZE at new-character (black screen). BUT the log showed this was a
  PRE-EXISTING DFU char-creation bug, NOT this change: "[StartGameBehaviour] NewCharacter
  failed: System.IndexOutOfRangeException" at DungeonTextureTables.RandomTextureTableClassic
  line 37 (climateIndices[climate - Ocean]), player never spawned. The change only touched
  VRUIOverlay anchor logic and could not cause it.
- Reverted: commit 12fdf57 (because the freeze masked whether the fix worked).
- Lesson: the frozen-black-screen was a SEPARATE pre-existing bug (now fixed by da6703c
  climate clamp). Don't conflate char-creation flakiness with panel-anchor changes.

## 6. Rig-follow with position + yaw (commit 3e2e6a8) — CURRENT, still not fixing panel
- What: VRRigBootstrap.LateUpdate drives rig position AND yaw from the live player every
  frame (unparented), preserving the yaw-coupling. VRUIOverlay anchors to camera world
  transform in gameplay (GameInProgress gate).
- Status: controls work, gameplay loads, rig follows player. BUT the UI panel is STILL
  invisible in gameplay. This is the current state (stable-gameplay-loads / da6703c).
- Lesson: fixing the rig so the camera is at the player did NOT make the panel visible.
  The panel invisibility must have ANOTHER cause (render target not updating in gameplay?
  panel quad not rendering? DaggerfallUI.CustomRenderTarget not drawing the HUD in-game?
  the panel being hidden by the IsPlayingGame gate?).

## 7. [DefaultExecutionOrder(100)] on VRTriggerBridge broke menu clicks (commit 89e6331 -> 910b306)
- What: To make ActionStarted controls (ReadyWeapon/SwingWeapon/Jump) fire, I added
  [DefaultExecutionOrder(100)] to VRTriggerBridge so it runs AFTER InputManager.Update
  (which clears currentActions at its start). This DID fix ActionStarted controls.
- Why it broke clicks: VRTriggerBridge sets vrClickQueued (the menu-click flag). With order
  100 it ran AFTER the UI already read the flag at order 0, and InputManager clears it in
  LateUpdate — so clicks never registered. Symptom: new-game menu clicks stopped working.
- Reverted/fixed: commit 910b306. Split into TWO components with their required orders:
  - VRTriggerBridge (DEFAULT order) — sets vrClickQueued BEFORE the UI reads it → menu clicks.
  - VRActionInjector (NEW, DefaultExecutionOrder[100]) — does the AddAction injections,
    AFTER InputManager.Update clears currentActions → ActionStarted controls.
- Lesson: vrClickQueued (needs order 0, before UI) and AddAction injection (needs order 100,
  after InputManager.Update clears) have CONFLICTING execution-order requirements. They must
  live in separate MonoBehaviours with their own DefaultExecutionOrder. Never put both in one.
- STATUS (milestone-jump-run-menuclicks @ 910b306): jump, run toggle, and menu clicks work.
  STILL BROKEN: unsheathe (A=ReadyWeapon), spell cast (left trigger=RecastSpell) — both are
  action-injected but DFU's WeaponManager/PlayerSpellCasting still don't fire on them
  (need investigation: maybe ActionStarted reads on the frame AFTER the one we inject? or the
  window/manager needs the action held? or these specific consumers run at order < 100, before
  VRActionInjector?). And pause menu (menu button) still doesn't open pause options.

================================================================================
## OTHER FIXES THAT WORKED (for reference, do not regress)
- Spell book stuck menu: new Texture2D(0,0,...) throws in Unity 6 (was valid in 2019.4).
  Fixed all 3 sites (SpellIconCollection, SaveLoadManager, DaggerfallBookReaderWindow) to
  Texture2D(2,2) — LoadImage resizes anyway. Commit 1308ad2.
- Frozen black screen on new character: climate index out of range in
  DungeonTextureTables.RandomTextureTableClassic. Clamped both climate indices. Commit da6703c.
- Menu button as back: vrEscapeQueuedFrame int counter (survives LateUpdate) wired into
  GetBackButton*. Commit 109dfa8.
- Stick calibration: yaw rotates PlayerObject (not rig), pitch on dedicated VRHeadPitch node.
  Commit b073966. Left-stick 180 negate removed when yaw moved to Player. Commit 8a19cf4.
- Legacy joystick double-read: EnableController=false. Commit c14dae0.
- Jump on X button (held, like spacebar). Commit 0066922.

================================================================================
## STEREO SPLIT — FIXED (2026-09-01) — SETTINGS, NOT CODE
- Symptom: broken stereo in gameplay — right eye BLACK OUTLINES on poly edges
  (cell-shade look), left eye loses the world-space VRKeyboard label overlay (blank
  letters on save screen). Opening the in-game effects settings menu fixed BOTH and
  stuck.
- Root cause: TAA (AntialiasingMethod=3) per-eye temporal-history corruption under
  OpenXR Multi-pass. TAA's history buffer desyncs between the eyes -> dark ghost
  edges on one eye + corrupted per-eye state drops the keyboard chars from the other.
- Fix: AntialiasingMethod=0 (None) in settings.ini. CONFIRMED on-headset. This is a
  SETTINGS change, NOT in git. Restore file:
  ~/Distros/turboquant-home/dfu-backup-20260902/settings.ini.stereo-fixed-AA0
- The deferred-PPv2-redeploy code (ee78b3c/d66f84e) does NOT fix stereo. The
  whole-layer PostProcessLayer.enabled bounce (in ee78b3c, removed in d66f84e) is
  HARMFUL — DFU's own comment warns NEVER to toggle the whole layer; it corrupts
  per-eye PPv2 state such that even the effects-menu repair can't recover it.
- Lesson: TAA is incompatible with OpenXR Multi-pass on this stack. Keep AA off.

## KEYBOARD SAVE-SCREEN BLANK LETTERS — STILL OPEN (2026-09-02)
- The effects-settings trick (open the effects menu, no toggle) makes the letters
  appear AND stick. This is the SAME mechanism that fixed stereo (a clean PPv2
  re-init). So the keyboard blank-letters may ALSO be a per-eye PPv2 render-state
  issue (the label overlay drops from one eye), NOT occlusion.
- TAA-off (the stereo fix) did NOT clear the keyboard bug — so it is a DIFFERENT PP
  effect or render path than TAA. Investigate the keyboard label quads' per-eye PP
  state on the save screen specifically.
- See DFU_VR_HANDOFF.md ACTIVE BUG for the full evidence + next steps.
