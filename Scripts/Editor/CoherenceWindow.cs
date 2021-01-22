
using UnityEditor;

namespace Coherence
{
    /// <summary>
    /// Independent editor window for modifying the global settings
    /// </summary>
    public class CoherenceWindow : EditorWindow
    {
        const string WINDOW_TITLE = "Coherence";

        [MenuItem("Window/Coherence")]
        public static void ShowWindow()
        {
            GetWindow<CoherenceWindow>(WINDOW_TITLE, true);
        }

        private Editor settingsEditor;

        /// <summary>
        /// Delegate rendering to the inspector GUI of the scriptable object
        /// </summary>
        private void OnGUI()
        {
            if (settingsEditor == null)
            {
                settingsEditor = Editor.CreateEditor(CoherenceSettings.Instance);
            }

            settingsEditor.OnInspectorGUI();
        }
    }
}
