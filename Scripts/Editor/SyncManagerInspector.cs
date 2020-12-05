using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Coherence
{
    [CustomEditor(typeof(SyncManager))]
    public class SyncManagerInspector : Editor
    {
        public override void OnInspectorGUI()
        {
            GUILayout.Label("Nothing to see here.");
        }
    }
}
