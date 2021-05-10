﻿using SharedMemory;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Coherence
{
    /// <summary>
    /// How to represent Grease Pencil data in the scene
    /// </summary>
    public enum GreasePencilRenderMode
    {
        LineRenderer,
        Quads,
    }

    public enum ViewportUpdateMode
    {
        /// <summary>
        /// Send every frame we've got
        /// </summary>
        Unlimited,

        /// <summary>
        /// Send frames to Blender at a maximum of 30 FPS
        /// </summary>
        ThirtyFramesPerSecond,

        /// <summary>
        /// Send frames to Blender at a maximum of 60 FPS
        /// </summary>
        SixtyFramesPerSecond,

        /// <summary>
        /// Send frames to Blender only when data from Blender has changed
        /// (viewport camera transformation changes, mesh data changes, etc).
        ///
        /// This is most efficient option when there are no additional background
        /// animations that you need to see to the Blender viewport while working.
        /// </summary>
        OnChanges,
    }

    public enum TransformTransferMode
    {
        // TODO: What makes sense here?

        /// <summary>
        /// Synced objects in Unity will *always* match the transform data from Blender.
        /// </summary>
        UseBlenderTransforms,

        /// <summary>
        /// Synced objects in Unity will ignore transform data from Blender
        /// but Blender will update transforms to match the position in Unity.
        /// </summary>
        UseUnityTransforms,

        /// <summary>
        /// Synced objects may be moved in *either* Blender or Unity scene view and
        /// the synced copy will update accordingly.
        /// </summary>
        TwoWayTransforms,

        /// <summary>
        /// Synced objects will not update their transforms between the two applications.
        ///
        /// Objects synced from Blender will always be added with an identity transform.
        /// </summary>
        IgnoreTransforms,
    }

    /// <summary>
    ///
    /// </summary>
    public enum CoordinateMode
    {

    }

    /// <summary>
    /// Override default mapping behaviour between a named Blender material and a Unity material.
    /// </summary>
    [Serializable]
    public class BlenderMaterialOverride
    {
        [Tooltip("Name of the material in Blender to re-map")]
        public string blenderMaterial;

        [Tooltip("Unity material to use as a replacement")]
        public Material replacement;
    }

    // TODO: Hide all this in the inspector, OR use a custom inspector
    // and render it all nicely into the CoherenceWindow OR just have
    // a button that shows the settings window.

    public class CoherenceSettings : ScriptableObject
    {
        #region Settings Singleton

        const string SETTINGS_ASSET_PATH = "Assets/Settings/Coherence.asset";

        [SerializeField]
        internal bool isStarted = false;

        public static CoherenceSettings Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = AssetDatabase.LoadAssetAtPath<CoherenceSettings>(SETTINGS_ASSET_PATH);
                    if (instance == null)
                    {
                        instance = CreateInstance<CoherenceSettings>();
                        AssetDatabase.CreateAsset(instance, SETTINGS_ASSET_PATH);
                    }
                }

                return instance;
            }
        }

        [NonSerialized]
        private static CoherenceSettings instance;

        #endregion

        #region Shared Memory

        /// <summary>
        /// Estimate how much of shared memory will be allocated between Unity and Blender given current settings
        /// </summary>
        public string CalculateSharedMemoryUsage()
        {
            var bufferHeaderSize = FastStructure.SizeOf<SharedHeader>();
            var messageBufferSize = nodeSize * nodeCount + bufferHeaderSize;
            var pixelsBufferSize = pixelsNodeCount * PixelsNodeSizeBytes + bufferHeaderSize;

            var expectedSharedMemorySize = (messageBufferSize + pixelsBufferSize) / 1024.0 / 1024.0;
            var units = "MB";

            if (expectedSharedMemorySize > 1024)
            {
                expectedSharedMemorySize /= 1024.0;
                units = "GB";
            }

            return $"{expectedSharedMemorySize:F2} {units}";
        }

        [Tooltip("Global name of the shared message buffer")]
        public string bufferName = "Coherence";

        [Tooltip("Should Coherence restart if previously started after reloading scripts or entering game mode")]
        public bool restartAfterAssemblyReload = true;

        [Tooltip("Number of nodes in the shared message buffer")]
        [Range(2, 20)]
        public int nodeCount = 5;

        [Tooltip("Size of each node in the shared message buffer in bytes")]
        public int nodeSize = 4*1024*1024;

        [Tooltip("Number of nodes in the pixel buffer for viewport textures")]
        [Range(2, 20)]
        public int pixelsNodeCount = 2;

        /// <summary>
        /// Calculate how many bytes need to be in each node for the viewport texture buffer.
        ///
        /// This is based on the header + viewport width*height scaled down by viewportTextureScale
        /// </summary>
        public int PixelsNodeSizeBytes
        {
            // We need a fixed upper bound to allocate - even if the
            // individual viewport images are smaller than this upper bound.
            get {
                return FastStructure.SizeOf<InteropRenderHeader>() + (
                    Mathf.CeilToInt(maxViewportWidth / (float)viewportTextureScale) *
                    Mathf.CeilToInt(maxViewportHeight / (float)viewportTextureScale) *
                    3
                );
            }
        }

        [Tooltip(
            "Maximum width for a viewport camera in Blender.\n\n" +
            "This helps define the upper bound of the shared pixel buffer"
        )]
        public int maxViewportWidth = 1920;

        [Tooltip(
            "Maximum height for a viewport camera in Blender.\n\n" +
            "This helps define the upper bound of the shared pixel buffer"
        )]
        public int maxViewportHeight = 1080;

        #endregion

        #region Viewport Cameras

        /// <summary>
        /// Process a single message off the queue per tick - instead of
        /// ... don't I need something in the scene for this?
        /// </summary>
        // public bool processMessagePerTick;

        // 1/viewportScale is applied (e.g. 1/4 for 25% smaller texture)
        public int viewportTextureScale = 1;

        [Tooltip(
            "(Optional) - Prefab to use while instantiating cameras for each Blender viewport. \n\n" +
            "Transform and projecttion will be automatically updated to match Blender's viewport settings."
        )]
        public Camera viewportCameraPrefab;

        /// <summary>
        /// How frequently do we send Unity viewport renders to Blender
        /// </summary>
        public ViewportUpdateMode viewportUpdateMode;

        #endregion

        #region Meshes

        [Tooltip(
            "(Optional) - Prefab to use while instantiating scene objects for Blender meshes. \n\n" +
            "Some components will be automatically applied based on its representation within Blender" +
            " - e.g. adding a MeshRenderer."
        )]
        public GameObject sceneObjectPrefab;

        #endregion

        #region Materials

        [Tooltip("Material to use when a mapping cannot be made between a Blender material and one in Unity")]
        public Material defaultMaterial;

        [Tooltip("Material to use while using alternative display modes (UV checker, normals, vertex colors, etc)")]
        public Material displayMaterial;

        [Tooltip("Overrides to automatic mapping between Blender and Unity material names")]
        public List<BlenderMaterialOverride> materialOverrides;

        [Tooltip(
            "If a material is not mapped through Material Overrides then a material with " +
            "the same name as the Blender material will be loaded from this path via Resources.Load()"
        )]
        public string resourcesPath = "Materials";

        public Material GetMatchingMaterial(string blenderMaterialName)
        {
            // Find an override for the Blender material
            var match = materialOverrides.Find((mo) => mo.blenderMaterial == blenderMaterialName);
            if (match != null)
            {
                if (match.replacement == null)
                {
                    Debug.LogWarning(
                        $"Ignoring override setting for [{blenderMaterialName}] - " +
                        $"no replacement material was specified."
                    );
                }
                else
                {
                    return match.replacement;
                }
            }

            // Try to find a loadable resource with a matching name
            string path = $"{resourcesPath}/{blenderMaterialName}";

            var mat = Resources.Load<Material>(path);
            if (mat != null)
            {
                return mat;
            }

            Debug.LogWarning(
                $"Could not find a material matching Blender's [{blenderMaterialName}] from " +
                $"override list or [*/Resources/{resourcesPath}/{blenderMaterialName}]."
            );

            // Fallback to default
            return defaultMaterial;
        }

        #endregion

        #region Grease Pencil

        /// <summary>
        /// How we represent grease pencil data in Unity
        /// </summary>
        public GreasePencilRenderMode greasePencilRenderMode;

        /// <summary>
        /// Material to apply if transfer mode is set to quads
        /// </summary>
        public Material greasePencilMaterial;

        /// <summary>
        /// GameObject prefab for each grease pencil stroke represented in the scene
        /// </summary>
        public GameObject greasePencilPrefab;

        #endregion

        #region Search Paths

        // TODO: Smarter.
        public string pathToBlender = "C:\\Program Files\\Blender Foundation\\Blender 2.82\\blender.exe";

        // TODO: Smarter.
        public string pathToBlendFiles = "D:\\Blender\\Coherence_Blend_Files";

        #endregion

        #region Texture Sync

        [Tooltip("Render Texture targets for textures synced from Blender")]
        public List<BlenderTexture> textureSlots;

        #endregion

        // TODO: Metaballs
        // material / GO prefab
        // going to convert to mesh in Blender first probably. Don't want
        // to write a bunch of code to support a custom transfer format
        // AND THEN write the compute shaders for it if I'm ultimately
        // converting it to mesh anyway in my own use cases.
        // Ref: https://blender.stackexchange.com/a/179015

        // NOTE: Any settings that need to be communicated back to Blender
        // on connect should be encapsulated in an interop struct that
        // we can just push right through to Blender.

        // TODO: Settings to ignore blender transform OR apply unity transforms
        // to Blender. For example, I'd like to be able to model some stuff and
        // apply unity physics to it - and then adjust the model a bit without
        // blender's transform updating the physics-based positioning.
        // Alternatively - I'd like to be able to pull in a model from blender,
        // move it around in Unity, and then move around in blender independently.

        #region Plugin Management

        /// <summary>
        /// Delegate is listening to a Coherence event
        /// </summary>
        struct EventHandler
        {
            public IPlugin plugin;
            public Action callable;
        }

        /// <summary>
        /// Plugins instances actively listening to Coherence events (OnDisconnect, OnConnect, etc)
        /// </summary>
        internal DictionarySet<string, IPlugin> EventHandlers { get; } = new DictionarySet<string, IPlugin>();

        /// <summary>
        /// Get a list of all plugins, regardless of registration state
        /// </summary>
        internal Dictionary<string, PluginInfo> Plugins {
            get {
                // If we came out of an assembly reload, try to restore.
                if (plugins == null)
                {
                    RestorePlugins();
                }
                return plugins;
            }
        }

        private Dictionary<string, PluginInfo> plugins;

        internal Dictionary<string, PluginInfo> RegisteredPlugins
        {
            get
            {
                if (registeredPlugins == null)
                {
                    RestorePlugins();
                }
                return registeredPlugins;
            }
        }

        private Dictionary<string, PluginInfo> registeredPlugins;

        /// <summary>
        /// Name of registered plugins stored between assembly reloads
        /// </summary>
        [SerializeField]
        private List<string> registeredPluginNames;

        /// <summary>
        /// Populate the list of event delegates with all event methods
        /// that can be executed on the plugin instance.
        /// </summary>
        internal void BindEventHandlers(PluginInfo plugin, IPlugin instance)
        {
            foreach (var method in plugin.EventMethods.Items(instance.GetType()))
            {
                EventHandlers.Add(method.Name, instance);
                instance.AddEventDelegate(method);
            }
        }

        /// <summary>
        /// Remove a plugin from all event handlers
        /// </summary>
        internal void UnbindEventHandlers(IPlugin instance)
        {
            EventHandlers.RemoveAll(instance);
            instance.ClearEventDelegates();
        }

        /// <summary>
        /// Dispatch a named event (e.g. "OnConnected") to all plugins with a matching method.
        /// </summary>
        /// <param name="eventName"></param>
        internal void DispatchEvent(string eventName)
        {
            foreach (var plugin in EventHandlers.Items(eventName))
            {
                plugin.DispatchEvent(eventName);
            }
        }

        /* This all probably belongs in Sync. Since it's the only thing that manages objects.
            Sans global plugins...

        /// <summary>
        /// Instantiate a plugin into the global scope
        /// </summary>
        internal void InstantiateGlobalPlugin(string name)
        {
            var plugin = FindPlugin(name);
            var instance = plugin.Instantiate(this);

            BindEventHandlers(plugin, instance);
        }

        internal void DestroyGlobalPlugin(string name)
        {
            var plugin = FindPlugin(name);

            UnbindEventHandlers(plugin.Instances[0]);

            plugin.DestroyAllInstances();
        }

        internal void InstantiateSceneObjectPlugin(ObjectController target, string name, string kind)
        {
            var plugin = FindPlugin(name, kind);
            var instance = plugin.Instantiate(this, target);

            BindEventHandlers(plugin, instance);
        }

        internal void DestroySceneObjectPlugin(ObjectController target, string name, string kind)
        {
            var plugin = FindPlugin(name, kind);

            throw new NotImplementedException("need to figure this out");
            // UnbindEventHandlers(plugin.Inxxstances[0]);

            plugin.DestroyInstance(target);
        }
        */

        internal void RegisterPlugin(string name)
        {
            var plugin = Plugins[name];
            RegisteredPlugins.Add(name, plugin);
            registeredPluginNames.Add(name);
        }

        internal void UnregisterPlugin(string name)
        {
            RegisteredPlugins.Remove(name);
            registeredPluginNames.Remove(name);
        }

        /// <summary>
        /// Restore previously registered plugins after an assembly reload
        /// </summary>
        internal void RestorePlugins()
        {
            plugins = new Dictionary<string, PluginInfo>();
            registeredPlugins = new Dictionary<string, PluginInfo>();

            var pluginType = typeof(IPlugin);

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var type in assembly.GetTypes())
                {
                    if (pluginType.IsAssignableFrom(type) && !type.IsInterface)
                    {
                        AddPluginInfo(type);
                    }
                }
            }

            // Register everything previously added
            var prevRegisteredPlugins = registeredPluginNames;
            registeredPluginNames = new List<string>();

            foreach (var name in prevRegisteredPlugins)
            {
                if (!plugins.ContainsKey(name))
                {
                    Debug.LogError($"Could not restore plugin [{name}] - missing after assembly reload");
                }
                else
                {
                    RegisterPlugin(name);
                }
            }
        }

        private void AddPluginInfo(Type type)
        {
            var attr = type.GetCustomAttribute<PluginAttribute>();
            if (attr == null)
            {
                Debug.LogError("missing attr"); // TODO: message
                return;
            }

            if (!Plugins.TryGetValue(attr.Name, out PluginInfo plugin))
            {
                plugin = new PluginInfo();
                plugin.Name = attr.Name;
                Plugins.Add(attr.Name, plugin);
            }

            if (string.IsNullOrEmpty(attr.Kind))
            {
                plugin.SetGlobal(type);
            }
            else
            {
                plugin.AddKind(attr.Kind, type);
            }
        }

        #endregion
    }
}
