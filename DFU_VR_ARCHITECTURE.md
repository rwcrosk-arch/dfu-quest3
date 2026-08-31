# DFU Quest3 VR — Codebase Structure & Key Insights

Living reference for future sessions. This documents the architecture, the VR
integration points, and the hard-won lessons about controls, cameras, input, and
rendering. Keep it updated as we learn more.

---

## 1. Project Layout

- **`Assets/VR/Scripts/`** — ALL our VR integration lives here (our own code).
  - `VRSceneSetup.cs` — bootstraps the whole VR rig at startup. `[DefaultExecutionOrder(-5000)]`.
    Creates XROrigin, VR UI overlay, weapon renderer, keyboard, pose bridge. All DDOL.
  - `VRRigBootstrap.cs` — LateUpdate rig-follow (position + yaw), ambient rebind,
    `FindGameCamera<PlayerMouseLook>()`, XROrigin→Player parenting, `VRHeadPitch` node.
  - `VRUIOverlay.cs` — the floating 2D UI panel (DFU's IMGUI captured to a RenderTexture
    shown on a world-space quad ~2m in front). Reticle + ray. `DontDestroyOnLoad`.
  - `VRTriggerBridge.cs` — default execution order (0). Sets `vrClickQueued` for menu clicks.
  - `VRActionInjector.cs` — `[DefaultExecutionOrder(100)]`. All `AddAction` button injections.
  - `VRActionBinder.cs` — the InputSystem action bindings (buttons, sticks, grips).
  - `VRWeaponRenderer.cs` — renders the DFU weapon as a 3D world-space quad at the hand.
  - `VRKeyboard.cs` — world-space QWERTY keyboard for text entry (save/player names).
  - `MCPPoseBridge.cs` — controller pose in tracking space (port 8720).
  - `VRStartGameBridge.cs` — `SceneManager.LoadScene(1)` Single mode.
- **`Assets/VR/Shaders/`** — `VRUIChromaKey.shader` (keys out near-black → transparent).
- **`Assets/Scripts/`** — vanilla DFU source (we patch surgically, prefer VR-adjacent edits).
- **`DFU_WINDOW_CATALOG.md`** — every DFU window + where its components live in the panel tree.
- **`ATTEMPTED_FIXES.md`** — every fix that failed/regressed, so we don't re-invent the wheel.

---

## 2. The VR UI Panel (how the 2D UI is shown in VR)

- DFU's UI is IMGUI/OnGUI. `DaggerfallUI` has a `CustomRenderTarget`
  (`UserInterfaceRenderTarget`) that captures the UI into a RenderTexture.
- `VRUIOverlay` shows that RenderTexture on a world-space quad ~2m in front of the player.
- The panel is `DontDestroyOnLoad` and re-wired on scene load (commit `d11cbff`) because
  DFU loads the game scene via `SceneManager.LoadScene(1)` **Single** mode, which destroys
  non-DDOL objects.
- **KNOWN BUG (panelrewire):** `VRUIOverlay.OnSceneLoaded` doesn't always fire, so the panel
  can keep pointing at the old (destroyed) render target after char-creation→gameplay. This
  makes the char-creation screen persist onto the panel. Also leaks ~5MB/s VRAM in
  `CheckTargetTexture()`. NOT yet fixed.

---

## 3. Execution-Order Split (THE core control insight)

**Root cause of most control timing bugs:** `InputManager.Update()` (default order 0)
clears `currentActions` at the start of its update. DFU's gameplay managers (WeaponManager,
EntityEffectManager, etc.) also run at order 0 and read `ActionStarted`/`HasAction` from
`currentActions`.

- **Clicks** must be set at order 0 (before the UI reads `vrClickQueued`).
- **Action injection** must run at order 100 (after `InputManager.Update` clears).

So they live in SEPARATE components:
- `VRTriggerBridge` (order 0) → sets `vrClickQueued` for menu clicks.
- `VRActionInjector` (order 100) → all `AddAction` button injections.

**`ActionStarted`-based actions (ReadyWeapon, SwingWeapon, Jump) fail** if injected at order
100, because the consumer (order 0) already read the empty list. **`ActionComplete`-based**
actions (menu cycle, magic menu, Escape) work because they read `previousActions`.

**The robust fix for order-sensitive actions: call the manager method DIRECTLY** instead of
injecting an action. Examples:
- Unsheathe: `GameManager.Instance.WeaponManager.ToggleSheath()` (direct call works).
- Attack: `WeaponManager.VRTriggerAttack()` (new method mirroring the click-attack path).
- Spell cast: `PlayerEffectManager.SetReadySpell(LastSpell)` (direct).

---

## 4. Cameras & Head Tracking

- **`InputDevices.devicePosition` (HMD) is XR TRACKING space**, not world space. Must be
  transformed through the XROrigin's world transform to get world position.
- **`Camera.main` is UNRELIABLE in gameplay** — it can resolve to a stale camera near the
  origin while the rig/player is elsewhere. Use the HMD pose via `InputDevices` +
  `XROrigin` transform (same as `VRUIOverlay`), or `GameManager.MainCamera`.
- **`PlayerMotor` presence is the reliable "in gameplay" discriminator** — `StateManager.
  GameInProgress` is FALSE in gameplay.
- **Ray anchor = player's feet (tracking origin)** — controller pose carries its own height
  (0.70m); eye-height double-counted → ray at y=3.16. Anchor to feet.
- **Ray direction must rotate through `rigYaw`** — controller pose is XR tracking space;
  rotate through XROrigin world yaw in all three ray branches.

