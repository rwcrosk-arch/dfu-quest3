# DFU Quest3 VR — Window / Panel Catalog

Purpose: catalog every DFU window and where its interactive components (especially
TextBoxes for text input) live in the panel tree, so VR features (keyboard, ray
clicking, HUD interaction) can reliably find them. The recurring gotcha: DFU windows
add their controls to `NativePanel.Components` (or a panel nested inside NativePanel),
NOT to `ParentPanel.Components` directly. `NativePanel` is itself a child of
`ParentPanel`. So a component-tree walk must recurse into NativePanel (and nested
panels) to find TextBoxes/buttons.

## Panel hierarchy (DaggerfallBaseWindow)
- `ParentPanel` (UserInterfaceWindow) — fits the whole viewport.
  - `NativePanel` (DaggerfallBaseWindow) — the classic 320x200 native panel, scaled to
    fit. Most windows add their controls here.
    - window-specific panels/controls (buttons, TextBoxes, listboxes, etc.)

## Windows with TEXT INPUT (TextBox) — where the TextBox lives
| Window | TextBox field | Panel holding it |
|--------|--------------|------------------|
| CreateCharNameSelect | `textBox` | NativePanel.Components |
| CreateCharCustomClass | `nameTextBox` | NativePanel.Components |
| CreateCharSummary | `textBox` | NativePanel.Components |
| ColorPicker | `hexColor` | NativePanel.Components (pickerPanel) |
| DaggerfallAdvancedSettingsWindow | `textBox` (local) | NativePanel.Components (panel) |
| DaggerfallBankingWindow | `transactionInput` | NativePanel.Components (mainPanel) |
| DaggerfallInputMessageBox | `textBox` | NativePanel.Components (textPanel) |
| DaggerfallUnityMouseControlsWindow | `textBox` (local) | NativePanel.Components (panel) |
| DaggerfallUnitySaveGameWindow | `saveNameTextBox` | NativePanel.Components (mainPanel) |

NOTE: None of these set the TextBox as the window's `FocusControl`. `FocusControl` is
null for these windows. So keyboard detection MUST walk the component tree, not check
`FocusControl is TextBox`.

## Other notable windows (no text input, but interactive)
- DaggerfallPauseOptionsWindow — Save/Load/Settings/Controls buttons (NativePanel).
- DaggerfallInventoryWindow, DaggerfallSpellBookWindow, DaggerfallCharacterSheetWindow,
  DaggerfallQuestJournalWindow, DaggerfallAutomapWindow, DaggerfallTravelMapWindow,
  DaggerfallRestWindow, DaggerfallTransportWindow — menu windows opened by the cycler.
- DaggerfallHUD — the persistent HUD (vitals, spell icons, compass).
- DaggerfallMessageBox — modal message boxes (may have buttons).
- DaggerfallStartNewGameWizard — the new-game flow (char creation sequence).

## VR keyboard detection (VRKeyboard.cs)
`TextBoxFocused()` must return true when the top window's component tree (recursing
into NativePanel and nested panels) contains a TextBox. Current implementation walks
`ParentPanel.Components` and one level of nested panels — MUST also recurse into
`NativePanel` (a child of ParentPanel) and its nested panels, since that's where the
TextBoxes actually live.
