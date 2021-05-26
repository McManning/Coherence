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
    /// Plugin that handles the creation and registration of
    /// Coherence Components attached to synced objects
    /// </summary>
    public class ComponentFactory : MonoBehaviour, IPlugin
    {
        public void OnRegistered()
        {
            Network.Register(RpcRequest.AddComponent, OnAddComponent);
        }

        private void OnAddComponent(IntPtr ptr, InteropMessageHeader header)
        {
            var component = FastStructure.PtrToStructure<InteropComponent>(ptr);

            var info = ComponentInfo.Find(component.name);
            if (info == null)
            {
                throw new Exception($"No components registered under name '{component.name}'");
            }

            var obj = Network.FindTarget(NetworkTargetType.Object, component.target) as ObjectController;
            if (obj == null)
            {
                throw new Exception($"No objects found with the name '{component.target}'");
            }

            // Add component to the object's GameObject and controller
            var instance = obj.gameObject.AddComponent(info.Type) as IComponent;
            obj.Components.Add(instance);

            var state = instance.InitializeCoherenceState(component, info);
            Network.RegisterTarget(state);
        }

        public void OnUnregistered()
        {
            // Network handlers are automatically unregistered.
        }

        public void OnCoherenceStart()
        {

        }

        public void OnCoherenceStop()
        {

        }
    }
}
