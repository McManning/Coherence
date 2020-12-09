
using System;
using System.Runtime.InteropServices;

#if !MOCKING
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

namespace Coherence
{
    /// <summary>
    /// API exposed the Python addon for Blender.
    /// </summary>
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

        /// <summary>
        /// Get a Python-friendly string representation of the last exception
        /// that occurred internally - for handling outside of the library.
        /// </summary>
        /// <returns></returns>
        [DllExport]
        [return: MarshalAs(UnmanagedType.LPStr)]
        public static string GetLastError()
        {
            return LastError;
        }

        /// <summary>
        /// Test case to ensure library compilation worked
        /// </summary>
        /// <returns></returns>
        [DllExport]
        public static int Fourteen()
        {
            return 14;
        }

        /// <summary>
        /// Connect to a shared memory space hosted by Unity and sync scene data
        /// </summary>
        /// <param name="connectionName">Common name for the shared memory space between Blender and Unity</param>
        [DllExport]
        public static int Connect(
            [MarshalAs(UnmanagedType.LPStr)] string connectionName,
            [MarshalAs(UnmanagedType.LPStr)] string versionInfo
        ) {
            try
            {
                if (Bridge.Connect(connectionName, versionInfo))
                {
                    return 1;
                }

                // Failed to start
                return 0;
            }
            catch (Exception e)
            {
                SetLastError(e);
                return -1;
            }
        }

        /// <summary>
        /// Dispose shared memory and shutdown communication to Unity
        /// </summary>
        [DllExport]
        public static int Disconnect()
        {
            try
            {
                Bridge.Disconnect();
                return 1;
            }
            catch (Exception e)
            {
                SetLastError(e);
                return -1;
            }
        }

        /// <summary>
        /// Clear all cached data from the bridge (scene objects, viewports, etc).
        ///
        /// This should NOT be called while connected.
        /// </summary>
        [DllExport]
        public static int Clear()
        {
            try
            {
                Bridge.Clear();
                return 1;
            }
            catch (Exception e)
            {
                SetLastError(e);
                return -1;
            }
        }

        /// <summary>
        /// Pull data from read buffers and update local state
        /// </summary>
        [DllExport]
        public static int Update()
        {
            try
            {
                Bridge.Update();
                return 1;
            }
            catch (Exception e)
            {
                SetLastError(e);
                return -1;
            }
        }

        [DllExport]
        public static bool IsConnectedToUnity()
        {
            return Bridge.IsConnectedToUnity;
        }

