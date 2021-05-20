using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

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
        /// <summary>
        /// Coherence component metadata attached to a MonoBehaviour
        /// </summary>
        internal class ComponentData
        {
            internal ComponentInfo Info { get; set; }

            internal SyncManager Sync { get; set; }

            internal MeshController Mesh { get; set; }

            internal Dictionary<string, List<IVertexDataStreamHandler>> VertexDataStreams { get; }
                = new Dictionary<string, List<IVertexDataStreamHandler>>();

            /// <summary>
            /// Event handler for OnUpdateMesh events triggered by <see cref="MeshController"/>
            /// </summary>
            internal OnUpdateMeshEvent OnUpdateMesh;

            internal OnCoherenceEvent OnCoherenceConnected;
            internal OnCoherenceEvent OnCoherenceDisconnected;

            internal Dictionary<string, List<IEventHandler>> OnNetworkEvent { get; } = new Dictionary<string, List<IEventHandler>>();

            internal void SetMeshController(MeshController mesh)
            {
                if (Mesh == mesh)
                    return;

                // Unbind previous mesh
                if (Mesh != null)
                {
                    Mesh.OnUpdateMesh -= OnUpdateMesh;
                }

                // Bind new mesh
                Mesh = mesh;
                if (OnUpdateMesh != null)
                {
                    mesh.OnUpdateMesh += OnUpdateMesh;
                    OnUpdateMesh.Invoke(mesh);
                }
            }
        }

        // TODO: What if someone destroys the component through Unity's API and we don't
        // clean up events? Can I add a finalizer to ComponentData that'll trigger?
        // Unity overrides == null so we could track ALL component instances and iterate
        // through that list periodically to determine if one was nullified for cleanup.
        // Not a super great solution though.

        private static readonly ConditionalWeakTable<IComponent, ComponentData> data
            = new ConditionalWeakTable<IComponent, ComponentData>();

        /// <summary>
        /// Setup the Coherence component data associated with the MonoBehaviour instance.
        ///
        /// This instantiation step runs once per instantiated MonoBehaviour in order to register
        /// event listeners to Coherence events and add properties used for passing data through apps.
        /// </summary>
        /// <param name="component"></param>
        /// <param name="info"></param>
        /// <param name="sync"></param>
        /// <returns></returns>
        internal static ComponentData CreateCoherenceData(this IComponent component, ComponentInfo info, SyncManager sync)
        {
            var data = GetCoherenceData(component);
            data.Info = info;
            data.Sync = sync;

            // Turn MethodInfo's into delegates into this instance
            foreach (var entry in info.EventLookupTable)
            {
                switch (entry.Key)
                {
                    case "OnUpdateMesh":
                        BindCoherenceEvent(component, entry.Value, ref data.OnUpdateMesh);
                        break;
                    case "OnCoherenceConnected":
                        sync.OnCoherenceConnected += BindCoherenceEvent(component, entry.Value, ref data.OnCoherenceConnected);
                        break;
                    case "OnCoherenceDisconnected":
                        sync.OnCoherenceDisconnected += BindCoherenceEvent(component, entry.Value, ref data.OnCoherenceDisconnected);
                        break;
                    // and so on... this is awful tbh.
                }
            }

            return data;
        }

        private static T BindCoherenceEvent<T>(this IComponent component, MethodInfo method, ref T localStore) where T : Delegate
        {
            localStore = (T)Delegate.CreateDelegate(
                typeof(T),
                component, method
            );

            return localStore;
        }

        public static void UnbindCoherenceEvents(this IComponent component)
        {
            var data = GetCoherenceData(component);
            data.Sync.OnCoherenceConnected -= data.OnCoherenceConnected;
            data.Sync.OnCoherenceDisconnected -= data.OnCoherenceDisconnected;
            // and so on...

            if (data.Mesh != null)
            {
                data.Mesh.OnUpdateMesh -= data.OnUpdateMesh;
            }
        }

        public static void AddHandler<T>(this IComponent component, string id, Action<string, T> callback) where T : struct
        {
            Debug.Log("add handler for " + id);

            var handlers = data.GetOrCreateValue(component).OnNetworkEvent;

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

        internal static void SetMeshController(this IComponent component, MeshController mesh)
        {
            data.GetOrCreateValue(component).SetMeshController(mesh);
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

        public static void DispatchNetworkEvent(this IComponent obj, string id, int size, IntPtr ptr)
        {
            var handlers = data.GetOrCreateValue(obj).OnNetworkEvent;

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
