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

    public static class TransformExtensions
    {
        public static void FromInteropMatrix4x4(this Transform transform, InteropMatrix4x4 matrix)
        {
            transform.localScale = matrix.Scale();
            transform.rotation = matrix.Rotation();
            transform.position = matrix.Position();
        }
    }

    public static class InteropVector3Extensions
    {
        public static Vector3 ToVector3(this InteropVector3 vec)
        {
            return new Vector3(vec.x, vec.y, vec.z);
        }
    }

    public static class InteropQuaternionExtensions
    {
        public static Quaternion ToQuaternion(this InteropQuaternion q)
        {
            return new Quaternion(q.x, q.y, q.z, q.w);
        }
    }

    /// <summary>
    /// Extension methods that are Unity-only
    /// </summary>
    public static class InteropMatrix4x4Extensions
    {
        public static Quaternion Rotation(this InteropMatrix4x4 matrix)
        {
            Vector3 forward;
            forward.x = matrix.m02;
            forward.y = matrix.m12;
            forward.z = matrix.m22;

            Vector3 upwards;
            upwards.x = matrix.m01;
            upwards.y = matrix.m11;
            upwards.z = matrix.m21;

            return Quaternion.LookRotation(forward, upwards);
        }

        public static Vector3 Position(this InteropMatrix4x4 matrix)
        {
            Vector3 position;
            position.x = matrix.m03;
            position.y = matrix.m13;
            position.z = matrix.m23;
            return position;
        }

        public static Vector3 Scale(this InteropMatrix4x4 matrix)
        {
            Vector3 scale;
            scale.x = new Vector4(matrix.m00, matrix.m10, matrix.m20, matrix.m30).magnitude;
            scale.y = new Vector4(matrix.m01, matrix.m11, matrix.m21, matrix.m31).magnitude;
            scale.z = new Vector4(matrix.m02, matrix.m12, matrix.m22, matrix.m32).magnitude;
            return scale;
        }
    }

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
        Material material;
        MaterialPropertyBlock materialProperties;

        bool applyDirtiedBuffers;

        public void Awake()
        {
            gameObject.tag = "EditorOnly";
            gameObject.hideFlags = HideFlags.DontSave;
        }

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
            transform.position = obj.transform.position.ToVector3();
            transform.rotation = obj.transform.rotation.ToQuaternion();
            transform.localScale = obj.transform.scale.ToVector3();

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