        [DllExport]
        public static bool IsConnectedToSharedMemory()
        {
            return Bridge.IsConnectedToSharedMemory;
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
        public static int AddViewport(int viewportId)
        {
            InteropLogger.Debug($"Adding viewport={viewportId}");

            try
            {
                Bridge.AddViewport(viewportId);
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
                    viewport.CameraFromInterop(camera);
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

        /// <summary>
        /// Experimental control over bridge's pixel buffer consumer.
        /// </summary>
        [DllExport]
        public static int ConsumeRenderTextures()
        {
            try
            {
                Bridge.ConsumePixels();
                return 1;
            }
            catch (Exception e)
            {
                SetLastError(e);
                return -1;
            }
        }

        /// <summary>
        /// Get the render texture data from Unity's last render of the given viewport
        /// and lock the texture data from writes until UnlockRenderTexture() is called.
        /// </summary>
        /// <param name="viewportId"></param>
        /// <returns></returns>
        [DllExport]
        public static RenderTextureData GetRenderTexture(int viewportId)
        {
            Viewport viewport;
            try
            {
                viewport = Bridge.GetViewport(viewportId);
            }
            catch (Exception e)
            {
                SetLastError(e);
                return RenderTextureData.Invalid;
            }

            try
            {
                var rt = viewport.GetRenderTextureAndLock();
                return rt;
            }
            catch (Exception e)
            {
                viewport.ReleaseRenderTextureLock();
                SetLastError(e);
                return RenderTextureData.Invalid;
            }
        }

        [DllExport]
        public static int ReleaseRenderTextureLock(int viewportId)
        {
            Viewport viewport;
            try
            {
                viewport = Bridge.GetViewport(viewportId);
                viewport.ReleaseRenderTextureLock();
            }
            catch (Exception e)
            {
                SetLastError(e);
                return -1;
            }

            return 1;
        }

        #endregion

        #region Scene Management API

        [DllExport]
        public static int AddMeshObjectToScene(
            [MarshalAs(UnmanagedType.LPStr)] string name,
            InteropMatrix4x4 transform,
            [MarshalAs(UnmanagedType.LPStr)] string material
        ) {
            InteropLogger.Debug($"Adding mesh <name={name}, material={material}>");

            try
            {
                var obj = new SceneObject(name, SceneObjectType.Mesh);
                obj.Transform = transform;
                obj.Material = material;

                Bridge.AddObject(obj);
                return 1;
            }
            catch (Exception e)
            {
                SetLastError(e);
                return -1;
            }
        }

        /// <summary>
        /// Update <see cref="InteropSceneObject.transform" /> and notify Unity
        /// </summary>
        [DllExport]
        public static int SetObjectTransform(
            [MarshalAs(UnmanagedType.LPStr)] string name,
            InteropMatrix4x4 transform
        ) {
            try
            {
                var obj = Bridge.GetObject(name);
                obj.Transform = transform;

                Bridge.SendEntity(RpcRequest.UpdateSceneObject, obj);
                return 1;
            }
            catch (Exception e)
            {
                SetLastError(e);
                return -1;
            }
        }

        /// <summary>
        /// Update <see cref="InteropSceneObject.material" /> and notify Unity
        /// </summary>
        /// <param name="objectId"></param>
        /// <param name="transform"></param>
        /// <param name="material"></param>
        /// <returns></returns>
        [DllExport]
        public static int SetObjectMaterial(
            [MarshalAs(UnmanagedType.LPStr)] string name,
            [MarshalAs(UnmanagedType.LPStr)] string material
        ) {
            try
            {
                var obj = Bridge.GetObject(name);

                if (material != obj.Material)
                {
                    obj.Material = material;
                    Bridge.SendEntity(RpcRequest.UpdateSceneObject, obj);
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
        public static int RemoveObjectFromScene(
            [MarshalAs(UnmanagedType.LPStr)] string name
        ) {
            InteropLogger.Debug($"Removing object {name} from the scene");

            try
            {
                Bridge.RemoveObject(name);
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
            [MarshalAs(UnmanagedType.LPStr)] string name,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] MLoop[] loops,
            uint loopCount,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 4)] MVert[] vertices,
            uint verticesCount
        #pragma warning restore IDE0060 // Remove unused parameter
        ) {
            InteropLogger.Debug($"Copy {loops.Length} loops and {vertices.Length} vertices for `{name}`");

            try
            {
                var obj = Bridge.GetObject(name);

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
            [MarshalAs(UnmanagedType.LPStr)] string name,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] MLoopTri[] loopTris,
            uint loopTrisCount
        #pragma warning restore IDE0060 // Remove unused parameter
        ) {
            InteropLogger.Debug($"Copy {loopTris.Length} loop triangles for `{name}`");

            try
            {
                var obj = Bridge.GetObject(name);

                obj.CopyFromMLoopTris(loopTris);

                // If the vertex count changes, we'll need to push a
                // change to the object's metadata
                if (obj.data.triangleCount != obj.Triangles.Length)
                {
                    obj.data.triangleCount = obj.Triangles.Length;
                    Bridge.SendEntity(RpcRequest.UpdateSceneObject, obj);
                }

                Bridge.SendArray(RpcRequest.UpdateTriangles, obj.Name, obj.Triangles, true);

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
