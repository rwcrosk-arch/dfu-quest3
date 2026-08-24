// DFUQuest3 native OpenXR API layer — reads the REAL controller trigger at the OpenXR
// runtime level and writes it to a shared file for Unity to read.
//
// Professional flat2VR-style mechanism (same pattern as Meta's XrApiLayer_METAX_operator):
// a native OpenXR API layer that runs BELOW Unity's managed input stack, so it reads the
// physical controller even though Unity 6 + OpenXR won't surface it to game code.
//
// It chains to the next layer/runtime, creates its OWN action set + trigger action bound
// to the meta touch_controller_plus profile, and each frame syncs + reads the action,
// writing currentState to a file Unity's C# side polls.

#include <android/log.h>
#include <stdio.h>
#include <string.h>
#include <atomic>

#define XR_USE_PLATFORM_ANDROID 1
#include "openxr_loader_negotiation.h"

#define LOGI(...) __android_log_print(ANDROID_LOG_INFO, "DFUApiLayer", __VA_ARGS__)
#define LOGE(...) __android_log_print(ANDROID_LOG_ERROR, "DFUApiLayer", __VA_ARGS__)

// ---------------------------------------------------------------------------
// global state
// ---------------------------------------------------------------------------
static PFN_xrGetInstanceProcAddr    g_nextGetProcAddr = nullptr;
static PFN_xrCreateApiLayerInstance g_nextCreateInstance = nullptr;
static XrInstance g_instance = XR_NULL_HANDLE;
static XrSession  g_session  = XR_NULL_HANDLE;

static std::atomic<XrActionSet> g_actionSet{XR_NULL_HANDLE};
static std::atomic<XrAction>    g_triggerAction{XR_NULL_HANDLE};
static std::atomic<int>         g_triggerPressed{0};
static std::atomic<bool>        g_ready{false};

// runtime function pointers (resolved via the chained getInstanceProcAddr)
static PFN_xrCreateActionSet                   real_CreateActionSet = nullptr;
static PFN_xrCreateAction                      real_CreateAction = nullptr;
static PFN_xrStringToPath                      real_StringToPath = nullptr;
static PFN_xrSuggestInteractionProfileBindings real_SuggestBindings = nullptr;
static PFN_xrAttachSessionActionSets           real_AttachSessionActionSets = nullptr;
static PFN_xrSyncActions                       real_SyncActions = nullptr;
static PFN_xrGetActionStateBoolean             real_GetActionStateBoolean = nullptr;
static PFN_xrBeginSession                      real_BeginSession = nullptr;

#define TRIGGER_FILE "/data/data/com.dfworkshop.dfuquest3/files/trigger_state.txt"

static void write_trigger_file(int val) {
    FILE* f = fopen(TRIGGER_FILE, "w");
    if (f) { fprintf(f, "%d\n", val); fclose(f); }
}

// ---------------------------------------------------------------------------
// set up our action set + trigger action, bind and attach
// ---------------------------------------------------------------------------
static void setupTrigger(XrSession session)
{
    if (!real_CreateActionSet) {
        g_nextGetProcAddr(g_instance, "xrCreateActionSet", (PFN_xrVoidFunction*)&real_CreateActionSet);
        g_nextGetProcAddr(g_instance, "xrCreateAction", (PFN_xrVoidFunction*)&real_CreateAction);
        g_nextGetProcAddr(g_instance, "xrStringToPath", (PFN_xrVoidFunction*)&real_StringToPath);
        g_nextGetProcAddr(g_instance, "xrSuggestInteractionProfileBindings", (PFN_xrVoidFunction*)&real_SuggestBindings);
        g_nextGetProcAddr(g_instance, "xrAttachSessionActionSets", (PFN_xrVoidFunction*)&real_AttachSessionActionSets);
        g_nextGetProcAddr(g_instance, "xrSyncActions", (PFN_xrVoidFunction*)&real_SyncActions);
        g_nextGetProcAddr(g_instance, "xrGetActionStateBoolean", (PFN_xrVoidFunction*)&real_GetActionStateBoolean);
        if (!real_CreateActionSet) { LOGE("could not resolve runtime action fns"); return; }
    }

    XrActionSet set = XR_NULL_HANDLE;
    XrActionSetCreateInfo setInfo = {};
    setInfo.type = XR_TYPE_ACTION_SET_CREATE_INFO;
    strncpy(setInfo.actionSetName, "dfu_trigger", XR_MAX_ACTION_SET_NAME_SIZE);
    strncpy(setInfo.localizedActionSetName, "DFU Trigger", XR_MAX_LOCALIZED_ACTION_SET_NAME_SIZE);
    if (real_CreateActionSet(g_instance, &setInfo, &set) != XR_SUCCESS) { LOGE("action set create failed"); return; }

    XrAction act = XR_NULL_HANDLE;
    XrActionCreateInfo actInfo = {};
    actInfo.type = XR_TYPE_ACTION_CREATE_INFO;
    strncpy(actInfo.actionName, "trigger", XR_MAX_ACTION_NAME_SIZE);
    strncpy(actInfo.localizedActionName, "Trigger", XR_MAX_LOCALIZED_ACTION_NAME_SIZE);
    actInfo.actionType = XR_ACTION_TYPE_BOOLEAN_INPUT;
    if (real_CreateAction(set, &actInfo, &act) != XR_SUCCESS) { LOGE("action create failed"); return; }

    XrPath profile = XR_NULL_PATH, trigPath = XR_NULL_PATH;
    real_StringToPath(g_instance, "/interaction_profiles/meta/touch_controller_plus", &profile);
    real_StringToPath(g_instance, "/user/hand/right/input/trigger", &trigPath);
    if (profile != XR_NULL_PATH) {
        XrActionSuggestedBinding b = { act, trigPath };
        XrInteractionProfileSuggestedBinding binfo = {};
        binfo.type = XR_TYPE_INTERACTION_PROFILE_SUGGESTED_BINDING;
        binfo.interactionProfile = profile;
        binfo.countSuggestedBindings = 1;
        binfo.suggestedBindings = &b;
        real_SuggestBindings(g_instance, &binfo);
    }

    g_actionSet = set;
    g_triggerAction = act;

    XrSessionActionSetsAttachInfo ainfo = {};
    ainfo.type = XR_TYPE_SESSION_ACTION_SETS_ATTACH_INFO;
    ainfo.countActionSets = 1;
    ainfo.actionSets = &set;
    real_AttachSessionActionSets(session, &ainfo);

    g_ready = true;
    LOGI("native trigger action set + trigger bound and attached");
}

