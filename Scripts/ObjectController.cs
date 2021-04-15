using SharedMemory;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;

namespace Coherence
{
    [ExecuteAlways]
    public class ObjectController : MonoBehaviour
    {
        /// <summary>
        /// The matching interop data for this entity
        /// </summary>
        public InteropSceneObject Data { get; set; }

        public MeshController Mesh { get; private set; }

        MeshFilter meshFilter;
        MeshRenderer meshRenderer;
        Material material;
        MaterialPropertyBlock materialProperties;

        public void Awake()
        {
            gameObject.tag = "EditorOnly";
            gameObject.hideFlags = HideFlags.DontSave;
        }

        public void Sync()
        {
            // TODO: Delete?
        }

        /// <summary>
        /// Add a Unity mesh representation of this object.
        /// We expect it to be filled through data chunks coming from Blender.
        /// </summary>
        void AddMesh()
        {
            // TODO: Need to fin dthe active SyncManager instance.
            // meshController = CoherenceSettings.Instance.

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

            meshFilter.mesh = Mesh.mesh;
        }

        /// <summary>
        /// Remove the current mesh referenced by this object
        /// </summary>
        private void RemoveMesh()
        {
            Mesh = null;
            if (meshRenderer)
            {
                meshRenderer.enabled = false;
            }
        }

        public void SetMesh(MeshController mesh)
        {
            if (Mesh == mesh)
            {
                return;
            }

            Mesh = mesh;
            if (Mesh == null)
            {
                RemoveMesh();
                return;
            }

            AddMesh();
        }

        internal void UpdateFromInterop(InteropSceneObject obj)
        {
            transform.position = obj.transform.position.ToVector3();
            transform.rotation = obj.transform.rotation.ToQuaternion();
            transform.localScale = obj.transform.scale.ToVector3();

            // Material name change
            if (Data.material != obj.material)
            {
                SetMaterial(obj.material);
            }

            if (obj.display != Data.display)
            {
                SetDisplayMode(obj.display);
            }

            // TODO: Mesh change should be here too but it's happening
            // in SyncManager.UpdateObject() because I don't have a
            // singleton of SyncManager to access the mesh factory from.

            Data = obj;
        }

        void SetMaterial(string name)
        {
            var mat = CoherenceSettings.Instance.GetMatchingMaterial(name);

            material = mat;

            if (meshRenderer != null)
            {
                meshRenderer.sharedMaterial = mat;
            }
        }

        /// <summary>
        /// Change out material for alternatives based on the chosen display mode
        /// </summary>
        void SetDisplayMode(ObjectDisplayMode display)
        {
            if (display == ObjectDisplayMode.Material)
            {
                meshRenderer.enabled = true;
                meshRenderer.sharedMaterial = material;
            }
            else if (display == ObjectDisplayMode.Hidden)
            {
                meshRenderer.enabled = false;
            }
            else // Use the channel tester material
            {
                materialProperties.SetInt("_DisplayMode", (int)display);
                materialProperties.SetInt("_ApplyTexture", display >= ObjectDisplayMode.UV ? 1 : 0);

                meshRenderer.enabled = true;
                meshRenderer.sharedMaterial = CoherenceSettings.Instance.displayMaterial;
                meshRenderer.SetPropertyBlock(materialProperties);
            }
        }
    }
}
