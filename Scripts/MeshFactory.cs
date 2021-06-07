using System;
using System.Collections.Generic;
using UnityEngine;
using SharedMemory;

namespace Coherence
{
    /// <summary>
    /// Coherence plugin that instantiates and routes messages to networked meshes
    /// </summary>
    public class MeshFactory : MonoBehaviour, IPlugin
    {
        // Temporary - just so we can show a debug list view
        // of all the meshes that have been loaded in memory
        [SerializeField]
        List<Mesh> unityMeshes = new List<Mesh>();

        static readonly Dictionary<string, SyncedMesh> meshes = new Dictionary<string, SyncedMesh>();

        public static SyncedMesh Find(string name)
        {
            if (!meshes.ContainsKey(name))
            {
                return null;
            }

            return meshes[name];
        }

        public void OnRegistered()
        {
            Network.Register(RpcRequest.AddMesh, OnAddMesh);
            Network.OnDisconnected += OnDisconnect;
        }

        public void OnUnregistered()
        {

        }

        private void OnAddMesh(InteropMessage msg)
        {
            var data = msg.Reinterpret<InteropMesh>();

            if (Find(data.name) != null)
            {
                Debug.LogError($"Mesh '{data.name}' already exist");
            }

            var mesh = new SyncedMesh(data);

            unityMeshes.Add(mesh.mesh);
            meshes.Add(data.name, mesh);
        }

        /// <summary>
        /// Release all synced meshes when Coherence disconnects
        /// </summary>
        public void OnDisconnect()
        {
            foreach (var mesh in meshes.Values)
            {
                Network.Unregister(mesh);
                DestroyImmediate(mesh.mesh);

                // TODO: Nicer cleanup.
            }

            unityMeshes.Clear();
            meshes.Clear();
        }
    }
}
