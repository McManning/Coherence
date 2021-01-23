using SharedMemory;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Coherence
{
    public class ArrayBuffer<T> where T : struct
    {
        public T[] data;
        public bool isDirty;

        public T[] Read()
        {
            isDirty = false;
            return data;
        }

        /// <summary>
        /// Resize the buffer and mark dirty until the next call
        /// to <see cref="Read"/>.
        /// </summary>
        /// <param name="size"></param>
        /// <returns></returns>
        public ArrayBuffer<T> Resize(int size)
        {
            if (data == null)
            {
                data = new T[size];
            }
            else
            {
                Array.Resize(ref data, size);
            }

            isDirty = true;
            return this;
        }

        /// <summary>
        /// Fill the buffer from the source memory location and mark
        /// dirty until the next call to <see cref="Read"/>.
        /// </summary>
        /// <param name="ptr"></param>
        /// <param name="index"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public ArrayBuffer<T> Fill(IntPtr ptr, int index, int count)
        {
            FastStructure.ReadArray(data, ptr, index, count);
            isDirty = true;
            return this;
        }

        /// <summary>
        /// Deallocate the buffer and mark dirty until the next call
        /// to <see cref="Read"/>.
        /// </summary>
        /// <returns></returns>
        public ArrayBuffer<T> Clear()
        {
            data = null;
            isDirty = true;
            return this;
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
        MaterialPropertyBlock materialProperties;

        public void Sync()
        {
            // TODO: A better per-buffer isDirty check.
            // Once I can confirm everything is safe to move to buffers.
            if (triangles.isDirty) // || normals.isDirty)
            {
             //   isDirty = false;
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
        }

        void SetMaterial(string name)
        {
            // TODO: Smarter path lookup and mapping
            string path = $"Materials/{name}";

            var mat = Resources.Load<Material>(path);
            if (!mat)
            {
                Debug.LogWarning($"Cannot find material resource at '{path}'");
                mat = CoherenceSettings.Instance.defaultMaterial;
            }

            material = mat;
            meshRenderer.sharedMaterial = mat;
        }

        Material material;

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
            // Skip if we're still waiting on more data to fill in
            if (mesh == null || triangles == null || vertices == null)
            {
                return;
            }

            // TODO: Figure out if we've filled out these arrays.
            // Each OnUpdate* would probably let us fill in a min/max
            // range value (assuming no gaps between ranges...).
            // Alternatively - screw it. Who cares if there's a bunch
            // of zeroed data while waiting for more batches.

            // Upload to Unity's managed mesh data
            mesh.Clear();
            try
            {
                // TODO: Only really needs to be filled if dirtied.
                // Could save on performance for larger meshes.
                mesh.vertices = vertices.Read();
                mesh.normals = normals.Read();
                mesh.triangles = triangles.Read();
                mesh.colors32 = colors.Read();
                mesh.uv = uv.Read();
                mesh.uv2 = uv2.Read();
                mesh.uv3 = uv3.Read();
                mesh.uv4 = uv4.Read();

                // TODO: Additional channels
                // mesh.boneWeights =
                // mesh.bindposes =
                // mesh.tangents =
            }
            catch (Exception e)
            {
                // Removing subd from blender errors out on 226 for:
                // Failed setting triangles. Some indices are referencing out of bounds vertices. IndexCount: 188928, VertexCount: 507

                // Probably due to the ordering of getting mesh data. We get updated verts first (with new count smaller)
                // and then use the old indices list until we get the updated indices immediately after.
                // TODO: We might need a footer message for "yes here's everything, please rebuild the mesh now"

                // TODO: Seems like this is uncatchable. Raised from UnityEngine.Mesh:set_triangles(Int32[])
                Debug.LogWarning($"Could not copy Blender mesh data: {e}");
            }

            // We check for normal length here because if a vertex array
            // update comes in and we rebuild the mesh *before* a followup
            // normals array comes in - they could differ in size if the
            // mesh has vertices added/removed from within Blender.
            /*if (normals.data != null && normals.data.Length == vertices.Length)
            {
                mesh.normals = normals.data;
            }*/


            // mesh.MarkModified();

            // In editor mode - if the editor doesn't have focus then the mesh won't be updated.
            // So we force upload to the GPU once we have everything ready. We also maintain
            // local copies of mesh data to be updated by Blender whenever new data comes in.
            // (so we don't free up, say, UVs, and then not have that buffer when re-applying from Blender)
            mesh.UploadMeshData(false);
        }
    }
}
