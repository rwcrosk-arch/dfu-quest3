using UnityEngine;

namespace DaggerfallWorkshop.Game
{
    /// <summary>
    /// Additive VR helper: syncs InputManager.Instance.vrMousePosition from the
    /// DaggerfallUI.CustomMousePosition that VRUIOverlay writes each frame, so
    /// DFU's non-overlay UI components (or components whose CustomMousePosition
    /// wasn't propagated) still see the VR cursor position.
    /// Safe with VRUIOverlay.cs because it never edits that file.
    /// </summary>
    public class VRUICursorSync : MonoBehaviour
    {
        // Self-wire: attach to the DaggerfallUI GameObject at runtime so the cursor sync
        // runs without any scene/overlay edits.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Autostart()
        {
            var dfUI = Object.FindObjectOfType<DaggerfallUI>();
            if (dfUI == null) return;
            if (dfUI.GetComponent<VRUICursorSync>() == null)
                dfUI.gameObject.AddComponent<VRUICursorSync>();
            Debug.Log("[DFUQuest3] VRUICursorSync auto-wired to DaggerfallUI");
        }

        void LateUpdate()
        {
            var dfUI = GetComponent<DaggerfallUI>();
            if (dfUI == null || InputManager.Instance == null)
                return;

            var pos = dfUI.CustomMousePosition;
            if (pos.HasValue)
            {
                // CustomMousePosition is top-left origin; InputManager.MousePosition
                // is Unity-style bottom-left origin. Convert Y here.
                InputManager.Instance.vrMousePosition = new Vector3(
                    pos.Value.x,
                    Screen.height - pos.Value.y,
                    0f);
            }
            else
            {
                InputManager.Instance.vrMousePosition = null;
            }

            // DIAGNOSTIC: log the New Game button's Rectangle vs the cursor, to see the
            // coordinate-space mismatch that's blocking the click.
            diagTimer -= Time.unscaledDeltaTime;
            if (diagTimer <= 0f)
            {
                diagTimer = 2f;
                var startWin = Object.FindObjectOfType<DaggerfallWorkshop.Game.UserInterfaceWindows.DaggerfallStartWindow>();
                if (startWin != null)
                {
                    // Find the new game button via reflection-free: it's a private field, so
                    // log the window's native panel rect + the cursor instead.
                    var panel = startWin.NativePanel;
                    Debug.Log($"[DFUQuest3] CURSORDIAG screen={Screen.width}x{Screen.height} cursor={pos} " +
                              $"panelRect={panel.Rectangle} panelScale={panel.LocalScale}");
                }
            }
        }

        float diagTimer = 2f;
    }
}
