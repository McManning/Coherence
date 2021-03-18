using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Profiling.Memory.Experimental;
using UnityEngine.Rendering;
using SharedMemory;
using UnityEditor;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

namespace Coherence
{
    /// <summary>
    /// Handles communication between Blender and Unity and passes
    /// the appropriate data to subcomponents for internal processing.
    /// </summary>
    [ExecuteAlways]
    public class SyncManager : MonoBehaviour
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

        /// <summary>
        /// Do we have shared memory allocated and ready to listen for Blender connections
        /// </summary>
        public bool IsRunning { get; private set; }

        /// <summary>
        /// Is an instance of Blender connected to our shared memory
        /// </summary>
        public bool IsConnected { get; private set; }

        /// <summary>
        /// Shared memory we write RenderTexture pixel data into
        /// </summary>
        CircularBuffer pixelsProducer;

        InteropMessenger messages;

        /// <summary>
        /// Viewport controller to match Blender's viewport configuration
        /// </summary>
        readonly OrderedDictionary viewports = new OrderedDictionary();
        readonly Dictionary<string, ObjectController> objects = new Dictionary<string, ObjectController>();

        /// <summary>
        /// Objects added with parents that are not (yet) in the scene.
        /// </summary>
        readonly HashSet<ObjectController> orphans = new HashSet<ObjectController>();

        /// <summary>
        /// Current index in <see cref="viewports"/> to use for
        /// sending a render texture to Blender
        /// </summary>
        int viewportIndex;

        GameObject viewportsContainer;
        GameObject objectsContainer;

        /// <summary>
        /// Current state information of Unity
        /// </summary>
        InteropUnityState unityState;

        /// <summary>
        /// Most recent state information received from Blender
        /// </summary>
        InteropBlenderState blenderState;

