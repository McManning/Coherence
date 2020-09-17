using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ViewportController))]
public class ViewportControllerInspector : Editor 
{
    public void OnGUI()
    {
        var viewport = target as ViewportController;

        if (viewport.tex)
        {
            EditorGUI.DrawPreviewTexture(new Rect(25, 60, 100, 100), viewport.tex);
        }
    }
}
