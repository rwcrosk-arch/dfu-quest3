// DFU Quest3 VR — reads the native OpenXR API layer's trigger state file.
// The native layer (libdfutrigger_layer.so) reads the REAL physical controller trigger at
// the OpenXR runtime level and writes it to:
//   /data/data/com.dfworkshop.dfuquest3/files/trigger_state.txt
// Unity 6 + OpenXR can't surface the trigger to managed input, so this additive component
// polls that file and, on a rising edge, sets InputManager.vrClickQueued (the exact flag the
// overlay's click path consumes). Purely additive — does NOT touch VRUIOverlay.cs.

using System.IO;
using UnityEngine;
using DaggerfallWorkshop.Game;

namespace DFUQuest3
{
    public class NativeTriggerReader : MonoBehaviour
    {
        string triggerPath;
        bool lastPressed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Autostart()
        {
            var go = new GameObject("DFUQuest3 NativeTriggerReader");
            go.AddComponent<NativeTriggerReader>();
            DontDestroyOnLoad(go);
        }

        void Awake()
        {
            // App-private files dir (scoped storage safe)
            triggerPath = Path.Combine(Application.persistentDataPath, "trigger_state.txt");
            Debug.Log("[DFUQuest3] NativeTriggerReader watching " + triggerPath);
        }

        void Update()
        {
            if (string.IsNullOrEmpty(triggerPath) || !File.Exists(triggerPath)) return;
            bool pressed = false;
            try
            {
                string line = File.ReadAllText(triggerPath).Trim();
                pressed = line == "1";
            }
            catch { return; }

            if (pressed && !lastPressed)
            {
                var im = InputManager.Instance;
                if (im != null)
                {
                    im.vrClickQueued = true;
                    Debug.Log("[DFUQuest3] NativeTriggerReader: trigger pressed -> click queued");
                }
            }
            lastPressed = pressed;
        }
    }
}
