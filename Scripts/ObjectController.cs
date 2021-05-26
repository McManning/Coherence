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
    [ExecuteAlways]
    public class ObjectController : MonoBehaviour, INetworkTarget
    {
        /// <summary>
        /// The matching interop data for this entity
        /// </summary>
        public InteropSceneObject Data { get; set; }

        public HashSet<IComponent> Components { get; } = new HashSet<IComponent>();

        public NetworkTargetType GetNetworkType() => NetworkTargetType.Object;

        public string GetNetworkName() => Data.name;

        public void Awake()
        {
            gameObject.tag = "EditorOnly";
            gameObject.hideFlags = HideFlags.DontSave;
        }

        public void Sync()
        {
            // TODO: Delete?
        }

        internal void UpdateFromInterop(InteropSceneObject obj)
        {
            transform.position = obj.transform.position.ToVector3();
            transform.rotation = obj.transform.rotation.ToQuaternion();
            transform.localScale = obj.transform.scale.ToVector3();

            Data = obj;
        }

        public void OnRegistered() // registered as a network target
        {
            Network.Register(this, RpcRequest.UpdateObject, OnInteropUpdate);
            // Destruction of this object is handled via ObjectFactory
        }

        public void OnUnregistered()
        {

        }

        private void OnInteropUpdate(IntPtr ptr, InteropMessageHeader header)
        {
            var interop = FastStructure.PtrToStructure<InteropSceneObject>(ptr);
            UpdateFromInterop(interop);
        }
    }
}
