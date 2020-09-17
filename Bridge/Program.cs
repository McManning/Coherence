
using System;
using System.Runtime.InteropServices;

#if BUILD_DLL
using RGiesecke.DllExport;
#else
[AttributeUsage(AttributeTargets.Method)]
class DllExportAttribute : Attribute
{
    // public CallingConvention CallingConvention;

    public DllExportAttribute(string name = null)
    {
        // noop
    }
}
#endif

namespace UnityBlenderBridge
{
    class Program
    {
        public static Bridge Bridge { get; } = new Bridge();

        public static string LastError { get; private set; }

        #region Bridge Management API

        public static void SetLastError(Exception e)
        {
            InteropLogger.Error(e.ToString());
            LastError = e.ToString();
        }

        [DllExport]
        [return: MarshalAs(UnmanagedType.LPStr)]
        public static string GetLastError()
        {
            return LastError;
        }
        
        [DllExport]
        public static int Version()
        {
            return 14;
        }

        /// <summary>
        /// Create the shared memory space and protocols between Blender and Unity
        /// and start listening for requests.
        /// </summary>
        [DllExport]
        public static void Start()
        {
            Bridge.Start();
        }
        
        /// <summary>
        /// Dispose shared memory and shutdown communication to Unity
        /// </summary>
        [DllExport]
        public static void Shutdown()
        {
            Bridge.Shutdown();
        }

        /// <summary>
        /// Pull data from read buffers and update local state
        /// </summary>
        [DllExport]
        public static void Update()
        {
            Bridge.Update();
        }
        
        [DllExport]
        public static bool IsConnectedToUnity()
        {
            return Bridge.IsConnected;
        }

        #endregion

        #region Viewport API

        /// <summary>
        /// Create a new viewport to be tracked, designated by unique ID
        /// </summary>
        /// <param name="viewportId"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        [DllExport]
        public static int AddViewport(int viewportId, int width, int height)
        {
            InteropLogger.Debug($"Adding viewport={viewportId} width={width}, height={height}");
            
            try
            {
                Bridge.AddViewport(viewportId, width, height);
                return 1;
            } 
            catch (Exception e)
            {
                SetLastError(e);
                return -1;
            }
        }

        /// <summary>
        /// Remove a viewport by ID
        /// </summary>
        /// <param name="viewportId"></param>
        [DllExport]
        public static int RemoveViewport(int viewportId)
        {
            InteropLogger.Debug($"Removing viewport={viewportId}");
            
            try
            {
                Bridge.RemoveViewport(viewportId);
                return 1;
            } 
            catch (Exception e)
            {
                SetLastError(e);
                return -1;
            }
        }
        
        [DllExport]
        public static int SetViewportCamera(int viewportId, InteropCamera camera)
        {
            try {
                var viewport = Bridge.GetViewport(viewportId);

                if (!camera.Equals(viewport.data.camera))
                {
                    InteropLogger.Debug($"Set Camera viewport={viewportId} camera={camera}");

                    viewport.data.camera = camera;
                    Bridge.SendEntity(RpcRequest.UpdateViewport, viewport);
                }
                
                return 1;
            }
            catch (Exception e)
            {
                SetLastError(e);
                return -1;
            }
        }

        [DllExport]
        public static int ResizeViewport(int viewportId, int width, int height)
        {
            InteropLogger.Debug($"Resizing viewport={viewportId} width={width}, height={height}");

            try
            {
                var viewport = Bridge.GetViewport(viewportId);
                viewport.Resize(width, height);
                
                Bridge.SendEntity(RpcRequest.UpdateViewport, viewport);
                return 1;
            } 
            catch (Exception e)
            {
                SetLastError(e);
                return -1;
            }
        }

        /// <summary>
        /// Set the list of object IDs visible from the specific viewport
        /// </summary>
        /// <param name="viewportId"></param>
        /// <param name="visibleObjectIds"></param>
        [DllExport]
        public static int SetVisibleObjects(
        #pragma warning disable IDE0060 // Remove unused parameter
            int viewportId, 
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] int[] visibleObjectIds,
            int totalVisibleObjectIds
        #pragma warning restore IDE0060 // Remove unused parameter
        ) {
            InteropLogger.Debug($"Set Visible Objects viewport={viewportId}");
            
            foreach (var i in visibleObjectIds)
            {
                InteropLogger.Debug($" - {i}");
            }

            try
            {
                var viewport = Bridge.GetViewport(viewportId);
                viewport.SetVisibleObjects(visibleObjectIds);
            
                Bridge.SendArray(
                    RpcRequest.UpdateVisibleObjects, 
                    viewport.Name, 
                    viewport.VisibleObjectIds, 
                    false
                );
                return 1;
            } 
            catch (Exception e)
            {
                SetLastError(e);
                return -1;
            }
        }

        [DllExport]
        public static void ConsumeRenderTextures()
        {
            // Experimental control over bridge's pixel buffer consumer. 
            Bridge.ConsumePixels();
        }
        
