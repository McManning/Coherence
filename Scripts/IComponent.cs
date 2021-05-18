using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Runtime.CompilerServices;
using System.Reflection;

namespace Coherence
{
    public interface IComponent
    {

    }

    /// <summary>
    /// Extension methods attached to <see cref="IComponent"/> MonoBehaviours.
    ///
    /// <para>
    ///     We use extensions on an interface instead of forcing developers to extend off of
    ///     a custom MonoBehaviour in case they also want to define their own base class
    ///     to replace MonoBehaviour for all of their components (or are using some third party
    ///     library that replaces MonoBehaviour).
    /// </para>
    /// </summary>
    public static class ComponentExtensions
    {
        private static readonly ConditionalWeakTable<IComponent, ComponentData> data
            = new ConditionalWeakTable<IComponent, ComponentData>();

        public static void AddHandler<T>(this IComponent component, string id, Action<string, T> callback) where T : struct
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

        internal static ComponentData GetCoherenceData(this IComponent component)
        {
            return data.GetOrCreateValue(component);
        }

        internal static void AddEventDelegate(this IComponent component, MethodInfo method)
        {
            data.GetOrCreateValue(component).Events.Add(
                method.Name,
                (Action)Delegate.CreateDelegate(typeof(Action), component, method)
            );
        }

        internal static void ClearEventDelegates(this IComponent component)
        {
            data.GetOrCreateValue(component).Events.Clear();
        }

        internal static void DispatchEvent(this IComponent component, string eventName)
        {
            data.GetOrCreateValue(component).Events[eventName]();
        }

        public static void SendEvent<T>(this IComponent component, string id, T payload) where T : struct
        {
            Debug.Log($"SendEvent {id} with {payload}");

            var instance = data.GetOrCreateValue(component);
            throw new NotImplementedException();

            // Send payload to Blender
            // plugin.Sync.SendPluginSceneObjectMessage(data, id, payload);
        }

        public static void AddVertexDataStream<T>(this IComponent component, string id, Action<string, Mesh, ArrayBuffer<T>> callback) where T : struct
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

        public static void DispatchHandlers(this IComponent obj, string id, int size, IntPtr ptr)
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

        public static void DispatchVertexDataStreams(this IComponent obj, string id, Mesh mesh, ArrayBuffer<byte> arr)
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
}