        /// <summary>
        /// Create a shared memory space for Blender to connect to.
        /// </summary>
        public bool Setup()
        {
            gameObject.transform.parent = null;

            if (IsRunning)
            {
                return true;
            }

            IsRunning = true;

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
                pixelsProducer = new CircularBuffer(
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

            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            EditorApplication.update += OnEditorUpdate;
            SceneManager.activeSceneChanged += OnSceneUnloaded;
            EditorSceneManager.activeSceneChangedInEditMode += OnSceneUnloaded;

            return true;
        }

        /// <summary>
        /// Destroy the shared memory space and temporary scene objects
        /// </summary>
        public void Teardown()
        {
            if (!IsRunning)
            {
                return;
            }

            IsRunning = false;

            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            EditorApplication.update -= OnEditorUpdate;
            SceneManager.activeSceneChanged -= OnSceneUnloaded;
            EditorSceneManager.activeSceneChangedInEditMode -= OnSceneUnloaded;

            // Notify Blender if we're shutting down from the Unity side
            if (IsConnected)
            {
                messages.WriteDisconnect();
                OnDisconnectFromBlender();
            }
            else
            {
                // Still clear if we're not connected - we might've torn down
                // the connection uncleanly and have some persisted data to clean up.
                Clear();
            }

            // Dispose shared memory
            messages?.Dispose();
            messages = null;

            pixelsProducer?.Dispose();
            pixelsProducer = null;
        }

        /// <summary>
        /// Send Blender updated state/settings information from Unity.
        /// We expect an <see cref="RpcRequest.UpdateBlenderState"/> in response.
        /// </summary>
        public void SendUnityState()
        {
            if (IsConnected)
            {
                messages.Queue(RpcRequest.UpdateUnityState, Application.unityVersion, ref unityState);
            }
        }

        /// <summary>
        /// Push/pull messages between Blender and publish new viewport render textures when available.
        /// </summary>
        public void Sync()
        {
            if (IsRunning)
            {
                messages.ProcessOutboundQueue();
                ConsumeMessages();
            }

            if (IsConnected)
            {
                // Messages from Blender will typically affect the scene view
                // (camera movement, object movement, new mesh data, etc).
                // While running in editor mode we don't typically see these
                // repaint - so we'll need to force a redraw of scene views.
                SceneView.RepaintAll();

                PublishNextRenderTexture();
            }

            // Propagate Sync to every tracked object (updates their mesh data, etc)
            foreach (var obj in objects.Values)
            {
                obj.Sync();
            }
        }

        private void OnSceneUnloaded(Scene current, Scene next)
        {
            Teardown();
        }

        private void OnDisable()
        {
            Teardown();
        }

        private void OnEditorUpdate()
        {
            // In editor, process alongside editor updates
            if (!Application.isPlaying)
            {
                Sync();
            }
        }

        private void Update()
        {
            // In play mode, process alongside the typical Update()
            if (Application.isPlaying)
            {
                Sync();
            }
        }

        /// <summary>
        /// Clear state and scene object caches to their defaults
        /// </summary>
        private void Clear()
        {
            if (objectsContainer != null)
            {
                DestroyImmediate(objectsContainer);
                objectsContainer = null;
            }

            if (viewportsContainer != null)
            {
                DestroyImmediate(viewportsContainer);
                viewportsContainer = null;
            }

            viewports.Clear();
            objects.Clear();
        }

        #region Viewports

        /// <summary>
        /// Publish the RT of the next viewport in the dictionary.
        ///
        /// <para>
        ///     We do this to ensure that every viewport has a chance of writing
        ///     to the circular buffer - in case there isn't enough room to write
        ///     all the viewports in one frame.
        /// </para>
        /// </summary>
        private void PublishNextRenderTexture()
        {
            if (viewports.Count < 1)
            {
                return;
            }

            viewportIndex = (viewportIndex + 1) % viewports.Count;

            var viewport = viewports[viewportIndex] as ViewportController;
            PublishRenderTexture(viewport, viewport.CaptureRenderTexture);
        }

        private ViewportController GetViewport(string name)
        {
            if (!viewports.Contains(name))
            {
                throw new Exception($"Viewport {name} does not exist");
            }

            return viewports[name] as ViewportController;
        }

        private ViewportController AddViewport(string name, InteropViewport iv)
        {
            if (viewportsContainer == null)
            {
                viewportsContainer = new GameObject("Viewports")
                {
                    tag = "EditorOnly",
                    hideFlags = HideFlags.NotEditable | HideFlags.DontSave
                };

                viewportsContainer.transform.parent = transform;
            }

            var prefab = CoherenceSettings.Instance.viewportCameraPrefab;
            var go = prefab ? Instantiate(prefab.gameObject) : new GameObject();

            go.name = name;
            go.transform.parent = viewportsContainer.transform;

            var controller = go.AddComponent<ViewportController>();
            controller.Sync = this;
            controller.UpdateFromInterop(iv);

            viewports[name] = controller;
            return controller;
        }

        private void RemoveViewport(string name)
        {
            if (!viewports.Contains(name))
            {
                return;
            }

            var viewport = viewports[name] as ViewportController;

            DestroyImmediate(viewport.gameObject);
            viewports.Remove(name);

            // Reset viewport iterator, in case this causes us to go out of range
            viewportIndex = 0;
        }

        #endregion

        #region Textures

        private BlenderTexture GetTexture(string name)
        {
            return CoherenceSettings.Instance.textureSlots.Find(
                (tex) => tex.name == name
            );
        }

        #endregion

        #region Objects

        private ObjectController GetObject(string name)
        {
            if (!objects.ContainsKey(name))
            {
                throw new Exception($"Object {name} does not exist");
            }

            return objects[name];
        }

        private ObjectController AddObject(string name, InteropSceneObject iso)
        {
            if (objectsContainer == null)
            {
                objectsContainer = new GameObject("Objects")
                {
                    tag = "EditorOnly",
                    hideFlags = HideFlags.NotEditable | HideFlags.DontSave
                };

                objectsContainer.transform.parent = transform;
            }

            var prefab = CoherenceSettings.Instance.sceneObjectPrefab;
            var go = prefab ? Instantiate(prefab) : new GameObject();
            var controller = go.AddComponent<ObjectController>();

            go.name = name;

            objects[name] = controller;
            controller.UpdateFromInterop(iso);

            ReparentObject(controller);

            // Attach any orphaned objects that were waiting for this parent to be added.
            CheckOrphanedObjects();

            return controller;
        }

        private void RemoveObject(string name)
        {
            if (!objects.ContainsKey(name))
            {
                return;
            }

            var instance = objects[name];

            // Move children to the main container and orphan them.
            var children = instance.GetComponentsInChildren<ObjectController>();
            foreach (var child in children)
            {
                child.transform.parent = objectsContainer.transform;
                orphans.Add(child);
            }

            DestroyImmediate(instance.gameObject);
            objects.Remove(name);
            orphans.Remove(instance);
        }

        private void UpdateObject(string name, InteropSceneObject iso)
        {
            var obj = GetObject(name);
            var needsReparenting = !iso.transform.parent.Equals(obj.Data.transform.parent);

            obj.UpdateFromInterop(iso);

            if (needsReparenting)
            {
                ReparentObject(obj);
            }
        }

        /// <summary>
        /// Find an object in the Unity scene matching the current parent name
        /// provided by Blender and attach the object to it.
        ///
        /// If the parent cannot be found - the object is attached to the container
        /// and added to the orphans list to be later picked up by new entries.
        /// </summary>
        /// <param name="obj"></param>
        private void ReparentObject(ObjectController obj)
        {
            var parentName = obj.Data.transform.parent.Value;

            // Object is root level / unparented.
            if (parentName.Length < 1)
            {
                obj.transform.parent = objectsContainer.transform;
                orphans.Remove(obj);
            }

            if (objects.TryGetValue(parentName, out ObjectController match))
            {
                obj.transform.parent = match.transform;
                orphans.Remove(obj);
            }
            else
            {
                // A parent was defined by this object but it's not in the scene
                // (could be added out of sequence, or a non-transferrable type).
                // Add to the orphan list to be later picked up if it does get added.
                obj.transform.parent = objectsContainer.transform;
                orphans.Add(obj);
            }
        }

        /// <summary>
        /// Scan through orphaned objects for any new parent associations to add.
        /// </summary>
        private void CheckOrphanedObjects()
        {
            var parentedOrphans = new List<ObjectController>();
            foreach (var orphan in orphans)
            {
                var parentName = orphan.Data.transform.parent.Value;
                if (objects.TryGetValue(parentName, out ObjectController parent))
                {
                    orphan.transform.parent = parent.transform;
                    parentedOrphans.Add(orphan);
                }
            }

            foreach (var orphan in parentedOrphans)
            {
                orphans.Remove(orphan);
            }
        }

        #endregion

        #region IO
        private void OnConnectToBlender()
        {
            Debug.Log("Connected to Blender");

            IsConnected = true;

            // Send our state/settings to Blender to sync up
            messages.Queue(RpcRequest.Connect, Application.unityVersion, ref unityState);
        }

        /// <summary>
        /// Cleanup any lingering blender data from the scene (viewport cameras, meshes, etc)
        /// </summary>
        private void OnDisconnectFromBlender()
        {
            Debug.Log("Disconnected from Blender");
            IsConnected = false;

            // Clear any messages still queued to send
            messages.ClearQueue();

            // Reset Blender state information
            blenderState = new InteropBlenderState();

            // Clear the scene of any synced data from Blender
            Clear();
        }

        /// <summary>
        /// Make sure we teardown our connection on reload to avoid
        /// entering an invalid state between assembly reloads
        /// (as things could be unloaded/reset by dependent scripts)
        /// </summary>
        private void OnBeforeAssemblyReload()
        {
            Teardown();
        }

        /// <summary>
        /// Consume a single message off the interop message queue
        /// </summary>
        /// <returns>Number of bytes read</returns>
        private int ConsumeMessage()
        {
            var disconnected = false;

            var bytesRead = messages.Read((target, header, ptr) =>
            {
                // While not connected - only accept connection requests
                if (!IsConnected)
                {
                    if (header.type != RpcRequest.Connect)
                    {
                        Debug.LogWarning($"Unhandled request type {header.type} for {target} - expected RpcRequest.Connect");
                    }

                    blenderState = FastStructure.PtrToStructure<InteropBlenderState>(ptr);
                    OnConnectToBlender();
                    return 0;
                }

                switch (header.type)
                {
                    case RpcRequest.UpdateBlenderState:
                        blenderState = FastStructure.PtrToStructure<InteropBlenderState>(ptr);
                        break;
                    case RpcRequest.Disconnect:
                        // Don't call OnDisconnectFromBlender() from within
                        // the read handler - we want to release the read node
                        // safely first before disposing the connection.
                        disconnected = true;
                        break;

                    // Viewport messages
                    case RpcRequest.AddViewport:
                        AddViewport(
                            target,
                            FastStructure.PtrToStructure<InteropViewport>(ptr)
                        );
                        break;
                    case RpcRequest.RemoveViewport:
                        RemoveViewport(target);
                        break;
                    case RpcRequest.UpdateViewport:
                        GetViewport(target).UpdateFromInterop(
                            FastStructure.PtrToStructure<InteropViewport>(ptr)
                        );
                        break;
                    case RpcRequest.UpdateVisibleObjects:
                        var visibleObjectIds = new int[header.count];
                        FastStructure.ReadArray(visibleObjectIds, ptr, 0, header.count);
                        GetViewport(target).SetVisibleObjects(
                            visibleObjectIds
                        );
                        break;

                    // Object messages
                    case RpcRequest.AddObjectToScene:
                        AddObject(
                            target,
                            FastStructure.PtrToStructure<InteropSceneObject>(ptr)
                        );
                        break;
                    case RpcRequest.RemoveObjectFromScene:
                        RemoveObject(target);
                        break;
                    case RpcRequest.UpdateSceneObject:
                        UpdateObject(
                            target,
                            FastStructure.PtrToStructure<InteropSceneObject>(ptr)
                        );
                        break;
                    case RpcRequest.UpdateTriangles:
                        GetObject(target)
                            .triangles
                            .Resize(header.length)
                            .CopyFrom(ptr, header.index, header.count);
                        break;
                    case RpcRequest.UpdateVertices:
                        GetObject(target)
                            .vertices
                            .Resize(header.length)
                            .CopyFrom(ptr, header.index, header.count);
                        break;
                    case RpcRequest.UpdateNormals:
                        GetObject(target)
                            .normals
                            .Resize(header.length)
                            .CopyFrom(ptr, header.index, header.count);
                        break;
                    case RpcRequest.UpdateUV:
                        GetObject(target)
                            .uv
                            .Resize(header.length)
                            .CopyFrom(ptr, header.index, header.count);
                        break;
                    case RpcRequest.UpdateUV2:
                        GetObject(target)
                            .uv2
                            .Resize(header.length)
                            .CopyFrom(ptr, header.index, header.count);
                        break;
                    case RpcRequest.UpdateUV3:
                        GetObject(target)
                            .uv3
                            .Resize(header.length)
                            .CopyFrom(ptr, header.index, header.count);
                        break;
                    case RpcRequest.UpdateUV4:
                        GetObject(target)
                            .uv4
                            .Resize(header.length)
                            .CopyFrom(ptr, header.index, header.count);
                        break;
                    case RpcRequest.UpdateVertexColors:
                        GetObject(target)
                            .colors
                            .Resize(header.length)
                            .CopyFrom(ptr, header.index, header.count);
                        break;
                    // TODO: ... and so on for weights/bones/etc

                    // Texture messages
                    case RpcRequest.UpdateTexture:
                        GetTexture(target).UpdateFromInterop(
                            FastStructure.PtrToStructure<InteropTexture>(ptr)
                        );
                        break;
                    case RpcRequest.UpdateTextureData:
                        GetTexture(target).CopyFrom(
                            ptr, header.index, header.count, header.length
                        );
                        break;

                    default:
                        Debug.LogWarning($"Unhandled request type {header.type} for {target}");
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
                OnDisconnectFromBlender();
            }

            return bytesRead;
        }

        /// <summary>
        /// Read a batch of messages off the message queue from Blender
        /// </summary>
        private void ConsumeMessages()
        {
            Profiler.BeginSample("Consume Message");

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
        /// Copy the RenderTexture data from the ViewportController into shared memory with Blender.
        ///
        /// <para>
        ///     The <paramref name="pixelsRGB24Func"/> callback is executed IFF we have room in the
        ///     buffer to write - letting us skip the heavy pixel copy operations if the consumer
        ///     is backed up in processing data.
        /// </para>
        /// </summary>
        internal void PublishRenderTexture(ViewportController viewport, Func<byte[]> pixelsRGB24Func)
        {
            if (!IsConnected)
            {
                Debug.LogWarning("Cannot send RT - No connection");
            }

            Profiler.BeginSample("Write wait on pixelsProducer");

            int bytesWritten = pixelsProducer.Write((ptr) => {

                // If we have a node we can write on, actually do the heavy lifting
                // of pulling the pixel data from the RenderTexture (in the callback)
                // and write into the buffer.
                var pixelsRGB24 = pixelsRGB24Func();

                Profiler.BeginSample("Write Pixels into Shared Memory");

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
        #endregion
    }
}
