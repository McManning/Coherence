using UnityEngine;
using UnityEditor;
using System.Runtime.InteropServices;
using SharedMemory;
using MessageType = UnityEditor.MessageType;

namespace Coherence
{
    [CustomEditor(typeof(CoherenceSettings))]
    public class CoherenceSettingsEditor : Editor
    {
        /*public CoherenceSettings Settings
        {
            get
            {
                return CoherenceSettings.Instance;
            }
        }*/

        /// <summary>
        /// Sync management singleton attached to the current scene.
        ///
        /// This will instantiate a new one if one does not already exist.
        /// </summary>
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
                    var go = new GameObject("[Coherence Sync]")
                    {
                        tag = "EditorOnly",
                        hideFlags = HideFlags.NotEditable | HideFlags.DontSave
                    };

                    sync = go.AddComponent<SyncManager>();
                }

                return sync;
            }
        }

        /// <summary>
        /// Instance of the sync manager in the scene
        /// </summary>
        private SyncManager sync;

        private bool showAdvancedSettings = false;
        private bool showExperimentalSettings = false;

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
            EditorGUI.BeginDisabledGroup(Sync.IsConnected);

            EditorGUILayout.PropertyField(serializedObject.FindProperty("viewportUpdateMode"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("viewportCameraPrefab"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("sceneObjectPrefab"));

            EditorGUI.EndDisabledGroup();

            if (sync.IsConnected)
            {
                EditorGUILayout.HelpBox(
                    "The above settings cannot be modified while Blender is connected",
                    MessageType.Warning
                );
            }
        }

        private void DrawMaterialSettings()
        {
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.PropertyField(serializedObject.FindProperty("defaultMaterial"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("displayMaterial"));

            // TODO: This counts as a "change" if you're opening/closing the property drawers.
            // Weird...
            EditorGUILayout.PropertyField(serializedObject.FindProperty("materialOverrides"));

            if (EditorGUI.EndChangeCheck())
            {
                Debug.LogWarning("Changed");
            }
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

            EditorGUILayout.PropertyField(serializedObject.FindProperty("bufferName"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("nodeCount"));

            EditorGUILayout.IntPopup(
                serializedObject.FindProperty("nodeSize"),
                // TODO: Cache / simplify
                new GUIContent[] {
                    new GUIContent("1 MB"),
                    new GUIContent("2 MB"),
                    new GUIContent("4 MB"),
                    new GUIContent("8 MB"),
                    new GUIContent("16 MB"),
                    new GUIContent("32 MB"),
                    new GUIContent("64 MB")
                },
                new int[] { 1, 2, 4, 8, 16, 32, 64 }
            );

            EditorGUILayout.PropertyField(serializedObject.FindProperty("pixelsNodeCount"));

            EditorGUILayout.PropertyField(serializedObject.FindProperty("maxViewportWidth"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("maxViewportHeight"));

            EditorGUI.EndDisabledGroup();

            var sharedMemoryUsage = CoherenceSettings.Instance.CalculateSharedMemoryUsage();

            EditorGUILayout.HelpBox(
                $"{sharedMemoryUsage} of shared memory will be used between Unity and Blender",
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

        private void DrawGreasePencilSettings()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Grease Pencil", EditorStyles.boldLabel);

            var greasePencilRenderMode = serializedObject.FindProperty("greasePencilRenderMode");

            EditorGUILayout.PropertyField(greasePencilRenderMode, new GUIContent("Render Mode"));

            if ((GreasePencilRenderMode)greasePencilRenderMode.enumValueIndex == GreasePencilRenderMode.Quads)
            {
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty("greasePencilMaterial"),
                    new GUIContent("    Material")
                );
            }

            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("greasePencilPrefab"),
                new GUIContent("Prefab")
            );

            if (EditorGUI.EndChangeCheck())
            {
                RefreshGreasePencilObjects();
            }
        }

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
            EditorGUILayout.IntPopup(
                serializedObject.FindProperty("viewportTextureScale"),
                // TODO: Cache / simplify
                new GUIContent[] {
                    new GUIContent("100%"),
                    new GUIContent("50%"),
                    new GUIContent("25%")
                },
                new int[] { 1, 2, 4 }
            );

            EditorGUI.BeginChangeCheck();

            DrawGreasePencilSettings();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Open With Blender", EditorStyles.boldLabel);

            // TODO: Encapsulate these as attributes / CustomPropertyDrawers
            // (Odin Inspector has some nice ones, maybe just copy the same behaviour)

            using (new EditorGUILayout.HorizontalScope())
            {
                var pathToBlender = serializedObject.FindProperty("pathToBlender");
                EditorGUILayout.PropertyField(pathToBlender);

                if (GUILayout.Button("Open"))
                {
                    var path = EditorUtility.OpenFilePanel("Location of Blender", pathToBlender.stringValue, "exe");
                    if (path.Length > 0)
                    {
                        pathToBlender.stringValue = path;
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                var pathToBlendFiles = serializedObject.FindProperty("pathToBlendFiles");
                EditorGUILayout.PropertyField(pathToBlendFiles);

                if (GUILayout.Button("Open"))
                {
                    var path = EditorUtility.OpenFolderPanel("Path to .blend Files", pathToBlendFiles.stringValue, "");
                    if (path.Length > 0)
                    {
                        pathToBlendFiles.stringValue = path;
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

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.HelpBox(
                "Coherence v#.#\n\n" +
                "Big blurb here about Coherence configurations, etc etc.",
                MessageType.None
            );

            DrawRunControls();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                DrawBasic();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Material Settings", EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                DrawMaterialSettings();
            }

            EditorGUILayout.Space();
            showAdvancedSettings = EditorGUILayout.BeginFoldoutHeaderGroup(showAdvancedSettings, "Advanced Settings");
            if (showAdvancedSettings)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    DrawAdvanced();
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space();
            showExperimentalSettings = EditorGUILayout.BeginFoldoutHeaderGroup(showExperimentalSettings, "Experimental");
            if (showExperimentalSettings)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    DrawExperimental();
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
