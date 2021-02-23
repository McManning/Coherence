using SharedMemory;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Coherence
{
    [ExecuteAlways]
    public class ObjectController : MonoBehaviour
    {
        /// <summary>
        /// The matching BlenderObject to this entity
        /// </summary>
        public InteropSceneObject Data { get; set; }

        // Data layers loaded from Blender (many are optional)
        internal readonly ArrayBuffer<Vector3> vertices = new ArrayBuffer<Vector3>();
        internal readonly ArrayBuffer<int> triangles = new ArrayBuffer<int>();
        internal readonly ArrayBuffer<Vector3> normals = new ArrayBuffer<Vector3>();
        internal readonly ArrayBuffer<Color32> colors = new ArrayBuffer<Color32>();
        internal readonly ArrayBuffer<Vector2> uv = new ArrayBuffer<Vector2>();
        internal readonly ArrayBuffer<Vector2> uv2 = new ArrayBuffer<Vector2>();
        internal readonly ArrayBuffer<Vector2> uv3 = new ArrayBuffer<Vector2>();
        internal readonly ArrayBuffer<Vector2> uv4 = new ArrayBuffer<Vector2>();

        Mesh mesh;
        MeshFilter meshFilter;
        MeshRenderer meshRenderer;
        MaterialPropertyBlock materialProperties;

        bool applyDirtiedBuffers;

        public void Sync()
        {
            if (applyDirtiedBuffers)
            {
                applyDirtiedBuffers = false;
                UpdateMesh();
            }
        }

        /// <summary>
        /// Add a Unity mesh representation of this object.
        /// We expect it to be filled through data chunks coming from Blender.
        /// </summary>
        void AddMesh(string name)
        {
            mesh = new Mesh();
            mesh.name = name;

            mesh.MarkDynamic();

            meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null)
            {
                meshFilter = gameObject.AddComponent<MeshFilter>();
            }

            meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer == null)
            {
                meshRenderer = gameObject.AddComponent<MeshRenderer>();

                material = CoherenceSettings.Instance.defaultMaterial;
                meshRenderer.sharedMaterial = material;
            }

            if (materialProperties == null)
            {
                materialProperties = new MaterialPropertyBlock();
            }

            meshFilter.mesh = mesh;
        }

        internal void UpdateFromInterop(InteropSceneObject obj)
        {
            // Blender is z-up - swap z/y everywhere
            // TODO: But they could also change the up axis manually...
            InteropMatrix4x4 t = obj.transform;
            transform.position = new Vector3(t.m03, t.m23, t.m13);

            Vector3 forward;
            forward.x = t.m02;
            forward.y = t.m22;
            forward.z = t.m12;

            Vector3 up;
            up.x = t.m01;
            up.y = t.m21;
            up.z = t.m11;

            transform.rotation = Quaternion.LookRotation(forward, up);

            // Multiplying X by -1 to account for different axes.
            // TODO: Improve on.
            transform.localScale = new Vector4(
                new Vector4(t.m00, t.m10, t.m30, t.m20).magnitude * -1,
                new Vector4(t.m01, t.m11, t.m31, t.m21).magnitude,
                new Vector4(t.m02, t.m12, t.m32, t.m22).magnitude
            );

            if (obj.type == SceneObjectType.Mesh && mesh == null)
            {
                AddMesh($"Blender Mesh `{obj.name}`");
            }

            // Material name change
            if (Data.material != obj.material)
            {
                SetMaterial(obj.material);
            }

            if (obj.display != Data.display)
            {
                SetDisplayMode(obj.display);
            }

            Data = obj;

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

        void SetMaterial(string name)
        {
            var mat = CoherenceSettings.Instance.GetMatchingMaterial(name);

            material = mat;
            meshRenderer.sharedMaterial = mat;
        }

        /// <summary>
        /// Change out material for alternatives based on the chosen display mode
        /// </summary>
        void SetDisplayMode(ObjectDisplayMode display)
        {
            if (display == ObjectDisplayMode.Material)
            {
                meshRenderer.sharedMaterial = material;
            }
            else // Use the channel tester material
            {
                materialProperties.SetInt("_DisplayMode", (int)display);
                materialProperties.SetInt("_ApplyTexture", display >= ObjectDisplayMode.UV ? 1 : 0);

                meshRenderer.sharedMaterial = CoherenceSettings.Instance.displayMaterial;
                meshRenderer.SetPropertyBlock(materialProperties);
            }
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
                Debug.Log($"Dirty vertices={vertices.Length}");
                mesh.vertices = vertices.Read();
            }

            if (normals.IsDirty)
            {
                Debug.Log($"Dirty normals={normals.Length}");
                mesh.normals = normals.Read();
            }

            if (triangles.IsDirty)
            {
                Debug.Log($"Dirty triangles={triangles.Length}");

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
        }
    }
}
