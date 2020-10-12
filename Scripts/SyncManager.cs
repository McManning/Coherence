﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Profiling.Memory.Experimental;
using UnityEngine.Rendering;
using SharedMemory;

namespace Coherence
{
    /// <summary>
    /// Handles communication between Blender and Unity and passes
    /// the appropriate data to subcomponents for internal processing.
    /// </summary>
    public class SyncManager : MonoBehaviour
    {
        const int SYNC_API_VERSION = 1;

        const string VIEWPORT_IMAGE_BUFFER_ID = "UnityViewportImage";

        // Pay close attention - these are flipped from the Bridge DLL's constants. 
        const string MESSAGE_PRODUCER_ID = "UnityMessages";
        const string MESSAGE_CONSUMER_ID = "BlenderMessages";
    
        /// <summary>
        /// How long to wait for a writable node to become available (milliseconds)
        /// 
        /// This directly affects Unity's framerate. 
        /// </summary>
        const int WRITE_WAIT = 0;

        public bool IsConnected { get; private set; }

        [Tooltip(
            "Camera prefab to instantiate for each synced Blender viewport.\n\n" + 
            "Transform and projection will be automatically updated to " + 
            "match Blender's viewport settings."
        )] public GameObject viewportCameraPrefab;

        [Tooltip(
            "GO prefab to instantiate for each synced Blender object.\n\n" +
            "Additional components may be added to the object based on its type " +
            "within Blender - e.g. adding Mesh Renderers"
        )]  public GameObject sceneObjectPrefab;

        /// <summary>
        /// Shared memory we write RenderTexture pixel data into
        /// </summary>
        CircularBuffer pixelsProducer;
    
        InteropMessenger messages;

        /// <summary>
        /// Viewport controller to match Blender's viewport configuration
        /// </summary>
        OrderedDictionary viewports;
    
        Dictionary<string, ObjectController> objects = new Dictionary<string, ObjectController>();

        /// <summary>
        /// Current index in <see cref="viewports"/> to use for  
        /// sending a render texture to Blender
        /// </summary>
        int viewportIndex;

        SceneManager scene;
        GameObject viewportsContainer;
        GameObject objectsContainer;
    
        /// <summary>
        /// State information reported to Blender on connect
        /// </summary>
        InteropUnityState unityState;

        // Start is called before the first frame update
        private void OnEnable()
        {
            viewports = new OrderedDictionary();

            unityState = new InteropUnityState
            {
                version = SYNC_API_VERSION
            };
        }
    
        private void OnDisable()
        {
            if (IsConnected)
            {
                messages.WriteDisconnect();
                OnDisconnectFromBlender();
            }
        }
    
        private void Update()
        {
            if (IsConnected)
            {
                // SendRequestToBlender(RpcRequest.SyncBlenderState);
                messages.ProcessQueue();
                ConsumeMessages();

                PublishNextRenderTexture();
            }

            // This is our equivalent to a "Connect" button in the UI for now.
            if (Input.GetKeyDown(KeyCode.Space))
            {
                if (!IsConnected)
                {
                    SetupConnection();
                }
                else
                {
                    messages.Queue(RpcRequest.UpdateUnityState, Application.version, ref unityState);
                    // SendRequestWithDataToBlender(RpcRequest.UpdateUnityState, ref unityState);
                }
            }
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
                viewportsContainer = new GameObject("Blender Viewports");
            }

            var go = (viewportCameraPrefab) ? Instantiate(viewportCameraPrefab) : new GameObject();

            go.name = $"Viewport {name}";
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

            Destroy(viewport.gameObject);
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
                objectsContainer = new GameObject("Blender Objects");
            }
        
            var go = (sceneObjectPrefab) ? Instantiate(sceneObjectPrefab) : new GameObject();

            go.name = $"{name} (ID: {iso.id})";
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

            Destroy(instance.gameObject);
            objects.Remove(name);
        }
    
        private void OnConnectToBlender()
        {
            if (viewportsContainer == null)
            {
                viewportsContainer = new GameObject("Blender Viewports");
            }

            if (scene == null)
            {
                var go = new GameObject("Blender Scene");
                scene = go.AddComponent<SceneManager>();
            }
        }

        /// <summary>
        /// Close all shared memory buffers with Blender
        /// </summary>
        private void OnDisconnectFromBlender()
        {
            Debug.Log("Disconnected from Blender");
            IsConnected = false;

            pixelsProducer?.Dispose();
            pixelsProducer = null;
        
            messages?.Dispose();
            messages = null;

            if (scene != null)
            {
                Destroy(scene.gameObject);
                scene = null;
            }

            if (viewportsContainer != null)
            {
                Destroy(viewportsContainer);
                viewportsContainer = null;
            }
        }

        /// <summary>
        /// Connect to the shared memory hosted by Blender and send an initial hello.
        /// 
        /// Upon failure, this disconnects us. 
        /// </summary>
        /// <returns></returns>
        bool SetupConnection()
        {
            if (IsConnected)
            {
                return true;
            }

            Debug.Log("Establishing new connection");

            try 
            {
                pixelsProducer = new CircularBuffer(VIEWPORT_IMAGE_BUFFER_ID);

                messages = new InteropMessenger();
                messages.ConnectAsSlave(MESSAGE_CONSUMER_ID, MESSAGE_PRODUCER_ID);
            
                IsConnected = true;

                messages.Queue(RpcRequest.Connect, Application.unityVersion, ref unityState);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to setup RPC Buffer: {e.Message}");
                OnDisconnectFromBlender();
                throw;
            }

            return true;
        }

        private void ConsumeMessages()
        {
            if (!SetupConnection())
            {
                Debug.LogWarning("Cannot consume messages - No connection");
            }
        
            Profiler.BeginSample("Consume Message");

            messages.Read((target, header, ptr) =>
            {
                ObjectController obj;

                switch (header.type)
                {
                    case RpcRequest.Connect:
                        OnConnectToBlender();
                        break;
                    case RpcRequest.Disconnect:
                        // Don't call OnDisconnectFromBlender() from within 
                        // the read handler - we want to release the read node
                        // safely first before disposing the connection.
                        IsConnected = false;
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

                return 0; // TODO: :\
            });

            // If we disconnected during the read, notify.
            if (!IsConnected)
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
            if (!SetupConnection())
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

                InteropLogger.Debug($"Writing {pixelsRGB24.Length} bytes with meta {header.width} x {header.height} and pix 0 is " + 
                    $"{pixelsRGB24[0]}, {pixelsRGB24[1]}, {pixelsRGB24[2]}"
                );

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
