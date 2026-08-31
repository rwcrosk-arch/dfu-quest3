# DFU Quest3 VR — TODO List (living document)

Last updated: 2026-08-31

## Keyboard (active)
- [x] Keyboard works — all keys + can type (7b88939, milestone-keyboard-usable)
- [x] Key labels render (baked textures, e1240f3, milestone-keyboard-labels)
- [x] Repositioned below panel + shift swaps labels (590117c, milestone-keyboard-shift)
- [ ] Save-screen letter keys blank — fix shipped 72952ec (HMD-pose anchoring), AWAITING TEST

## Pending (in order)
1. Looting corpses — may be broken, hard to confirm
2. 6DOF hands — weapons/shields bound to each hand separately (left/right)
3. Load 3D weapon modpack (Unity AssetBundle) for real 3D weapon models at the hands
4. ~~VRUIOverlay scene-reload re-wiring bug~~ — RESOLVED / super rare, deprioritized
5. VRAM leak in UserInterfaceRenderTarget.CheckTargetTexture (~5MB/s)
6. Verify Save/Load works end-to-end from pause menu
7. Blocking/defending — melee attack works; block/parry not yet bound
8. Suppress 2D-panel weapon sprite once real 3D hand weapons land
9. Verify spell casting fires with visible effect + hits
10. Bow/archery support in the 3D weapon work
11. HUD clickable elements respond to the ray
12. Performance/GPU check (render target + weapon quad + panel + ray all rendering)
13. Ray depth-awareness in multi-panel scenes — keyboard active casts right through (input works but ray not depth-aware); gameplay ray not aware of actionable objects

## Milestones
- milestone-controls-sticks-working
- milestone-fullcolor-world
- milestone-hud-transparency
- milestone-jump-run-menuclicks
- milestone-keyboard-labels
- milestone-keyboard-shift
- milestone-keyboard-usable
- milestone-melee-combat
- milestone-pause-menu
- milestone-raycast-menu-follow
- milestone-weapon-visible
