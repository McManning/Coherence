using UnityEngine;
using UnityEditor;
using System.Runtime.InteropServices;
using SharedMemory;
using MessageType = UnityEditor.MessageType;

namespace Coherence
{
    public class CoherenceWindow : EditorWindow
    {
        public CoherenceSettings Settings
        {
            get
            {
                return CoherenceSettings.Instance;
            }
        }

        public SyncManager Sync
        {
            get
            {
                if (sync == null)
                {
                    sync = FindObjectOfType<SyncManager>();
                }

                if (sync == null)
                {
                    var go = new GameObject("[Coherence Sync]");
                    go.tag = "EditorOnly";
                    go.hideFlags = HideFlags.NotEditable | HideFlags.DontSave;
                    sync = go.AddComponent<SyncManager>();
                }

                return sync;
            }
        }

        const string WINDOW_TITLE = "Coherence";
        const int MIN_NODE_COUNT = 2;
        const int MAX_NODE_COUNT = 20;

        private bool showAdvancedSettings = false;
        private bool showExperimentalSettings = false;

        /// <summary>
        /// Instance of the sync manager in the scene
        /// </summary>
        private SyncManager sync;

        [MenuItem("Window/Coherence")]
        public static void ShowWindow()
        {
            GetWindow<CoherenceWindow>(WINDOW_TITLE, true);
        }

        private void DrawRunControls()
        {
            var running = Sync.IsSetup;

            if (Sync.IsConnected)
            {
                EditorGUILayout.HelpBox("Connected to Blender", MessageType.Info);
            }
            else if (running)
            {
                EditorGUILayout.HelpBox("Waiting for Blender connection.", MessageType.Info);
            }

            if (running && GUILayout.Button("Stop"))
            {
                Stop();
            }

            if (!running && GUILayout.Button("Start"))
            {
                Start();
            }
        }

        private void DrawBasic()
        {
            if (Sync.IsConnected)
            {
                EditorGUILayout.HelpBox(
                    $"Some settings may not take effect while Blender is connected.",
                    MessageType.Warning
                );
            }

            // TODO: These don't really make sense to be editable while running.
            // I'm not going to iterate through and re-instantiate prefabs for anything
            // that was already added to the scene when they change these.

            Settings.viewportUpdateMode = (ViewportUpdateMode)EditorGUILayout.EnumPopup(
                "Viewport Update Mode",
                Settings.viewportUpdateMode
            );

            Settings.viewportCameraPrefab = (Camera)EditorGUILayout.ObjectField(
                "Viewport Prefab",
                Settings.viewportCameraPrefab,
                typeof(Camera),
                false
            );

            Settings.sceneObjectPrefab = (GameObject)EditorGUILayout.ObjectField(
                "Object Prefab",
                Settings.sceneObjectPrefab,
                typeof(GameObject),
                false
            );

            // TODO: Material overrides table
        }

        private void DrawOpenWithBlender()
        {
            if (Selection.activeGameObject == null)
            {
                EditorGUILayout.LabelField("Select a Game Object in the scene");
            }
            else if (CanOpenSelectionInBlender())
            {
                if (GUILayout.Button("Open TODO in Blender"))
                {
                    OpenSelectionInBlender();
                }
            }
            else
            {
                EditorGUILayout.LabelField("The selected object cannot be opened in Blender");
            }
        }

        private void Stop()
        {
            Sync.Teardown();

            DestroyImmediate(Sync.gameObject);
            sync = null;
        }

        private void Start()
        {
            Sync.Setup();
        }

        private bool CanOpenSelectionInBlender()
        {
            if (Selection.activeGameObject == null) {
                return false;
            }

            var meshRenderer = Selection.activeGameObject.GetComponent<MeshRenderer>();
            if (meshRenderer == null)
            {
                return false;
            }

            // TODO: Scan for a matching .blend file to whatever the source mesh is.
            // Return true/false based on that.

            return true;
        }

        private void OpenSelectionInBlender()
        {
            /*
                TODO:
                    IF there's not an existing blender connection:
                        - find the matching .blend for the selection
                        - start blender, passing args that will trigger the coherence plugin
                            to connect using the current CoherenceSettings.bufferName and
                            target .blend file

                    ELSE IF there's an existing blender connection
                        - SAFELY close the current .blend scene loaded and disconnect any synced objects
                        - Send the new .blend path to Blender to open and sync

                    - Somehow, in Unity, merge the existing selected object(s) with the sync data.
            */
            Debug.LogWarning("TODO");
        }

