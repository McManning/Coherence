
using System;
using System.Collections.Generic;
using System.Linq; // TODO: Drop Linq usage
using SharedMemory;

namespace Coherence
{
    public delegate int MessageHandler(string target, IntPtr ptr);

    /// <summary>
    /// Manage shared memory buffers and messages between Blender and Unity
    /// </summary>
    class Bridge : IDisposable
    {
        const string VIEWPORT_IMAGE_BUFFER = "_UnityViewportImage";
        const string UNITY_MESSAGES_BUFFER = "_UnityMessages";
        const string BLENDER_MESSAGES_BUFFER = "_BlenderMessages";

        public bool IsConnected { get; private set; }

        /// <summary>
        /// How long to wait for a readable node to become available
        /// </summary>
        const int READ_WAIT = 1;

        Dictionary<int, Viewport> Viewports { get; set; }

        Dictionary<int, SceneObject> Objects { get; set; }

        InteropBlenderState blenderState;
        InteropUnityState unityState;

        CircularBuffer pixelsConsumer;

        InteropMessenger messages;

        public Dictionary<RpcRequest, MessageHandler> handlers;

        public Bridge()
        {
            Viewports = new Dictionary<int, Viewport>();
            Objects = new Dictionary<int, SceneObject>();

            // Message handlers for everything that comes from Unity
            handlers = new Dictionary<RpcRequest, MessageHandler>
            {
                [RpcRequest.Connect] = OnConnect,
                [RpcRequest.Disconnect] = OnDisconnect,
                [RpcRequest.UpdateUnityState] = OnUpdateUnityState,
            };
        }

        ~Bridge()
        {
            Dispose();
        }

        public void Dispose()
        {
            // Notify Unity of a disconnect if possible
            // TODO: Is this safe in the destructor? May not be.
            if (IsConnected)
            {
                messages.WriteDisconnect();
                IsConnected = false;
            }

            pixelsConsumer?.Dispose();
            pixelsConsumer = null;

            messages?.Dispose();
            messages = null;

            foreach (var viewport in Viewports)
            {
                viewport.Value.Dispose();
            }

            Viewports.Clear();
        }

        #region Unity IO Management

        /// <summary>
        /// Connect to a shared memory space hosted by Unity and sync scene data
        /// </summary>
        /// <param name="connectionName">Common name for the shared memory space between Blender and Unity</param>
        public bool Start(string connectionName)
        {
            try
            {
                InteropLogger.Debug($"Connecting to `{connectionName + VIEWPORT_IMAGE_BUFFER}`");

                // Buffer for render data coming from Unity (consume-only)
                pixelsConsumer = new CircularBuffer(connectionName + VIEWPORT_IMAGE_BUFFER);

                InteropLogger.Debug($"Connecting to `{connectionName + UNITY_MESSAGES_BUFFER}` and `{connectionName + BLENDER_MESSAGES_BUFFER}`");

                // Two-way channel between Blender and Unity
                messages = new InteropMessenger();
                messages.ConnectAsSlave(
                    connectionName + UNITY_MESSAGES_BUFFER,
                    connectionName + BLENDER_MESSAGES_BUFFER
                );
            }
            catch (System.IO.FileNotFoundException)
            {
                // Shared memory space is not valid - Unity may not have started it.
                // This is an error that should be gracefully handled by the UI.
                return false;
            }

            // Send an initial connect message to let Unity know we're in
            messages.Queue(RpcRequest.Connect, "", ref blenderState);

            return true;
        }

        /// <summary>
        /// Dispose shared memory and shutdown communication to Unity
        /// </summary>
        public void Shutdown()
        {
            IsConnected = false;
            messages?.WriteDisconnect();

            messages?.Dispose();
            messages = null;

            pixelsConsumer?.Dispose();
            pixelsConsumer = null;

            // Release our copy of synced scene objects. Next time the Python
            // addon starts up the connection, it should load in objects to sync.
            Objects.Clear();
        }

