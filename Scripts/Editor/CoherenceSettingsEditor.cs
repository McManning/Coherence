using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using System.Runtime.InteropServices;
using SharedMemory;
using MessageType = UnityEditor.MessageType;
using System.Collections.Generic;

namespace Coherence
{
    [CustomEditor(typeof(CoherenceSettings))]
    public class CoherenceSettingsEditor : Editor
    {
        private bool showMainSettings = true;
        private bool showMaterialSettings = true;
        private bool showPlugins = true;
        private bool showTextureSyncSettings = true;
        private bool showAdvancedSettings = false;
        private bool showExperimentalSettings = false;

        private bool IsRunning
        {
            get { return sync != null && sync.IsRunning; }
        }

        private bool IsConnected
        {
            get { return sync != null && sync.IsConnected; }
        }

        /// <summary>
        /// Instance of the sync manager in the scene
        /// </summary>
        private SyncManager sync;

        private ReorderableList materialOverridesList;
        private ReorderableList textureSlotList;

        private void OnEnable()
        {
            materialOverridesList = new ReorderableList(
                serializedObject,
                serializedObject.FindProperty("materialOverrides"),
                true, true, true, true
            ) {
                drawHeaderCallback = DrawMaterialOverridesHeader,
                drawElementCallback = DrawMaterialOverride,
                showDefaultBackground = false
            };

            textureSlotList = new ReorderableList(
                serializedObject,
                serializedObject.FindProperty("textureSlots"),
                true, true, true, true
            ) {
                drawHeaderCallback = DrawTextureSlotsHeader,
                drawElementCallback = DrawTextureSlot,
                showDefaultBackground = false
            };

            // If we went through an assembly reload - check if we should reboot Coherence.
            var instance = CoherenceSettings.Instance;
            if (instance.isStarted && !IsRunning && instance.restartAfterAssemblyReload)
            {
                Start();
            }
            else
            {
                instance.isStarted = false;
            }
        }

        private void DrawMaterialOverridesHeader(Rect rect)
        {
            EditorGUI.LabelField(rect, "Material Overrides");
        }

        private void DrawMaterialOverride(Rect rect, int index, bool isActive, bool isFocused)
        {
            var element = materialOverridesList.serializedProperty.GetArrayElementAtIndex(index);

            EditorGUI.PropertyField(
                new Rect(rect.x, rect.y, 120, EditorGUIUtility.singleLineHeight),
                element.FindPropertyRelative("blenderMaterial"),
                GUIContent.none
            );

            EditorGUI.PropertyField(
                new Rect(rect.x + 120, rect.y, rect.width - rect.x - 100, EditorGUIUtility.singleLineHeight),
                element.FindPropertyRelative("replacement"),
                GUIContent.none
            );
        }

        private void DrawTextureSlotsHeader(Rect rect)
        {
            EditorGUI.LabelField(rect, "Texture Slots");
        }

        private void DrawTextureSlot(Rect rect, int index, bool isActive, bool isFocused)
        {
            var element = textureSlotList.serializedProperty.GetArrayElementAtIndex(index);

            EditorGUI.PropertyField(
                new Rect(rect.x, rect.y, 120, EditorGUIUtility.singleLineHeight),
                element.FindPropertyRelative("name"),
                GUIContent.none
            );

            EditorGUI.PropertyField(
                new Rect(rect.x + 120, rect.y, rect.width - rect.x - 100, EditorGUIUtility.singleLineHeight),
                element.FindPropertyRelative("target"),
                GUIContent.none
            );
        }

