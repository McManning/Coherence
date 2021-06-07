using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Coherence
{
    public interface IComponent
    {
        // Components are also network message targets.
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
        /// Coherence component state information attached to a MonoBehaviour.
        ///
        /// This handles all bindings and events between the host MonoBehaviour and Coherence.
        /// </summary>
        internal class ComponentState : INetworkTarget
        {
            /// <summary>
            /// Host component that this is a state of.
            /// </summary>
            internal IComponent Component { get; set; }

            /// <summary>
            /// Name of this component for interprocess messages.
            /// In the form `objectName:componentName` to avoid conflict
            /// with other components and objects.
            /// </summary>
            public string Name { get; set; }

            internal ComponentInfo Info { get; set; }

            /// <summary>
            /// Event handler for OnUpdateMesh events triggered
            /// by a referenced <see cref="SyncedMesh"/>
            /// </summary>
            internal OnUpdateMeshEvent OnUpdateMesh;

            // Generated delegates to OnCoherence* event methods on the
            // host MonoBehaviour IFF declared on that MonoBehaviour.

            internal CoherenceEvent OnCoherenceConnected;
            internal CoherenceEvent OnCoherenceDisconnected;
            internal CoherenceEvent OnCoherenceEnabled;
            internal CoherenceEvent OnCoherenceDisabled;

            /// <summary>
            /// Buffer of properties provided by the remote application
            /// assigned from <see cref="RpcRequest.UpdateProperties"/>.
            /// </summary>
            internal ArrayBuffer<InteropProperty> RemoteProperties { get; }
                = new ArrayBuffer<InteropProperty>();

            /// <summary>
            /// Properties on the attached MonoBehaviour to update when associated
            /// remote properties change and are monitored for updates to notify
            /// the remote application on local changes
            /// (if the property exists in <see cref="RemoteProperties"/>).
            /// </summary>
            internal Dictionary<string, ISyncedProperty> Properties { get; }
                = new Dictionary<string, ISyncedProperty>();

            /// <summary>
            /// Handlers for custom network events between applications
            /// </summary>
            internal Dictionary<string, IEventHandler> OnNetworkEvent { get; }
                = new Dictionary<string, IEventHandler>();

            /// <summary>
            /// Handlers for custom vertex data shared between applications
            /// </summary>
            internal Dictionary<string, IVertexDataStreamHandler> VertexDataStreams { get; }
                = new Dictionary<string, IVertexDataStreamHandler>();

            internal void UnbindEvents()
            {
                Network.OnConnected -= OnCoherenceConnected;
                Network.OnDisconnected -= OnCoherenceDisconnected;
                Network.OnEnabled -= OnCoherenceEnabled;
                Network.OnDisabled -= OnCoherenceDisabled;
                // and so on...
            }

            internal void Setup()
            {
                Network.Register(this, RpcRequest.UpdateComponent, OnInteropUpdate);
                Network.Register(this, RpcRequest.DestroyComponent, OnInteropDestroy);
                Network.Register(this, RpcRequest.UpdateProperties, OnUpdateProperties);
            }

            internal void Destroy()
            {
                UnbindEvents();
                Network.Unregister(this);
            }

            internal void DispatchNetworkEvent(string id, int size, IntPtr ptr)
            {
                if (OnNetworkEvent.TryGetValue(id, out var handler))
                {
                    handler.Dispatch(id, size, ptr);
                }
            }

            public void DispatchVertexDataStreams(string id, Mesh mesh, ArrayBuffer<byte> arr)
            {
                // TODO:
                // This assumes individual implementations can reinterpret the buffer.
                // Using ArrayBuffer - maybe not since it's managed memory already
                // (which would incur a second copy op)
                // And NativeArray doesn't have the ability to specify a dirty range here.
                // Need to really consolidate these array types betwen here, SharpRNA, LibCo, etc.
                if (VertexDataStreams.TryGetValue(id, out var handler))
                {
                    handler.Dispatch(id, mesh, arr);
                }
            }

            /// <summary>
            /// Apply a set of property changes to the associated MonoBehaviour when
            /// the local state differs from the remote.
            ///
            /// <para>
            ///     Note that only properties defined by the remote will be
            ///     updated through this method.
            /// </para>
            /// </summary>
            internal void DispatchRemotePropertyUpdates()
            {
                // Call property setters for each modified interop prop
                for (int i = RemoteProperties.DirtyStart; i < RemoteProperties.DirtyLength; i++)
                {
                    var prop = RemoteProperties[i];
                    if (Properties.TryGetValue(prop.name, out var synced))
                    {
                        synced.FromInterop(prop);
                    }
                    else
                    {
                        InteropLogger.Warning(
                            $"[{Name}] Received {prop.type} property '{prop.name}' but there is no matching class property. " +
                            $"Declare a matching property in your IComponent class or remove the property from " +
                            $"the linked component in the connected application."
                        );
                    }
                }

                // Remove the dirty flag since we've confirmed and loaded property changes.
                RemoteProperties.Clean();
            }

            /// <summary>
            /// Send a set of property changes to the remote application
            /// when the local state differs from the remote.
            ///
            /// <para>
            ///     Note that this only updates properties that were already
            ///     declared by the component in the remote application.
            /// </para>
            /// </summary>
            internal void DispatchLocalPropertyUpdates()
            {
                for (int i = 0; i < RemoteProperties.Length; i++)
                {
                    var prop = RemoteProperties[i];

                    if (Properties.TryGetValue(prop.name, out var synced))
                    {
                        var newProp = synced.ToInterop();

                        // If there's a property change, dirty the array entry.
                        // This is done onto the remote set so that if we somehow
                        // get an UpdateProperties from remote, we don't need to do
                        // anything since the prop value was already set locally.
                        if (!prop.Equals(newProp))
                        {
                            Debug.Log($"[{Name}] OUTBOUND CHANGE: {prop} vs {newProp}");
                            RemoteProperties[i] = newProp;
                        }
                    }
                }

                // If we modified any properties - send the dirtied set to the remote.
                if (RemoteProperties.IsDirty)
                {
                    // TODO!
                    //Network.SendArray(RpcRequest.UpdateProperties, Name, RemoteProperties);
                    RemoteProperties.Clean();
                }
            }

            public void OnUpdateProperties(InteropMessage msg)
            {
                RemoteProperties
                    .Resize(msg.header.length)
                    .CopyFrom(msg.data, msg.header.index, msg.header.count);

                DispatchRemotePropertyUpdates();
            }

            public void OnInteropUpdate(InteropMessage msg)
            {
                var data = msg.Reinterpret<InteropComponent>();

                Debug.Log($"OnInteropUpdate {Name} for component");
                (Component as MonoBehaviour).enabled = (data.enabled == 1);
            }

            public void OnInteropDestroy(InteropMessage msg)
            {
                // TODO: Less roundabout way of doing this.
                Component.DestroyCoherenceComponent();
            }
        }

        // TODO: What if someone destroys the component through Unity's API and we don't
        // clean up events? Can I add a finalizer to ComponentData that'll trigger?
        // Unity overrides == null so we could track ALL component instances and iterate
        // through that list periodically to determine if one was nullified for cleanup.
        // Not a super great solution though.

        private static readonly ConditionalWeakTable<IComponent, ComponentState> data
            = new ConditionalWeakTable<IComponent, ComponentState>();

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
        internal static ComponentState InitializeCoherenceState(
            this IComponent component,
            InteropComponent interop,
            ComponentInfo info
        ) {
            var state = GetCoherenceState(component);
            state.Name = interop.target + ":" + interop.name;
            state.Info = info;
            state.Component = component;
            state.Setup();

            // Turn MethodInfo's into delegates for known events
            foreach (var method in info.Type.GetMethods())
            {
                switch (method.Name)
                {
                    case "OnUpdateMesh":
                        BindEvent(component, method, ref state.OnUpdateMesh);
                        break;

                    case "OnCoherenceConnected":
                        Network.OnConnected += BindEvent(component, method, ref state.OnCoherenceConnected);
                        break;
                    case "OnCoherenceDisconnected":
                        Network.OnDisconnected += BindEvent(component, method, ref state.OnCoherenceDisconnected);
                        break;
                    case "OnCoherenceEnabled":
                        Network.OnEnabled += BindEvent(component, method, ref state.OnCoherenceEnabled);
                        break;
                    case "OnCoherenceDisabled":
                        Network.OnDisabled += BindEvent(component, method, ref state.OnCoherenceDisabled);
                        break;
                    default: break;
                    // and so on... this is awful tbh.
                }
            }

            // Turn PropertyInfo entries to delegates into this instance
            foreach (var prop in info.Type.GetProperties())
            {
                try
                {
                    var synced = SyncedPropertyFactory.Create(component, prop);
                    state.Properties[synced.Name] = synced;
                }
                catch (NotSupportedException)
                {
                    // The factory will throw for anything that is picked up but can't be bound as a
                    // synced property. Either because it's missing a getter/setter or it's an
                    // unsupported data type. We'll ignore these errors since this list also includes
                    // things like "gameObject" or "rigidbody2D" for MonoBehaviours)
                }
                catch (Exception e)
                {
                    InteropLogger.Error($"Exception while binding property '{prop.Name}': {e}");
                }
            }

            // Dispatch an initial update
            DispatchUpdate(component, interop);

            return state;
        }

        private static T BindEvent<T>(this IComponent component, MethodInfo method, ref T localStore) where T : Delegate
        {
            localStore = (T)Delegate.CreateDelegate(
                typeof(T),
                component, method
            );

            return localStore;
        }

        public static void AddHandler<T>(this IComponent component, string id, Action<string, T> callback) where T : struct
        {
            Debug.Log("add handler for " + id);

            var handlers = component.GetCoherenceState().OnNetworkEvent;

            if (handlers.ContainsKey(id))
            {
                throw new Exception($"A handler is already registered for event '{id}'");
            }

            handlers[id] = new EventHandler<T>
            {
                Callback = callback
            };
        }

        internal static ComponentState GetCoherenceState(this IComponent component)
        {
            return data.GetOrCreateValue(component);
        }

        /// <summary>
        /// Coherence API method to send a custom event to the synced component in
        /// the connected application.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="component"></param>
        /// <param name="id"></param>
        /// <param name="payload"></param>
        public static void SendEvent<T>(this IComponent component, string id, T payload) where T : struct
        {
            Debug.Log($"SendEvent {id} with {payload}");

            // target ID is `obj_name:component_name`

            throw new NotImplementedException();

            // Send payload to Blender
            // plugin.Sync.SendPluginSceneObjectMessage(data, id, payload);
        }

        /// <summary>
        /// Coherence API event to add a new handler for vertex data
        /// from the connected application.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="component"></param>
        /// <param name="id"></param>
        /// <param name="callback"></param>
        public static void AddVertexDataStream<T>(this IComponent component, string id, Action<string, Mesh, ArrayBuffer<T>> callback) where T : struct
        {
            Debug.Log("add vertex stream handler for " + id);

            var handlers = component.GetCoherenceState().VertexDataStreams;

            if (handlers.ContainsKey(id))
            {
                throw new Exception($"A handler is already registered for vertex data stream '{id}'");
            }

            handlers[id] = new VertexDataStreamHandler<T>
            {
                Callback = callback
            };
        }

        public static void DispatchUpdate(this IComponent component, InteropComponent interop)
        {
            var state = component.GetCoherenceState();
            // TODO: enabled state toggle (unless it's just a property... ?)
        }

        /// <summary>
        /// Safely destroy both this component and its internal Coherence state.
        /// </summary>
        /// <param name="obj"></param>
        internal static void DestroyCoherenceComponent(this IComponent obj, bool destroyMonoBehaviour = true)
        {
            obj.GetCoherenceState().Destroy();
            data.Remove(obj);

            if (destroyMonoBehaviour)
            {
            #if UNITY_EDITOR
                UnityEngine.Object.DestroyImmediate(obj as UnityEngine.Object);
            #else
                UnityEngine.Object.Destroy(obj as UnityEngine.Object);
            #endif
            }
        }

        /// <summary>
        /// Sync event executed whenever the sync manager syncs.
        /// </summary>
        internal static void Sync(this IComponent obj)
        {
            // Send local property changes if we have any
            obj.GetCoherenceState().DispatchLocalPropertyUpdates();
        }
    }
}
