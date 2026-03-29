using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

#if UNITY_EDITOR
public static class AutoAssignGaze
{
    [MenuItem("Tools/VirtualHire/Attach Gaze And Controller Mic")]
    public static void AttachGaze()
    {
        var ai = Object.FindObjectOfType<AIAudioClient>();
        if (ai == null)
        {
            EditorUtility.DisplayDialog("Attach Gaze", "No AIAudioClient found in the current scene.", "OK");
            return;
        }

        var existing = Object.FindObjectOfType<GazeAndControllerMic>();
        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            EditorUtility.DisplayDialog("Attach Gaze", "GazeAndControllerMic already exists on: " + existing.gameObject.name, "OK");
            return;
        }

        GameObject parent = ai.gameObject;
        var comp = parent.AddComponent<GazeAndControllerMic>();
        comp.aiAudioClient = ai;

        // Try to assign micTarget by name contains 'mic'
        GameObject mic = GameObject.Find("Mic");
        if (mic == null)
        {
            foreach (var go in Object.FindObjectsOfType<GameObject>())
            {
                if (go.name.ToLower().Contains("mic"))
                {
                    mic = go;
                    break;
                }
            }
        }
        if (mic != null) comp.micTarget = mic;

        // Collect likely UI blocker panels (names containing 'trang_4' or 'position')
        var blockers = new List<GameObject>();
        foreach (var go in Object.FindObjectsOfType<GameObject>())
        {
            var n = go.name.ToLower();
            if (n.Contains("trang_4") || n.Contains("position") || n.Contains("trang4")) blockers.Add(go);
        }
        if (blockers.Count > 0) comp.uiBlockers = blockers.ToArray();

        EditorUtility.SetDirty(comp);
        Selection.activeGameObject = parent;
        EditorUtility.DisplayDialog("Attach Gaze", "GazeAndControllerMic added to " + parent.name + (mic != null ? " (mic: " + mic.name + ")" : " (mic not found)"), "OK");
    }
}
#endif
