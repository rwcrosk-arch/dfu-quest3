// DFU Quest3 VR — OVRInput helper wrapper.
// Thin, reliable access to Meta's OVRInput for the Quest Touch controllers.
// OVRInput (from com.meta.xr.sdk.core) is guaranteed to work on Quest hardware,
// unlike Input System binding paths which depend on layout resolution.

using UnityEngine;

namespace Oculus
{
    public struct ControllerPose
    {
        public Vector3 position;
        public Quaternion rotation;
        public bool triggerDown;
        public Vector2 thumbstick;
    }

    public static class OvrInputHelpers
    {
        /// <summary>Reads the right Touch controller pose + inputs.</summary>
        public static ControllerPose RTouch
        {
            get
            {
                var c = OVRInput.Controller.RTouch;
                var pose = new ControllerPose
                {
                    position = OVRInput.GetLocalControllerPosition(c),
                    rotation = OVRInput.GetLocalControllerRotation(c),
                    triggerDown = OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, c),
                    thumbstick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, c)
                };
                return pose;
            }
        }

        /// <summary>Reads the left-touch controller pose + inputs.</summary>
        public static ControllerPose LTouch
        {
            get
            {
                var c = OVRInput.Controller.LTouch;
                return new ControllerPose
                {
                    position = OVRInput.GetLocalControllerPosition(c),
                    rotation = OVRInput.GetLocalControllerRotation(c),
                    triggerDown = OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, c),
                    thumbstick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, c)
                };
            }
        }
    }
}
