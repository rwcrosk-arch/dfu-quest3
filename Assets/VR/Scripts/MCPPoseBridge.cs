// DFU Quest3 VR — on-device MCP pose bridge (hardened).
// Reads the REAL controller pose from the on-device Meta XR Operator MCP server
// (Development build, port 8720) bypassing Unity's zero-pose controller backend.
//
// MCP SSE flow (bounded, no hanging streams):
//   1. GET /sse -> server immediately streams "event: endpoint\ndata: /message?session_id=..."
//      We read just enough bytes to capture that endpoint, then stop.
//   2. POST JSON-RPC tools/call to /message?session_id=... for openxr_get_controller_pose.
//   3. Parse the SSE response line "data: {jsonrpc result}" -> pose.
// Uses System.Net.Http with explicit timeouts; runs on a background thread pool task
// (avoids blocking the main thread / coroutine).

using UnityEngine;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace DFUQuest3
{
    public class MCPPoseBridge : MonoBehaviour
    {
        [Tooltip("On-device MCP server base URL (adb forward tcp:8720 to reach from host; in-app hits localhost directly).")]
        public string mcpUrl = "http://localhost:8720";

        public bool controllerValid;
        public Vector3 controllerPosition;
        public Quaternion controllerRotation;

        const float pollInterval = 0.05f; // 20 Hz
        const int discoverTimeoutMs = 2000;
        const int pollTimeoutMs = 250;

        string messageEndpoint;
        System.Threading.Thread pollThread;
        volatile bool running;

        void OnEnable()
        {
            running = true;
            pollThread = new System.Threading.Thread(ThreadMain);
            pollThread.IsBackground = true;
            pollThread.Start();
        }

        void OnDisable()
        {
            running = false;
            if (pollThread != null) { pollThread.Join(500); pollThread = null; }
        }

        void ThreadMain()
        {
            // Discover the message endpoint — read the /sse stream incrementally (the
            // server sends "event: endpoint\ndata: /message?session_id=..." immediately,
            // then heartbeats forever; ReadAsStringAsync would block, so read a bounded chunk).
            using (var client = new HttpClient())
            {
                client.Timeout = System.TimeSpan.FromMilliseconds(discoverTimeoutMs);
                try
                {
                    var resp = client.GetAsync(mcpUrl + "/sse", HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
                    if (!resp.IsSuccessStatusCode)
                    {
                        Debug.LogWarning("[DFUQuest3] MCP SSE status " + resp.StatusCode);
                        return;
                    }
                    using (var stream = resp.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                    {
                        byte[] buf = new byte[512];
                        // Bounded read — capture the first chunk containing the endpoint event.
                        var n = stream.Read(buf, 0, buf.Length);
                        string chunk = System.Text.Encoding.UTF8.GetString(buf, 0, n);
                        var idx = chunk.IndexOf("data: ");
                        if (idx >= 0)
                        {
                            var endpoint = chunk.Substring(idx + 6).Trim();
                            // endpoint line may be followed by \r\n — strip.
                            endpoint = endpoint.Split('\r')[0].Split('\n')[0];
                            messageEndpoint = mcpUrl + endpoint;
                            Debug.Log("[DFUQuest3] MCP pose bridge endpoint: " + messageEndpoint);
                        }
                        else
                        {
                            Debug.LogWarning("[DFUQuest3] MCP SSE: no endpoint in first chunk. [" + chunk + "]");
                            return;
                        }
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[DFUQuest3] MCP pose bridge discover failed: " + e.Message);
                    return;
                }
            }

            if (string.IsNullOrEmpty(messageEndpoint))
            {
                Debug.LogWarning("[DFUQuest3] MCP pose bridge: no endpoint; disabling.");
                return;
            }

            // Poll loop on the same thread.
            using (var client = new HttpClient())
            {
                client.Timeout = System.TimeSpan.FromMilliseconds(pollTimeoutMs);
                while (running)
                {
                    PollOnce(client);
                    System.Threading.Thread.Sleep((int)(pollInterval * 1000));
                }
            }
        }

        void PollOnce(HttpClient client)
        {
            var json = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"openxr_get_controller_pose\",\"arguments\":{\"hand\":\"right\"}}}";
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            try
            {
                var resp = client.PostAsync(messageEndpoint, content).GetAwaiter().GetResult();
                if (!resp.IsSuccessStatusCode) return;
                var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                ParsePoseResponse(body);
            }
            catch
            {
                // transient — ignore
            }
        }

        void ParsePoseResponse(string responseText)
        {
            var dataIdx = responseText.IndexOf("data: ");
            if (dataIdx < 0) return;
            var json = responseText.Substring(dataIdx + 6).Trim();
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
                float[] pos = new float[3], rot = new float[4];
                for (int i = 0; i < 3 && i < posStr.Length; i++)
                {
                    float.TryParse(posStr[i], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out pos[i]);
                }
                for (int i = 0; i < 4 && i < oriStr.Length; i++)
                {
                    float.TryParse(oriStr[i], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out rot[i]);
                }

                controllerPosition = new Vector3(pos[0], pos[1], pos[2]);
                controllerRotation = new Quaternion(rot[0], rot[1], rot[2], rot[3]);
                controllerValid = true;
                if (activeIdx >= 0)
                {
                    var actStr = poseJson.Substring(activeIdx + 12, 1);
                    controllerValid = actStr == "1";
                }
            }
        }
    }
}
