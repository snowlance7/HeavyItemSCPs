using UnityEngine;
using UnityEditor;

public static class TransformPathUtility
{
    [MenuItem("GameObject/Copy Transform Path", false, 0)]
    private static void CopyPath()
    {
        Transform t = Selection.activeTransform;
        if (t == null)
            return;

        string path = t.name;

        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }

        GUIUtility.systemCopyBuffer = path;
        Debug.Log(path);
    }
}