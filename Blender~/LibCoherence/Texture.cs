using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Coherence
{
    public class Texture : IDisposable, IInteropSerializable<InteropTexture>
    {
        public string Name { get; private set; }

        /// <summary>
        /// Data that will be shared with Unity
        /// </summary>
        InteropTexture data;

        bool dirty;

        readonly NativeArray<float> pixels = new NativeArray<float>();

        public Texture(string name)
        {
            Name = name;
            data = new InteropTexture();
        }

        public InteropTexture Serialize()
        {
            return data;
        }

        public void Deserialize(InteropTexture data)
        {
            // do work.
            this.data = data;
        }

        public void CopyPixels(int width, int height, NativeArray<float> pixels)
        {
            // Same data - do nothing.
            if (data.width == width && data.height == height && this.pixels.Equals(pixels))
            {
                return;
            }

            data.width = width;
            data.height = height;

            // TODO: Some form of chunking - and sending dirtied chunks.
            // the way memory is laid out though - chunks wouldn't be
            // square regions - they'd be more scanlines of data.

            // For now - we'll just send the whole ass chunk.

            this.pixels.CopyFrom(pixels);
            dirty = true;
        }

        public void Dispose()
        {
            pixels.Dispose();
        }

        public void SendDirty()
        {
            if (!dirty)
            {
                return;
            }

            var b = Bridge.Instance;

            InteropLogger.Debug($"Pixel buffer is {pixels.Length} elements for {data.width}x{data.height} image");

            b.SendEntity(RpcRequest.UpdateTexture, this);
            b.SendArray(RpcRequest.UpdateTextureData, Name, pixels);

            dirty = false;
        }
    }
}
