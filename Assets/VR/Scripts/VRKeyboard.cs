// DFU Quest3 VR — self-contained world-space keyboard for text entry.
// Auto-shows when a DFU TextBox has focus (save name, player name, etc.).
// Keys are world-space quads with TextMesh labels; the user points the controller
// ray at a key and pulls the trigger to press it. Characters/keys are injected via
// DaggerfallUI.QueueVRCharacter/QueueVRKey, which DaggerfallUI.Update drains before
// TopWindow.Update so the TextBox sees them exactly like OnGUI-delivered keypresses.
// No Meta/XRI package dependencies.

using UnityEngine;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;

namespace DFUQuest3
{
    public class VRKeyboard : MonoBehaviour
    {
        static readonly string[] Rows =
        {
            "1234567890",
            "qwertyuiop",
            "asdfghjkl",
            "zxcvbnm",
        };
        const float KeySize = 0.09f;   // key quad size in meters (bigger for readability)
        const float KeyGap = 0.012f;
        const float BoardScale = 1.0f;

        GameObject board;
        bool shift;
        bool lastTrigger;
        public MCPPoseBridge poseBridge;

        void Update()
        {
            // Heartbeat: confirm the component runs and log the top window/focus state
            // periodically (unconditional, so we see it even when top/FocusControl is null).
            hb -= Time.unscaledDeltaTime;
            if (hb <= 0f)
            {
                hb = 2f;
                var ui = DaggerfallUI.Instance;
                var top = ui != null ? DaggerfallUI.UIManager.TopWindow : null;
                var fc = top != null ? top.FocusControl : null;
                Debug.Log("[DFUQuest3] VRKeyboard heartbeat: ui=" + (ui != null) +
                    " top=" + (top != null ? top.GetType().Name : "null") +
                    " focus=" + (fc != null ? fc.GetType().Name : "null") +
                    " isTextBox=" + (fc is TextBox) +
                    " isTextInputWin=" + (top != null && IsTextInputWindow(top)));
            }

            bool wantShown = TextBoxFocused();
            if (wantShown && board == null)
            {
                try { Build(); Debug.Log("[DFUQuest3] VRKeyboard: built (TextBox focused)"); }
                catch (System.Exception e) { Debug.LogError("[DFUQuest3] VRKeyboard Build failed: " + e); }
            }
            if (!wantShown && board != null) { Destroy(board); board = null; Debug.Log("[DFUQuest3] VRKeyboard: dismissed"); return; }
            if (board == null) return;

            AnchorInFrontOfHead();
            PollKeys();
        }

        float hb = 2f;

        static bool TextBoxFocused()
        {
            var ui = DaggerfallUI.Instance;
            var top = ui != null ? DaggerfallUI.UIManager.TopWindow : null;
            if (top == null) return false;

            // RELIABLE PATH: DFU text-entry windows never set FocusControl (their
            // TextBoxes use UseFocus=false + IME composition), and the NativePanel
            // component walk proved unreliable on-device (Setup() is deferred until
            // the window's first Update after dfUnity.IsReady, so freshly pushed
            // windows can have an empty NativePanel containing only the auto-added
            // "Outline" component — see Panel ctor). A window-type whitelist is the
            // deterministic signal: these window classes ALWAYS show a TextBox for
            // text input as soon as they are topmost.
            if (IsTextInputWindow(top))
                return true;

            // Fallback 1: a TextBox actually has focus (e.g. AddTextBoxWithFocus boxes).
            var focus = top.FocusControl;
            if (focus is TextBox)
                return true;

            // Fallback 2: walk NativePanel / ParentPanel trees for any TextBox.
            // Works once Setup() has run; harmless to keep for modded windows.
            if (top is DaggerfallBaseWindow baseWin && baseWin.NativePanel != null
                && ContainsTextBox(baseWin.NativePanel))
                return true;
            var panel = top.ParentPanel;
            if (panel != null && ContainsTextBox(panel))
                return true;
            return false;
        }

