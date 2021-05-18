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

        [Tooltip(
            "Texture2D or RenderTexture to update with the Blender data. \n" +
            "Will be resized to match the texture from Blender."
        )]
        public Texture target;

        /// <summary>
        /// Temporary Texture2D if <see cref="target"/> is a <see cref="RenderTexture"/>
        /// </summary>
        private Texture2D tempTexture;

        internal void UpdateFromInterop(InteropImage data)
        {
            if (target is RenderTexture)
            {
                if (!tempTexture || data.width != tempTexture.width || data.height != tempTexture.height)
                {
                    RebuildTempTexture(data.width, data.height);
                }
            }
            else if (target is Texture2D tex)
            {
                // Resize and update format (if necessary)
                tex.Resize(data.width, data.height, TextureFormat.RGBAFloat, false);
            }
            else
            {
                throw new Exception(
                    $"Syncing a Blender texture ({name}) to {target.GetType()} is not supported. " +
                    $"Must be either a Texture2D or RenderTexture"
                );
            }
        }

        void RebuildTempTexture(int width, int height)
        {
            tempTexture = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
        }

        /// <summary>
        /// Copy from the shared memory buffer directly into our temporary Texture2D
        /// </summary>
        /// <param name="src"></param>
        /// <param name="index"></param>
        /// <param name="count"></param>
        /// <param name="length"></param>
        internal void CopyFrom(IntPtr src, int index, int count, int length)
        {
            Texture2D tex;

            if (target is RenderTexture)
            {
                tex = tempTexture;
            }
            else if (target is Texture2D tex2d)
            {
                tex = tex2d;
            }
            else
            {
                return;
            }

            var data = tex.GetRawTextureData<float>();

            int elementSize = UnsafeUtility.SizeOf<float>(); // RGBAFloat

            int offset = index * elementSize; // 0
            int size = count * elementSize; // 4mil

            // Make sure we don't try to write outside of allowable memory
            if (offset < 0 || offset + size > data.Length * elementSize)
            {
                throw new OverflowException(
                    $"Write out of bounds - index={index}, count={count}, " +
                    $"length={length}, data.Length={data.Length}, elementSize={elementSize}"
                );
            }

            unsafe
            {
                var dst = IntPtr.Add((IntPtr)data.GetUnsafePtr(), offset);
                UnsafeUtility.MemCpy(dst.ToPointer(), src.ToPointer(), size);
            }

            tex.Apply();

            // If our final output is an RT - blit from the temp texture into that.
            if (target is RenderTexture rt)
            {
                Graphics.Blit(tex, rt);
            }
        }
    }
}
