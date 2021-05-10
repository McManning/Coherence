using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Coherence
{
    /// <summary>
    /// Convenience methods for a Dictionary containing a HashSet.
    /// </summary>
    /// <typeparam name="TKey"></typeparam>
    /// <typeparam name="TValue"></typeparam>
    internal class DictionarySet<TKey, TValue> : Dictionary<TKey, HashSet<TValue>>
    {
        internal HashSet<TValue> GetOrCreateSet(TKey key)
        {
            if (!TryGetValue(key, out HashSet<TValue> set))
            {
                set = new HashSet<TValue>();
                Add(key, set);
            }

            return set;
        }

        internal bool Add(TKey key, TValue value)
        {
            return GetOrCreateSet(key).Add(value);
        }

        internal bool Remove(TKey key, TValue value)
        {
            if (TryGetValue(key, out HashSet<TValue> set))
            {
                return set.Remove(value);
            }

            return false;
        }

        internal void RemoveAll(TValue value)
        {
            foreach (var set in Values)
            {
                set.Remove(value);
            }
        }

        internal IEnumerable<TValue> Items(TKey key)
        {
            return GetOrCreateSet(key);
        }

        internal TKey[] KeysAsArray()
        {
            var keys = new TKey[Keys.Count];
            Keys.CopyTo(keys, 0);

            return keys;
        }
    }

    /// <summary>
    /// Metadata about a Coherence plugin and its kinds/instances
    /// </summary>
    internal class PluginInfo
    {
        // TODO: Serializable?

        internal string Name { get; set; }

        internal Type globalType;
        internal IPlugin globalInstance;

        internal Dictionary<string, Type> kindTypes = new Dictionary<string, Type>();
        internal DictionarySet<string, IPlugin> kindInstances = new DictionarySet<string, IPlugin>();

        /// <summary>
        /// Methods that can handle Coherence events (e.g. OnConnected, OnDisconnected)
        /// </summary>
        internal DictionarySet<Type, MethodInfo> EventMethods { get; } = new DictionarySet<Type, MethodInfo>();

        internal void AddKind(string kind, Type type)
        {
            // TODO: dupe error check
            kindTypes.Add(kind, type);
            CacheEventMethods(type);
        }

        internal void SetGlobal(Type type)
        {
            // TODO: dupe error check
            globalType = type;
            CacheEventMethods(type);
        }

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

        internal IPlugin Instantiate(SyncManager sync, ObjectController target, string kind)
        {
            var type = kindTypes[kind];
            var component = target.gameObject.AddComponent(type) as IPlugin;

            // Fill in plugin data
            var instance = component.GetCoherencePlugin();
            instance.Sync = sync;
            instance.pluginName = Name;

            // ... event stuff?

            kindInstances.Add(kind, component);
            return component;
        }

        internal void InstantiateGlobal(SyncManager sync)
        {
            // TODO: Use an existing instance slot here?
            globalInstance = ScriptableObject.CreateInstance(globalType) as IPlugin;

            // Fill in plugin data
            /*var instance = globalInstance.GetCoherencePlugin();
            instance.Sync = sync;
            instance.uid = target.Data.name;
            instance.pluginName = Name;
            instance.objectKind = Kind;*/
        }

        internal void DestroyAllInstances()
        {
            /*
            var component = obj.gameObject.GetComponent(type.ClassType);
            Destroy(component);

            // Remove all On* event handlers
            UnbindEventHandlers(component as IPlugin);

            activeObjectPlugins.Remove(obj, component as IPlugin);*/
            throw new NotImplementedException();
        }

        internal void DestroyInstance(ObjectController target)
        {
            throw new NotImplementedException();
        }
    }
}
