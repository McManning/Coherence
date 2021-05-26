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
    // A plugin that listens to typical Coherence events (just like a Component would... wink wink)
    // plugins are attached to the Coherence root [Coherence] GameObject as monobehaviours.
    // So we can easily iterate through them by pulling IPlugin instances.
    public interface IPlugin
    {
        void OnUnregistered();

        void OnCoherenceStart();
        void OnCoherenceStop();
    }

    /// <summary>
    /// Coherence plugin that instantiates networked game objects on request
    /// </summary>
    public class ObjectFactory : MonoBehaviour, IPlugin
    {
        GameObject container;

        /// <summary>
        /// Objects added with parents that are not (yet) in the scene.
        /// </summary>
        readonly HashSet<ObjectController> orphans = new HashSet<ObjectController>();

        public void OnRegistered()
        {
            if (container == null)
            {
                container = new GameObject("Objects")
                {
                    tag = "EditorOnly",
                    hideFlags = HideFlags.NotEditable | HideFlags.DontSave
                };

                container.transform.parent = transform;
            }

            Network.Register(RpcRequest.AddObject, OnAddObject);
            Network.Register(RpcRequest.RemoveObject, OnRemoveObject);
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

        private void OnAddObject(IntPtr ptr, InteropMessageHeader header)
        {
            var obj = FastStructure.PtrToStructure<InteropSceneObject>(ptr);

            var prefab = CoherenceSettings.Instance.sceneObjectPrefab;
            var go = prefab ? Instantiate(prefab) : new GameObject();
            go.name = obj.name;

            var controller = go.AddComponent<ObjectController>();
            controller.UpdateFromInterop(obj);

            ReparentObject(controller);

            // Attach any orphaned objects that were waiting for this parent to be added.
            CheckOrphanedObjects();

            // Add the object as a listener for network events
            Network.RegisterTarget(controller);
        }

        private void OnRemoveObject(IntPtr ptr, InteropMessageHeader header)
        {
            var obj = FastStructure.PtrToStructure<InteropSceneObject>(ptr);

            var instance = Network.FindTarget(NetworkTargetType.Object, obj.name) as ObjectController;

            // Move children to the main container and orphan them.
            var children = instance.GetComponentsInChildren<ObjectController>();
            foreach (var child in children)
            {
                child.transform.parent = container.transform;
                orphans.Add(child);
            }

            // Cascade a destroy event to every component in the object
            foreach (var component in instance.Components)
            {
                component.DestroyCoherenceComponent();
            }

            orphans.Remove(instance);

            Network.UnregisterTarget(instance);
            DestroyImmediate(instance.gameObject);
        }

        /// <summary>
        /// Find an object in the Unity scene matching the current parent name
        /// provided by Blender and attach the object to it.
        ///
        /// If the parent cannot be found - the object is attached to the container
        /// and added to the orphans list to be later picked up by new entries.
        /// </summary>
        /// <param name="obj"></param>
        private void ReparentObject(ObjectController obj)
        {
            var parentName = obj.Data.transform.parent.Value;

            // Object is root level / unparented.
            if (string.IsNullOrEmpty(parentName))
            {
                obj.transform.parent = container.transform;
                orphans.Remove(obj);
                return;
            }

            // Find an already loaded parent
            var parent = Network.FindTarget(NetworkTargetType.Object, parentName) as ObjectController;
            if (parent != null)
            {
                obj.transform.parent = parent.transform;
                orphans.Remove(obj);
                return;
            }

            // A parent was defined by this object but it's not in the scene
            // (could be added out of sequence, or a non-transferrable type).
            // Add to the orphan list to be later picked up if it does get added.
            obj.transform.parent = container.transform;
            orphans.Add(obj);
        }

        /// <summary>
        /// Scan through orphaned objects for any new parent associations to add.
        /// </summary>
        private void CheckOrphanedObjects()
        {
            var parentedOrphans = new HashSet<ObjectController>();
            foreach (var orphan in orphans)
            {
                var parentName = orphan.Data.transform.parent.Value;
                var parent = Network.FindTarget(NetworkTargetType.Object, parentName) as ObjectController;
                if (parent != null)
                {
                    orphan.transform.parent = parent.transform;
                    parentedOrphans.Add(orphan);
                }
            }

            foreach (var orphan in parentedOrphans)
            {
                orphans.Remove(orphan);
            }
        }
    }
}
