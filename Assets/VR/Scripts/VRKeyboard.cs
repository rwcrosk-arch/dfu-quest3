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
        const float KeySize = 0.05f;   // key quad size in meters
        const float KeyGap = 0.006f;
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
                    " isTextBox=" + (fc is TextBox));
            }

            bool wantShown = TextBoxFocused();
            if (wantShown && board == null) { Build(); Debug.Log("[DFUQuest3] VRKeyboard: built (TextBox focused)"); }
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
            bool focused = top != null && top.FocusControl is TextBox;
            if (top != null && top.FocusControl != null && !focused)
                Debug.Log("[DFUQuest3] VRKeyboard: top=" + top.GetType().Name + " focus=" + top.FocusControl.GetType().Name);
            return focused;
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
            var mat = new Material(Shader.Find("Unlit/Color"));
            mat.color = special ? new Color(0.3f, 0.3f, 0.4f, 1f) : new Color(0.5f, 0.5f, 0.6f, 1f);
            rend.sharedMaterial = mat;

            // TextMesh label
            var tm = go.AddComponent<TextMesh>();
            tm.text = label;
            tm.characterSize = 0.02f;
            tm.fontSize = 48;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.white;
            var tr = tm.transform;
            tr.localPosition = new Vector3(0f, 0f, -0.01f);
            tr.localScale = new Vector3(0.5f, 0.5f, 1f);
            tr.localRotation = Quaternion.identity;
        }

        void AnchorInFrontOfHead()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;
            Vector3 anchor = gm.PlayerObject ? gm.PlayerObject.transform.position : Vector3.zero;
            var rig = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
            Quaternion rigYaw = rig ? Quaternion.Euler(0, rig.transform.eulerAngles.y, 0) : Quaternion.identity;
            // Place ~1.2m in front, ~1.4m up (eye-ish), facing the player.
            Vector3 pos = anchor + rigYaw * new Vector3(0f, 1.4f, 1.2f);
            board.transform.SetPositionAndRotation(pos, rigYaw * Quaternion.Euler(0, 0, 0));
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
