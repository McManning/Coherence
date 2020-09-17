using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ObjectController : MonoBehaviour
{
    /// <summary>
    /// The matching BlenderObject to this entity
    /// </summary>
    public InteropSceneObject Data { get; set; }

    /// <summary>
    /// Number of viewports that can see this object
    /// </summary>
    public int ViewportCount { get; set; }

    internal int[] triangles;
    internal Vector3[] vertices;
    internal Vector3[] normals;

    Mesh mesh;
    MeshFilter meshFilter;
    MeshRenderer renderer;

    bool isDirty;

    public void LateUpdate()
    {
        // After the Update() cycle where we potentially replace regions of 
        // our mesh data, check if we need to copy this data back into Unity.
        // This lets us avoid the copy op multiple times in the same Update() 
        // in case the Sync manager reads multiple chunks at once.
        if (isDirty)
        {   
            isDirty = false;
            UpdateMesh();
        }

    }

    /// <summary>
    /// Add a Unity mesh representation of this object. 
    /// We expect it to be filled through data chunks coming from Blender. 
    /// </summary>
    void AddMesh()
    {
        mesh = new Mesh();
        mesh.name = $"Blender Mesh #{Data.id}";

        mesh.MarkDynamic();

        meshFilter = GetComponent<MeshFilter>();
        if (meshFilter == null) 
        {
            meshFilter = gameObject.AddComponent<MeshFilter>();
        }

        renderer = GetComponent<MeshRenderer>();
        if (renderer == null)
        {
            renderer = gameObject.AddComponent<MeshRenderer>();

            // TODO: Determine the material per object to load from metadata.
            // ... somehow :)
            var defaultMaterial = Resources.Load<Material>("Materials/Blender-Default");
            renderer.sharedMaterial = defaultMaterial;
        }

        meshFilter.mesh = mesh;
    }

    internal void UpdateFromInterop(InteropSceneObject obj)
    {
        Data = obj;
        
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
            AddMesh();
        }
    }

    internal Vector3[] GetOrCreateVertexBuffer()
    {
        // No vertex data for this object
        if (Data.vertexCount < 1)
        {
            return null;
        }

        // Check for an unallocated buffer
        if (vertices == null)
        {
            vertices = new Vector3[Data.vertexCount];
        }

        // Check for a resized vertex buffer
        if (vertices.Length != Data.vertexCount)
        {
            Array.Resize(ref vertices, Data.vertexCount);
        }

        return vertices;
    }

    internal Vector3[] GetOrCreateNormalBuffer()
    {
        // No vertices in this object, thus no normals
        if (Data.vertexCount < 1)
        {
            return null;
        }

        // Check for an unallocated buffer
        if (normals == null)
        {
            normals = new Vector3[Data.vertexCount];
        }

        // Check for a resized buffer
        if (normals.Length != Data.vertexCount)
        {
            Array.Resize(ref normals, Data.vertexCount);
        }

        return normals;
    }

    internal int[] GetOrCreateTriangleBuffer()
    {
        // No triangles in this object, thus no buffer
        if (Data.triangleCount < 1)
        {
            return null;
        }

        // Check for an unallocated buffer
        if (triangles == null)
        {
            triangles = new int[Data.triangleCount];
        }

        // Check for a resized buffer
        if (triangles.Length != Data.triangleCount)
        {
            Array.Resize(ref triangles, Data.triangleCount);
        }

        return triangles;
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
        mesh.vertices = vertices;
        mesh.triangles = triangles;

        // We check for normal length here because if a vertex array
        // update comes in and we rebuild the mesh *before* a followup
        // normals array comes in - they could differ in size if the 
        // mesh has vertices added/removed from within Blender.
        if (normals != null && normals.Length == vertices.Length)
        {
            mesh.normals = normals;
        }

        // uv#, colors, etc.

        mesh.MarkModified();
    }

    internal void OnUpdateVertexRange(int index, int count)
    {
        Debug.Log($"updating {count} verts starting at {index}");
        isDirty = true;
    }

    internal void OnUpdateTriangleRange(int index, int count)
    {
        isDirty = true;
    }

    internal void OnUpdateNormalRange(int index, int count)
    {
        isDirty = true;
    }

    // UpdateUV#, UpdateColors
}
