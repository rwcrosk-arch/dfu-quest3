# Daggerfall Unity — Quest 3 VR Port

A [Daggerfall Unity](https://github.com/Interkarma/daggerfall-unity) fork that brings
the classic **Elder Scrolls II: Daggerfall** to the **Meta Quest 3** as a native,
room-scale virtual-reality game.

Daggerfall Unity runs the 1996 classic as a desktop-first mouse-and-keyboard
experience. This fork takes that same Unity codebase and re-targets it for the Quest 3:
floor-level head tracking, menus floating in your play space, and a playable
first-person adventure running directly on the headset — no streaming, no PC required.

This is not VR layered over an emulator. It is the real Daggerfall Unity engine,
compiled for Android/ARM64 with OpenXR, with a VR rig that works alongside DFU's
existing cameras, input, and UI.

---

## Status: beta

This build is playable end-to-end: create a character, fight, explore, save, and load.
It is ready for playtesting — expect rough edges, and see Known Issues below.

## Features

- **Native Quest 3 build** — Unity 6000.0.82f1, IL2CPP/ARM64, Vulkan, OpenXR (multi-pass).
- **Head tracking** — full room-scale XR rig; look around the world naturally.
- **Menus in your space** — DFU's interface is captured to a render texture and shown on
  a floating panel ~2m ahead, with a pointing ray and reticle for clicking.
- **Settings-first boot** — the game opens into DFU's settings screen, then flows into
  the New Game / Load Game menu rendered inside the game world, complete with the
  original intro videos on a new game.
- **Full control map** — move/turn/pitch on the sticks, activate/click on the right
  trigger, sheath/unsheathe, attack, jump, crouch, run toggle, magic menu, pause menu.
- **Melee combat** — unsheathe and swing, with the weapon visible as a 3D quad at your hand.
- **World-space QWERTY keyboard** — type save and character names directly in VR.
- **Save / load** — save, load, and switch character from the pause menu or the start
  menu. Saves persist across app restarts.
- **Classic Daggerfall content** — the complete arena2 game data: quests, dungeons,
  guilds, the full world.

## Known issues

- **Antialiasing must stay off** — set `AntialiasingMethod=0` in `settings.ini` (the
  default shipped config does this). Temporal antialiasing corrupts per-eye history
  under OpenXR multi-pass, producing broken stereo.
- **Start-menu mirror artifact** — on the New Game / Load Game menu, the background can
  show a faint mirror-like reflection of the menu and your pointing ray. Cosmetic only;
  the menu is fully usable.
- Performance is playable but not yet tuned; expect dips in dense areas.

## Planned improvements

A living backlog tracks the headlines below (kept locally by the maintainers):

- **Loot interaction** — open corpses and containers from inside the world.
- **6DOF hands** — physical weapons and shields tracked in space, both hands.
- **Polish pass** — comfort options, HUD refinements, ray depth-awareness, performance
  tuning.
- **Mod support surface** — a mod checklist panel in the settings screen, building on
  DFU's existing mod system.
- **VRAM leak fix** — the UI render target grows over long sessions; tracked upstream.
- **Save/load hardening** — save screenshots, quick-save slots, and load verification.

---

## Requirements

- A **Meta Quest 3** headset in developer / sideloading mode.
- A free copy of the original **DOS Daggerfall** for its game data — get it from
  [Steam](https://store.steampowered.com/app/1812390/The_Elder_Scrolls_II_Daggerfall/)
  or GOG.
- **Unity 6000.0.82f1** with the Android build module, to build from source.
- **adb** (Android Debug Bridge) to install the APK.

---

## How it works

This fork does not create a second OpenXR camera. It finds DFU's existing cameras and
augments them:

- **`VRSceneSetup`** bootstraps the VR rig at startup (XROrigin, UI overlay, weapon,
  keyboard, pose bridge).
- **`VRRigBootstrap`** drives head tracking and re-points DFU's camera lookups at the
  real gameplay camera, so every DFU system (mouse-look, post-processing,
  PlayerActivate) keeps working.
- **`VRUIOverlay`** captures DFU's IMGUI into a rendered panel in front of you and
  drives a reticle + ray for interacting with menus.
- **`VRKeyboard`, `VRWeaponRenderer`, `VRTriggerBridge`, `VRActionInjector`** handle
  text entry, weapon rendering, and VR control bindings.

The VR integration lives in **`Assets/VR/`**, so the DFU source under `Assets/Scripts/`
is only patched surgically.

---

## Building & installing

### 1. Get the game data

Extract a DOS Daggerfall install so you have the classic file structure (an `ARENA2`
directory with `.BSA`/`.SND` files). On the headset it must live in the app's own
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

`settings.ini` lives at
`/storage/emulated/0/Android/data/com.dfworkshop.dfuquest3/files/settings.ini`.
`AntialiasingMethod=0` is required for correct stereo (see Known Issues).

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

## Credits & license

This project is a fork of **Daggerfall Unity**, created by
[Daggerfall Workshop](http://www.dfworkshop.net) and led by Gavin Clayton, and is
distributed under the **MIT License** (see `LICENSE`). The original project's copyright
and contributors apply to the upstream code. The VR port layer in `Assets/VR/` is
original work for this fork, also under the MIT License.

Daggerfall and The Elder Scrolls are properties of Bethesda Softworks / Zenimax. This
project is a fan recreation and is not affiliated with or endorsed by Bethesda.