// DFU Quest3 VR — on-device MCP pose bridge.
// Unity 6 + OpenXR reports the Quest controller pose as ZEROS to Unity's app-side input
// backend. But the on-device Meta XR Operator MCP server (Development build, reachable at
// http://localhost:8720) reads the real pose via its `openxr_get_controller_pose` tool.
// This client polls that tool over SSE/JSON-RPC and exposes the REAL controller pose so the
// pointer can use it — bypassing Unity's broken controller backend entirely.
//
// MCP protocol: GET /sse returns an `event: endpoint / data: /message?session_id=<id>`
// stream. We POST JSON-RPC to /message?session_id=<id> and read the SSE response.
// Only tools/call for openxr_get_controller_pose is needed here.

using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace DFUQuest3
{
    public class MCPPoseBridge : MonoBehaviour
    {
        [Tooltip("On-device MCP server endpoint (adb forward tcp:8720 → device:8720).")]
        public string mcpUrl = "http://localhost:8720";

        public bool controllerValid;
        public Vector3 controllerPosition;
        public Quaternion controllerRotation;
        public bool pollEnabled = true;

        const float pollInterval = 0.05f; // 20 Hz

        string sessionId;
        string messageEndpoint;

        void Start()
        {
            StartCoroutine(ConnectAndPoll());
        }

        IEnumerator ConnectAndPoll()
        {
            // 1) Open the SSE stream to discover the message endpoint.
            yield return StartCoroutine(DiscoverEndpoint());
            if (string.IsNullOrEmpty(messageEndpoint))
            {
                Debug.LogWarning("[DFUQuest3] MCP pose bridge: could not reach SSE endpoint.");
                yield break;
            }

            // 2) Poll the controller pose.
            while (pollEnabled)
            {
                yield return StartCoroutine(GetControllerPose());
                yield return new WaitForSeconds(pollInterval);
            }
        }

        IEnumerator DiscoverEndpoint()
        {
            using (var uwr = UnityWebRequest.Get(mcpUrl + "/sse"))
            {
                // Must hold the stream open to get the endpoint event. We read a chunk.
                yield return uwr.SendWebRequest();
                if (uwr.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning("[DFUQuest3] MCP SSE connect failed: " + uwr.error);
                    yield break;
                }
                // The /sse response body contains: event: endpoint / data: /message?session_id=...
                var text = uwr.downloadHandler.text;
                int idx = text.IndexOf("data: ");
                if (idx >= 0)
                {
                    var endpoint = text.Substring(idx + 6).Trim();
                    messageEndpoint = mcpUrl + endpoint;
                    Debug.Log("[DFUQuest3] MCP pose bridge endpoint: " + messageEndpoint);
                }
            }
        }

        IEnumerator GetControllerPose()
        {
            var json = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"openxr_get_controller_pose\",\"arguments\":{\"hand\":\"right\"}}}";
            using (var uwr = new UnityWebRequest(messageEndpoint, "POST"))
            {
                var body = new System.Text.UTF8Encoding().GetBytes(json);
                uwr.uploadHandler = new UploadHandlerRaw(body);
                uwr.downloadHandler = new DownloadHandlerBuffer();
                uwr.SetRequestHeader("Content-Type", "application/json");
                uwr.SetRequestHeader("Accept", "application/json, text/event-stream");
                yield return uwr.SendWebRequest();

                if (uwr.result == UnityWebRequest.Result.Success)
                {
                    ParsePoseResponse(uwr.downloadHandler.text);
                }
            }
        }

        void ParsePoseResponse(string responseText)
        {
            // The JSON-RPC result is inside an SSE `data: {json}` line.
            var dataIdx = responseText.IndexOf("data: ");
            if (dataIdx < 0) return;
            var json = responseText.Substring(dataIdx + 6).Trim();
            // Result shape: {"result":{"content":[{"type":"text","text":"{...pose...}"}]}}
            var textStart = json.IndexOf("\"text\":\"");
            if (textStart < 0) return;
            textStart += 8;
            var textEnd = json.IndexOf("\"", textStart);
            if (textEnd < 0) return;
            var poseJson = json.Substring(textStart, textEnd - textStart)
                .Replace("\\\"", "\"").Replace("\\/", "/");
            ParsePoseJson(poseJson);
        }

        void ParsePoseJson(string poseJson)
        {
            // Expect: {"pose":{"position":[x,y,z],"orientation":[x,y,z,w]}, "is_active":1, "flags":{...}}
            var posStart = poseJson.IndexOf("\"position\":[");
            var oriStart = poseJson.IndexOf("\"orientation\":[");
            var activeIdx = poseJson.IndexOf("\"is_active\":");
            if (posStart < 0 || oriStart < 0) return;

            posStart += 12;
            var posEnd = poseJson.IndexOf("]", posStart);
            var posStr = poseJson.Substring(posStart, posEnd - posStart).Split(',');
            oriStart += 15;
            var oriEnd = poseJson.IndexOf("]", oriStart);
            var oriStr = poseJson.Substring(oriStart, oriEnd - oriStart).Split(',');

            if (posStr.Length >= 3 && oriStr.Length >= 4)
            {
                float.TryParse(posStr[0], out float px);
                float.TryParse(posStr[1], out float py);
                float.TryParse(posStr[2], out float pz);
                float.TryParse(oriStr[0], out float qx);
                float.TryParse(oriStr[1], out float qy);
                float.TryParse(oriStr[2], out float qz);
                float.TryParse(oriStr[3], out float qw);

                controllerPosition = new Vector3(px, py, pz);
                controllerRotation = new Quaternion(qx, qy, qz, qw);
                controllerValid = true;
                // is_active
                if (activeIdx >= 0)
                {
                    var actStr = poseJson.Substring(activeIdx + 12, 1);
                    controllerValid = actStr == "1";
                }
            }
        }
    }
}
