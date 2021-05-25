
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
        /// <returns>14</returns>
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
        /// Push/pull messages between Unity and update our state.
        /// </summary>
        /// <returns>The last message read from Unity for further processing in Python</returns>
        [DllExport]
        public static InteropMessage Update()
        {
            try
            {
                return Bridge.Update();
            }
            catch (Exception e)
            {
                SetLastError(e);
                return InteropMessage.Invalid;
            }
        }

        [DllExport]
        public static bool IsConnectedToUnity()
        {
            return Bridge.IsConnectedToUnity;
        }

        [DllExport]
        public static bool IsConnected()
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
            InteropTransform transform
        ) {
            InteropLogger.Debug($"Add object {name}");

            try
            {
                var obj = new SceneObject(name, transform);

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
            InteropLogger.Debug($"Set transform {name} to parent={transform.parent}");
            try
            {
                var obj = Bridge.GetObject(name);
                obj.data.transform = transform;

                Bridge.SendEntity(RpcRequest.UpdateObject, obj);
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
            InteropLogger.Debug($"Remove object {name}");

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

        #region Component API

        [DllExport]
        public static int AddComponent(InteropComponent component)
        {
            try
            {
                var obj = Bridge.GetObject(component.target);
                Bridge.AddComponent(obj, component);
                InteropLogger.Debug($"Add component={component.name} to object={obj.Name}");
                return 1;
            }
            catch (Exception e)
            {
                SetLastError(e);
                return -1;
            }
        }

        [DllExport]
        public static int DestroyComponent(InteropComponent component)
        {
            try
            {
                var obj = Bridge.GetObject(component.target);
                Bridge.DestroyComponent(obj, component.name);
                return 1;
            }
            catch (Exception e)
            {
                SetLastError(e);
                return -1;
            }
        }

        [DllExport]
        public static int UpdateComponent(InteropComponent component)
        {
            try
            {
                var obj = Bridge.GetObject(component.target);
                var instance = Bridge.GetComponent(obj, component.name);

                if (!instance.data.Equals(component))
                {
                    instance.data = component;
                    Bridge.SendEntity(RpcRequest.UpdateComponent, instance);
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
        public static int UpdateComponentProperty(
            InteropComponent component,
            InteropProperty property
        ) {

            InteropLogger.Debug($"UpdateComponentProperty {property.name} of {property.type}" +
                $" intval={property.intValue}" +
                $" strval={property.stringValue.Value}"
            );

            try
            {
                var obj = Bridge.GetObject(component.target);
                var instance = Bridge.GetComponent(obj, component.name);

                // Find an existing property match to replace
                var found = false;
                for (int i = 0; i < instance.properties.Length; i++)
                {
                    var prop = instance.properties[i];
                    if (prop.name == property.name)
                    {
                        // Only assign if it differs so we don't unnecessarily dirty the array
                        if (!prop.Equals(property))
                        {
                            instance.properties[i] = property;

                            InteropLogger.Debug($"Update {instance.Name} - {instance.properties[i].name}" +
                                $" intval={instance.properties[i].intValue}" +
                                $" strval={instance.properties[i].stringValue}"
                            );
                        }
                        else
                        {
                            InteropLogger.Debug($"Same value {instance.Name} - {instance.properties[i].name}" +
                                $" intval={instance.properties[i].intValue} vs {property.intValue}" +
                                $" strval={instance.properties[i].stringValue} vs {property.stringValue}"
                            );
                        }

                        found = true;
                        break;
                    }
                }

                // Doesn't exist, add it as a new property
                if (!found)
                {
                    InteropLogger.Debug($"Add prop {property.name} [{property.type}] for {instance.Name}");
                    instance.properties.Add(property);
                }

                if (instance.properties.IsDirty)
                {
                    // If there were multiple properties updated in the same frame in Python,
                    // these should all dirty parts of the array together so that a *single*
                    // UpdateProperties message routes to the connected app.
                    Bridge.SendArray(RpcRequest.UpdateProperties, instance.Name, instance.properties);
                    instance.properties.Clean();
                }

                return 1;
            }
            catch (Exception e)
            {
                SetLastError(e);
                return -1;
            }
        }

        #endregion

        #region Image Sync API

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
                foreach (var texture in Bridge.Images)
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
                var image = Bridge.GetImage(name);

                image.CopyPixels(
                    width, height,
                    new NativeArray<float>(pixels, width * height * 4)
                );

                image.SendDirty();
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
