// DFU Quest3 VR — native OpenXR trigger reader (pure C# via Unity's OpenXRFeature handles).
//
// WHY: Unity 6 + OpenXR does NOT surface the physical controller trigger to managed input
// (InputSystem/InputDevices show only HeadTrackingOpenXR; OVRInput is silent under OpenXR).
// But OpenXRFeature hands us Unity's native xrInstance/xrSession + xrGetInstanceProcAddr,
// so we create our OWN action bound to the right-controller trigger and read its boolean
// state directly at the OpenXR layer — joining Unity's session, no NDK plugin needed.
//
// A separate self-wiring MonoBehaviour (TriggerPoller) calls PollTrigger() each frame and,
// on a rising edge, sets InputManager.vrClickQueued (the flag the overlay's click path uses).

using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR.OpenXR.Features;
using DaggerfallWorkshop.Game;

namespace DFUQuest3
{
#if UNITY_EDITOR
    [UnityEditor.XR.OpenXR.Features.OpenXRFeature(
        UiName = "DFU Quest3 Native Trigger Reader",
        BuildTargetGroups = new[] { UnityEditor.BuildTargetGroup.Android },
        Company = "DFU Quest3",
        Desc = "Reads the physical Quest controller trigger via the OpenXR action system (Unity 6 + OpenXR surfaces no controller input device to managed code otherwise).",
        Version = "0.0.1",
        FeatureId = TriggerOpenXRFeature.FeatureIdInternal)]
#endif
    public class TriggerOpenXRFeature : OpenXRFeature
    {
        const int XR_MAX_ACTION_SET_NAME = 64;
        const int XR_MAX_LOCALIZED_ACTION_SET_NAME = 128;
        const int XR_MAX_ACTION_NAME = 64;
        const int XR_MAX_LOCALIZED_ACTION_NAME = 128;
        const int XR_MAX_SUBACTION_PATHS = 32;

        const int XR_TYPE_ACTION_SET_CREATE_INFO = 28;
        const int XR_TYPE_ACTION_CREATE_INFO = 29;
        const int XR_TYPE_ACTION_STATE_GET_INFO = 58;
        const int XR_TYPE_ACTIONS_SYNC_INFO = 61;
        const int XR_TYPE_ACTION_STATE_BOOLEAN = 23;
        const int XR_TYPE_INTERACTION_PROFILE_SUGGESTED_BINDING = 34;
        const int XR_ACTION_TYPE_BOOLEAN_INPUT = 1;

        ulong instanceHandle;
        ulong sessionHandle;
        ulong actionSet;
        ulong actionTrigger;
        bool created;
        bool bound;

        public const string FeatureIdInternal = "com.dfworkshop.dfuquest3.trigger";

        // The feature is a ScriptableObject asset, not a scene GameObject, so the poller
        // can't use FindObjectOfType. Register a static instance in OnInstanceCreate.
        public static TriggerOpenXRFeature Instance;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int PFN_xrCreateActionSet(ulong instance, IntPtr createInfo, out ulong actionSet);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int PFN_xrCreateAction(ulong actionSet, IntPtr createInfo, out ulong action);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int PFN_xrGetActionStateBoolean(ulong session, IntPtr getInfo, ref XrActionStateBoolean state);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int PFN_xrSyncActions(ulong session, IntPtr syncInfo);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int PFN_xrStringToPath(ulong instance, string path, out ulong pathId);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int PFN_xrSuggestInteractionProfileBindings(ulong instance, IntPtr suggestedBindings);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate IntPtr PFN_xrGetInstanceProcAddr(ulong instance, string name);

        PFN_xrCreateActionSet createActionSet;
        PFN_xrCreateAction createAction;
        PFN_xrGetActionStateBoolean getActionStateBoolean;
        PFN_xrSyncActions syncActions;
        PFN_xrStringToPath stringToPath;
        PFN_xrSuggestInteractionProfileBindings suggestBindings;

