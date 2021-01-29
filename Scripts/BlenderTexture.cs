using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace Coherence
{
    /// <summary>
    /// Texture data synced between Blender and Unity
    /// </summary>
    [Serializable]
    [ExecuteAlways]
    public class BlenderTexture
    {
        [Tooltip("Common name for the texture shared between Unity and Blender")]
        public string name;

        [Tooltip("Render Texture that gets updated as the source texture changes in Blender")]
        public RenderTexture texture;

        public Texture2D tempTexture;

        internal void UpdateFromInterop(InteropTexture data)
        {
            if (!tempTexture || data.width != tempTexture.width || data.height != tempTexture.height)
            {
                RebuildTempTexture(data.width, data.height);
            }
        }

        void RebuildTempTexture(int width, int height)
        {
            tempTexture = new Texture2D(width, height, TextureFormat.RGBAFloat, false);
        }

        /// <summary>
        /// Copy from the shared memory buffer directly into our temporary Texture2D
        /// </summary>
        /// <param name="src"></param>
        /// <param name="index"></param>
        /// <param name="count"></param>
        /// <param name="length"></param>
        internal unsafe void CopyFrom(IntPtr src, int index, int count, int length)
        {
            var data = tempTexture.GetRawTextureData<float>();
            int elementSize = UnsafeUtility.SizeOf<float>(); // RGBAFloat

            int offset = index * elementSize; // 0
            int size = count * elementSize; // 4mil

            // Write out of bounds - index=0, count=4194304, length=4194304,
            // data.Length=4194304, elementSize=16

            // Make sure we don't try to write outside of allowable memory
            if (offset < 0 || offset + size > data.Length * elementSize)
            {
                throw new OverflowException(
                    $"Write out of bounds - index={index}, count={count}, " +
                    $"length={length}, data.Length={data.Length}, elementSize={elementSize}"
                );
            }

            Debug.Log($"Max storage will be {data.Length} for {size} bytes");

            // Max storage will be 4 194 304 for 16 777 216 bytes
            var dst = IntPtr.Add((IntPtr)data.GetUnsafePtr(), offset);
            UnsafeUtility.MemCpy(dst.ToPointer(), src.ToPointer(), size);

            tempTexture.Apply();

            Debug.Log("Blitting to target RT");
            Graphics.Blit(tempTexture, texture);
        }
    }
}