        /// <summary>
        /// Send queued messages and read new messages and render texture data from Unity
        /// </summary>
        internal void Update()
        {
            if (messages != null)
            {
                messages.ProcessOutboundQueue();
                ConsumeMessage();
            }
        }

        /// <summary>
        /// Read from the viewport image buffer and copy
        /// pixel data into the appropriate viewport.
        /// </summary>
        internal void ConsumePixels()
        {
            if (pixelsConsumer == null || pixelsConsumer.ShuttingDown)
            {
                return;
            }

            pixelsConsumer.Read((ptr) =>
            {
                var headerSize = FastStructure.SizeOf<InteropRenderHeader>();
                var header = FastStructure.PtrToStructure<InteropRenderHeader>(ptr);

                if (!Viewports.ContainsKey(header.viewportId))
                {
                    InteropLogger.Warning($"Got render texture for unknown viewport {header.viewportId}");
                    return headerSize;
                }

                var viewport = Viewports[header.viewportId];
                var pixelDataSize = viewport.ReadPixelData(header, ptr + headerSize);

                return headerSize + pixelDataSize;
            }, READ_WAIT);
        }

        /// <summary>
        /// Consume a single message off the read queue.
        /// </summary>
        internal void ConsumeMessage()
        {
            messages.Read((target, header, ptr) =>
            {
                if (!handlers.ContainsKey(header.type))
                {
                    InteropLogger.Warning($"Unhandled request type {header.type} for {target}");
                    return 0;
                }

                return handlers[header.type](target, ptr);
            });
        }

        /// <summary>
        /// Send an <see cref="RpcRequest"/> with target <see cref="IInteropSerializable{T}.Name"/>
        /// of <paramref name="entity"/> and a <typeparamref name="T"/> payload.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="type"></param>
        /// <param name="entity"></param>
        internal void SendEntity<T>(RpcRequest type, IInteropSerializable<T> entity) where T : struct
        {
            if (!IsConnected)
            {
                return;
            }

            var data = entity.Serialize();
            messages.ReplaceOrQueue(type, entity.Name, ref data);
        }

        /// <summary>
        /// Send an array of <typeparamref name="T"/> to Unity, splitting into multiple
        /// <see cref="RpcRequest"/> if all the objects cannot fit in a single message.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="type"></param>
        /// <param name="target"></param>
        /// <param name="data"></param>
        /// <param name="allowSplitMessages"></param>
        internal void SendArray<T>(RpcRequest type, string target, T[] data, bool allowSplitMessages) where T : struct
        {
            if (!IsConnected)
            {
                return;
            }

            // TODO: Zero length array support. Makes sense in some use cases
            // but not others (e.g. don't send RpcRequest.UpdateUVs if there
            // are no UVs to send)
            if (data == null || data.Length < 1)
            {
                return;
            }

            if (messages.ReplaceOrQueueArray(type, target, data, allowSplitMessages))
            {
                InteropLogger.Debug($"Replaced queued {type} for {target}");
            }
        }

        /// <summary>
        /// Send <b>all</b> available mesh data (vertices, triangles, normals, UVs, etc) to Unity
        /// </summary>
        /// <param name="obj"></param>
        internal void SendAllMeshData(SceneObject obj)
        {
            if (!IsConnected)
            {
                return;
            }

            SendArray(RpcRequest.UpdateVertices, obj.Name, obj.Vertices, true);
            SendArray(RpcRequest.UpdateTriangles, obj.Name, obj.Triangles, true);
            SendArray(RpcRequest.UpdateNormals, obj.Name, obj.Normals, true);
            SendArray(RpcRequest.UpdateUVs, obj.Name, obj.GetUV(0), true);
            // ... and so on, per-buffer ...

        }