        private void DrawRunControls()
        {
            if (IsConnected)
            {
                EditorGUILayout.HelpBox("Connected to Blender", MessageType.Info);
            }
            else if (IsRunning)
            {
                EditorGUILayout.HelpBox("Waiting for Blender connection.", MessageType.Info);
            }

            if (IsRunning && GUILayout.Button("Stop"))
            {
                Stop();
            }

            if (!IsRunning && GUILayout.Button("Start"))
            {
                Start();
            }

            //var prevWidth = EditorGUIUtility.labelWidth;
            //EditorGUIUtility.labelWidth = EditorGUIUtility.currentViewWidth - 50;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                EditorGUIUtility.labelWidth = 180;
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty("restartAfterAssemblyReload")
                );
                EditorGUIUtility.labelWidth = 0;
            }

        }

        private void DrawBasic()
        {
            EditorGUI.BeginDisabledGroup(IsConnected);

            // EditorGUILayout.PropertyField(serializedObject.FindProperty("viewportUpdateMode"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("viewportCameraPrefab"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("sceneObjectPrefab"));

            EditorGUI.EndDisabledGroup();

            if (IsConnected)
            {
                EditorGUILayout.HelpBox(
                    "The above settings cannot be modified while Blender is connected",
                    MessageType.Warning
                );
            }
        }

        private void DrawPlugin(PluginInfo plugin)
        {
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Header
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(plugin.Name, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Unregister"))
                {
                    CoherenceSettings.Instance.UnregisterPlugin(plugin.Name);
                }

                if (plugin.globalType != null && GUILayout.Button("Edit"))
                {
                    // open script
                }
            }

            // High level information
            if (plugin.globalType != null)
            {
                EditorGUILayout.LabelField(
                    $"Global={plugin.globalType}"
                );
            }

            // Registered SceneObject.kind scripts
            if (plugin.kindTypes.Count > 0)
            {
                DrawPluginKinds(plugin);
            }

            EditorGUILayout.EndVertical();

            if (EditorGUI.EndChangeCheck())
            {
                // Debug.LogWarning("Changed");
                // TODO
            }
        }

        private void DrawPluginKinds(PluginInfo plugin)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Kinds", EditorStyles.boldLabel);

            foreach (var kind in plugin.kindTypes)
            {
                var rect = EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

                EditorGUILayout.PrefixLabel(kind.Key);

                var instances = 0;
                if (plugin.kindInstances.ContainsKey(kind.Key))
                {
                    instances = plugin.kindInstances[kind.Key].Count;
                }

                if (GUILayout.Button($"{instances} objects"))
                {
                    // select instances
                }

                if (GUILayout.Button("Edit"))
                {
                    // open script
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawPlugins()
        {
            var plugins = CoherenceSettings.Instance.Plugins;
            var registered = CoherenceSettings.Instance.RegisteredPlugins;

            foreach (var plugin in plugins)
            {
                if (registered.ContainsKey(plugin.Key))
                {
                    DrawPlugin(plugin.Value);
                    EditorGUILayout.Space();
                }
            }

            var rect = EditorGUILayout.BeginHorizontal();

            if (EditorGUILayout.DropdownButton(
                new GUIContent("Register Plugin"),
                FocusType.Passive
            )) {
                GenericMenu menu = new GenericMenu();

                foreach (var name in plugins.Keys)
                {
                    if (!registered.ContainsKey(name))
                    {
                        menu.AddItem(
                            new GUIContent(name),
                            false,
                            () => CoherenceSettings.Instance.RegisterPlugin(name)
                        );
                    }
                }

                if (menu.GetItemCount() < 1)
                {
                    menu.AddItem(new GUIContent("No unregistered plugins"), false, null);
                }

                menu.DropDown(rect);
            }

            GUILayout.FlexibleSpace();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawMaterialSettings()
        {
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.PropertyField(serializedObject.FindProperty("defaultMaterial"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("resourcesPath"));

            materialOverridesList.DoLayoutList();

            if (EditorGUI.EndChangeCheck())
            {
                // Debug.LogWarning("Changed");
                // TODO: Change materials dynamically for
                // everything loaded already.
            }
        }

        private void DrawTextureSettings()
        {
            textureSlotList.DoLayoutList();
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

        /// <summary>
        /// Stop and destroy all instances of the sync manager.
        ///
        /// This includes instances that may be lingering in the editor and not
        /// necessarily attached to the current scene Hierarchy.
        /// </summary>
        private void Stop()
        {
            CoherenceSettings.Instance.isStarted = false;

            foreach (var instance in Resources.FindObjectsOfTypeAll<SyncManager>())
            {
                instance.Teardown();
                DestroyImmediate(instance.gameObject);
            }

            sync = null;
        }

        private void Start()
        {
            Stop();

            var go = new GameObject("[Coherence Sync]")
            {
                tag = "EditorOnly",
                hideFlags = HideFlags.NotEditable | HideFlags.DontSave
            };

            sync = go.AddComponent<SyncManager>();
            sync.Setup();

            CoherenceSettings.Instance.isStarted = true;
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
            EditorGUILayout.PropertyField(serializedObject.FindProperty("displayMaterial"));

            EditorGUILayout.LabelField("Shared Memory Buffer Settings", EditorStyles.boldLabel);

            EditorGUI.BeginDisabledGroup(IsRunning);

            EditorGUILayout.PropertyField(serializedObject.FindProperty("bufferName"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("nodeCount"));

            int toMB = 1024*1024;
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
                    new GUIContent("64 MB"),
                    new GUIContent("128 MB")
                },
                new int[] { 1*toMB, 2*toMB, 4*toMB, 8*toMB, 16*toMB, 32*toMB, 64*toMB, 128*toMB }
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

            if (IsRunning)
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
                "This is a preview build of Coherence and many features are still a work in progress.\n\n" +
                "For more information, check on GitHub.",
                MessageType.Warning
            );
            DrawRunControls();
            EditorGUILayout.Space();

            showMainSettings = EditorGUILayout.BeginFoldoutHeaderGroup(
                showMainSettings, "Settings"
            );
            if (showMainSettings)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    DrawBasic();
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            EditorGUILayout.Space();

            showPlugins = EditorGUILayout.BeginFoldoutHeaderGroup(
                showPlugins, "Plugins"
            );
            if (showPlugins)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    DrawPlugins();
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            EditorGUILayout.Space();

            showMaterialSettings = EditorGUILayout.BeginFoldoutHeaderGroup(
                showMaterialSettings, "Material Settings"
            );
            if (showMaterialSettings)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    DrawMaterialSettings();
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            EditorGUILayout.Space();

            showTextureSyncSettings = EditorGUILayout.BeginFoldoutHeaderGroup(
                showTextureSyncSettings, "Texture Sync"
            );
            if (showTextureSyncSettings)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    DrawTextureSettings();
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            EditorGUILayout.Space();

            showAdvancedSettings = EditorGUILayout.BeginFoldoutHeaderGroup(
                showAdvancedSettings, "Advanced Settings"
            );
            if (showAdvancedSettings)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    DrawAdvanced();
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            EditorGUILayout.Space();

            showExperimentalSettings = EditorGUILayout.BeginFoldoutHeaderGroup(
                showExperimentalSettings, "Experimental"
            );
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
