﻿
using SharedMemory;
using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

namespace Coherence
{
    public delegate int MessageHandler(string target, IntPtr ptr);

    /// <summary>
    /// Manage shared memory buffers and messages between Blender and Unity
    /// </summary>
    class Bridge : IDisposable
    {
        public static Bridge Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new Bridge();
                }

                return instance;
            }
        }

        private static Bridge instance;

        const string VIEWPORT_IMAGE_BUFFER = "_UnityViewportImage";
        const string UNITY_MESSAGES_BUFFER = "_UnityMessages";
        const string BLENDER_MESSAGES_BUFFER = "_BlenderMessages";

        public bool IsConnectedToSharedMemory { get; private set; }

        public bool IsConnectedToUnity { get; private set; }

        /// <summary>
        /// How long to wait for a readable node to become available
        /// </summary>
        const int READ_WAIT = 1;

        /// <summary>
        /// How long after <see cref="lastUpdateFromUnity"/> to consider
        /// Unity crashed and disconnect automatically. This should
        /// be longer than the ping rate from Unity.
        /// </summary>
        const int TIMEOUT_SECONDS = 10000;

        readonly Dictionary<int, Viewport> viewports;
        readonly Dictionary<string, SceneObject> objects;
        readonly Dictionary<string, Mesh> meshes;

        InteropBlenderState blenderState;
        InteropUnityState unityState;

        CircularBuffer pixelsConsumer;
        InteropMessenger messages;

        Dictionary<RpcRequest, MessageHandler> handlers;

        /// <summary>
        /// Last time we got an update from Unity's side.
        ///
        /// <para>
        ///     Used for automatically disconnecting in case Unity crashes
        ///     and doesn't send a proper <see cref="RpcRequest.Disconnect"/>
        /// </para>
        /// </summary>
        DateTime lastUpdateFromUnity;

        public Bridge()
        {
            viewports = new Dictionary<int, Viewport>();
            objects = new Dictionary<string, SceneObject>();
            meshes = new Dictionary<string, Mesh>();

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
            Disconnect();
            Clear();
        }

        #region Unity IO Management

        /// <summary>
        /// Connect to a shared memory space hosted by Unity and sync scene data
        /// </summary>
        /// <param name="connectionName">Common name for the shared memory space between Blender and Unity</param>
        public bool Connect(string connectionName, string versionInfo)
        {
            blenderState = new InteropBlenderState
            {
                version = versionInfo
            };

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
                IsConnectedToSharedMemory = false;
                return false;
            }

            IsConnectedToSharedMemory = true;

            // Send an initial connect message to let Unity know we're in
            messages.Queue(RpcRequest.Connect, versionInfo, ref blenderState);

            return true;
        }

        /// <summary>
        /// Dispose shared memory and shutdown communication to Unity.
        ///
        /// Anything from Unity's side would get cleaned up.
        /// </summary>
        public void Disconnect()
        {
            messages?.WriteDisconnect();

            IsConnectedToUnity = false;
            IsConnectedToSharedMemory = false;

            messages?.Dispose();
            messages = null;

            pixelsConsumer?.Dispose();
            pixelsConsumer = null;
        }

        /// <summary>
        /// Clear all cached data from the bridge (scene objects, viewports, etc).
        ///
        /// This should NOT be called while connected.
        /// </summary>
        public void Clear()
        {
            meshes.Clear();
            objects.Clear();

            // Safely dispose all the viewport data before dereferencing
            foreach (var viewport in viewports)
            {
                viewport.Value.Dispose();
            }

            viewports.Clear();
        }

        /// <summary>
        /// Send queued messages and read new messages and render texture data from Unity
        /// </summary>
        [HandleProcessCorruptedStateExceptions]
        internal void Update()
        {
            try
            {
                if (messages != null)
                {
                    messages.ProcessOutboundQueue();
                    ConsumeMessage();
                    CheckForTimeout();
                }
            }
            catch (AccessViolationException)
            {
                // We run into an access violation when Unity disposes the
                // shared memory buffer but Blender is still trying to do IO with it.
                // So we explicitly catch this case and gracefully disconnect.
                // See: https://docs.microsoft.com/en-us/dotnet/api/system.runtime.exceptionservices.handleprocesscorruptedstateexceptionsattribute#remarks

                // TODO: IDEALLY - this should happen closer to the IO with the shared memory buffer,
                // as doing it here might catch access violations elsewhere in the library that weren't
                // intended since this method essentially wraps EVERYTHING.
                // Possibly @ RpcMessenger.Read/Write? Or deeper in CircularBuffer.Read/Write.
                // Actual exception for Read was thrown within CircularBuffer.ReturnNode
                InteropLogger.Error($"Access Violation doing IO to the shared memory - disconnecting");
                Disconnect();
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

                if (!viewports.ContainsKey(header.viewportId))
                {
                    InteropLogger.Warning($"Got render texture for unknown viewport {header.viewportId}");
                    return headerSize;
                }

                var viewport = viewports[header.viewportId];
                var pixelDataSize = viewport.ReadPixelData(header, ptr + headerSize);

                return headerSize + pixelDataSize;
            }, READ_WAIT);
        }

        /// <summary>
        /// Consume a single message off the read queue.
        /// </summary>
        private void ConsumeMessage()
        {
            messages.Read((target, header, ptr) =>
            {
                lastUpdateFromUnity = DateTime.Now;

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
            if (!IsConnectedToSharedMemory)
            {
                return;
            }

            var data = entity.Serialize();
            messages.ReplaceOrQueue(type, entity.Name, ref data);
        }

        internal void SendArray<T>(RpcRequest type, string target, IArray<T> buffer) where T : struct
        {
            if (!IsConnectedToSharedMemory || buffer.Length < 1)
            {
                return;
            }

            InteropLogger.Debug(
                $"SendBuffer target={target} type={type} length={buffer.Length}"
            );

            messages.QueueArray(type, target, buffer);
        }

        /// <summary>
        /// Send <b>everything</b> we have to Unity - viewports, objects, mesh data, etc.
        /// </summary>
        private void SendAllSceneData()
        {
            // Send all objects currently in the scene
            foreach (var obj in objects)
            {
                SendEntity(RpcRequest.AddObjectToScene, obj.Value);
            }

            // Send active viewports and their visibility lists
            foreach (var vp in viewports)
            {
                var viewport = vp.Value;

                SendEntity(RpcRequest.AddViewport, viewport);

                /* TODO: Reimplement
                SendArray(
                    RpcRequest.UpdateVisibleObjects,
                    viewport.Name,
                    viewport.VisibleObjectIds,
                    false
                );*/
            }

            // THEN send current mesh data for all objects with meshes.
            // We do this last so that we can ensure Unity is completely setup and
            // ready to start accepting large data chunks.
            foreach (var mesh in meshes.Values)
            {
                mesh.SendAll();
            }
        }

        /// <summary>
        /// Determine if we should close the connection automatically in
        /// assumption that Unity has crashed.
        /// </summary>
        private void CheckForTimeout()
        {
            var elapsed = DateTime.Now - lastUpdateFromUnity;
            if (elapsed.Seconds < TIMEOUT_SECONDS)
            {
                return;
            }

            // TODO: If we've timed out, close the connection and cleanup.
        }

        #endregion

        #region Unity Message Handlers

        private int OnConnect(string target, IntPtr ptr)
        {
            unityState = FastStructure.PtrToStructure<InteropUnityState>(ptr);
            IsConnectedToUnity = true;

            InteropLogger.Debug($"{target} - {unityState.version} connected. Flavor Blasting.");

            SendAllSceneData();

            return FastStructure.SizeOf<InteropUnityState>();
        }

        private int OnDisconnect(string target, IntPtr ptr)
        {
            InteropLogger.Debug("Unity disconnected");

            // Disconnect from invalidated shared memory since Unity was the owner
            Disconnect();

            // Based on Unity's side of things - we may never even see this message.
            // If unity sends out a Disconnect and then immediately disposes the
            // shared memory - we won't be able to read the disconnect and instead
            // just get an access violation on the next read of shared memory
            // (which is caught in Update() and calls Disconnect() anyway).
            // If there was some delay though between Unity sending a Disconnect
            // and memory cleanup, then we can safely catch it here and disconnect
            // ourselves while avoiding a potential access violation.

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
            if (!viewports.ContainsKey(id))
            {
                throw new Exception($"Viewport {id} does not exist");
            }

            return viewports[id];
        }

        /// <summary>
        /// Add a new viewport to our state and notify Unity
        /// </summary>
        /// <param name="id"></param>
        /// <param name="initialWidth"></param>
        /// <param name="initialHeight"></param>
        internal void AddViewport(int id)
        {
            if (viewports.ContainsKey(id))
            {
                throw new Exception($"Viewport {id} already exists");
            }

            var viewport = new Viewport(id);
            viewports[id] = viewport;

            SendEntity(RpcRequest.AddViewport, viewport);
        }

        /// <summary>
        /// Remove a viewport from our state and notify Unity
        /// </summary>
        /// <param name="id"></param>
        internal void RemoveViewport(int id)
        {
            var viewport = viewports[id];

            SendEntity(RpcRequest.RemoveViewport, viewport);

            viewports.Remove(id);
            viewport.Dispose();
        }

        #endregion

        #region Mesh Management

        public Mesh GetOrCreateMesh(string name)
        {
            if (!meshes.ContainsKey(name))
            {
                var mesh = new Mesh(name);
                meshes[name] = mesh;
                return mesh;
            }

            return meshes[name];
        }

        #endregion

        #region Object Management

        public SceneObject GetObject(string name)
        {
            if (!objects.ContainsKey(name))
            {
                throw new Exception($"Object `{name}` does not exist in the scene");
            }

            return objects[name];
        }

        public void AddObject(SceneObject obj)
        {
            if (objects.ContainsKey(obj.Name))
            {
                throw new Exception($"Object `{obj.Name}` already exists in the scene");
            }

            objects[obj.Name] = obj;

            SendEntity(RpcRequest.AddObjectToScene, obj);
        }

        public void RemoveObject(string name)
        {
            if (!objects.ContainsKey(name))
            {
                throw new Exception($"Object `{name}` does not exist in the scene");
            }

            var obj = objects[name];
            SendEntity(RpcRequest.RemoveObjectFromScene, obj);
            objects.Remove(name);
        }

        #endregion

        #region Texture Sync

        public readonly List<Texture> Textures = new List<Texture>()
        {
            new Texture("Foo"),
            new Texture("Bar")
        };

        public Texture GetTexture(string name)
        {
            var match = Textures.Find((tex) => tex.Name == name);
            if (match == null)
            {
                throw new Exception($"Texture `{name}` does not exist");
            }

            return match;
        }

        #endregion
    }
}
