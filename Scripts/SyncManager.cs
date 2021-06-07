using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Profiling.Memory.Experimental;
using UnityEngine.Rendering;
using SharedMemory;
using UnityEditor;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

namespace Coherence
{
    public delegate void CoherenceEvent();

    /// <summary>
    /// Handles communication between Blender and Unity and passes
    /// the appropriate data to subcomponents for internal processing.
    /// </summary>
    [ExecuteAlways]
    public class SyncManager : MonoBehaviour
    {
        ObjectFactory objects;
        MeshFactory meshes;
        Viewports viewports;
        ImageSync images;

        void OnEnable()
        {
            InteropLogger.Debug("SyncManager.OnEnable");
        }

        void OnDisable()
        {
            InteropLogger.Debug("SyncManager.OnDisable");

            // Can't teardown in OnDisable because we'd DestroyImmediate()
            // child components which isn't allowed in Unity.
            // Teardown();
        }

        private void LoadPlugins()
        {
            if (!viewports)
            {
                viewports = gameObject.AddComponent<Viewports>();
                viewports.OnRegistered();
            }

            if (!meshes)
            {
                meshes = gameObject.AddComponent<MeshFactory>();
                meshes.OnRegistered();
            }

            if (!objects)
            {
                objects = gameObject.AddComponent<ObjectFactory>();
                objects.OnRegistered();
            }

            if (!images)
            {
                images = gameObject.AddComponent<ImageSync>();
                images.OnRegistered();
            }
        }

        private void UnloadPlugins()
        {
            Network.UnregisterAll();

            if (images)
            {
                images.OnUnregistered();
                DestroyImmediate(images);
                images = null;
            }

            if (objects)
            {
                objects.OnUnregistered();
                DestroyImmediate(objects);
                objects = null;
            }

            if (meshes)
            {
                meshes.OnUnregistered();
                DestroyImmediate(meshes);
                meshes = null;
            }

            if (viewports)
            {
                viewports.OnUnregistered();
                DestroyImmediate(viewports);
                viewports = null;
            }
        }

        /// <summary>
        /// Create a shared memory space for Blender to connect to.
        /// </summary>
        public void Setup()
        {
            if (Network.IsRunning)
            {
                return;
            }

            gameObject.transform.parent = null;

            LoadPlugins();

            Network.Start();

            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            EditorApplication.update += OnEditorUpdate;
            SceneManager.activeSceneChanged += OnSceneUnloaded;
            EditorSceneManager.activeSceneChangedInEditMode += OnSceneUnloaded;
            EditorApplication.playModeStateChanged += OnTogglePlayMode;
        }

        /// <summary>
        /// Destroy the shared memory space and temporary scene objects
        /// </summary>
        public void Teardown()
        {
            InteropLogger.Debug("SyncManager.Teardown!");

            Network.Stop();

            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            EditorApplication.update -= OnEditorUpdate;
            SceneManager.activeSceneChanged -= OnSceneUnloaded;
            EditorSceneManager.activeSceneChangedInEditMode -= OnSceneUnloaded;
            EditorApplication.playModeStateChanged -= OnTogglePlayMode;

            UnloadPlugins();

            // Destroy any lingering children that may have been created
            // by plugins that were not properly cleaned up
            /*foreach (Transform child in transform)
            {
                DestroyImmediate(child.gameObject);
            }*/

            DestroyImmediate(gameObject);
        }

        private void OnTogglePlayMode(PlayModeStateChange obj)
        {
            InteropLogger.Debug("SyncManager.OnTogglePlayMode");
            CoherenceSettings.Instance.shouldRestart = false;

            Teardown();
        }

        private void OnSceneUnloaded(Scene current, Scene next)
        {
            InteropLogger.Debug("SyncManager.OnSceneUnloaded");
            Teardown();
        }

        private void OnEditorUpdate()
        {
            // In editor, process alongside editor updates
            if (!Application.isPlaying)
                OnUpdate();
        }

        private void Update()
        {
            // In play mode, process alongside the typical Update()
            if (Application.isPlaying)
                OnUpdate();
        }

        private void OnUpdate()
        {
            if (Network.IsConnected)
            {
                // Messages from Blender will typically affect the scene view
                // (camera movement, object movement, new mesh data, etc).
                // While running in editor mode we don't typically see these
                // repaint - so we'll need to force a redraw of scene views.
                SceneView.RepaintAll();
            }

            Network.Sync();
        }

        /// <summary>
        /// Make sure we teardown our connection on reload to avoid
        /// entering an invalid state between assembly reloads
        /// (as things could be unloaded/reset by dependent scripts)
        /// </summary>
        private void OnBeforeAssemblyReload()
        {
            InteropLogger.Debug("SyncManager.OnBeforeAssemblyReload");

            var settings = CoherenceSettings.Instance;
            settings.shouldRestart = Network.IsRunning && settings.restartAfterAssemblyReload;

            Teardown();
        }
    }
}
