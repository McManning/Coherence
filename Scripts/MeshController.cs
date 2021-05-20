using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;

namespace Coherence
{
    public delegate void OnUpdateMeshEvent(MeshController mesh);

    public class MeshController
    {
        public event OnUpdateMeshEvent OnUpdateMesh;

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
        internal readonly List<Action<MeshController>> listeners = new List<Action<MeshController>>();

        /// <summary>
        /// The underlying Unity Mesh to update
        /// </summary>
        public Mesh mesh { get; }

        bool applyDirtiedBuffers;

        public MeshController(string name)
        {
            mesh = new Mesh
            {
                name = name
            };

            mesh.MarkDynamic();
        }

        internal void UpdateFromInterop(InteropMesh interop)
        {
            Debug.Log($"Update mesh {interop.name}");
            Data = interop;

            if (HasDirtyBuffers())
            {
                UpdateMesh();
            }
        }

        public void Sync()
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
                InteropLogger.Debug($"Dirty vertices={vertices.Length}");
                mesh.vertices = vertices.Read();
            }

            if (normals.IsDirty)
            {
                InteropLogger.Debug($"Dirty normals={normals.Length}");
                mesh.normals = normals.Read();
            }

            if (triangles.IsDirty)
            {
                InteropLogger.Debug($"Dirty triangles={triangles.Length}");

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

            // TODO: Additional channels
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
    }
}
