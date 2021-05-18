using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Coherence
{
    /// <summary>
    /// Metadata about a Coherence plugin and its kinds/instances
    /// </summary>
    internal class ComponentInfo
    {
        internal string Name { get; set; }

        internal Type Type { get; set; }

        internal Dictionary<ObjectController, IComponent> instances = new Dictionary<ObjectController, IComponent>();

        /// <summary>
        /// Methods that can handle Coherence events (e.g. OnConnected, OnDisconnected)
        /// </summary>
        internal DictionarySet<Type, MethodInfo> EventMethods { get; } = new DictionarySet<Type, MethodInfo>();

        /// <summary>
        /// Load declared On* Coherence event methods into a lookup table
        /// </summary>
        private void CacheEventMethods(Type type)
        {
            foreach (var method in type.GetMethods())
            {
                if (method.Name.StartsWith("On"))
                {
                    EventMethods.Add(type, method);
                }
            }
        }

        internal IComponent Instantiate(SyncManager sync, ObjectController target)
        {
            var component = target.gameObject.AddComponent(Type) as IComponent;

            // Fill in plugin data
            var data = component.GetCoherenceData();
            data.Sync = sync;
            data.name = Name;

            // ... event stuff?

            instances[target] = component;
            return component;
        }

        internal void DestroyAllInstances()
        {
            foreach (var instance in instances.Values)
            {
                UnityEngine.Object.Destroy(instance as UnityEngine.Object);
            }

            instances.Clear();
        }

        internal void DestroyInstance(ObjectController target)
        {
            UnityEngine.Object.Destroy(instances[target] as UnityEngine.Object);
            instances.Remove(target);
        }
    }
}
