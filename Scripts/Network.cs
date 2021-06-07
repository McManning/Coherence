using SharedMemory;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;

namespace Coherence
{
    public interface INetworkTarget
    {
        string Name { get; }
    }

    // New idea - generic Coherence network interface. Statically available.
    public class Network : IDisposable
    {
        const string VIEWPORT_IMAGE_BUFFER = "_UnityViewportImage";
        const string UNITY_MESSAGES_BUFFER = "_UnityMessages";
        const string BLENDER_MESSAGES_BUFFER = "_BlenderMessages";

        /// <summary>
        /// How long to wait for a writable node to become available (milliseconds)
        ///
        /// This directly affects Unity's framerate.
        /// </summary>
        const int WRITE_WAIT = 0;

        public delegate void RequestHandler(InteropMessage msg);

        internal static event CoherenceEvent OnConnected;
        internal static event CoherenceEvent OnDisconnected;
        internal static event CoherenceEvent OnEnabled;
        internal static event CoherenceEvent OnDisabled;
        internal static event CoherenceEvent OnSync;

        public static Network Instance { get; private set; }

        /// <summary>
        /// Do we have shared memory allocated and ready to listen for Blender connections
        /// </summary>
        public static bool IsRunning { get; private set; }

        /// <summary>
        /// Is an instance of Blender connected to our shared memory
        /// </summary>
        public static bool IsConnected { get; private set; }

        /// <summary>
        /// Shared memory containing RenderTexture pixel data
        /// </summary>
        static CircularBuffer pixels;

        static InteropMessenger messages;

        static InteropUnityState unityState;
        static InteropBlenderState blenderState;

        /// <summary>
        /// Lookup of a target by class type
        /// </summary>
        internal static Dictionary<Type, Dictionary<string, INetworkTarget>> targets
            = new Dictionary<Type, Dictionary<string, INetworkTarget>>();

        /// <summary>
        /// Routing table for request type to specific target invocations
        /// </summary>
        internal static Dictionary<RpcRequest, Dictionary<string, RequestHandler>> targetHandlers
            = new Dictionary<RpcRequest, Dictionary<string, RequestHandler>>();

        // NOTE: Routing table doesn't take in type so there may be multiple objects of
        // different types with the same name field in the dictionary. But this should
        // be fine as long as there's a different RpcRequest used per-type.

        /// <summary>
        /// All registered network event handlers for plugins.
        ///
        /// These are one-to-one (an event may only be handled by one plugin)
        /// </summary>
        internal static Dictionary<RpcRequest, RequestHandler> handlers
            = new Dictionary<RpcRequest, RequestHandler>();

        private bool disposed;

        public static void Register(RpcRequest request, RequestHandler handler)
        {
            if (handlers.ContainsKey(request) || targetHandlers.ContainsKey(request))
            {
                throw new Exception($"There is already a handler registered to '{request}'");
            }

            handlers.Add(request, handler);
        }

        public static void Register(INetworkTarget target, RpcRequest request, RequestHandler handler)
        {
            if (string.IsNullOrEmpty(target.Name))
            {
                throw new Exception(
                    $"Cannot register message '{request}' for an unnamed target {target}"
                );
            }

            if (!targetHandlers.ContainsKey(request))
            {
                targetHandlers[request] = new Dictionary<string, RequestHandler>();
            }

            if (targetHandlers[request].ContainsKey(target.Name))
            {
                throw new Exception(
                    $"Already have a handler registered for '{request}' under the name '{target.Name}'"
                );
            }

            var type = target.GetType();
            if (!targets.ContainsKey(type))
            {
                targets[type] = new Dictionary<string, INetworkTarget>();
            }

            if (!targets[type].ContainsKey(target.Name))
            {
                targets[type].Add(target.Name, target);
            }

            targetHandlers[request][target.Name] = handler;
        }

        /// <summary>
        /// Unregister all handlers for the given target
        /// </summary>
        /// <param name="target"></param>
        public static void Unregister(INetworkTarget target)
        {
            var name = target.Name;

            // Scrub handlers from every request type
            foreach (var targets in targetHandlers.Values)
            {
                if (targets.TryGetValue(name, out var handler))
                {
                    targets.Remove(name);
                }
            }

            // Scrub from target list
            var type = target.GetType();
            targets[type].Remove(name);
        }

        public static T FindTarget<T>(string name) where T : INetworkTarget
        {
            var type = typeof(T);

            if (!targets.ContainsKey(type))
            {
                return default;
            }

            if (!targets[type].ContainsKey(name))
            {
                return default;
            }

            return (T)targets[type][name];
        }

        internal static IEnumerator<T> ForAllTargets<T>() where T : class, INetworkTarget
        {
            var type = typeof(T);

            if (!targets.ContainsKey(type))
            {
                yield break;
            }

            foreach (var target in targets[type].Values)
            {
                yield return target as T;
            }
        }