        [StructLayout(LayoutKind.Sequential)]
        struct XrActionSetCreateInfo
        {
            public int type;
            public IntPtr next;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = XR_MAX_ACTION_SET_NAME)]
            public byte[] actionSetName;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = XR_MAX_LOCALIZED_ACTION_SET_NAME)]
            public byte[] localizedActionSetName;
            public int priority;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct XrActionCreateInfo
        {
            public int type;
            public IntPtr next;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = XR_MAX_ACTION_NAME)]
            public byte[] actionName;
            public int actionType;
            public int countSubactionPaths;
            public IntPtr subactionPaths;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = XR_MAX_LOCALIZED_ACTION_NAME)]
            public byte[] localizedActionName;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct XrActionStateGetInfo
        {
            public int type;
            public IntPtr next;
            public ulong action;
            public ulong subactionPath;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct XrActiveActionSet
        {
            public ulong actionSet;
            public ulong subactionPath;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct XrActionsSyncInfo
        {
            public int type;
            public IntPtr next;
            public int countActiveActionSets;
            public IntPtr activeActionSets;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct XrActionStateBoolean
        {
            public int type;
            public IntPtr next;
            public int isActive;
            public int currentState;
            public long changedSinceLastSync;
            public long lastChangeTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct XrActionSuggestedBinding
        {
            public ulong action;
            public ulong binding;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct XrInteractionProfileSuggestedBinding
        {
            public int type;
            public IntPtr next;
            public ulong interactionProfile;
            public int countSuggestedBindings;
            public IntPtr suggestedBindings;
        }

        protected override bool OnInstanceCreate(ulong xrInstance)
        {
            instanceHandle = xrInstance;
            Instance = this;
            try
            {
                var loader = Marshal.GetDelegateForFunctionPointer<PFN_xrGetInstanceProcAddr>(xrGetInstanceProcAddr);
                createActionSet = GetProc<PFN_xrCreateActionSet>(loader, xrInstance, "xrCreateActionSet");
                createAction = GetProc<PFN_xrCreateAction>(loader, xrInstance, "xrCreateAction");
                getActionStateBoolean = GetProc<PFN_xrGetActionStateBoolean>(loader, xrInstance, "xrGetActionStateBoolean");
                syncActions = GetProc<PFN_xrSyncActions>(loader, xrInstance, "xrSyncActions");
                stringToPath = GetProc<PFN_xrStringToPath>(loader, xrInstance, "xrStringToPath");
                suggestBindings = GetProc<PFN_xrSuggestInteractionProfileBindings>(loader, xrInstance, "xrSuggestInteractionProfileBindings");
                Debug.Log("[DFUQuest3] TriggerOpenXRFeature: proc addrs resolved");
            }
            catch (Exception e) { Debug.LogError("[DFUQuest3] TriggerOpenXRFeature OnInstanceCreate: " + e); }
            return true;
        }

        static T GetProc<T>(PFN_xrGetInstanceProcAddr loader, ulong instance, string name) where T : Delegate
        {
            IntPtr fn = loader(instance, name);
            return fn == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<T>(fn);
        }

        protected override void OnSessionCreate(ulong xrSession)
        {
            sessionHandle = xrSession;
            Debug.Log("[DFUQuest3] TriggerOpenXRFeature OnSessionCreate session=" + xrSession + " created=" + created);
            CreateActions();
        }

        void CreateActions()
        {
            if (created || createActionSet == null)
            {
                Debug.Log("[DFUQuest3] TriggerOpenXRFeature CreateActions skip: created=" + created + " createActionSet=" + (createActionSet != null));
                return;
            }
            try
            {
                Debug.Log("[DFUQuest3] TriggerOpenXRFeature CreateActions start");
                var setInfo = new XrActionSetCreateInfo
                {
                    type = XR_TYPE_ACTION_SET_CREATE_INFO,
                    actionSetName = StringBytes("dfu_trigger"),
                    localizedActionSetName = StringBytes("DFU Trigger")
                };
                ulong set = 0;
                int rc = createActionSet(instanceHandle, StructPtr(setInfo), out set);
                Debug.Log("[DFUQuest3] xrCreateActionSet rc=" + rc);
                if (rc != 0) { Debug.LogError("[DFUQuest3] xrCreateActionSet failed rc=" + rc); return; }
                actionSet = set;

                var actInfo = new XrActionCreateInfo
                {
                    type = XR_TYPE_ACTION_CREATE_INFO,
                    actionName = StringBytes("trigger"),
                    localizedActionName = StringBytes("Trigger"),
                    actionType = XR_ACTION_TYPE_BOOLEAN_INPUT
                };
                rc = createAction(actionSet, StructPtr(actInfo), out actionTrigger);
                Debug.Log("[DFUQuest3] xrCreateAction rc=" + rc);
                if (rc != 0) { Debug.LogError("[DFUQuest3] xrCreateAction failed rc=" + rc); return; }

                created = true;
                BindTriggerToInteractionProfile();
                Debug.Log("[DFUQuest3] TriggerOpenXRFeature: action set + trigger created");
            }
            catch (Exception e) { Debug.LogError("[DFUQuest3] TriggerOpenXRFeature CreateActions EX: " + e); }
        }

        void BindTriggerToInteractionProfile()
        {
            try
            {
                // Bind the trigger action to the Oculus Touch controller right trigger path.
                ulong profile = 0, triggerPath = 0;
                stringToPath(instanceHandle, "/interaction_profiles/oculus/touch_controller", out profile);
                stringToPath(instanceHandle, "/user/hand/right/input/trigger", out triggerPath);

                var binding = new XrActionSuggestedBinding { action = actionTrigger, binding = triggerPath };
                var bind = new XrInteractionProfileSuggestedBinding
                {
                    type = XR_TYPE_INTERACTION_PROFILE_SUGGESTED_BINDING,
                    interactionProfile = profile,
                    countSuggestedBindings = 1,
                    suggestedBindings = StructPtr(binding)
                };
                int rc = suggestBindings(instanceHandle, StructPtr(bind));
                Debug.Log("[DFUQuest3] TriggerOpenXRFeature: suggestBindings rc=" + rc);
            }
            catch (Exception e) { Debug.LogError("[DFUQuest3] TriggerOpenXRFeature Bind: " + e); }
        }

        static byte[] StringBytes(string s)
        {
            var bytes = new byte[s.Length + 1];
            System.Text.Encoding.ASCII.GetBytes(s, 0, s.Length, bytes, 0);
            return bytes;
        }

        static IntPtr StructPtr(object s)
        {
            int size = Marshal.SizeOf(s);
            IntPtr p = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(s, p, false);
            return p;
        }

        // Returns true if the physical right trigger is currently pressed. Called by the
        // poller every frame.
        public bool PollTrigger()
        {
            if (!created || sessionHandle == 0) return false;
            try
            {
                var active = new XrActiveActionSet { actionSet = actionSet };
                IntPtr activePtr = StructPtr(active);
                var syncInfo = new XrActionsSyncInfo
                {
                    type = XR_TYPE_ACTIONS_SYNC_INFO,
                    countActiveActionSets = 1,
                    activeActionSets = activePtr
                };
                IntPtr syncPtr = StructPtr(syncInfo);
                int rc = syncActions(sessionHandle, syncPtr);
                Marshal.FreeHGlobal(syncPtr);
                Marshal.FreeHGlobal(activePtr);
                if (rc != 0) return false;

                var getInfo = new XrActionStateGetInfo
                {
                    type = XR_TYPE_ACTION_STATE_GET_INFO,
                    action = actionTrigger
                };
                var state = new XrActionStateBoolean { type = XR_TYPE_ACTION_STATE_BOOLEAN };
                IntPtr getPtr = StructPtr(getInfo);
                rc = getActionStateBoolean(sessionHandle, getPtr, ref state);
                Marshal.FreeHGlobal(getPtr);
                if (rc != 0) return false;

                return state.isActive != 0 && state.currentState != 0;
            }
            catch
            {
                return false;
            }
        }
    }

    // Self-wiring poller: each frame, if the feature exists, read PollTrigger and queue a
    // click on a rising edge.
    public class TriggerPollMono : MonoBehaviour
    {
        bool lastTrigger;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Autostart()
        {
            var go = new GameObject("DFUQuest3 TriggerPollMono");
            go.AddComponent<TriggerPollMono>();
            DontDestroyOnLoad(go);
        }

        void Update()
        {
            var feat = TriggerOpenXRFeature.Instance;
            if (feat == null) return;
            bool now = feat.PollTrigger();
            if (now && !lastTrigger)
            {
                var im = InputManager.Instance;
                if (im != null)
                {
                    im.vrClickQueued = true;
                    Debug.Log("[DFUQuest3] TriggerPollMono: trigger -> click queued");
                }
            }
            lastTrigger = now;
        }
    }
}