        private void DrawAdvanced()
        {
            EditorGUILayout.LabelField("Shared Memory Buffer Settings", EditorStyles.boldLabel);

            EditorGUI.BeginDisabledGroup(Sync.IsSetup);

            Settings.bufferName = EditorGUILayout.TextField("Buffer Name", Settings.bufferName);

            Settings.nodeCount = EditorGUILayout.IntSlider(
                "Node Count",
                Settings.nodeCount,
                MIN_NODE_COUNT,
                MAX_NODE_COUNT
            );

            Settings.nodeSize = EditorGUILayout.IntPopup(
                "Node Size",
                Settings.nodeSize,
                new string[] { "1 MB", "2 MB", "4 MB", "8 MB", "16 MB", "32 MB", "64 MB" },
                new int[] { 1, 2, 4, 8, 16, 32, 64 }
            );

            Settings.pixelsNodeCount = EditorGUILayout.IntSlider(
                "Pixels Node Count",
                Settings.pixelsNodeCount,
                MIN_NODE_COUNT,
                MAX_NODE_COUNT
            );

            Settings.maxViewportWidth = EditorGUILayout.IntField(
                "Max Viewport Width",
                Settings.maxViewportWidth
            );

            Settings.maxViewportHeight = EditorGUILayout.IntField(
                "Max Viewport Height",
                Settings.maxViewportHeight
            );

            var bufferHeaderSize = FastStructure.SizeOf<SharedHeader>();
            var messageBufferSize = Settings.NodeSizeBytes * Settings.nodeCount + bufferHeaderSize;
            var pixelsBufferSize = Settings.pixelsNodeCount * Settings.PixelsNodeSizeBytes + bufferHeaderSize;

            var expectedSharedMemorySize = (messageBufferSize + pixelsBufferSize) / 1024.0 / 1024.0;
            var units = "MB";

            if (expectedSharedMemorySize > 1024)
            {
                expectedSharedMemorySize /= 1024.0;
                units = "GB";
            }

            EditorGUI.EndDisabledGroup();

            EditorGUILayout.HelpBox(
                $"{expectedSharedMemorySize:F2} {units} of shared memory will be used between Unity and Blender",
                MessageType.Info
            );

            if (Sync.IsSetup)
            {
                EditorGUILayout.HelpBox(
                    "The above settings cannot be modified while Coherence is running",
                    MessageType.Warning
                );
            }
        }

        private int progressId;

        private void DrawExperimental()
        {
            EditorGUILayout.HelpBox(
                "Experimental features are a work in progress and may not function or crash the editor.",
                MessageType.Warning
            );

            // I don't like this - has a permanent indicator in the bottom toolbar in Unity.
            // But it'd be nice for certain long jobs (initial sync, big object sync, etc)

            if (GUILayout.Button("Start Task"))
            {
                progressId = Progress.Start("Foo bar", "Blah blah", Progress.Options.Indefinite); //  | Progress.Options.Unmanaged);
            }

            if (GUILayout.Button("Stop task"))
            {
                Progress.Remove(progressId);
                progressId = 0;
            }

            // Technically isn't "experimental" setting - but I want to encapsulate it into
            // the "unsafe" section for now BECAUSE the allocated space is based on this.
            // So if you have it at 25%, setup memory, and then scale it up to 100% it'll
            // run out of memory. But 100% then down to 50% for performance is fine.
            // I'd LIKE to be able to do this @ runtime, but need to make it safer first.
            Settings.viewportTextureScale = EditorGUILayout.IntPopup(
                "Viewport Texture Scale",
                Settings.viewportTextureScale,
                new string[] { "100%", "50%", "25%" },
                new int[] { 1, 2, 4 }
            );

            EditorGUI.BeginChangeCheck();

            EditorGUILayout.LabelField("Grease Pencil", EditorStyles.boldLabel);

            Settings.greasePencilRenderMode = (GreasePencilRenderMode)EditorGUILayout.EnumPopup(
                "Render Mode",
                Settings.greasePencilRenderMode
            );

            if (Settings.greasePencilRenderMode == GreasePencilRenderMode.Quads)
            {
                Settings.greasePencilMaterial = (Material)EditorGUILayout.ObjectField(
                    "    Material",
                    Settings.greasePencilMaterial,
                    typeof(Material),
                    false
                );
            }

            Settings.greasePencilPrefab = (GameObject)EditorGUILayout.ObjectField(
                "Scene Object Prefab",
                Settings.greasePencilPrefab,
                typeof(GameObject),
                false
            );

            if (EditorGUI.EndChangeCheck())
            {
                RefreshGreasePencilObjects();
            }

            EditorGUILayout.LabelField("Open With Blender", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                Settings.pathToBlender = EditorGUILayout.TextField("Path to Blender", Settings.pathToBlender);
                if (GUILayout.Button("Pick"))
                {
                    var path = EditorUtility.OpenFilePanel("Location of Blender", Settings.pathToBlender, "exe");
                    if (path.Length > 0)
                    {
                        Settings.pathToBlender = path;
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                Settings.pathToBlendFiles = EditorGUILayout.TextField("Path to .blend Files", Settings.pathToBlendFiles);
                if (GUILayout.Button("Pick"))
                {
                    var path = EditorUtility.OpenFolderPanel("Path to .blend Files", Settings.pathToBlendFiles, "");
                    if (path.Length > 0)
                    {
                        Settings.pathToBlendFiles = path;
                    }
                }
            }

            DrawOpenWithBlender();
        }

        private void RefreshGreasePencilObjects()
        {
            // TODO: Iterate GPs and either convert the GOs to line renderer-based or
            // quad based and apply the specified material.

            throw new System.NotImplementedException();
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Coherence v#.#", MessageType.None);

            DrawRunControls();

            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                DrawBasic();
            }

            showAdvancedSettings = EditorGUILayout.BeginFoldoutHeaderGroup(showAdvancedSettings, "Advanced Settings");
            if (showAdvancedSettings)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    DrawAdvanced();
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();


            showExperimentalSettings = EditorGUILayout.BeginFoldoutHeaderGroup(showExperimentalSettings, "Experimental");
            if (showExperimentalSettings)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    DrawExperimental();
                }
            }

            EditorUtility.SetDirty(Settings);
        }
    }
}
