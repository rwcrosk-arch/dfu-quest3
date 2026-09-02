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
        bool posLogged;
        public MCPPoseBridge poseBridge;
        // Track letter keys so their labels can be re-baked when shift toggles.
        readonly System.Collections.Generic.List<GameObject> letterKeys = new System.Collections.Generic.List<GameObject>();

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
            letterKeys.Clear(); // don't accumulate stale refs across rebuilds

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

            // Register letter keys so their labels can be re-baked when shift toggles.
            if (!special && label.Length == 1 && char.IsLetter(label[0]))
                letterKeys.Add(go);

            var rend = go.GetComponent<Renderer>();
            // Bake the glyph(s) into a per-key Texture2D from the dynamic font
            // atlas, then draw the key with Unlit/Texture. TextMesh NREs on
            // tm.text in this Unity 6 IL2CPP Android build even with a populated
            // atlas (256x512 dynamic=True) — the TextGenerator path is broken.
            // The GL-blit bake path only needs Font.RequestCharactersInTexture /
            // GetCharacterInfo / font.material, all of which are confirmed working.
            // FINAL FIX (root cause found via magenta isolator + border test):
            // The glyphs were ALWAYS rendering — as WHITE ink on a key background that
            // PPv2 (auto-exposure/bloom in gameplay) blows out past clipping to WHITE.
            // White-on-white = invisible letters. The darker special-key grey survived
            // brightening, which is why only the alphanum keys went blank on the save
            // screen while the menu (no PP brightening) always looked correct.
            // FIX: darken the alphanum key background so white glyphs keep contrast
            // after any brightening. Use the same darker grey as the special keys for
            // a uniform board.
            Color bg = new Color(0.3f, 0.3f, 0.4f, 1f); // darker grey for ALL keys

            // Dedicated always-on-top shader: Overlay queue + ZTest Always + ZWrite Off,
            // so the keys draw after (on top of) the DFU UI panel and the save window's
            // dark NativePanel backdrop. Unlit/Texture does NOT honor _ZTest/_ZWrite via
            // SetInt (not exposed properties) — this shader makes draw-after explicit.
            Shader s = Shader.Find("DFUQuest3/VRKeyboardAlwaysOnTop");
            if (s == null || !s.isSupported) s = Shader.Find("Unlit/Texture");
            if (s != null)
            {
                var mat = new Material(s);
                // Overlay queue is baked into the shader tag; keep renderQueue high
                // as a belt-and-suspenders in case the tag is stripped.
                mat.renderQueue = 4000; // Overlay+: post-UI
                var tex = BakeLabelTexture(label, bg);
                if (tex != null)
                    mat.mainTexture = tex;
                else
                    mat.color = bg;
                rend.sharedMaterial = mat;
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rend.receiveShadows = false;
                Debug.Log("[DFUQuest3] VRKeyboard MakeKey '" + label + "' special=" + special +
                    " tex=" + (tex != null ? tex.width + "x" + tex.height : "NULL") +
                    " matTex=" + (mat.mainTexture != null ? "set" : "null"));
            }
        }

        // Re-bake letter-key labels to upper/lower case when shift toggles.
        void RefreshLetterLabels()
        {
            for (int i = 0; i < letterKeys.Count; i++)
            {
                var go = letterKeys[i];
                if (go == null) continue;
                string baseLabel = go.name.Substring(4); // "Key_a" -> "a"
                string disp = shift ? baseLabel.ToUpperInvariant() : baseLabel.ToLowerInvariant();
                var rend = go.GetComponent<Renderer>();
                if (rend == null || rend.sharedMaterial == null) continue;
                // Same darker grey as MakeKey — keep shift re-bakes consistent.
                Color bg = new Color(0.3f, 0.3f, 0.4f, 1f);
                var tex = BakeLabelTexture(disp, bg);
                if (tex != null)
                    rend.sharedMaterial.mainTexture = tex;
                else
                    Debug.LogWarning("[DFUQuest3] VRKeyboard RefreshLetterLabels: re-bake null for '" + disp + "'");
            }
        }

        void AnchorInFrontOfHead()
        {
            // Anchor to the camera/head, NOT PlayerObject — in the char-creation screen
            // there is no Player object yet, and gm.PlayerObject throws
            // "GameManager could not find GameObject with tag Player" every frame,
            // which propagated out of Update() and left the board at origin (invisible).
            //
            // CRITICAL: In gameplay the DFU UI overlay quad (VRUIOverlay) sits at
            // cam.forward * 2.0m at eye height. The save-game window is drawn ON that
            // quad. Our keyboard anchors at cam.forward * 1.0m / y-0.65m, which is
            // BELOW the overlay quad. However, DFU's NativePanel components (like the
            // save window's backdrop) are rendered as world-space UI quads by DFU's
            // internal panel system. In gameplay those quads can extend downward far
            // enough to overlap the keyboard, and because they draw later (same queue)
            // they BURY the letter keys. The special keys sit lower / are on a
            // different row and escape the panel edge.
            //
            // FIX: Use the SAME pose source as VRUIOverlay. In gameplay the
            // authoritative head-in-world is the PLAYER OBJECT + eye-height offset
            // (gm.PlayerObject gated on PlayerMotor — PlayerMotor only exists on the
            // real playable player, not the char-creation temp). The raw HMD pose is
            // TRACKING space; converting it via FindFirstObjectByType<XROrigin>()
            // fails in gameplay because that lookup can return a rig that is not at
            // the player (VRRigBootstrap.LateUpdate moves its own rig field, not
            // necessarily the XROrigin instance we find), so head resolves to world
            // origin and the board parks far from the user's face. PlayerObject +
            // yaw is exactly what VRUIOverlay uses and always lands on the player.
            Vector3 headPos;
            Quaternion headRot;

            var gm = DaggerfallWorkshop.Game.GameManager.Instance;
            Transform playerT = null;
            if (gm != null)
            {
                try
                {
                    // PlayerMotor exists ONLY on the real gameplay player (not the
                    // char-creation temp), so this is the reliable gameplay gate.
                    if (gm.PlayerMotor != null && gm.PlayerObject != null)
                        playerT = gm.PlayerObject.transform;
                }
                catch { }
            }

            if (playerT != null)
            {
                // Gameplay: player body at eye height, facing = player yaw.
                headPos = playerT.position + Vector3.up * 1.5f;
                headRot = Quaternion.Euler(0f, playerT.eulerAngles.y, 0f);
            }
            else
            {
                // Menu/char-creation: raw HMD pose is correct (tracking origin ==
                // world origin there). No XROrigin transform needed.
                bool has = false;
                headPos = Vector3.zero; headRot = Quaternion.identity;
                var devs = new System.Collections.Generic.List<UnityEngine.XR.InputDevice>();
                UnityEngine.XR.InputDevices.GetDevices(devs);
                foreach (var d in devs)
                {
                    if ((d.characteristics & UnityEngine.XR.InputDeviceCharacteristics.HeadMounted) != 0 &&
                        d.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out Vector3 hp) &&
                        d.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out Quaternion hr))
                    {
                        headPos = hp; headRot = hr; has = true;
                        break;
                    }
                }
                if (!has)
                {
                    Camera cam = (gm != null && gm.MainCamera != null) ? gm.MainCamera : Camera.main;
                    if (cam == null) return;
                    headPos = cam.transform.position;
                    headRot = cam.transform.rotation;
                }
            }

            // Position the keyboard BELOW the menu panel (which sits ~2m ahead at eye
            // level) so the user looks DOWN at it while typing. Keep YAW-ONLY rotation
            // (facing forward) — a LookRotation tilt made the board edge-on and invisible.
            Vector3 fwd = headRot * Vector3.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 pos = headPos + fwd * 1.0f;
            pos.y = headPos.y - 0.65f; // below eye level (a bit lower)
            Quaternion rot = Quaternion.Euler(0, headRot.eulerAngles.y, 0);
            board.transform.SetPositionAndRotation(pos, rot);

            // One-shot position diagnostic: where is the board vs the player/camera?
            if (!posLogged)
            {
                posLogged = true;
                Camera diagCam = (gm != null && gm.MainCamera != null) ? gm.MainCamera : Camera.main;
                Debug.Log("[DFUQuest3] VRKeyboard anchored: board=" + pos +
                    " player=" + (playerT != null ? playerT.position.ToString() : "null") +
                    " cam=" + (diagCam != null ? diagCam.transform.position.ToString() : "null") +
                    " head=" + headPos + " fwd=" + fwd + " rot=" + rot.eulerAngles);
            }
        }

        // Per-label texture cache so rebuilt boards or repeated characters reuse bakes.
        static readonly System.Collections.Generic.Dictionary<string, Texture2D> labelTexCache
            = new System.Collections.Generic.Dictionary<string, Texture2D>();

        // Bake a key label into a Texture2D: clear to the key's background color,
        // then draw each glyph's quad from the dynamic font atlas using GL
        // immediate mode into a RenderTexture. This path avoids TextMesh and
        // TextGenerator entirely — the NRE source on Unity 6 IL2CPP Android.
        static Texture2D BakeLabelTexture(string label, Color bg)
        {
            if (string.IsNullOrEmpty(label)) return null;
            string cacheKey = label + "|" + bg;
            Texture2D cached;
            if (labelTexCache.TryGetValue(cacheKey, out cached) && cached != null)
                return cached;

            Font f = LoadKeyboardFont();
            if (f == null)
            {
                Debug.LogWarning("[DFUQuest3] VRKeyboard: no font for label '" + label + "'");
                return null;
            }

            try
            {
                const int glyphPx = 64;
                f.RequestCharactersInTexture(label, glyphPx, FontStyle.Normal);
                var atlasMat = f.material;
                if (atlasMat == null || atlasMat.mainTexture == null)
                {
                    Debug.LogWarning("[DFUQuest3] VRKeyboard bake: font atlas null for '" + label + "'");
                    return null;
                }

                // Gather glyph metrics to compute label width for centering.
                var infos = new CharacterInfo[label.Length];
                float totalAdv = 0f;
                int valid = 0;
                for (int i = 0; i < label.Length; i++)
                {
                    if (!f.GetCharacterInfo(label[i], out infos[i], glyphPx, FontStyle.Normal))
                        infos[i] = new CharacterInfo();
                    else
                        valid++;
                    totalAdv += infos[i].advance;
                }
                if (valid == 0)
                {
                    Debug.LogWarning("[DFUQuest3] VRKeyboard bake: no glyph info for '" + label + "'");
                    return null;
                }

                // Render the label into a square RenderTexture (256px for 64px glyphs).
                const int rtSize = 256;
                var rt = RenderTexture.GetTemporary(rtSize, rtSize, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                GL.Clear(true, true, bg);

                GL.PushMatrix();
                GL.LoadPixelMatrix(0, rtSize, 0, rtSize); // bottom-left origin, pixels (matches ReadPixels)
                atlasMat.SetPass(0);

                float penX = (rtSize - totalAdv) * 0.5f;
                float baselineY = rtSize * 0.5f;

                GL.Begin(GL.QUADS);
                GL.Color(Color.white);
                for (int i = 0; i < label.Length; i++)
                {
                    CharacterInfo ci = infos[i];
                    // Unity 6 CharacterInfo: uvBottomLeft/uvTopRight are Vector2 (UV
                    // space, V up). 'bearing' is an int here, so center each glyph on
                    // the pen position instead of using bearing offset.
                    Vector2 uvBL = ci.uvBottomLeft;
                    Vector2 uvTR = ci.uvTopRight;
                    float gw = ci.glyphWidth;
                    float gh = ci.glyphHeight;
                    // Center the glyph at the pen x (single-char keys most common).
                    float gx = penX + (ci.advance - gw) * 0.5f;
                    float gyBottom = baselineY - gh * 0.5f;
                    GL.TexCoord2(uvBL.x, uvTR.y); GL.Vertex3(gx, gyBottom + gh, 0f);
                    GL.TexCoord2(uvTR.x, uvTR.y); GL.Vertex3(gx + gw, gyBottom + gh, 0f);
                    GL.TexCoord2(uvTR.x, uvBL.y); GL.Vertex3(gx + gw, gyBottom, 0f);
                    GL.TexCoord2(uvBL.x, uvBL.y); GL.Vertex3(gx, gyBottom, 0f);
                    penX += ci.advance;
                }
                GL.End();
                GL.PopMatrix();

                var tex = new Texture2D(rtSize, rtSize, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, rtSize, rtSize), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);

                labelTexCache[cacheKey] = tex;
                Debug.Log("[DFUQuest3] VRKeyboard baked label '" + label + "' (" + valid + "/" + label.Length + " glyphs, adv=" + totalAdv + ")");
                return tex;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[DFUQuest3] VRKeyboard bake failed for '" + label + "': " + e + "\n" + e.StackTrace);
                return null;
            }
        }

        const string KeyboardChars = "1234567890qwertyuiopasdfghjklzxcvbnmShiftSpaceBkspEnter";

        // Populate the dynamic-font atlas for every key character, then verify it exists.
        // Returns true if f.material.mainTexture is usable as a glyph atlas.
        static bool EnsureFontAtlas(Font f)
        {
            if (f == null || f.material == null) return false;
            f.RequestCharactersInTexture(KeyboardChars, 64, FontStyle.Normal);
            return f.material.mainTexture != null;
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
                if (f != null)
                {
                    // Ensure the font has a material (stripped builds can drop it).
                    if (f.material == null)
                    {
                        var sh = Shader.Find("GUI/Text Shader");
                        if (sh == null) sh = Shader.Find("Unlit/Texture");
                        if (sh != null) f.material = new Material(sh);
                    }
                    if (!EnsureFontAtlas(f))
                    {
                        Debug.LogWarning("[DFUQuest3] VRKeyboard font '" + tries[i] +
                            "' has no atlas texture after RequestCharactersInTexture (dynamic=" + f.dynamic +
                            ") — trying next font");
                        continue;
                    }
                    cachedFont = f;
                    Debug.Log("[DFUQuest3] VRKeyboard font loaded: " + tries[i] + " mat=" + (f.material != null) +
                        " atlas=" + f.material.mainTexture.width + "x" + f.material.mainTexture.height +
                        " dynamic=" + f.dynamic);
                    return f;
                }
            }
            // Fallback: scan all Resources for any Font.
            var all = Resources.LoadAll<Font>("");
            foreach (var cand in all)
            {
                if (cand == null) continue;
                if (cand.material == null)
                {
                    var sh = Shader.Find("GUI/Text Shader");
                    if (sh == null) sh = Shader.Find("Unlit/Texture");
                    if (sh != null) cand.material = new Material(sh);
                }
                if (!EnsureFontAtlas(cand)) continue;
                cachedFont = cand;
                Debug.Log("[DFUQuest3] VRKeyboard font fallback: " + cand.name);
                return cachedFont;
            }
            Debug.LogWarning("[DFUQuest3] VRKeyboard: no usable font found in Resources");
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
                case "Shift": shift = !shift; RefreshLetterLabels(); break;
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