        /// <summary>
        /// Get the render texture data from Unity's last render of the given viewport.
        /// </summary>
        /// <param name="viewportId"></param>
        /// <returns></returns>
        [DllExport]
        public static RenderTextureData GetRenderTexture(int viewportId)
        {
            try
            {
                var viewport = Bridge.GetViewport(viewportId);
                return viewport.GetRenderTextureData();
            }
            catch (Exception e)
            {
                SetLastError(e);
                return RenderTextureData.Invalid;
            }
        }

        #endregion 

        #region Scene Management API

        [DllExport]
        public static int AddMeshObjectToScene(
            int objectId, 
            [MarshalAs(UnmanagedType.LPStr)] string displayName, 
            InteropMatrix4x4 transform
        ) {
            InteropLogger.Debug($"Adding mesh {displayName} ({objectId}) to the scene");

            try
            {
                var obj = new SceneObject(objectId, displayName, SceneObjectType.Mesh);
                obj.SetTransform(transform);
                Bridge.Scene.AddObject(obj);

                Bridge.SendEntity(RpcRequest.AddObjectToScene, obj);
                return 1;
            }
            catch (Exception e)
            {
                SetLastError(e);
                return -1;
            }
        }

        [DllExport]
        public static int SetObjectTransform(int objectId, InteropMatrix4x4 transform)
        {
            try
            {
                var obj = Bridge.Scene.GetObject(objectId);
                obj.SetTransform(transform);

                Bridge.SendEntity(RpcRequest.UpdateSceneObject, obj);
                return 1;
            }
            catch (Exception e)
            {
                SetLastError(e);
                return -1;
            }
        }
        
        [DllExport]
        public static int RemoveObjectFromScene(int objectId)
        {
            InteropLogger.Debug($"Removing object {objectId} from the scene");

            try
            {
                var obj = Bridge.Scene.RemoveObject(objectId);
                Bridge.SendEntity(RpcRequest.RemoveObjectFromScene, obj);
                return 1;
            }
            catch (Exception e)
            {
                SetLastError(e);
                return -1;
            }
        }

        #endregion 

        #region Mesh Data API

        /// <summary>
        /// Read an array of <see cref="MVert"/> from Blender to push updated
        /// vertex coordinates and normals with Unity.
        /// </summary>
        /// <param name="objectId"></param>
        /// <param name="loops"></param>
        /// <param name="loopCount"></param>
        /// <param name="vertices"></param>
        /// <param name="verticesCount"></param>
        /// <returns></returns>
        [DllExport]
        public static int CopyVertices(
        #pragma warning disable IDE0060 // Remove unused parameter
            int objectId,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] MLoop[] loops,
            uint loopCount,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 4)] MVert[] vertices,
            uint verticesCount
        #pragma warning restore IDE0060 // Remove unused parameter
        ) {
            InteropLogger.Debug($"Copy {loops.Length} loops and {vertices.Length} vertices for {objectId}");

            try
            {
                var obj = Bridge.Scene.GetObject(objectId);

                obj.CopyFromMVerts(vertices);
                obj.CopyFromMLoops(loops);
                
                // If the vertex count changes, we'll need to push a 
                // change to the object's metadata
                if (obj.data.vertexCount != obj.Vertices.Length)
                {
                    obj.data.vertexCount = obj.Vertices.Length; 
                    Bridge.SendEntity(RpcRequest.UpdateSceneObject, obj);
                }

                // Followed by changes to the vertex coordinate and normals
                Bridge.SendArray(RpcRequest.UpdateVertices, obj.Name, obj.Vertices, true);
                Bridge.SendArray(RpcRequest.UpdateNormals, obj.Name, obj.Normals, true);
            
                return 1;
            }
            catch (Exception e)
            {
                SetLastError(e);
                return -1;
            }
        }
        
        /// <summary>
        /// Read an array of <see cref="MLoopTri"/> from Blender to push updated
        /// triangle indices to Unity
        /// </summary>
        /// <param name="objectId"></param>
        /// <param name="loopTris"></param>
        /// <param name="loopTrisCount"></param>
        /// <returns></returns>
        [DllExport]
        public static int CopyLoopTriangles(
        #pragma warning disable IDE0060 // Remove unused parameter
            int objectId,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] MLoopTri[] loopTris,
            uint loopTrisCount
        #pragma warning restore IDE0060 // Remove unused parameter
        ) {
            InteropLogger.Debug($"Copy {loopTris.Length} loop triangles for {objectId}");

            try
            {
                var obj = Bridge.Scene.GetObject(objectId);

                obj.CopyFromMLoopTris(loopTris);

                // If the vertex count changes, we'll need to push a 
                // change to the object's metadata
                if (obj.data.triangleCount != obj.Triangles.Length)
                {
                    obj.data.triangleCount = obj.Triangles.Length; 
                    Bridge.SendEntity(RpcRequest.UpdateSceneObject, obj);
                }

                Bridge.SendArray(RpcRequest.UpdateTrianges, obj.Name, obj.Triangles, true);
            
                return 1;
            }
            catch (Exception e)
            {
                SetLastError(e);
                return -1;
            }
        }

        #endregion      
    }
}
