using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Coherence
{
    public delegate void OnUpdateMeshEvent(SyncedMesh mesh);

    /// <summary>
    /// Mesh data synced between a remote application and a <see cref="Mesh"/>.
    ///
    /// <para>
    ///     Instances are created through <see cref="MeshFactory"/> when mesh data
    ///     is received from a connected application and made available to other
    ///     plugins for referencing meshes while actively connected.
    /// </para>
    /// </summary>
    public class SyncedMesh : INetworkTarget, IDisposable
    {
        public event OnUpdateMeshEvent OnUpdateMesh;

        public string Name => Data.name;

        /// <summary>
        /// The matching interop data for this entity
        /// </summary>
        public InteropMesh Data { get; set; }

        // Data layers loaded from Blender (many are optional)
        internal readonly ArrayBuffer<Vector3> vertices = new ArrayBuffer<Vector3>();
        internal readonly ArrayBuffer<int> triangles = new ArrayBuffer<int>();
        internal readonly ArrayBuffer<Vector3> normals = new ArrayBuffer<Vector3>();
        internal readonly ArrayBuffer<Color32> colors = new ArrayBuffer<Color32>();
        internal readonly ArrayBuffer<Vector2> uv = new ArrayBuffer<Vector2>();
        internal readonly ArrayBuffer<Vector2> uv2 = new ArrayBuffer<Vector2>();
        internal readonly ArrayBuffer<Vector2> uv3 = new ArrayBuffer<Vector2>();
        internal readonly ArrayBuffer<Vector2> uv4 = new ArrayBuffer<Vector2>();

        /// <summary>
        /// OnUpdateMesh listeners for changes on this mesh instance.
        /// </summary>
        internal readonly List<Action<SyncedMesh>> listeners = new List<Action<SyncedMesh>>();

        /// <summary>
        /// The underlying Unity Mesh to update
        /// </summary>
        public Mesh mesh { get; private set; }

        bool applyDirtiedBuffers;
        private bool disposed;

        public SyncedMesh(InteropMesh data)
        {
            Data = data;

            // Unity mesh instance to load geometry data into
            mesh = new Mesh
            {
                name = data.name
            };

            mesh.MarkDynamic();

            // Network messages for different vertex data buffers
            Network.Register(this, RpcRequest.UpdateTriangles, triangles.CopyFrom);
            Network.Register(this, RpcRequest.UpdateVertices, vertices.CopyFrom);
            Network.Register(this, RpcRequest.UpdateNormals, normals.CopyFrom);
            Network.Register(this, RpcRequest.UpdateUV, uv.CopyFrom);
            Network.Register(this, RpcRequest.UpdateUV2, uv2.CopyFrom);
            Network.Register(this, RpcRequest.UpdateUV3, uv3.CopyFrom);
            Network.Register(this, RpcRequest.UpdateUV4, uv4.CopyFrom);
            Network.Register(this, RpcRequest.UpdateVertexColors, colors.CopyFrom);

            Network.Register(this, RpcRequest.UpdateMesh,
                (msg) => UpdateFromInterop(msg.Reinterpret<InteropMesh>())
            );

            Network.OnSync += OnSync;
        }

        internal void UpdateFromInterop(InteropMesh interop)
        {
            Data = interop;

            if (HasDirtyBuffers())
            {
                UpdateMesh();
            }
        }

        public void OnSync()
        {
            if (applyDirtiedBuffers)
            {
                applyDirtiedBuffers = false;
                UpdateMesh();
            }

            if (HasDirtyBuffers())
            {
                applyDirtiedBuffers = true;
            }
        }

        bool HasDirtyBuffers()
        {
            return vertices.IsDirty
                || colors.IsDirty
                || triangles.IsDirty
                || uv.IsDirty
                || uv2.IsDirty
                || uv3.IsDirty
                || uv4.IsDirty
                || normals.IsDirty;
        }

        void UpdateMesh()
        {
            // If vertex length changes - Unity will throw a fit since we can't
            // just fill buffers without it trying to second guess us each step.
            //
            // This is especially common with metaballs.
            // So we assume vertex length changes = everything will be dirtied.
            // Not a great assumption though so.. TODO! :)
            if (vertices.Length != mesh.vertices.Length)
            {
                mesh.Clear();
            }

            // Channels that were dirtied from last time get loaded.
            if (vertices.IsDirty)
            {
                mesh.vertices = vertices.Read();
            }

            if (normals.IsDirty)
            {
                mesh.normals = normals.Read();
            }

            if (triangles.IsDirty)
            {
                // Change index format for large Blender meshes - when needed
                if (triangles.Length > short.MaxValue)
                {
                    mesh.indexFormat = IndexFormat.UInt32;
                }
                else
                {
                    mesh.indexFormat = IndexFormat.UInt16;
                }

                mesh.triangles = triangles.Read();
            }


            if (colors.IsDirty)
                mesh.colors32 = colors.Read();

            if (uv.IsDirty)
                mesh.uv = uv.Read();

            if (uv2.IsDirty)
                mesh.uv2 = uv2.Read();

            if (uv3.IsDirty)
                mesh.uv3 = uv3.Read();

            if (uv4.IsDirty)
                mesh.uv4 = uv4.Read();

            // TODO: Additional standard channels / custom channels

            // mesh.boneWeights =
            // mesh.bindposes =
            // mesh.tangents =

            // mesh.MarkModified();

            // In editor mode - if the editor doesn't have focus then the mesh won't be updated.
            // So we force upload to the GPU once we have everything ready. We also maintain
            // local copies of mesh data to be updated by Blender whenever new data comes in.
            // (so we don't free up, say, UVs, and then not have that buffer when re-applying from Blender)
            mesh.UploadMeshData(false);

            OnUpdateMesh?.Invoke(this);
        }

        void Dispose(bool disposing)
        {
            if (disposed)
            {
                return;
            }

            if (disposing)
            {
                Network.OnSync -= OnSync;

                UnityEngine.Object.DestroyImmediate(mesh);
                mesh = null;
            }

            disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
