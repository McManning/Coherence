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
    public delegate void OnCoherenceEvent();

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
        OrderedDictionary Viewports { get; } = new OrderedDictionary();

        /// <summary>
        /// Instances of synced objects. Mapped by their unique object name.
        /// </summary>
        Dictionary<string, ObjectController> Objects { get; } = new Dictionary<string, ObjectController>();

        /// <summary>
        /// Instances of synced mesh data. Mapped by their unique mesh IDs.
        /// </summary>
        Dictionary<string, MeshController> Meshes { get; } = new Dictionary<string, MeshController>();

        /// <summary>
        /// Components that can receive events. Mapped by their "objectName:componentName"
        /// identifier for quick targetting of inbound messages.
        /// </summary>
        Dictionary<string, IComponent> Components { get; } = new Dictionary<string, IComponent>();

        //public Dictionary<string, HashSet<OnCoherenceEvent>> EventDelegates { get; } = new Dictionary<string, HashSet<OnCoherenceEvent>>();

        internal event OnCoherenceEvent OnCoherenceConnected;
        internal event OnCoherenceEvent OnCoherenceDisconnected;
        internal event OnCoherenceEvent OnCoherenceEnabled;
        internal event OnCoherenceEvent OnCoherenceDisabled;

        /// <summary>
        /// Objects added with parents that are not (yet) in the scene.
        /// </summary>
        readonly HashSet<ObjectController> orphans = new HashSet<ObjectController>();

        /// <summary>
        /// Current index in <see cref="Viewports"/> to use for
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
        public void Setup()
        {
            if (IsRunning)
            {
                return;
            }

            gameObject.transform.parent = null;
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

            // Notify listeners that this is ready to go
            OnCoherenceEnabled?.Invoke();
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

            // Notify listeners we're shutting down
            OnCoherenceDisabled?.Invoke();

            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            EditorApplication.update -= OnEditorUpdate;
            SceneManager.activeSceneChanged -= OnSceneUnloaded;
            EditorSceneManager.activeSceneChangedInEditMode -= OnSceneUnloaded;

            // Notify Blender if we're shutting down from the Unity side
            if (IsConnected)
            {
                messages.WriteDisconnect();
                OnDisconnected();
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
            foreach (var obj in Objects.Values)
            {
                obj.Sync();
            }

            foreach (var mesh in Meshes.Values)
            {
                mesh.Sync();
            }

            foreach (var component in Components.Values)
            {
                component.Sync();
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
            // Safely destroy all instantiated components
            foreach (var component in Components.Values)
            {
                component.DestroyCoherenceComponent();
            }

            Components.Clear();

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

            Viewports.Clear();
            Objects.Clear();
            Meshes.Clear();
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
            if (Viewports.Count < 1)
            {
                return;
            }

            viewportIndex = (viewportIndex + 1) % Viewports.Count;

            var viewport = Viewports[viewportIndex] as ViewportController;
            PublishRenderTexture(viewport, viewport.CaptureRenderTexture);
        }

        private ViewportController GetViewport(string name)
        {
            if (!Viewports.Contains(name))
            {
                throw new Exception($"Viewport {name} does not exist");
            }

            return Viewports[name] as ViewportController;
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

            Viewports[name] = controller;
            return controller;
        }

        private void RemoveViewport(string name)
        {
            if (!Viewports.Contains(name))
            {
                return;
            }

            var viewport = Viewports[name] as ViewportController;

            DestroyImmediate(viewport.gameObject);
            Viewports.Remove(name);

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

        #region Meshes

        internal MeshController GetOrCreateMesh(string name)
        {
            if (!Meshes.ContainsKey(name))
            {
                var mesh = new MeshController(name);
                Meshes[name] = mesh;
                return mesh;
            }

            return Meshes[name];
        }

        #endregion

        #region Components

        private IComponent GetCoherenceComponent(string name)
        {
            if (!Components.ContainsKey(name))
            {
                throw new Exception($"Component {name} does not exist");
            }

            return Components[name];
        }

        #endregion

        #region Objects

        internal ObjectController GetObject(string name)
        {
            if (!Objects.ContainsKey(name))
            {
                throw new Exception($"Object {name} does not exist");
            }

            return Objects[name];
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

            // TODO: prefab per object type mapping

            var prefab = CoherenceSettings.Instance.sceneObjectPrefab;
            var go = prefab ? Instantiate(prefab) : new GameObject();
            var controller = go.AddComponent<ObjectController>();

            go.name = name;

            Objects[name] = controller;
            controller.UpdateFromInterop(iso);

            ReparentObject(controller);

            // Attach any orphaned objects that were waiting for this parent to be added.
            CheckOrphanedObjects();

            return controller;
        }

        private void RemoveObject(string name)
        {
            if (!Objects.ContainsKey(name))
            {
                return;
            }

            var instance = Objects[name];

            // Move children to the main container and orphan them.
            var children = instance.GetComponentsInChildren<ObjectController>();
            foreach (var child in children)
            {
                child.transform.parent = objectsContainer.transform;
                orphans.Add(child);
            }

            DestroyImmediate(instance.gameObject);
            Objects.Remove(name);
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

            if (Objects.TryGetValue(parentName, out ObjectController match))
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
                if (Objects.TryGetValue(parentName, out ObjectController parent))
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
        private void OnConnected()
        {
            Debug.Log("Connected to Blender");

            IsConnected = true;

            // Send our state/settings to Blender to sync up
            messages.Queue(RpcRequest.Connect, Application.unityVersion, ref unityState);

            // Notify listeners
            OnCoherenceConnected?.Invoke();
        }

        /// <summary>
        /// Cleanup any lingering blender data from the scene (viewport cameras, meshes, etc)
        /// </summary>
        private void OnDisconnected()
        {
            Debug.Log("Disconnected from Blender");
            IsConnected = false;

            // Clear any messages still queued to send
            messages.ClearQueue();

            // Reset Blender state information
            blenderState = new InteropBlenderState();

            // Notify listeners
            OnCoherenceDisconnected?.Invoke();

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
                    OnConnected();
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
                    case RpcRequest.AddObject:
                        AddObject(
                            target,
                            FastStructure.PtrToStructure<InteropSceneObject>(ptr)
                        );
                        break;
                    case RpcRequest.RemoveObject:
                        RemoveObject(target);
                        break;
                    case RpcRequest.UpdateObject:
                        UpdateObject(
                            target,
                            FastStructure.PtrToStructure<InteropSceneObject>(ptr)
                        );
                        break;

                    // Component messages.
                    // Target is in the form of `obj_name:component_name`, mapped to our components dict.
                    case RpcRequest.AddComponent:
                        var component = FastStructure.PtrToStructure<InteropComponent>(ptr);
                        var instance = ComponentInfo.Find(component.name).Instantiate(this, component);

                        Components.Add(target, instance);
                        Debug.Log($"added component {target} for {component.target}");
                        break;
                    case RpcRequest.DestroyComponent:
                        GetCoherenceComponent(target).DestroyCoherenceComponent();
                        Components.Remove(target);
                        break;
                    case RpcRequest.UpdateComponent:
                        component = FastStructure.PtrToStructure<InteropComponent>(ptr);
                        GetCoherenceComponent(target).DispatchUpdate(component);
                        break;
                    case RpcRequest.ComponentMessage:
                        var msg = FastStructure.PtrToStructure<InteropComponentMessage>(ptr);
                        GetCoherenceComponent(target).GetCoherenceState()
                            .DispatchNetworkEvent(msg.id, msg.size, msg.data);
                        break;
                    case RpcRequest.UpdateProperties:
                        var data = GetCoherenceComponent(target).GetCoherenceState();
                        data.RemoteProperties
                            .Resize(header.length)
                            .CopyFrom(ptr, header.index, header.count);
                        data.DispatchRemotePropertyUpdates();
                        break;

                    // Mesh messages
                    case RpcRequest.UpdateTriangles:
                        GetOrCreateMesh(target)
                            .triangles
                            .Resize(header.length)
                            .CopyFrom(ptr, header.index, header.count);
                        break;
                    case RpcRequest.UpdateVertices:
                        GetOrCreateMesh(target)
                            .vertices
                            .Resize(header.length)
                            .CopyFrom(ptr, header.index, header.count);
                        break;
                    case RpcRequest.UpdateNormals:
                        GetOrCreateMesh(target)
                            .normals
                            .Resize(header.length)
                            .CopyFrom(ptr, header.index, header.count);
                        break;
                    case RpcRequest.UpdateUV:
                        GetOrCreateMesh(target)
                            .uv
                            .Resize(header.length)
                            .CopyFrom(ptr, header.index, header.count);
                        break;
                    case RpcRequest.UpdateUV2:
                        GetOrCreateMesh(target)
                            .uv2
                            .Resize(header.length)
                            .CopyFrom(ptr, header.index, header.count);
                        break;
                    case RpcRequest.UpdateUV3:
                        GetOrCreateMesh(target)
                            .uv3
                            .Resize(header.length)
                            .CopyFrom(ptr, header.index, header.count);
                        break;
                    case RpcRequest.UpdateUV4:
                        GetOrCreateMesh(target)
                            .uv4
                            .Resize(header.length)
                            .CopyFrom(ptr, header.index, header.count);
                        break;
                    case RpcRequest.UpdateVertexColors:
                        GetOrCreateMesh(target)
                            .colors
                            .Resize(header.length)
                            .CopyFrom(ptr, header.index, header.count);
                        break;
                    // TODO: ... and so on for weights/bones/etc

                    case RpcRequest.UpdateMesh:
                        GetOrCreateMesh(target).UpdateFromInterop(
                            FastStructure.PtrToStructure<InteropMesh>(ptr)
                        );
                        break;

                    // Image sync messages
                    case RpcRequest.UpdateImage:
                        GetTexture(target).UpdateFromInterop(
                            FastStructure.PtrToStructure<InteropImage>(ptr)
                        );
                        break;
                    case RpcRequest.UpdateImageData:
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
                OnDisconnected();
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

        internal void SendArray<T>(RpcRequest type, string target, IArray<T> buffer) where T : struct
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
        internal void SendEntity<T>(RpcRequest type, IInteropSerializable<T> entity) where T : struct
        {
            if (!IsConnected)
            {
                return;
            }

            var data = entity.Serialize();
            messages.ReplaceOrQueue(type, entity.Name, ref data);
        }

        #endregion
    }
}