// ---------------------------------------------------------------------------
// wrapped xrSyncActions: poll our trigger each frame, then chain
// ---------------------------------------------------------------------------
static XrResult wrap_xrSyncActions(XrSession session, const XrActionsSyncInfo* syncInfo)
{
    if (g_ready && g_actionSet.load() != XR_NULL_HANDLE && g_triggerAction.load() != XR_NULL_HANDLE) {
        XrActiveActionSet act = { g_actionSet.load(), XR_NULL_PATH };
        XrActionsSyncInfo ourSync = {};
        ourSync.type = XR_TYPE_ACTIONS_SYNC_INFO;
        ourSync.countActiveActionSets = 1;
        ourSync.activeActionSets = &act;
        real_SyncActions(session, &ourSync);

        XrActionStateGetInfo getInfo = {};
        getInfo.type = XR_TYPE_ACTION_STATE_GET_INFO;
        getInfo.action = g_triggerAction.load();
        XrActionStateBoolean state = {};
        if (real_GetActionStateBoolean(session, &getInfo, &state) == XR_SUCCESS) {
            int pressed = (state.isActive != 0 && state.currentState != 0) ? 1 : 0;
            g_triggerPressed = pressed;
            write_trigger_file(pressed);
        }
    }
    return real_SyncActions(session, syncInfo);
}

// ---------------------------------------------------------------------------
// wrapped xrBeginSession: the safe time to create + attach our action set
// ---------------------------------------------------------------------------
static XrResult wrap_xrBeginSession(XrSession session, const XrSessionBeginInfo* beginInfo)
{
    if (!g_ready && g_session == session) setupTrigger(session);
    return real_BeginSession(session, beginInfo);
}

// ---------------------------------------------------------------------------
// getInstanceProcAddr intercept: wrap the commands we care about
// ---------------------------------------------------------------------------
static XrResult getInstanceProcAddr(XrInstance instance, const char* name, PFN_xrVoidFunction* function)
{
    XrResult res = g_nextGetProcAddr(instance, name, function);
    if (XR_FAILED(res)) return res;
    if (strcmp(name, "xrSyncActions") == 0) *function = (PFN_xrVoidFunction)&wrap_xrSyncActions;
    else if (strcmp(name, "xrBeginSession") == 0) *function = (PFN_xrVoidFunction)&wrap_xrBeginSession;
    return res;
}

// ---------------------------------------------------------------------------
// createApiLayerInstance intercept: capture instance handle, chain to next
// ---------------------------------------------------------------------------
static XrResult createApiLayerInstance(const XrInstanceCreateInfo* info,
                                       const XrApiLayerCreateInfo* layerInfo,
                                       XrInstance* instance)
{
    XrResult res = g_nextCreateInstance(info, layerInfo, instance);
    if (XR_SUCCEEDED(res) && *instance != XR_NULL_HANDLE) g_instance = *instance;
    return res;
}

// ---------------------------------------------------------------------------
// layer negotiation entry point (loader calls this) — must be exported
// ---------------------------------------------------------------------------
extern "C" XrResult __attribute__((visibility("default"))) xrNegotiateLoaderApiLayerInterface(
    const XrNegotiateLoaderInfo* loaderInfo,
    const char* layerName,
    XrNegotiateApiLayerRequest* apiLayerRequest)
{
    if (loaderInfo == nullptr || apiLayerRequest == nullptr) return XR_ERROR_VALIDATION_FAILURE;
    if (strcmp(layerName, "XR_APILAYER_DFU_dfu_trigger_layer") != 0) return XR_ERROR_API_LAYER_NOT_PRESENT;
    if (loaderInfo->structType != XR_LOADER_INTERFACE_STRUCT_LOADER_INFO) return XR_ERROR_VALIDATION_FAILURE;
    if (loaderInfo->structVersion > 1) return XR_ERROR_INITIALIZATION_FAILED;

    apiLayerRequest->structType = XR_LOADER_INTERFACE_STRUCT_API_LAYER_REQUEST;
    apiLayerRequest->structVersion = 1;
    apiLayerRequest->structSize = sizeof(XrNegotiateApiLayerRequest);
    apiLayerRequest->layerInterfaceVersion = 1;
    apiLayerRequest->layerApiVersion = XR_MAKE_VERSION(1, 0, 0);
    apiLayerRequest->getInstanceProcAddr = &getInstanceProcAddr;
    apiLayerRequest->createApiLayerInstance = &createApiLayerInstance;
    return XR_SUCCESS;
}