        // Window classes that present a text-entry TextBox when shown.
        // Whitelist by runtime type name so mod windows can be added without
        // compile-time references.
        static readonly string[] TextInputWindowNames =
        {
            "CreateCharNameSelect",        // new-character name field
            "CreateCharCustomClass",       // custom class name field
            "DaggerfallUnitySaveGameWindow", // save-game name field
            "DaggerfallInputMessageBox",   // generic text prompt
            "DaggerfallBankingWindow",     // gold transaction amount
        };

        static bool IsTextInputWindow(DaggerfallWorkshop.Game.UserInterface.IUserInterfaceWindow window)
        {
            string name = window.GetType().Name;
            for (int i = 0; i < TextInputWindowNames.Length; i++)
                if (name == TextInputWindowNames[i])
                    return true;
            return false;
        }

        static void DumpTree(Panel panel, int depth, ref string tree)
        {
            for (int i = 0; i < panel.Components.Count; i++)
            {
                var c = panel.Components[i];
                tree += new string(' ', depth * 2) + c.GetType().Name + "\n";
                if (c is Panel p)
                    DumpTree(p, depth + 1, ref tree);
            }
        }

        // Recursively search a panel's component tree for a TextBox.
        static bool ContainsTextBox(Panel panel)
        {
            for (int i = 0; i < panel.Components.Count; i++)
            {
                var c = panel.Components[i];
                if (c is TextBox)
                    return true;
                if (c is Panel p && ContainsTextBox(p))
                    return true;
            }
            return false;
        }

        void Build()
        {
            board = new GameObject("DFU VR Keyboard");
            board.transform.localScale = Vector3.one * BoardScale;

            float rowH = KeySize + KeyGap;
            float totalH = Rows.Length * rowH + KeySize + KeyGap; // + special row
            float y = totalH * 0.5f - KeySize * 0.5f;

            for (int r = 0; r < Rows.Length; r++)
            {
                string row = Rows[r];
                float rowW = row.Length * (KeySize + KeyGap) - KeyGap;
                float x = -rowW * 0.5f + KeySize * 0.5f;
                for (int c = 0; c < row.Length; c++)
                {
                    MakeKey(new Vector2(x, y), row[c].ToString());
                    x += KeySize + KeyGap;
                }
                y -= rowH;
            }

            // Special row: Shift, Space, Backspace, Enter
            float spW = KeySize * 2 + KeyGap;
            float spX = -(spW * 2 + KeyGap * 2) * 0.5f + spW * 0.5f;
            MakeKey(new Vector2(spX, y), "Shift", true);
            spX += spW + KeyGap;
            MakeKey(new Vector2(spX, y), "Space", true);
            spX += spW + KeyGap;
            MakeKey(new Vector2(spX, y), "Bksp", true);
            spX += spW + KeyGap;
            MakeKey(new Vector2(spX, y), "Enter", true);

            DontDestroyOnLoad(board);
        }

        void MakeKey(Vector2 pos, string label, bool special = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Key_" + label;
            go.transform.SetParent(board.transform, false);
            go.transform.localPosition = new Vector3(pos.x, pos.y, 0f);
            go.transform.localScale = new Vector3(KeySize, KeySize, 1f);
            // Keep the collider — PollKeys raycasts against the key quads to detect
            // which key the controller ray is pointing at.

            var rend = go.GetComponent<Renderer>();
            // Null-safe shader lookup — new Material(null) throws. Unlit/Color is built-in
            // but can be stripped; fall back to Unlit/Texture.
            Shader s = Shader.Find("Unlit/Color");
            if (s == null || !s.isSupported) s = Shader.Find("Unlit/Texture");
            if (s == null || !s.isSupported) s = Shader.Find("Sprites/Default");
            if (s != null)
            {
                var mat = new Material(s);
                mat.color = special ? new Color(0.3f, 0.3f, 0.4f, 1f) : new Color(0.5f, 0.5f, 0.6f, 1f);
                rend.sharedMaterial = mat;
            }

            // TextMesh label — the builtin fonts (Arial/LegacyRuntime.ttf) are NOT in this
            // stripped Android build, so tm.font is null and setting tm.text throws NRE.
            // Load a real font that ships in the project's Resources instead.
            try
            {
                var tm = go.AddComponent<TextMesh>();
                Font f = tm.font;
                if (f == null) f = LoadKeyboardFont();
                if (f != null) tm.font = f;
                tm.text = label;
                tm.characterSize = 0.05f;
                tm.fontSize = 64;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.alignment = TextAlignment.Center;
                tm.color = Color.white;
                var tr = tm.transform;
                tr.localPosition = new Vector3(0f, 0f, -0.01f);
                tr.localScale = new Vector3(0.5f, 0.5f, 1f);
                tr.localRotation = Quaternion.identity;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[DFUQuest3] VRKeyboard TextMesh label failed for '" + label + "': " + e.Message);
            }
        }

