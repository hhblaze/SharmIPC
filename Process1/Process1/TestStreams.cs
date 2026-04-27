using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using tiesky.com;
using tiesky.com.SharmNpcInternals;

namespace Process1
{
    internal class TestStreams
    {
        static string sipctestfolder = @"D:\Temp\sharmipctest";

        public static async Task TestSreams(string[] args)
        {
            string pipeName = "SharmNpcDemoPipe";
           



            // 0. Setup: Create dummy files to use for streaming tests
            File.WriteAllText(Path.Combine(sipctestfolder, "client_payload.txt"), "Hello from the client stream! This is a file payload.");
            File.WriteAllText(Path.Combine(sipctestfolder, "server_response.txt"), "Hello from the server stream! This is the server's response file.");

            Debug.WriteLine("Starting Server...");
            using var server = CreateServer(pipeName);

            Debug.WriteLine("Starting Client...");
            using var client = CreateClient(pipeName);

            // Wait a brief moment for the connection to be established
            while (!client.IsConnected || !server.IsConnected)
            {
                await Task.Delay(100);
            }
            Debug.WriteLine("--- Connected! Starting API Tests ---\n");

            //// ==========================================
            //// 1. RemoteRequestWithoutResponse (Fire & Forget)
            //// ==========================================
            //Debug.WriteLine("[Client] Sending Fire-and-Forget message...");
            //client.RemoteRequestWithoutResponse(Encoding.UTF8.GetBytes("Hello Server (Fire&Forget)"));
            //await Task.Delay(500); // Give server a moment to print


            //// ==========================================
            //// 2. RemoteRequest (Synchronous / Callback)
            //// ==========================================
            //Debug.WriteLine("\n[Client] Sending Sync Request...");
            //// Notice: We are using a background task here just so we don't block our async Main method,
            //// but RemoteRequest is natively a blocking synchronous call.
            //var (syncSuccess, syncResp) = await Task.Run(() => client.RemoteRequest(Encoding.UTF8.GetBytes("Sync Request")));
            //if (syncSuccess)
            //    Debug.WriteLine($"[Client] Sync Response Received: {Encoding.UTF8.GetString(syncResp)}");


            //// ==========================================
            //// 3. RemoteRequestAsync (Asynchronous)
            //// ==========================================
            //Debug.WriteLine("\n[Client] Sending Async Request...");
            //var (asyncSuccess, asyncResp) = await client.RemoteRequestAsync(Encoding.UTF8.GetBytes("Async Request"));
            //if (asyncSuccess)
            //    Debug.WriteLine($"[Client] Async Response Received: {Encoding.UTF8.GetString(asyncResp)}");


            // ==========================================
            // 4. RemoteRequestStreamAsync (Streaming)
            // ==========================================
            Debug.WriteLine("\n[Client] Sending Stream Request (from file)...");

            byte[] streamMetadata = Encoding.UTF8.GetBytes("FileName: client_payload.txt");

            // NOTE: We do NOT use a `using` block here. The SharmNpc library will dispose the stream internally when finished.
            Stream fileStreamToSend = File.OpenRead(Path.Combine(sipctestfolder, "client_payload.txt"));

            var (streamSuccess, streamRespMetadata, responseStream) =
                await client.RemoteRequestStreamAsync(streamMetadata, fileStreamToSend, timeoutMs: 60000);

            if (streamSuccess)
            {
                Debug.WriteLine($"[Client] Stream Request Succeeded! Metadata: {Encoding.UTF8.GetString(streamRespMetadata)}");

                // If the server sent a stream back, save it to disk
                if (responseStream != null)
                {
                    using (var fs = File.Create(Path.Combine(sipctestfolder, "client_downloaded_response.txt")))
                    {
                        await responseStream.CopyToAsync(fs);
                    }
                    Debug.WriteLine("[Client] Downloaded response stream from server and saved to disk.");
                }
            }

            Debug.WriteLine("\n--- Tests Complete. Press any key to exit. ---");
            Console.ReadKey();
        }

        // =========================================================================
        // SERVER SETUP
        // =========================================================================
        static SharmNpc CreateServer(string pipeName)
        {
            var server = new SharmNpc(
                uniquePipeName: pipeName,
                role: PipeRole.Server,
                asyncRemoteCallHandler: (msgId, data) =>
                {
                    // This handles Standard Requests (eMsgType.Request / eMsgType.RpcRequest)
                    string msg = Encoding.UTF8.GetString(data);
                    Debug.WriteLine($"[Server] Received standard packet: {msg}");

                    // If it was an RPC request expecting an answer (has a msgId), we answer it:
                    // Note: In real life, you'd track if it was FireAndForget vs RPC, but SharmIPC 
                    // handles routing the response back by the msgId natively.
                    byte[] response = Encoding.UTF8.GetBytes($"Server ACK for: {msg}");

                    // We must have a reference to `server` to call AsyncAnswerOnRemoteCall. 
                    // (In a real app, this handler might be a class method).
                },
                externalProcessing: false
            );

            // Hook up the new STREAM handler
            server.AsyncStreamCallHandler = async (msgId, args, incomingStream) =>
            {
                string metadata = Encoding.UTF8.GetString(args);
                Debug.WriteLine($"[Server] Received incoming STREAM request. Metadata: {metadata}");

                // 1. Read the incoming stream and save it to disk
                using (var fs = File.Create(Path.Combine(sipctestfolder, "server_received_upload.txt")))
                {
                    await incomingStream.CopyToAsync(fs);
                }
                Debug.WriteLine("[Server] Successfully saved incoming stream to disk.");

                // 2. Prepare the response (Metadata + a return Stream)
                byte[] responseMetadata = Encoding.UTF8.GetBytes("Status: OK, File Accepted. Here is your receipt.");

                // NOTE: Do NOT use `using` here. The library takes ownership of this stream.
                Stream responseStream = File.OpenRead(Path.Combine(sipctestfolder, "server_response.txt"));

                // Return Tuple: (Success, Response Data, Optional Response Stream)
                return (true, responseMetadata, responseStream);
            };

            // Hack to allow the AsyncRemoteCallHandler to reference the server instance for replies
            var originalHandler = server.AsyncRemoteCallHandler;
            server.AsyncRemoteCallHandler = (msgId, data) =>
            {
                originalHandler(msgId, data);
                // Send response back
                server.AsyncAnswerOnRemoteCall(msgId, (true, Encoding.UTF8.GetBytes("Server Reply")));
            };

            return server;
        }

        // =========================================================================
        // CLIENT SETUP
        // =========================================================================
        static SharmNpc CreateClient(string pipeName)
        {
            var client = new SharmNpc(
                uniquePipeName: pipeName,
                role: PipeRole.Client,
                asyncRemoteCallHandler: (msgId, data) =>
                {
                    // If the server ever initiates a request TO the client, it arrives here.
                    Debug.WriteLine($"[Client] Server pushed a message: {Encoding.UTF8.GetString(data)}");
                },
                externalProcessing: false
            );

            client.PeerConnected += () => Debug.WriteLine("[Client] Event: Connected to Server.");
            client.PeerDisconnected += () => Debug.WriteLine("[Client] Event: Disconnected from Server.");

            return client;
        }


    }
}
