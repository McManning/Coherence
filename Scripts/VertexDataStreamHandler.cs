using System;
using UnityEngine;

namespace Coherence
{
    public interface IVertexDataStreamHandler
    {
        void Dispatch(string id, Mesh mesh, ArrayBuffer<byte> arr);
    }

    public class VertexDataStreamHandler<T> : IVertexDataStreamHandler where T : struct
    {
        public Action<string, Mesh, ArrayBuffer<T>> Callback { get; set; }

        public void Dispatch(string id, Mesh mesh, ArrayBuffer<byte> arr)
        {
            Callback(id, mesh, arr.Reinterpret<T>());
        }
    }
}
