using SharedMemory;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace Coherence
{
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

        /// <summary>
        /// Flag persisted between assembly reloads to indicate whether or
        /// not Coherence was running prior to the reload. This will help us
        /// decide whether or not to restart Coherence once reload is complete.
        /// </summary>
        [SerializeField]
        internal bool shouldRestart = false;

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

        /// <summary>
        /// Current instance of the sync manager.
        ///
        /// When queried, if one does not exist it will be created and added to the scene on-demand.
        /// </summary>
        internal static SyncManager Sync
        {
            get {
                if (sync == null)
                {
                    sync = FindObjectOfType<SyncManager>();
                    if (sync == null)
                    {
                        var go = new GameObject("[Coherence]")
                        {
                            tag = "EditorOnly",
                            hideFlags = HideFlags.NotEditable | HideFlags.DontSave
                        };

                        sync = go.AddComponent<SyncManager>();
                    }
                }
                return sync;
            }
        }

        [NonSerialized]
        private static CoherenceSettings instance;

        [NonSerialized]
        private static SyncManager sync;

        #endregion

        [DidReloadScripts]
        private static void OnScriptsReloaded()
        {
            if (Instance.shouldRestart)
            {
                Sync.Setup();
            }
        }

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
            if (string.IsNullOrEmpty(blenderMaterialName))
            {
                return defaultMaterial;
            }

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

        #region Search Paths

        // TODO: Smarter.
        public string pathToBlender = "C:\\Program Files\\Blender Foundation\\Blender 2.82\\blender.exe";

        // TODO: Smarter.
        public string pathToBlendFiles = "D:\\Blender\\Coherence_Blend_Files";

        #endregion

        #region Image Sync

        [Tooltip("Render Texture targets for textures synced from Blender")]
        public List<BlenderTexture> textureSlots;

        #endregion

        #region Component Management

        /*
        internal Dictionary<string, ComponentInfo> RegisteredComponents
        {
            get
            {
                if (registeredComponents == null)
                {
                    RestoreComponents();
                }
                return registeredComponents;
            }
        }

        private Dictionary<string, ComponentInfo> registeredComponents;

        /// <summary>
        /// Name of registered components stored between assembly reloads
        /// </summary>
        [SerializeField]
        private List<string> registeredComponentNames;
        */

        /*

        internal void RegisterComponent(string name)
        {
            var info = ComponentInfo.Find(name);
            RegisteredComponents.Add(name, info);
            registeredComponentNames.Add(name);
        }

        internal void UnregisterComponent(string name)
        {
            RegisteredComponents.Remove(name);
            registeredComponentNames.Remove(name);
        }

        /// <summary>
        /// Restore previously registered plugins after an assembly reload
        /// </summary>
        internal void RestoreComponents()
        {
            registeredComponents = new Dictionary<string, ComponentInfo>();

            // Register everything previously added
            var prevRegistered = registeredComponentNames;
            registeredComponentNames = new List<string>();

            foreach (var name in prevRegistered)
            {
                if (ComponentInfo.Find(name) == null)
                {
                    Debug.LogError($"Could not restore component [{name}] - missing after assembly reload");
                }
                else
                {
                    RegisterComponent(name);
                }
            }
        }
        */

        #endregion
    }
}