        internal static void RouteInboundMessage(InteropMessage msg)
        {
            var type = msg.Type;
            var target = msg.Target;

            // Invoke a plugin handler if we have one registered
            if (handlers.ContainsKey(type))
            {
                handlers[type](msg);
                return;
            }

            // Otherwise - lookup a target by type + name and route that way.
            if (targetHandlers.ContainsKey(type) && targetHandlers[type].ContainsKey(target))
            {
                targetHandlers[type][target](msg);
                return;
            }

            throw new Exception($"Could not route to target '{target}' for '{type}'");
        }

        internal static void UnregisterAll()
        {
            handlers.Clear();
            targetHandlers.Clear();
        }

        // and outbound message handlers here.


        /// <summary>
        /// Create a shared memory space for Blender to connect to.
        /// </summary>
        public static void Start()
        {
            InteropLogger.Debug("Network.Start");

            if (IsRunning)
            {
                Stop(); // Cycle a connection
            }

            var settings = CoherenceSettings.Instance;
            var name = settings.bufferName;

            try
            {
                messages = new InteropMessenger();

                messages.ConnectAsMaster(
                    name + BLENDER_MESSAGES_BUFFER,
                    name + UNITY_MESSAGES_BUFFER,
                    settings.nodeCount,
                    settings.nodeSize
                );
            }
            catch (System.IO.IOException)
            {
                Debug.LogError(
                    "Could not start the connection. This may happen when Coherence is shut down without " +
                    "properly cleaning up the connection. Make sure all other applications (e.g. Blender) " +
                    "have been disabled on their end before starting again."
                );
                return;
            }
            catch (Exception e)
            {
                // TODO: This is an IOException - which isn't as useful as
                // a FileNotFoundException that we'd get from Blender's side of things.
                // We could parse out the string to customize the error (e.g. instructions
                // on turning off Blender's side of things) but... feels hacky?
                Debug.LogError($"Failed to setup messaging: {e}");
                throw;
            }

            try
            {
                pixels = new CircularBuffer(
                    name + VIEWPORT_IMAGE_BUFFER,
                    settings.pixelsNodeCount,
                    settings.PixelsNodeSizeBytes
                );
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to setup pixels producer: {e}");
                throw;
            }

            IsRunning = true;

            // Notify listeners that network is ready to go
            OnEnabled?.Invoke();
        }

        /// <summary>
        /// Destroy the shared memory space and notify plugins / targets
        /// </summary>
        public static void Stop()
        {
            if (!IsRunning)
            {
                return;
            }

            InteropLogger.Debug("Network.Stop");

            IsRunning = false;

            DisposeSharedMemory();

            // Notify listeners we're shutting down
            OnDisabled?.Invoke();

            // Clear event listeners
            OnConnected = null;
            OnDisconnected = null;
            OnEnabled = null;
            OnDisabled = null;
            OnSync = null;
        }

        private static void DisposeSharedMemory()
        {
            InteropLogger.Debug("Network.DisposeSharedMemory");

            // Notify Blender if we're shutting down from the Unity side
            if (IsConnected)
            {
                messages.WriteDisconnect();
                OnDisconnectedFromBlender();
            }

            // Dispose shared memory
            messages?.Dispose();
            messages = null;

            pixels?.Dispose();
            pixels = null;
        }

        /// <summary>
        /// Send Blender updated state/settings information from Unity.
        /// We expect an <see cref="RpcRequest.UpdateBlenderState"/> in response.
        /// </summary>
        public static void SendUnityState()
        {
            if (IsConnected)
            {
                messages.Queue(RpcRequest.UpdateUnityState, Application.unityVersion, ref unityState);
            }
        }

        /// <summary>
        /// Push/pull messages between Blender and publish new viewport render textures when available.
        /// </summary>
        public static void Sync()
        {
            if (IsRunning)
            {
                messages.ProcessOutboundQueue();
                ConsumeMessages();
            }

            OnSync?.Invoke();
        }

        /// <summary>
        /// Clear state and scene object caches to their defaults
        /// </summary>
        private static void Clear()
        {
            // ???????
            // should probably destroy all handlers across different plugins.
            // (unless they don't destroy things). Maybe some destroy event to fire off?

            /*

            // Safely destroy all instantiated components
            foreach (var component in Components.Values)
            {
                component.DestroyCoherenceComponent();
            }

            Components.Clear();
            */

        }

        private static void OnConnectedToBlender()
        {
            InteropLogger.Debug("Network.OnConnectedToBlender");

            IsConnected = true;

            // Send our state/settings to Blender to sync up
            messages.Queue(RpcRequest.Connect, Application.unityVersion, ref unityState);

            // Notify listeners
            OnConnected?.Invoke();
        }

        /// <summary>
        /// Cleanup any lingering blender data from the scene (viewport cameras, meshes, etc)
        /// </summary>
        private static void OnDisconnectedFromBlender()
        {
            InteropLogger.Debug("Network.OnDisconnectFromBlender");

            IsConnected = false;

            // Clear any messages still queued to send
            messages.ClearQueue();

            // Reset Blender state information
            blenderState = new InteropBlenderState();

            // Notify listeners
            OnDisconnected?.Invoke();
        }

