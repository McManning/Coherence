using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using UnityEngine;

namespace Coherence
{
    /// <summary>
    /// Track local representation of objects in Blender scenes
    /// </summary>
    public class SceneManager : MonoBehaviour
    {
        Dictionary<string, ObjectController> controllers = new Dictionary<string, ObjectController>();

        InteropScene data;

        /// <summary>
        /// Update metadata about the scene within Blender
        /// </summary>
        internal void UpdateScene(InteropScene scene)
        {

        }

        internal ObjectController FindObject(string name)
        {
            if (controllers.TryGetValue(name, out ObjectController controller))
            {
                return controller;
            }

            return null;
        }
    
        internal void UpdateFromInterop(InteropScene interopScene)
        {
            data = interopScene;

            // Throws, but wasn't caught anywhere... ? Must've been caught in the RPC silently. Fak.
            // throw new NotImplementedException();
            InteropLogger.Debug($"Update from interop: {interopScene.objectCount} objects");
        }
    
        /*
        /// <summary>
        /// Add/remove/update ObjectControllers to match the input set.
        /// </summary>
        internal void UpdateObjects(InteropSceneObject[] objects)
        {
            var ids = objects.Select((obj) => obj.id);

            var addedIds = ids.Except(controllers.Keys);
            var removedIds = controllers.Keys.Except(ids);
            var updatedIds = ids.Intersect(controllers.Keys);
        
            Debug.Log($"Syncing objects {addedIds.Count()} added, {removedIds.Count()} removed, and {updatedIds.Count()} updated");

            foreach (var id in removedIds)
            {
                RemoveObject(id);
            }

            foreach (var obj in objects)
            {
                if (addedIds.Contains(obj.id))
                {
                    AddObject(obj);
                }
                else if (updatedIds.Contains(obj.id))
                {
                    controllers[obj.id].UpdateFromInterop(obj);
                }
            }
        }
        */

        internal void RemoveObject(string name)
        {
            InteropLogger.Debug($"Removing scene object {name}");
            controllers[name].gameObject.SetActive(false); // TODO: Actual removal?
            controllers.Remove(name);
        }

        internal void AddObject(string name, InteropSceneObject obj)
        {
            InteropLogger.Debug($"Adding scene object {name} with ID {obj.id}");

            var go = new GameObject($"{name} (ID: {obj.id})");
            go.transform.parent = transform;

            var controller = go.AddComponent<ObjectController>();

            controllers[name] = controller;
            controller.UpdateFromInterop(obj);

            InteropLogger.Debug($"Added GO {go.name}");
        }
    }
}