        /// <summary>
        /// Send <b>everything</b> we have to Unity - viewports, objects, mesh data, etc.
        /// </summary>
        private void SendAllSceneData()
        {
            // Send all objects currently in the scene
            foreach (var obj in Objects)
            {
                SendEntity(RpcRequest.AddObjectToScene, obj.Value);
            }

            // Send active viewports and their visibility lists
            foreach (var vp in Viewports)
            {
                var viewport = vp.Value;

                SendEntity(RpcRequest.AddViewport, viewport);
                SendArray(
                    RpcRequest.UpdateVisibleObjects,
                    viewport.Name,
                    viewport.VisibleObjectIds,
                    false
                );
            }

            // THEN send current mesh data for all objects with meshes.
            // We do this last so that we can ensure Unity is completely setup and
            // ready to start accepting large data chunks.
            foreach (var obj in Objects)
            {
                SendAllMeshData(obj.Value);
            }
        }

        #endregion

        #region Unity Message Handlers

        private int OnConnect(string target, IntPtr ptr)
        {
            unityState = FastStructure.PtrToStructure<InteropUnityState>(ptr);
            IsConnected = true;

            InteropLogger.Debug($"{target} - {unityState.version} connected. Flavor Blasting.");

            SendAllSceneData();

            return FastStructure.SizeOf<InteropUnityState>();
        }

        private int OnDisconnect(string target, IntPtr ptr)
        {
            IsConnected = false;
            messages.ClearQueue();

            InteropLogger.Debug("Unity disconnected");
            return 0;
        }

        private int OnUpdateUnityState(string target, IntPtr ptr)
        {
            unityState = FastStructure.PtrToStructure<InteropUnityState>(ptr);

            return FastStructure.SizeOf<InteropUnityState>();
        }

        #endregion

        #region Viewport Management

        public Viewport GetViewport(int id)
        {
            if (!Viewports.ContainsKey(id))
            {
                throw new Exception($"Viewport {id} does not exist");
            }

            return Viewports[id];
        }

        /// <summary>
        /// Add a new viewport to our state and notify Unity
        /// </summary>
        /// <param name="id"></param>
        /// <param name="initialWidth"></param>
        /// <param name="initialHeight"></param>
        internal void AddViewport(int id)
        {
            if (Viewports.ContainsKey(id))
            {
                throw new Exception($"Viewport {id} already exists");
            }

            var viewport = new Viewport(id);

            // Assume everything is visible upon creation
            viewport.SetVisibleObjects(Objects.Keys.ToArray());
            Viewports[id] = viewport;

            SendEntity(RpcRequest.AddViewport, viewport);
            SendArray(RpcRequest.UpdateVisibleObjects, viewport.Name, viewport.VisibleObjectIds, false);
        }

        /// <summary>
        /// Remove a viewport from our state and notify Unity
        /// </summary>
        /// <param name="id"></param>
        internal void RemoveViewport(int id)
        {
            var viewport = Viewports[id];

            SendEntity(RpcRequest.RemoveViewport, viewport);

            Viewports.Remove(id);
            viewport.Dispose();
        }

        #endregion

        #region Object Management

        public SceneObject GetObject(int id)
        {
            if (!Objects.ContainsKey(id))
            {
                throw new Exception($"Object {id} does not exist in the scene");
            }

            return Objects[id];
        }

        public void AddObject(SceneObject obj)
        {
            if (Objects.ContainsKey(obj.data.id))
            {
                throw new Exception($"Object {obj.data.id} already exists in the scene");
            }

            Objects[obj.data.id] = obj;

            SendEntity(RpcRequest.AddObjectToScene, obj);
        }

        public void RemoveObject(int id)
        {
            if (!Objects.ContainsKey(id))
            {
                throw new Exception($"Object {id} does not exist in the scene");
            }

            var obj = Objects[id];
            SendEntity(RpcRequest.RemoveObjectFromScene, obj);
            Objects.Remove(id);
        }

        #endregion
    }
}
