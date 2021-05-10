using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Runtime.CompilerServices;
using System.Reflection;

namespace Coherence
{
    public interface IPlugin
    {

    }

    public class CoherencePlugin : ScriptableObject
    {
        public bool isPluginEnabled;
    }

    /// <summary>
    /// Coherence plugin data associated with a MonoBehaviour
    /// representing plugin behaviour for a SceneObject or global scope.
    /// </summary>
    internal class Plugin
    {
        internal bool IsSceneObject => !string.IsNullOrEmpty(objectKind);

        internal bool IsGlobal => !IsSceneObject;

        // Fields that would be instantiated by SyncManager as we add a new plugin entity.

        internal SyncManager Sync { get; set; }

        internal string uid;
        internal string objectKind;
        internal string pluginName;

        internal Dictionary<string, List<IEventHandler>> Messages { get; }
                = new Dictionary<string, List<IEventHandler>>();

        internal Dictionary<string, List<IVertexDataStreamHandler>> VertexDataStreams { get; }
            = new Dictionary<string, List<IVertexDataStreamHandler>>();

        internal Dictionary<string, Action> Events { get; set; }
    }

    /// <summary>
    /// Extension methods attached to <see cref="IPlugin"/> MonoBehaviours.
    ///
    /// <para>
    ///     We use extensions on an interface instead of forcing developers to extend off of
    ///     a custom MonoBehaviour in case they also want to define their own base class
    ///     to replace MonoBehaviour for all of their plugins (or are using some third party
    ///     library that replaces MonoBehaviour).
    /// </para>
    /// </summary>
    public static class PluginExtensions
    {
        private static readonly ConditionalWeakTable<IPlugin, Plugin> data
            = new ConditionalWeakTable<IPlugin, Plugin>();

        public static void AddHandler<T>(this IPlugin component, string id, Action<string, T> callback) where T : struct
        {
            Debug.Log("add handler for " + id);

            var handlers = data.GetOrCreateValue(component).Messages;

            if (!handlers.TryGetValue(id, out List<IEventHandler> values))
            {
                values = new List<IEventHandler>();
                handlers.Add(id, values);
            }

            values.Add(new EventHandler<T>
            {
                Callback = callback
            });
        }

        internal static Plugin GetCoherencePlugin(this IPlugin component)
        {
            return data.GetOrCreateValue(component);
        }

        internal static void AddEventDelegate(this IPlugin component, MethodInfo method)
        {
            data.GetOrCreateValue(component).Events.Add(
                method.Name,
                (Action)Delegate.CreateDelegate(typeof(Action), component, method)
            );
        }

        internal static void ClearEventDelegates(this IPlugin component)
        {
            data.GetOrCreateValue(component).Events.Clear();
        }

        internal static void DispatchEvent(this IPlugin component, string eventName)
        {
            data.GetOrCreateValue(component).Events[eventName]();
        }

        public static void SendEvent<T>(this IPlugin component, string id, T payload) where T : struct
        {
            Debug.Log($"SendEvent {id} with {payload}");

            var plugin = data.GetOrCreateValue(component);

            // Send payload to Blender
            // plugin.Sync.SendPluginSceneObjectMessage(data, id, payload);
        }

        public static void AddVertexDataStream<T>(this IPlugin component, string id, Action<string, Mesh, ArrayBuffer<T>> callback) where T : struct
        {
            Debug.Log("add vertex stream handler for " + id);

            var handlers = data.GetOrCreateValue(component).VertexDataStreams;

            if (!handlers.TryGetValue(id, out List<IVertexDataStreamHandler> values))
            {
                values = new List<IVertexDataStreamHandler>();
                handlers.Add(id, values);
            }

            values.Add(new VertexDataStreamHandler<T>
            {
                Callback = callback
            });
        }

        public static void DispatchHandlers(this IPlugin obj, string id, int size, IntPtr ptr)
        {
            var handlers = data.GetOrCreateValue(obj).Messages;

            if (handlers.TryGetValue(id, out List<IEventHandler> values))
            {
                foreach (var handler in values)
                {
                    handler.Dispatch(id, size, ptr);
                }
            }
        }

        public static void DispatchVertexDataStreams(this IPlugin obj, string id, Mesh mesh, ArrayBuffer<byte> arr)
        {
            // This assumes individual implementations can reinterpret the buffer.
            // Using ArrayBuffer - maybe not since it's managed memory already
            // (which would incur a second copy op)
            // And NativeArray doesn't have the ability to specify a dirty range here.
            // Need to really consolidate these array types betwen here, SharpRNA, LibCo, etc.

            var handlers = data.GetOrCreateValue(obj).VertexDataStreams;

            if (handlers.TryGetValue(id, out List<IVertexDataStreamHandler> values))
            {
                foreach (var handler in values)
                {
                    handler.Dispatch(id, mesh, arr);
                }
            }
        }
    }

    /// <summary>
    /// Required attribute for any MonoBehaviours implementing <see cref="IPlugin"/>.
    ///
    /// <para>
    ///     This defines metadata for associating the MonoBehaviour with a specific
    ///     Blender plugin and SceneObject.
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class PluginAttribute : Attribute
    {
        /// <summary>
        /// The name of the Blender plugin (required).
        ///
        /// This allows messaging to target the correct plugin and GameObjects.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// If specified, this will attach a copy of this MonoBehaviour to every
        /// GameObject that was instantiated through the plugin with the given .kind.
        ///
        /// This allows each GO to directly communicate with the copy of
        /// itself on the Blender side.
        /// </summary>
        public string Kind { get; set; }

        public PluginAttribute(string name)
        {
            Name = name;
        }
    }
}
