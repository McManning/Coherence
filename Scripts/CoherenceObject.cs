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
    /// <summary>
    /// An object synced with Coherence.
    ///
    /// Objects contain transforms (matrix and hierarchy) and Coherence components.
    /// </summary>
    [ExecuteAlways]
    public class CoherenceObject : MonoBehaviour, INetworkTarget
    {
        public string Name => data.name;

        public string ParentName => data.transform.parent.Value;

        /// <summary>
        /// The matching interop data for this entity
        /// </summary>
        private InteropSceneObject data;

        private HashSet<IComponent> components;

        void Awake()
        {
            gameObject.tag = "EditorOnly";
            gameObject.hideFlags = HideFlags.DontSave;
        }

        internal void Initialize(InteropSceneObject obj)
        {
            components = new HashSet<IComponent>();

            UpdateFromInterop(obj);
            Network.Register(this, RpcRequest.UpdateObject, OnInteropUpdate);
        }

        void OnDestroy()
        {
            DestroyAllComponents();
            Network.Unregister(this);
        }

        internal void UpdateFromInterop(InteropSceneObject obj)
        {
            transform.position = obj.transform.position.ToVector3();
            transform.rotation = obj.transform.rotation.ToQuaternion();
            transform.localScale = obj.transform.scale.ToVector3();

            data = obj;
        }

        private void OnInteropUpdate(InteropMessage msg)
        {
            UpdateFromInterop(msg.Reinterpret<InteropSceneObject>());
        }

        internal void AddCoherenceComponent(InteropComponent component)
        {
            var info = ComponentInfo.Find(component.name);
            if (info == null)
            {
                throw new Exception($"No components registered under name '{component.name}'");
            }

            // Add component to the object's GameObject and controller
            var instance = gameObject.AddComponent(info.Type) as IComponent;
            components.Add(instance);

            instance.InitializeCoherenceState(component, info);
        }

        /// <summary>
        /// Destroy all Coherence components attached to this object
        /// </summary>
        private void DestroyAllComponents()
        {
            foreach (var component in components)
            {
                component.DestroyCoherenceComponent(false);
            }

            components.Clear();
        }
    }
}