---

## 5. Movement & Player Space

- **Yaw rotates the Player object, NOT the XROrigin.** DFU computes movement in Player
  LOCAL space (`FrictionMotor.TransformDirection`). Rig-only yaw desyncs view from movement.
- **Pitch on a dedicated `VRHeadPitch` node** between rig and camera, to keep rig/Player level.
- **`EnableController=false` is the clean path** — DFU legacy joystick double-reads XR sticks
  on Android (generic `AndroidJoystick`/`AndroidGamepad` devices leak).

---

## 6. Input Bindings (current control map)

- Left stick = move; Right stick X = turn; Right stick Y = pitch.
- Right trigger = activate/click; A = sheath/unsheathe; B = magic menu.
- X (hold) = jump; Y = menu cycle; Left grip = left-hand weapon/shield; Right grip = right-hand.
- Left trigger = cast/recast; Left stick click = crouch; Right stick click = toggle run.
- Menu button = context-aware (back/exit if window open, else pause options).
- **`grip` is an analog `AxisControl`**; the discrete button is `gripPressed` (alias "GripButton").

---

## 7. Rendering / Shaders

- **Chroma-key shader must be in `m_AlwaysIncludedShaders`** with the CORRECT fileID.
  `Shader.Find`-only references get stripped from Android builds. The entry must be
  `{fileID: 4800000, guid: <guid>, type: 3}` — a `fileID: 0, type: 0` entry is silently
  dropped (the shader never compiles into the build).
- **`Unlit/Color` ignores `mainTexture`** — use `Unlit/Texture` to show a texture.
- **Dynamic fonts have an EMPTY glyph atlas until `RequestCharactersInTexture()` is called.**
  TextMesh's `set_text` → TextGenerator NREs on Unity 6 IL2CPP even with a populated atlas.
  **Bake labels into Texture2Ds** (GL.QUADS blit from the font atlas → ReadPixels) instead.
- **`Texture2D(0,0)` throws on Unity 6** — use `Texture2D(2,2)`.
- **PPv2 re-enabled** (darkening fix) — `ppLayer.enabled=false` disabled ColorBoost + AA.
- **Brightness comes from `RenderSettings.ambientLight`** (PlayerAmbientLight +
  SunlightManager.DaylightScale), NOT ColorBoost/PPv2.

---

## 8. Weapon Rendering

- DFU's weapon is a **2D sprite** (FPSWeapon.OnGUI), not a 3D model — UNLESS we load the
  3D weapon modpack (a Unity AssetBundle `.dfmod`).
- `VRWeaponRenderer` mirrors `FPSWeapon`'s current frame texture onto a world-space quad
  anchored to the controller (tracking→world via player feet + rig yaw), billboarded.
- **Do NOT bake the weapon into the 2D panel render target** — it occludes the HUD.
- **Do NOT set a global `SuppressOnGUIDraw` flag** — it disables the only live weapon draw.
- The 3D weapon modpack (`/home/ross/Downloads/3D Weapons Shields And Items v0.906...zip`)
  is a Unity AssetBundle (UnityFS, Unity 2019.4.40f1). Load at runtime for real 3D models.

---

## 9. Keyboard

- Meta OS keyboard (OVRKeyboard) is NOT in the installed Meta XR SDK (205.0.0) — it lived
  in the legacy Oculus Integration asset. Not integrable without external packages.
- `TouchScreenKeyboard` doesn't reliably surface in immersive OpenXR. Ruled out.
- We built a **world-space QWERTY keyboard** (`VRKeyboard.cs`) that auto-shows when a
  text-input window is top, raycasts keys, and injects via a **character queue** on
  `DaggerfallUI` (`QueueVRCharacter`/`QueueVRKey`, drained at top of Update).
- **Text-input window detection:** use a **window-type whitelist** (`CreateCharNameSelect`,
  `CreateCharCustomClass`, `DaggerfallUnitySaveGameWindow`, `DaggerfallInputMessageBox`,
  `DaggerfallBankingWindow`). `FocusControl` is null for these windows, and the panel-tree
  walk races with deferred `Setup()`. Whitelist is deterministic.
- **Anchor the keyboard to the HMD pose via XROrigin** (not `Camera.main`, which is stale
  in gameplay). Position below eye level (look-down to type), yaw-only rotation.
- **Key labels:** bake into Texture2Ds from the font atlas (TextMesh is broken on IL2CPP).
  Re-bake to uppercase when shift toggles.

---

## 10. Milestones (git tags)

- `milestone-controls-sticks-working` — sticks work
- `milestone-fullcolor-world` — full-color world
- `milestone-hud-transparency` — HUD transparency perfect (context-aware chroma-key)
- `milestone-jump-run-menuclicks` — jump, run toggle, menu clicks
- `milestone-melee-combat` — unsheathe + melee attack work
- `milestone-pause-menu` — pause menu works
- `milestone-weapon-visible` — weapon renders in 3D at the hand
- `milestone-keyboard-usable` — keyboard shows all keys + can type
- `milestone-keyboard-labels` — keyboard labels render (baked textures)
- `milestone-keyboard-shift` — keyboard fully working on char-name (shift swaps labels)

---

## 11. Git Discipline

- Commit + push at every good state; tag milestones.
- Revert to last-known-good and re-apply a segmented version on regression.
- Segment every code change to only take effect in its intended phase (game-scene fix must
  be gated so it never runs in the menu/startup scene).
- Keep `ATTEMPTED_FIXES.md` updated with failed fixes.