        void AnchorInFrontOfHead()
        {
            // Anchor to the camera/head, NOT PlayerObject — in the char-creation screen
            // there is no Player object yet, and gm.PlayerObject throws
            // "GameManager could not find GameObject with tag Player" every frame,
            // which propagated out of Update() and left the board at origin (invisible).
            var cam = Camera.main;
            if (cam == null) return;
            // Keep the keyboard IN FRONT at eye level (the position that showed the grey
            // key squares). The build-in font is null on this stripped Android build so
            // TextMesh labels fail, but the board itself renders here.
            Vector3 pos = cam.transform.position + cam.transform.forward * 1.2f;
            pos.y = cam.transform.position.y - 0.1f; // slightly below eye level
            Quaternion rot = Quaternion.Euler(0, cam.transform.eulerAngles.y, 0);
            board.transform.SetPositionAndRotation(pos, rot);
        }

        static Font cachedFont;
        static Font LoadKeyboardFont()
        {
            if (cachedFont != null) return cachedFont;
            // Resources folder path of a shipped font (no Assets/ prefix).
            string[] tries = { "Fonts/OpenSans/OpenSansRegular", "Fonts/TESFonts/Kingthings Petrock", "Fonts/OpenSans/OpenSansSemibold" };
            for (int i = 0; i < tries.Length; i++)
            {
                var f = Resources.Load<Font>(tries[i]);
                if (f != null) { cachedFont = f; return f; }
            }
            return null;
        }

        void PollKeys()
        {
            // Build the controller ray (same tracking->world transform as VRUIOverlay).
            Vector3 rayAnchor = Vector3.zero;
            Quaternion rigYaw = Quaternion.identity;
            var rig = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (rig != null) { rigYaw = Quaternion.Euler(0, rig.transform.eulerAngles.y, 0); rayAnchor = rig.transform.position; }
            var gm = GameManager.Instance;
            if (gm != null)
            {
                try { if (gm.PlayerMotor != null && gm.PlayerObject != null) rayAnchor = gm.PlayerObject.transform.position; } catch { }
            }

            Vector3 origin = rayAnchor, dir = Vector3.forward;
            bool hasRay = false;
            if (poseBridge != null && poseBridge.controllerValid)
            {
                origin = rayAnchor + rigYaw * poseBridge.controllerPosition;
                dir = rigYaw * (poseBridge.controllerRotation * Vector3.forward);
                hasRay = true;
            }
            if (!hasRay) return;

            bool trigger = false;
            try { if (VRActionBinder.TriggerAction != null && VRActionBinder.TriggerAction.enabled) trigger = VRActionBinder.TriggerAction.WasPressedThisFrame(); } catch { }

            // Raycast against the board's key quads.
            if (Physics.Raycast(origin, dir, out RaycastHit hit, 10f))
            {
                var key = hit.collider != null ? hit.collider.GetComponentInParent<Transform>() : null;
                if (key != null && key.name.StartsWith("Key_"))
                {
                    string label = key.name.Substring(4);
                    if (trigger && !lastTrigger)
                        PressKey(label);
                }
            }
            lastTrigger = trigger;
        }

        void PressKey(string label)
        {
            var ui = DaggerfallUI.Instance;
            if (ui == null) return;
            switch (label)
            {
                case "Shift": shift = !shift; break;
                case "Space": ui.QueueVRCharacter(' '); break;
                case "Bksp": ui.QueueVRKey(KeyCode.Backspace); break;
                case "Enter": ui.QueueVRKey(KeyCode.Return); break;
                default:
                    if (label.Length == 1)
                        ui.QueueVRCharacter(shift ? char.ToUpper(label[0]) : label[0]);
                    break;
            }
        }
    }
}
