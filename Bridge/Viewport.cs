
using System;
using SharedMemory;
using System.Collections.Generic;
using System.Runtime.InteropServices;

/// <summary>
/// Data management for a Blender viewport.
/// 
/// Handles serialization of the data into a format consumable by Unity.
/// </summary>
class Viewport : IDisposable, IInteropSerializable<InteropViewport>
{
    /// <summary>
    /// Maximum width of a viewport, in pixels. 
    /// 
    /// Required for setting a fixed buffer size for pixel data.
    /// </summary>
    public const int MAX_VIEWPORT_WIDTH = 1920;
    
    /// <summary>
    /// Maximum height of a viewport, in pixels. 
    /// 
    /// Required for setting a fixed buffer size for pixel data.
    /// </summary>
    public const int MAX_VIEWPORT_HEIGHT = 1080;
    
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

    public Viewport(int id, int width, int height)
    {
        data = new InteropViewport
        {
            id = id,
            width = width,
            height = height
        };

        Resize(width, height);

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

    public void Resize(int width, int height)
    {
        if (width < 1 || height < 1)
        {
            throw new Exception("Viewport dimensions must be nonzero");
        }

        if (width > MAX_VIEWPORT_WIDTH || height > MAX_VIEWPORT_HEIGHT)
        {
            throw new Exception($"Viewport dimensions cannot exceed {MAX_VIEWPORT_WIDTH} x {MAX_VIEWPORT_HEIGHT}");
        }

        data.width = width;
        data.height = height;
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

        return pixelArraySize;
    }

    internal void ReleasePixelBuffer()
    {
        if (Pixels != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(Pixels);
            Pixels = IntPtr.Zero;
        }
    }

    internal RenderTextureData GetRenderTextureData()
    {
        return new RenderTextureData
        {
            viewportId = data.id,
            width = Header.width,
            height = Header.height,
            frame = Frame,
            pixels = Pixels
        };
    }
}
