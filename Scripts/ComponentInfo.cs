using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Coherence
{
    /// <summary>
    /// Tracking and metadata for Coherence components
    /// </summary>
    internal class ComponentInfo
    {
        /// <summary>
        /// Get a list of all components, regardless of registration state
        /// </summary>
        internal static Dictionary<string, ComponentInfo> Infos {
            get {
                // If we came out of an assembly reload, try to restore.
                if (infos == null)
                {
                    LoadComponentsFromAssemblies();
                }
                return infos;
            }
        }

        private static Dictionary<string, ComponentInfo> infos;

        internal static ComponentInfo Find(string name)
        {
            // TODO: Throw or something
            return Infos[name];
        }

        internal string Name { get; set; }

        internal Type Type { get; set; }

        internal Dictionary<string, IComponent> instances = new Dictionary<string, IComponent>();

        /// <summary>
        /// Methods that can handle Coherence events (e.g. OnConnected, OnDisconnected)
        /// </summary>
        internal Dictionary<string, MethodInfo> EventLookupTable { get; } = new Dictionary<string, MethodInfo>();

        private static void LoadComponentsFromAssemblies()
        {
            infos = new Dictionary<string, ComponentInfo>();
            var componentType = typeof(IComponent);

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var type in assembly.GetTypes())
                {
                    if (componentType.IsAssignableFrom(type) && !type.IsInterface)
                    {
                        Add(type);
                    }
                }
            }
        }

        internal void DestroyAllInstances()
        {
            foreach (var instance in instances.Values)
            {
                UnityEngine.Object.Destroy(instance as UnityEngine.Object);
            }

            instances.Clear();
        }

        internal void Instantiate(SyncManager sync, InteropComponent interop)
        {
            var target = sync.GetObject(interop.target);
            var component = target.gameObject.AddComponent(Type) as IComponent;
            //component.enabled = interop.enabled;

            // Fill in binding data
            component.CreateCoherenceData(this, sync);

            // Attach a mesh to it if we referenced one
            if (!string.IsNullOrEmpty(interop.mesh))
            {
                var mesh = sync.GetOrCreateMesh(interop.mesh);
                component.SetMeshController(mesh);
            }

            instances[interop.target] = component;
        }

        internal void Destroy(InteropComponent interop)
        {
            var instance = instances[interop.target];

            // TODO: If they destroy it via unity first, this won't be called.
            // How do we make sure it gets cleaned up?
            instance.UnbindCoherenceEvents();

            UnityEngine.Object.Destroy(instance as UnityEngine.Object);
        }

        internal void Update(InteropComponent interop)
        {
            var instance = instances[interop.target];
            // do thing
        }

        internal void OnMessage(InteropComponentMessage message)
        {
            var instance = instances[message.target];
            // do thing
        }

        internal static void Add(Type type)
        {
            var attr = type.GetCustomAttribute<ComponentAttribute>();
            if (attr == null)
            {
                Debug.LogError("missing attr"); // TODO: message
                return;
            }

            if (infos.ContainsKey(attr.Name))
            {
                Debug.Log("Already registered: " + attr.Name); // TODO: Error?
                return;
            }

            var info = new ComponentInfo
            {
                Name = attr.Name,
                Type = type
            };

            // Load all On* event handlers into a lookup table
            foreach (var method in type.GetMethods())
            {
                if (method.Name.StartsWith("On"))
                {
                    info.EventLookupTable.Add(method.Name, method);
                }
            }

            Infos.Add(attr.Name, info);
        }
    }
}
