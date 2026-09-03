# Daggerfall Unity — Quest 3 VR Port

A [Daggerfall Unity](https://github.com/Interkarma/daggerfall-unity) fork that ports
the classic **Elder Scrolls II: Daggerfall** to the **Meta Quest 3** as a native,
room-scale virtual-reality game.

The upstream project runs Daggerfall as a desktop-first mouse-and-keyboard experience.
This fork takes that same Unity codebase and re-targets it for the Quest 3: floor-level
VR head tracking, world-space UI, motion-relevant controls, and a playable first-person
gameplay loop running directly on the headset.

This is not a port that layers VR on top of an emulator. It is the real Daggerfall Unity
engine, compiled for Android/ARM64 with OpenXR, with a custom VR rig (in `Assets/VR/`)
that augments DFU's existing cameras, input, and UI rather than replacing them.

---

## What works today

- **Native Quest 3 build** — Unity 6000.0.82f1, IL2CPP/ARM64, Vulkan, OpenXR, multi-pass.
- **Head tracking** — floor-level tracking with a full XR rig. Look around in the world.
- **World-space UI** — DFU's desktop IMGUI menu is captured to a render texture and shown
  on a floating panel in front of you (no invisible menus).
- **Full control map** — move/turn/pitch on the sticks, activate/click on the right
  trigger, sheath/unsheathe, attack, jump, crouch, run toggle, magic menu, pause menu.
- **Melee combat** — unsheathe and attack with the weapon visible as a 3D quad at your hand.
- **World-space QWERTY keyboard** — for typing save / character names directly in VR.
- **Save / load** — save, load, and switch character, both from the in-game pause menu
  and from the start menu. Saves persist across app restarts.
- **Boot flow** — game opens into DFU's settings wizard (options + future mod panel),
  then flows into the in-world New Game / Load Game menu, with intro videos on new game.
- **Classic Daggerfall content** — full arena2 game data, saves, quests.

## Status: beta

This build is stable enough for playtesting: new game creation, melee combat, saving,
loading, and the full menu stack work end-to-end on the headset. Playtesters should
read the known issues below.

## Known issues / current state

- **Stereo fix**: Antialiasing must be set to `None` (`AntialiasingMethod=0` in
  `settings.ini`). TAA under OpenXR multi-pass corrupts per-eye temporal history —
  see `DFU_VR_HANDOFF.md` for the root-cause writeup.
- **Start-menu mirror artifact**: on the in-world New Game / Load Game menu, the
  background can show a faint mirror-like reflection of the menu and the pointing ray.
  Cosmetic only — the menu is fully usable. Under investigation.
- See `DFU_VR_HANDOFF.md` for the full session handoff, and `DFU_VR_TODO.md` for the
  backlog (loot, 6DOF hands, 3D weapon modpack, VRAM leak, and more).

---

## Requirements

- A **Meta Quest 3** headset in developer / sideloading mode.
- A free copy of the original **DOS Daggerfall** for its game data (Steam or GOG).
- **Unity 6000.0.82f1** and the **Unity Android build support** module to build.
- **adb** (Android Debug Bridge) to install the APK.
- The **Meta XR / OpenXR** packages (add the scoped registry `https://npm.developer.oculus.com`).

Building this project requires a free copy of DOS Daggerfall to supply all game assets
(textures, 3D models, sound). You can get it free from
[Steam](https://store.steampowered.com/app/1812390/The_Elder_Scrolls_II_Daggerfall/)
or GOG.

---

## How it works

This fork does **not** create a second OpenXR camera. Instead it finds DFU's existing
camera(s) and augments them:

- **`VRSceneSetup`** bootstraps the VR rig at startup (XROrigin, UI overlay, weapon,
  keyboard, pose bridge).
- **`VRRigBootstrap`** drives head tracking and re-points DFU's camera lookups at the
  real gameplay camera, so every DFU system (mouse-look, post-processing, PlayerActivate)
  keeps working.
- **`VRUIOverlay`** captures DFU's IMGUI into a rendered panel ~2m in front of you and
  drives a reticle + ray for interacting with menus.
- **`VRKeyboard`, `VRWeaponRenderer`, `VRTriggerBridge`, `VRActionInjector`** handle
  text entry, weapon rendering, and the control bindings for VR.

The VR-specific code lives almost entirely in **`Assets/VR/`** and `Assets/Editor/BuildDFU.cs`,
so the DFU source under `Assets/Scripts/` is only patched surgically.

The three hard-won problems (and how they were solved) are documented in the
`dfu-quest3-vr-port` skill and the in-repo docs (`DFU_VR_ARCHITECTURE.md`,
`DFU_VR_HANDOFF.md`, `DFU_WINDOW_CATALOG.md`, `ATTEMPTED_FIXES.md`).

---

## Building & installing

### 1. Get the game data

Extract a DOS Daggerfall install so you have the classic file structure (an `ARENA2`
directory with `.BSA`/`.SND` files). On the headset this must live in the app's own
external directory so scoped storage allows access:

```
/storage/emulated/0/Android/data/com.dfworkshop.dfuquest3/files/Daggerfall/arena2
```

Set `MyDaggerfallPath` in `settings.ini` to point at `.../files/Daggerfall/`.

### 2. Build the Android APK

From the repo root (Unity 6000.0.82f1 on the PATH):

```bash
UNITY=/path/to/Unity/Hub/Editor/6000.0.82f1/Editor/Unity
$UNITY -batchmode -nographics -quit \
  -logFile dfu-x.log \
  -projectPath "$PWD" \
  -executeMethod BuildDFU.BuildAndroidDev
```

The APK is written to `dfu-builds/android/DFU.apk`.

### 3. Deploy to the headset

```bash
adb install -r dfu-builds/android/DFU.apk
adb shell am force-stop com.dfworkshop.dfuquest3
adb shell monkey -p com.dfworkshop.dfuquest3 -c android.intent.category.LAUNCHER 1
```

### 4. On-device settings

The stereo fix (`AntialiasingMethod=0`) lives in `settings.ini` at
`/storage/emulated/0/Android/data/com.dfworkshop.dfuquest3/files/settings.ini`.
The working reference copy is backed up in `dfu-backup-20260902/`.

---

## Control map (Quest 3)

| Input | Action |
|-------|--------|
| Left stick | Move |
| Right stick X / Y | Turn / Pitch |
| Right trigger | Activate / click |
| A | Sheath / unsheathe |
| B | Magic menu |
| X (hold) | Jump |
| Y | Menu cycle |
| Left / Right grip | Left / right-hand weapon or shield |
| Left trigger | Cast / recast |
| Left stick click | Crouch |
| Right stick click | Toggle run |
| Menu button | Context (back / pause options) |

---

## Project layout

```
Assets/VR/Scripts/    All VR integration (our own code)
Assets/VR/Shaders/    VRUIChromaKey and related shaders
Assets/Editor/        BuildDFU.cs (Android build entry point)
Assets/Scripts/       Vanilla DFU source (patched surgically)
DFU_VR_ARCHITECTURE.md  Codebase structure + hard-won lessons
DFU_VR_HANDOFF.md       Session handoff + restore procedure
DFU_VR_TODO.md          Living backlog
DFU_WINDOW_CATALOG.md   Every DFU window in the panel tree
ATTEMPTED_FIXES.md      Every fix that failed/regressed
```

---

## Credits & license

This project is a fork of **Daggerfall Unity**, created by
[Daggerfall Workshop](http://www.dfworkshop.net) and led by Gavin Clayton, and is
distributed under the **MIT License** (see `LICENSE`). The original project's copyright
and contributors apply to the upstream code. The VR port layer in `Assets/VR/` is original
work for this fork, also under the MIT License.

Daggerfall and The Elder Scrolls are properties of Bethesda Softworks / Zenimax. This
project is a fan recreation and is not affiliated with or endorsed by Bethesda.
