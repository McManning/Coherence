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
    public interface IPlugin
    {
        void OnRegistered();

        void OnUnregistered();
    }

    /// <summary>
    /// Coherence plugin that instantiates networked game objects
    /// and adds/removes Coherence Components on request.
    /// </summary>
    public class ObjectFactory : MonoBehaviour, IPlugin
    {
        GameObject container;

        /// <summary>
        /// Objects added with parents that are not (yet) in the scene.
        /// </summary>
        readonly HashSet<CoherenceObject> orphans = new HashSet<CoherenceObject>();

        readonly HashSet<CoherenceObject> instances = new HashSet<CoherenceObject>();

        public void OnRegistered()
        {
            InteropLogger.Debug("ObjectFactory.OnRegistered");

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
            Network.Register(RpcRequest.AddComponent, OnAddComponent);

            Network.OnDisconnected += DestroyAllObjects;
        }

        public void OnUnregistered()
        {
            InteropLogger.Debug("ObjectFactory.OnUnregistered");
            Network.OnDisconnected -= DestroyAllObjects;
        }

        private void DestroyAllObjects()
        {
            foreach (var obj in instances)
            {
                DestroyImmediate(obj.gameObject);
            }

            orphans.Clear();
            instances.Clear();
        }

        private void OnAddObject(InteropMessage msg)
        {
            var obj = msg.Reinterpret<InteropSceneObject>();

            var prefab = CoherenceSettings.Instance.sceneObjectPrefab;
            var go = prefab ? Instantiate(prefab) : new GameObject();
            go.name = obj.name;

            var controller = go.AddComponent<CoherenceObject>();
            controller.Initialize(obj);

            // Attach to an existing parent and attach any orphans
            // waiting for this object to exist as new children
            ReparentObject(controller);
            CheckOrphanedObjects();

            instances.Add(controller);
        }

        private void OnRemoveObject(InteropMessage msg)
        {
            var obj = msg.Reinterpret<InteropSceneObject>();

            var instance = Network.FindTarget<CoherenceObject>(obj.name);

            // Move children to the main container and orphan them.
            var children = instance.GetComponentsInChildren<CoherenceObject>();
            foreach (var child in children)
            {
                child.transform.parent = container.transform;
                orphans.Add(child);
            }

            DestroyImmediate(instance.gameObject);

            orphans.Remove(instance);
            instances.Remove(instance);
        }

        private void OnAddComponent(InteropMessage msg)
        {
            var component = msg.Reinterpret<InteropComponent>();

            var obj = Network.FindTarget<CoherenceObject>(component.target);
            if (obj == null)
            {
                throw new Exception($"No objects found with the name '{component.target}'");
            }

            obj.AddCoherenceComponent(component);
        }

        /// <summary>
        /// Find an object in the Unity scene matching the current parent name
        /// provided by Blender and attach the object to it.
        ///
        /// If the parent cannot be found - the object is attached to the container
        /// and added to the orphans list to be later picked up by new entries.
        /// </summary>
        /// <param name="obj"></param>
        private void ReparentObject(CoherenceObject obj)
        {
            var parentName = obj.ParentName;

            // Object is root level / unparented.
            if (string.IsNullOrEmpty(parentName))
            {
                obj.transform.parent = container.transform;
                orphans.Remove(obj);
                return;
            }

            // Find an already loaded parent
            var parent = Network.FindTarget<CoherenceObject>(parentName);
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
            var parentedOrphans = new HashSet<CoherenceObject>();
            foreach (var orphan in orphans)
            {
                var parentName = orphan.ParentName;
                var parent = Network.FindTarget<CoherenceObject>(parentName);
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