        /// <summary>
        /// Read a batch of messages off the message queue from Blender
        /// </summary>
        private static void ConsumeMessages()
        {
            Profiler.BeginSample("Coherence: Read Network");

            // Try to pump at least half the queue size of pending messages.
            // We avoid pumping until the queue is empty in one tick - otherwise
            // we may end up in a scenario where we put Unity into a locked state
            // when updates are constantly happening (e.g. during sculpting)
            int bytesRead;
            int messagesRead = 0;
            int maxMessagesToRead = CoherenceSettings.Instance.nodeCount / 2;
            do
            {
                bytesRead = ConsumeMessage();
            }
            while (messagesRead < maxMessagesToRead && bytesRead > 0);

            Profiler.EndSample();
        }

        /// <summary>
        /// Consume a single message off the interop message queue
        /// </summary>
        /// <returns>Number of bytes read</returns>
        private static int ConsumeMessage()
        {
            var disconnected = false;

            var bytesRead = messages.Read((msg) =>
            {
                var type = msg.header.type;
                var target = msg.header.target.Value;

                // While not connected - only accept connection requests
                if (!IsConnected)
                {
                    if (type != RpcRequest.Connect)
                    {
                        Debug.LogWarning($"Unhandled request type {type} for {target} - expected RpcRequest.Connect");
                    }

                    blenderState = msg.Reinterpret<InteropBlenderState>();
                    OnConnectedToBlender();
                    return 0;
                }

                switch (type)
                {
                    case RpcRequest.UpdateBlenderState:
                        blenderState = msg.Reinterpret<InteropBlenderState>();
                        break;
                    case RpcRequest.Disconnect:
                        // Don't call OnDisconnectFromBlender() from within
                        // the read handler - we want to release the read node
                        // safely first before disposing the connection.
                        disconnected = true;
                        break;

                    default:
                        RouteInboundMessage(msg);
                        break;
                }

                // TODO: Necessary to count bytes? We won't read anything off this
                // buffer at this point so it's safe to drop the whole thing.
                // bytesRead will count the header size (indicating a message *was* read)
                return 0;
            });

            // Handle any disconnects that may have occured during the read
            if (disconnected)
            {
                OnDisconnectedFromBlender();
            }

            return bytesRead;
        }

        /// <summary>
        /// Copy the RenderTexture data from the ViewportController into shared memory with Blender.
        ///
        /// <para>
        ///     The <paramref name="pixelsRGB24Func"/> callback is executed IFF we have room in the
        ///     buffer to write - letting us skip the heavy pixel copy operations if the consumer
        ///     is backed up in processing data.
        /// </para>
        /// </summary>
        internal static void PublishRenderTexture(ViewportController viewport, Func<byte[]> pixelsRGB24Func)
        {
            if (!IsConnected)
            {
                Debug.LogWarning("Cannot send RT - No connection");
            }

            Profiler.BeginSample("Coherence: Write wait for viewport");

            int bytesWritten = pixels.Write((ptr) => {

                // If we have a node we can write on, actually do the heavy lifting
                // of pulling the pixel data from the RenderTexture (in the callback)
                // and write into the buffer.
                var pixelsRGB24 = pixelsRGB24Func();

                Profiler.BeginSample("Coherence: Write viewport pixels");

                // Pack a header into shared memory
                var header = new InteropRenderHeader
                {
                    viewportId = viewport.ID,
                    width = viewport.Width,
                    height = viewport.Height
                };

                var headerSize = FastStructure.SizeOf<InteropRenderHeader>();
                FastStructure.WriteBytes(ptr, FastStructure.ToBytes(ref header), 0, headerSize);

                // Copy render image data into shared memory
                FastStructure.WriteBytes(ptr + headerSize, pixelsRGB24, 0, pixelsRGB24.Length);

                /*InteropLogger.Debug($"Writing {pixelsRGB24.Length} bytes with meta {header.width} x {header.height} and pix 0 is " +
                    $"{pixelsRGB24[0]}, {pixelsRGB24[1]}, {pixelsRGB24[2]}"
                );*/

                Profiler.EndSample();
                return headerSize + pixelsRGB24.Length;
            }, WRITE_WAIT);

            /*
            if (bytesWritten < 1)
            {
                Debug.LogWarning("pixelsProducer buffer is backed up. Skipped write.");
            }*/

            Profiler.EndSample();
        }

        internal static void SendArray<T>(RpcRequest type, string target, IArray<T> buffer) where T : struct
        {
            // NOTE: Same method as Bridge.SendArray in LibCoherence.

            if (!IsConnected || buffer.Length < 1)
            {
                return;
            }

            messages.QueueArray(type, target, buffer);
        }

        /// <summary>
        /// Send an <see cref="RpcRequest"/> with target <see cref="IInteropSerializable{T}.Name"/>
        /// of <paramref name="entity"/> and a <typeparamref name="T"/> payload.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="type"></param>
        /// <param name="entity"></param>
        internal static void SendEntity<T>(RpcRequest type, IInteropSerializable<T> entity) where T : struct
        {
            if (!IsConnected)
            {
                return;
            }

            var data = entity.Serialize();
            messages.ReplaceOrQueue(type, entity.Name, ref data);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
            {
                return;
            }

            if (disposing)
            {
                // managed dispose
            }

            DisposeSharedMemory();
            disposed = true;
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
