
using System;
using System.Runtime.InteropServices;
using System.Threading;
using SharedMemory;

namespace Coherence
{
    /// <summary>
    /// Data management for a Blender viewport.
    ///
    /// Handles serialization of the data into a format consumable by Unity.
    /// </summary>
    class Viewport : IDisposable, IInteropSerializable<InteropViewport>
    {
        public string Name { get { return $"Viewport #{data.id}"; }}

        /// <summary>
        /// Number of times we've updated <see cref="Pixels"/> from Unity
        /// </summary>
        public int Frame { get; internal set; }

        /// <summary>
        /// Heap memory allocated for pixel data. This is used over a managed
        /// array so that we can provide a direct pointer into it for
        /// uploading pixel data to the GPU from within Blender.
        /// </summary>
        public IntPtr Pixels { get; private set; }

        public InteropRenderHeader Header { get; set; }

        public InteropViewport data;
        public int[] VisibleObjectIds;

        private readonly object renderTextureLock = new object();

        public Viewport(int id)
        {
            data = new InteropViewport
            {
                id = id,
                camera = new InteropCamera
                {
                    width = 100,
                    height = 100
                }
            };

            Frame = 0;
            Pixels = IntPtr.Zero;
        }

        ~Viewport()
        {
            Dispose();
        }

        public void Dispose()
        {
            ReleasePixelBuffer();
        }

        public void SetVisibleObjects(int[] ids)
        {
            VisibleObjectIds = ids;
        }

        public InteropViewport Serialize()
        {
            return data;
        }

        public void Deserialize(InteropViewport interopData)
        {
            throw new InvalidOperationException();
        }

        /// <summary>
        /// Read pixel data from <paramref name="intPtr"/> into our local buffer
        /// and return the total number of bytes read.
        /// </summary>
        /// <param name="header"></param>
        /// <param name="intPtr"></param>
        /// <returns></returns>
        internal int ReadPixelData(InteropRenderHeader header, IntPtr intPtr)
        {
            var pixelArraySize = header.width * header.height * 3;

            lock (renderTextureLock)
            {
                if (Pixels == IntPtr.Zero)
                {
                    InteropLogger.Debug($"Allocating {header.width} x {header.height}");
                    Pixels = Marshal.AllocHGlobal(pixelArraySize);
                }
                else if (header.width != Header.width || header.height != Header.height)
                {
                    // If the inbound data resized - resize our buffer
                    InteropLogger.Debug($"Reallocating {header.width} x {header.height}");
                    Pixels = Marshal.ReAllocHGlobal(Pixels, new IntPtr(pixelArraySize));
                }

                // Could do a Buffer.MemoryCopy here - but I'm locked to
                // .NET 4.5 due to the DllExport library we're using.
                UnsafeNativeMethods.CopyMemory(Pixels, intPtr, (uint)pixelArraySize);

                Header = header;
                Frame++;
            }

            return pixelArraySize;
        }

        internal void CameraFromInterop(InteropCamera camera)
        {
            if (camera.width != data.camera.width || camera.height != data.camera.height)
            {
                // TODO: .. anything?
            }

            data.camera = camera;
        }

        internal void ReleasePixelBuffer()
        {
            lock (renderTextureLock)
            {
                if (Pixels != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(Pixels);
                    Pixels = IntPtr.Zero;
                }
            }
        }

        internal RenderTextureData GetRenderTextureAndLock()
        {
            // We lock here and unlock in a separate thread.
            // This is so that Python can safely lock while retrieving,
            // do work, and call ReleaseRenderTextureLock once it's done.

            Monitor.Enter(renderTextureLock);
            return new RenderTextureData
            {
                viewportId = data.id,
                width = Header.width,
                height = Header.height,
                frame = Frame,
                pixels = Pixels
            };
        }

        internal void ReleaseRenderTextureLock()
        {
            Monitor.Exit(renderTextureLock);
        }
    }
}
