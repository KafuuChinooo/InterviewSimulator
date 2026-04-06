using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR
/// <summary>
/// Tool nhỏ trong Editor để gắn nhanh GazeAndControllerMic vào scene hiện tại.
/// </summary>
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

        // Try to assign a visible mic object by name contains 'mic'
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
        if (mic != null) comp.micTargets = new[] { mic };

        EditorUtility.SetDirty(comp);
        Selection.activeGameObject = parent;
        EditorUtility.DisplayDialog("Attach Gaze", "GazeAndControllerMic added to " + parent.name + (mic != null ? " (mic: " + mic.name + ")" : " (mic not found)"), "OK");
    }

    // /\_/\\
    // ( o.o )  [ kafuu ]
    //  > ^ <
}
#endif
