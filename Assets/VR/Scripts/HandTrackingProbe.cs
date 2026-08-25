// DFU Quest3 VR — additive hand-tracking probe.
// Hand devices DO surface through Unity's input on our OpenXR build (unlike controllers):
//   "Hand Interaction Poses OpenXR" and "Palm Pose Interaction OpenXR" appear in the legacy
//   device list. This probe reads hand pose + pinch from them. Purely additive — does NOT
//   touch VRUIOverlay.cs.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace DFUQuest3
{
    public class HandTrackingProbe : MonoBehaviour
    {
        float timer = 2f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Autostart()
        {
            var go = new GameObject("DFUQuest3 HandTrackingProbe");
            go.AddComponent<HandTrackingProbe>();
            DontDestroyOnLoad(go);
        }

        void Update()
        {
            timer -= Time.unscaledDeltaTime;
            if (timer > 0f) return;
            timer = 2f;

            var devices = new List<InputDevice>();
            InputDevices.GetDevices(devices);
            foreach (var d in devices)
            {
                bool isHand = (d.characteristics & InputDeviceCharacteristics.HandTracking) != 0;
                if (!isHand) continue;

                // Hand pose
                bool hasPos = d.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 pos);
                bool hasRot = d.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rot);

                // Pinch / select gesture (the "click")
                bool hasSelect = d.TryGetFeatureValue(CommonUsages.select, out bool select);
                bool hasTrigger = d.TryGetFeatureValue(CommonUsages.trigger, out float trig);

                Debug.Log($"[DFUQuest3] HAND device '{d.name}' pos={hasPos} rot={hasRot} select={hasSelect} trig={hasTrigger} " +
                          $"posVal={pos} rotVal={rot.eulerAngles} selectVal={select} trigVal={trig}");
            }
        }
    }
}
