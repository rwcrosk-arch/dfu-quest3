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
                client.Timeout = TimeSpan.FromMilliseconds(requestTimeoutMs);
                // MCP requires an initialize handshake before tools/call.
                // Use a supported protocol version (verified: 2025-06-18 accepted).
                SendRequest(client, "{\"jsonrpc\":\"2.0\",\"id\":0,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"dfu-quest3\",\"version\":\"1.0\"}}}");
                // Wait for the initialize result to be processed on the session.
                Thread.Sleep(300);
                SendRequest(client, "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\",\"params\":{}}");
                Thread.Sleep(200);

                while (running)
                {
                    string endpoint;
                    lock (readLock) { endpoint = messageEndpoint; }
                    if (!string.IsNullOrEmpty(endpoint))
                    {
                        var json = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"openxr_get_controller_pose\",\"arguments\":{\"hand\":\"right\"}}}";
                        SendRequest(client, json);
                    }
                    Thread.Sleep(pollIntervalMs);
                }
            }
        }

        void SendRequest(HttpClient client, string json)
        {
            try
            {
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                // Fire the POST; response will arrive on the SSE stream.
                client.PostAsync(messageEndpoint, content).GetAwaiter().GetResult();
            }
            catch { }
        }

        // SSE "data" for a tools/call result: {"jsonrpc":"2.0","id":1,"result":{"content":[{"type":"text","text":"{poseJson}"}]}}
        void ParseResponse(string data)
        {
            var textStart = data.IndexOf("\"text\":\"");
            if (textStart < 0) return;
            textStart += 8;
            // The text value is JSON-escaped (contains \" and \n). Read until the
            // UNESCAPED closing quote that ends the string (a quote NOT preceded by \).
            var sb = new System.Text.StringBuilder();
            bool esc = false;
            for (int i = textStart; i < data.Length; i++)
            {
                char c = data[i];
                if (esc)
                {
                    // Preserve escaped newline/CR as their 2-char marker (ParsePoseJson
                    // strips them); drop the backslash only for `\"` and `\\` and `\/`.
                    if (c == 'n') { sb.Append("\\n"); }
                    else if (c == 'r') { sb.Append("\\r"); }
                    else sb.Append(c);
                    esc = false;
                }
                else if (c == '\\')
                {
                    esc = true;
                }
                else if (c == '"')
                {
                    break; // end of the text string
                }
                else
                {
                    sb.Append(c);
                }
            }
            var poseJson = sb.ToString();
            ParsePoseJson(poseJson);
        }

        void ParsePoseJson(string poseJson)
        {
            // Strip the escaped newlines Unity's StringContent may carry, and whitespace.
            poseJson = poseJson.Replace("\\n", "").Replace("\\r", "").Replace(" ", "");
            var posStart = poseJson.IndexOf("\"position\":[");
            var oriStart = poseJson.IndexOf("\"orientation\":[");
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
                    float.TryParse(posStr[i].Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out pos[i]);
                for (int i = 0; i < 4 && i < oriStr.Length; i++)
                    float.TryParse(oriStr[i].Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out rot[i]);

                controllerPosition = new Vector3(pos[0], pos[1], pos[2]);
                controllerRotation = new Quaternion(rot[0], rot[1], rot[2], rot[3]);
                // Consider it valid if the position is non-zero (a real tracked pose).
                controllerValid = pos[0] * pos[0] + pos[1] * pos[1] + pos[2] * pos[2] > 0.0001f;
            }
        }
    }
}
