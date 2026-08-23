// DFU Quest3 VR — additive OVRManager bootstrap.
// OVRInput needs an initialized OVRManager/OVRPlugin to read controller input over the
// OpenXR backend (Meta's AndroidOpenXR/OVRPlugin.aar). DFUVR/flat2VR projects have an
// OVRCameraRig/OVRManager in-scene; our runtime-built rig doesn't. This self-wires an
// OVRManager so OVRInput can read the real controller trigger on the OpenXR build.
// Purely additive — new file, touches no existing VR script.

using UnityEngine;

namespace DFUQuest3
{
    public class AdditiveOVRManager : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Autostart()
        {
            var existing = FindObjectOfType<OVRManager>();
            if (existing != null) return;
            var go = new GameObject("DFUQuest3 AdditiveOVRManager");
            go.AddComponent<OVRManager>();
            DontDestroyOnLoad(go);
            Debug.Log("[DFUQuest3] AdditiveOVRManager wired");
        }
    }
}
