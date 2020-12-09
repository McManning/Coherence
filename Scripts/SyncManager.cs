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
        public bool IsSetup { get; private set; }

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
        OrderedDictionary viewports = new OrderedDictionary();

        Dictionary<string, ObjectController> objects = new Dictionary<string, ObjectController>();

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
        /// Connect to the shared memory hosted by Blender and send an initial hello.
        ///
        /// Upon failure, this disconnects us.
        /// </summary>
        /// <returns></returns>
        public bool Setup()
        {
            if (IsSetup)
            {
                return true;
            }

            Debug.Log("Setting up shared memory space");

            var settings = CoherenceSettings.Instance;
            var name = settings.bufferName;

            try
            {
                messages = new InteropMessenger();

                messages.ConnectAsMaster(
                    name + BLENDER_MESSAGES_BUFFER,
                    name + UNITY_MESSAGES_BUFFER,
                    settings.nodeCount,
                    settings.NodeSizeBytes
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

            IsSetup = true;
            return true;
        }

        /// <summary>
        /// Destroy the shared memory space and temporary scene objects
        /// </summary>
        public void Teardown()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            EditorApplication.update -= OnEditorUpdate;

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

            IsSetup = false;

            Debug.Log("Tearing down shared memory space");

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
            if (IsSetup)
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

            // This is our equivalent to a "Connect" button in the UI for now.
            /*if (Input.GetKeyDown(KeyCode.Space))
            {
                Connect();
            } */
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
                viewportsContainer = new GameObject("Viewports");
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
                objectsContainer = new GameObject("Objects");
                objectsContainer.transform.parent = transform;
            }

            var prefab = CoherenceSettings.Instance.sceneObjectPrefab;
            var go = prefab ? Instantiate(prefab) : new GameObject();

            go.name = name;
            go.transform.parent = objectsContainer.transform;

            var controller = go.AddComponent<ObjectController>();

            objects[name] = controller;
            controller.UpdateFromInterop(iso);

            return controller;
        }

        private void RemoveObject(string name)
        {
            if (!objects.ContainsKey(name))
            {
                return;
            }

            var instance = objects[name];

            DestroyImmediate(instance.gameObject);
            objects.Remove(name);
        }

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
        /// Make sure we teardown our connection on reload to avoid an invalid state.
        ///
        /// Hopefully (eventually) we can support keeping the connection alive during reloads.
        /// But right now there's too many uncertainties to do that safely.
        /// </summary>
        private void OnBeforeAssemblyReload()
        {
            Debug.LogWarning("Tearing down Coherence shared memory space due to assembly reload");
            Teardown();
        }

        /// <summary>
        /// Handle any messages coming from Blender
        /// </summary>
        private void ConsumeMessages()
        {
            Profiler.BeginSample("Consume Message");

            var disconnected = false;

            // TODO: Some messages should be skipped if !IsConnected.
            // Otherwise we may get a bad state. E.g. we see a disconnect
            // from Blender and THEN some other viewport/object data messages.

            messages.Read((target, header, ptr) =>
            {
                ObjectController obj;

                switch (header.type)
                {
                    case RpcRequest.Connect:
                        blenderState = FastStructure.PtrToStructure<InteropBlenderState>(ptr);
                        OnConnectToBlender();
                        break;
                    case RpcRequest.UpdateBlenderState:
                        blenderState = FastStructure.PtrToStructure<InteropBlenderState>(ptr);
                        break;
                    case RpcRequest.Disconnect:
                        // Don't call OnDisconnectFromBlender() from within
                        // the read handler - we want to release the read node
                        // safely first before disposing the connection.
                        disconnected = true;
                        break;
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
                        GetObject(target).UpdateFromInterop(
                            FastStructure.PtrToStructure<InteropSceneObject>(ptr)
                        );
                        break;
                    case RpcRequest.UpdateTriangles:
                        obj = GetObject(target);
                        FastStructure.ReadArray(obj.GetOrCreateTriangleBuffer(), ptr, header.index, header.count);
                        obj.OnUpdateTriangleRange(header.index, header.count);
                        break;
                    case RpcRequest.UpdateVertices:
                        obj = GetObject(target);
                        FastStructure.ReadArray(obj.GetOrCreateVertexBuffer(), ptr, header.index, header.count);
                        obj.OnUpdateVertexRange(header.index, header.count);
                        break;
                    case RpcRequest.UpdateNormals:
                        obj = GetObject(target);
                        FastStructure.ReadArray(obj.GetOrCreateNormalBuffer(), ptr, header.index, header.count);
                        obj.OnUpdateNormalRange(header.index, header.count);
                        break;
                    // TODO: ... and so on for UV/weights
                    default:
                        Debug.LogWarning($"Unhandled request type {header.type} for {target}");
                        break;
                }

                // TODO: Necessary to count bytes? We won't read anything off this
                // buffer at this point so it's safe to drop the whole thing.
                return 0;
            });

            // Handle any disconnects that may have occured during the read
            if (disconnected)
            {
                OnDisconnectFromBlender();
            }

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
    }
}
