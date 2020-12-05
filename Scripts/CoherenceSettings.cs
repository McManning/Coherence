using SharedMemory;
using System;
using System.Collections.Generic;
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
        public string blenderMaterialName;
        public Material material;
    }

    // TODO: Hide all this in the inspector, OR use a custom inspector
    // and render it all nicely into the CoherenceWindow OR just have
    // a button that shows the settings window.

    public class CoherenceSettings : ScriptableObject
    {
        #region Settings Singleton

        const string SETTINGS_ASSET_PATH = "Assets/Settings/Coherence.asset";

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
        /// Global name of the shared message buffer
        /// </summary>
        public string bufferName = "Coherence";

        /// <summary>
        /// Number of nodes in the shared message buffer
        /// </summary>
        public int nodeCount = 5;

        /// <summary>
        /// Size of each node in the shared message buffer in MB
        /// </summary>
        public int nodeSize = 4;

        public int NodeSizeBytes
        {
            get
            {
                return nodeSize * 1024 * 1024;
            }
        }

        /// <summary>
        /// Number of nodes in the pixel buffer for viewport textures
        /// </summary>
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

        /// <summary>
        /// Maximum width for a viewport camera in Blender.
        ///
        /// This helps define the upper bound of the shared pixel data
        /// between Unity and Blender.
        /// </summary>
        public int maxViewportWidth = 1920;

        /// <summary>
        /// Maximum height for a viewport camera in Blender.
        ///
        /// This helps define the upper bound of the shared pixel data
        /// between Unity and Blender.
        /// </summary>
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

        /// <summary>
        /// Camera prefab to instantiate for each synced Blender viewport.
        /// Transform and projection will be automatically updated to
        /// match Blender's viewport settings.
        /// </summary>
        public Camera viewportCameraPrefab;

        /// <summary>
        /// How frequently do we send Unity viewport renders to Blender
        /// </summary>
        public ViewportUpdateMode viewportUpdateMode;

        #endregion

        #region Meshes

        /// <summary>
        /// GO prefab to instantiate for each synced Blender object.
        ///
        /// Additional components may be automatically added to the object
        /// based on its representation within Blender - e.g. adding a MeshRenderer
        /// </summary>
        public GameObject sceneObjectPrefab;

        public BlenderMaterialOverride[] materialOverrides;

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

        // Question is though, transform sync mode universal or per-object?

        // TODO: Supporting instancing (e.g. all trees in a scene with a
        // shared blender mesh). Probably a new object type we're passing
        // from Blender that just contains instance data (root object's name,
        // transform info, etc)

        // TODO: Axis transform setting? Can we guarantee (or assume) it's
        // coming in from a particular up axis?
    }
}
