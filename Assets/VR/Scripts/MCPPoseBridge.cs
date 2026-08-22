// DFU Quest3 VR — on-device MCP pose bridge (full SSE client).
// Reads the REAL controller pose from the on-device Meta XR Operator MCP server
// (Development build, port 8720), bypassing Unity's zero-pose controller backend.
//
// MCP SSE transport: GET /sse opens a long-lived stream. The server sends
// "event: endpoint\ndata: /message?session_id=..." once, then keeps the stream open.
// Requests are POSTed to that message endpoint; RESPONSES arrive back on the SSE stream.
// So: one thread holds the SSE stream open and parses responses; another POSTs requests.

using System;
using System.Net.Http;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace DFUQuest3
{
    public class MCPPoseBridge : MonoBehaviour
    {
        public string mcpUrl = "http://localhost:8720";

        public bool controllerValid;
        public Vector3 controllerPosition;
        public Quaternion controllerRotation;

        const int pollIntervalMs = 50;      // 20 Hz pose polling
        const int requestTimeoutMs = 500;
        Thread readThread;
        Thread writeThread;
        volatile bool running;
        string messageEndpoint;
        object readLock = new object();

        void OnEnable()
        {
            running = true;
            readThread = new Thread(ReadThreadMain) { IsBackground = true };
            readThread.Start();
        }

        void OnDisable()
        {
            running = false;
            if (readThread != null) readThread.Join(500);
            if (writeThread != null) writeThread.Join(500);
        }

        // Holds the SSE stream open and parses incoming "data:" responses.
        void ReadThreadMain()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    var resp = client.GetAsync(mcpUrl + "/sse", HttpCompletionOption.ResponseHeadersRead)
                        .GetAwaiter().GetResult();
                    resp.EnsureSuccessStatusCode();
                    using (var stream = resp.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        while (running)
                        {
                            string line = reader.ReadLine();
                            if (line == null) break;
                            if (line.StartsWith("data: "))
                            {
                                string data = line.Substring(6).Trim();
                                if (data.StartsWith("/message?session_id="))
                                {
                                    lock (readLock)
                                    {
                                        messageEndpoint = mcpUrl + data;
                                    }
                                    Debug.Log("[DFUQuest3] MCP pose bridge endpoint: " + messageEndpoint);
                                    // Start the write thread once we know the endpoint.
                                    if (writeThread == null)
                                    {
                                        writeThread = new Thread(WriteThreadMain) { IsBackground = true };
                                        writeThread.Start();
                                    }
                                }
                                else
                                {
                                    ParseResponse(data);
                                }
                            }
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[DFUQuest3] MCP SSE reader ended: " + e.Message);
            }
        }

        void WriteThreadMain()
        {
            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromMilliseconds(pollIntervalMs);
                while (running)
                {
                    string endpoint;
                    lock (readLock) { endpoint = messageEndpoint; }
                    if (!string.IsNullOrEmpty(endpoint))
                    {
                        var json = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"openxr_get_controller_pose\",\"arguments\":{\"hand\":\"right\"}}}";
                        try
                        {
                            var content = new StringContent(json, Encoding.UTF8, "application/json");
                            // Fire the POST; response will arrive on the SSE stream.
                            client.PostAsync(endpoint, content).GetAwaiter().GetResult();
                        }
                        catch { }
                    }
                    Thread.Sleep(pollIntervalMs);
                }
            }
        }

        // SSE "data" for a tools/call result: {"jsonrpc":"2.0","id":1,"result":{"content":[{"type":"text","text":"{poseJson}"}]}}
        void ParseResponse(string data)
        {
            if (data.Length > 300) Debug.Log("[DFUQuest3] MCP bridge data(big): " + data.Substring(0, 300));
            else Debug.Log("[DFUQuest3] MCP bridge data: " + data);
            var textStart = data.IndexOf("\"text\":\"");
            if (textStart < 0) return;
            textStart += 8;
            var textEnd = data.IndexOf("\"", textStart);
            if (textEnd < 0) return;
            var poseJson = data.Substring(textStart, textEnd - textStart)
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
                    float.TryParse(posStr[i], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out pos[i]);
                for (int i = 0; i < 4 && i < oriStr.Length; i++)
                    float.TryParse(oriStr[i], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out rot[i]);

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
