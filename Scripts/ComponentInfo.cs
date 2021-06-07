using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Coherence
{
    /// <summary>
    /// Tracking Coherence components in loaded assemblies
    /// </summary>
    internal class ComponentInfo
    {
        /// <summary>
        /// Get a list of all known component types
        /// </summary>
        internal static Dictionary<string, ComponentInfo> Infos
        {
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
            return Infos[name];
        }

        internal string Name { get; set; }

        internal Type Type { get; set; }

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

        internal static void Add(Type type)
        {
            var attr = type.GetCustomAttribute<ComponentAttribute>();
            if (attr == null)
            {
                Debug.LogError($"Missing required [Component] attribute for IComponent '{type}'");
                return;
            }

            if (infos.ContainsKey(attr.Name))
            {
                Debug.LogError($"A component has already been registered under the name '{attr.Name}'");
                return;
            }

            var info = new ComponentInfo
            {
                Name = attr.Name,
                Type = type
            };

            Infos.Add(attr.Name, info);
        }
    }
}
