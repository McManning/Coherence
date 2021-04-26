
using System;
using System.Runtime.InteropServices;
using Coherence.BlenderDNA;

#if !MOCKING
using RGiesecke.DllExport;
using SharedMemory;
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
        public static Bridge Bridge {
            get => Bridge.Instance;
        }

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

                throw new NotImplementedException("TODO: Reimplement");
                /*
                Bridge.SendArray(
                    RpcRequest.UpdateVisibleObjects,
                    viewport.Name,
                    viewport.VisibleObjectIds,
                    false
                );*/
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
        /// and lock the texture data from writes until <see cref="ReleaseRenderTextureLock(int)"/>.
        /// </summary>
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

        /// <summary>
        /// Release the lock on a texture previously retrieved through <see cref="GetRenderTexture(int)"/>
        /// </summary>
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
        public static int AddObjectToScene(
            [MarshalAs(UnmanagedType.LPStr)] string name,
            [MarshalAs(UnmanagedType.LPStr)] string kind,
            InteropTransform transform
        ) {
            InteropLogger.Debug($"Adding object <name={name}, kind={kind}>");

            try
            {
                var obj = new SceneObject(name, kind, transform);

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
            InteropTransform transform
        ) {
            try
            {
                var obj = Bridge.GetObject(name);
                obj.data.transform = transform;

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
        /// Update scene object properties and notify Unity
        /// </summary>
        [DllExport]
        public static int UpdateObjectProperties(
            [MarshalAs(UnmanagedType.LPStr)] string name,
            ObjectDisplayMode display,
            [MarshalAs(UnmanagedType.LPStr)] string mesh,
            [MarshalAs(UnmanagedType.LPStr)] string material
        ) {
            try
            {
                var obj = Bridge.GetObject(name);

                var interopData = obj.data;

                InteropLogger.Debug($"{name} - will it send? {interopData.display}, {interopData.mesh}, {interopData.material}");

                interopData.display = display;
                interopData.mesh = mesh;
                interopData.material = material;

                if (!interopData.Equals(obj.data))
                {
                    InteropLogger.Debug($"{name} - it do! {display}, {mesh}, {material}");
                    obj.data = interopData;

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

        #region Texture Sync API

        /// <summary>
        /// Return names of all texture sync slots provided by Unity
        /// </summary>
        /// <param name="dst">Target buffer allocated to store up to size elements</param>
        /// <param name="size"></param>
        /// <returns>The number of elements filled to <paramref name="dst"/></returns>
        [DllExport]
        public static int GetTextureSlots(IntPtr dst, int size)
        {
            try
            {
                var elementSize = FastStructure.SizeOf<InteropString64>();

                var offset = dst;
                var count = 0;
                foreach (var texture in Bridge.Textures)
                {
                    var buffer = new InteropString64(texture.Name);
                    FastStructure.StructureToPtr(ref buffer, offset);
                    offset = IntPtr.Add(offset, elementSize);

                    // Limit write to the first `size` entries
                    if (count++ >= size) break;
                }

                return count;
            }
            catch (Exception e)
            {
                SetLastError(e);
                return -1;
            }
        }

        [DllExport]
        public static int UpdateTexturePixels(
            [MarshalAs(UnmanagedType.LPStr)] string name,
            int width,
            int height,
            IntPtr pixels
        ) {
            try
            {
                var texture = Bridge.GetTexture(name);

                texture.CopyPixels(
                    width, height,
                    new NativeArray<float>(pixels, width * height * 4)
                );

                texture.SendDirty();
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
        /// Perform a copy of <b>all</b> available mesh data to Unity in one go.
        /// </summary>
        [DllExport]
        public static int CopyMeshDataNative(
            [MarshalAs(UnmanagedType.LPStr)] string name,
            IntPtr loops,
            int loopsSize,
            IntPtr loopTris,
            int loopTrisSize,
            IntPtr verts,
            int vertsSize,
            // Only one vertex color layer is supported.
            IntPtr loopCols,
            // TODO: More dynamic UV support. Until then - we support up to 4 UV layers.
            IntPtr loopUVs,
            IntPtr loopUV2s,
            IntPtr loopUV3s,
            IntPtr loopUV4s
        ) {
            InteropLogger.Debug($"Native Copy {loopsSize} loops for `{name}`");

            try
            {
                var mesh = Bridge.GetOrCreateMesh(name);

                mesh.CopyMeshDataNative(
                    new NativeArray<MVert>(verts, vertsSize),
                    new NativeArray<MLoop>(loops, loopsSize),
                    new NativeArray<MLoopTri>(loopTris, loopTrisSize),
                    new NativeArray<MLoopCol>(loopCols, loopsSize),
                    new NativeArray<MLoopUV>(loopUVs, loopsSize)
                );
                // TODO: UV2-4

                mesh.SendDirty();
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
