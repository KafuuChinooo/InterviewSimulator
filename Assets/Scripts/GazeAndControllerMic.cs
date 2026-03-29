using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Kích hoạt ghi âm khi người dùng nhìn vào `micTarget` trong `gazeTriggerTime` giây
/// hoặc nhấn `controllerButton`. Khi kích hoạt, sẽ ghi âm trong `recordDuration` giây
/// rồi tự động dừng và gửi bằng `AIAudioClient.StartRecordForSeconds`.
/// 
/// Gắn script này lên một GameObject trống; kéo `micTarget` (UI/3D mic) và `aiAudioClient` vào.
/// </summary>
public class GazeAndControllerMic : MonoBehaviour
{
    [Tooltip("Tham chiếu tới AIAudioClient trong scene")]
    public AIAudioClient aiAudioClient;

    [Tooltip("GameObject đại diện cho mic (UI hoặc 3D object)")]
    public GameObject micTarget;

    [Tooltip("Camera dùng để raycast (để gaze). Mặc định là Camera.main nếu để trống")]
    public Camera vrCamera;

    [Tooltip("Thời gian (giây) nhìn liên tục vào mic để kích hoạt")]
    public float gazeTriggerTime = 3f;

    [Tooltip("Thời gian (giây) ghi âm sau khi kích hoạt")]
    public float recordDuration = 3f;

    [Tooltip("KeyCode nút controller để kích hoạt (một lần) - mặc định JoystickButton0")]
    public KeyCode controllerButton = KeyCode.JoystickButton0;

    [Tooltip("Layer mask cho Physics.Raycast (nếu mic là đối tượng 3D có Collider)")]
    public LayerMask physicsLayerMask = Physics.DefaultRaycastLayers;
    
    [Tooltip("Nếu có UI panel (ví dụ menu 4 nút) mở nên vô hiệu hoá gaze, kéo các panel đó vào đây")]
    public GameObject[] uiBlockers;

    [Tooltip("Chỉ nhận gaze khi target là top-most UI (ổn với world-space UI)")]
    public bool requireTopmostUI = true;

    float gazeTimer = 0f;
    bool gazeTriggered = false;

    Collider targetCollider;
    RectTransform targetRect;
    GraphicRaycaster graphicRaycaster;
    EventSystem eventSystem;

    void Start()
    {
        if (vrCamera == null) vrCamera = Camera.main;
        // Auto-assign AIAudioClient nếu người dùng quên gán
        if (aiAudioClient == null)
        {
            aiAudioClient = FindObjectOfType<AIAudioClient>();
            if (aiAudioClient != null) Debug.Log("GazeAndControllerMic: auto-assigned aiAudioClient from scene.");
            else Debug.LogWarning("GazeAndControllerMic: aiAudioClient not assigned and not found in scene!");
        }

        // Auto-assign micTarget bằng tên 'Mic' hoặc tìm GameObject có tên chứa 'mic'
        if (micTarget == null)
        {
            GameObject f = GameObject.Find("Mic");
            if (f == null)
            {
                var all = GameObject.FindObjectsOfType<GameObject>();
                foreach (var go in all)
                {
                    if (go.name.ToLower().Contains("mic"))
                    {
                        f = go;
                        break;
                    }
                }
            }
            micTarget = f;
            if (micTarget != null) Debug.Log("GazeAndControllerMic: auto-assigned micTarget: " + micTarget.name);
            else Debug.LogWarning("GazeAndControllerMic: micTarget not assigned and not found in scene!");
        }

        if (micTarget != null)
        {
            targetCollider = micTarget.GetComponent<Collider>() ?? micTarget.GetComponentInChildren<Collider>();
            targetRect = micTarget.GetComponent<RectTransform>();
            if (targetRect != null)
            {
                Canvas canvas = micTarget.GetComponentInParent<Canvas>();
                if (canvas != null)
                {
                    graphicRaycaster = canvas.GetComponent<GraphicRaycaster>();
                    if (graphicRaycaster == null) graphicRaycaster = canvas.gameObject.AddComponent<GraphicRaycaster>();
                }
            }
        }

        eventSystem = EventSystem.current;
    }

    void Update()
    {
        if (aiAudioClient == null || micTarget == null) return;

        // Controller press: nhấn để bắt đầu ghi âm trong recordDuration giây
        if (Input.GetKeyDown(controllerButton))
        {
            if (!aiAudioClient.IsRecording && !aiAudioClient.IsBusy)
            {
                aiAudioClient.StartRecordForSeconds(Mathf.CeilToInt(recordDuration));
            }
            else if (aiAudioClient.IsRecording)
            {
                aiAudioClient.OnStopAndSendClicked();
            }
        }

        // Gaze detection
        bool looking = IsLookingAtTarget();
        if (looking)
        {
            gazeTimer += Time.deltaTime;
            if (!gazeTriggered && gazeTimer >= gazeTriggerTime)
            {
                gazeTriggered = true;
                if (!aiAudioClient.IsRecording && !aiAudioClient.IsBusy)
                {
                    aiAudioClient.StartRecordForSeconds(Mathf.CeilToInt(recordDuration));
                }
            }
        }
        else
        {
            gazeTimer = 0f;
            gazeTriggered = false;
        }
    }

    bool IsLookingAtTarget()
    {
        if (vrCamera == null) return false;
        Ray ray = new Ray(vrCamera.transform.position, vrCamera.transform.forward);
        RaycastHit hit;

        // Nếu mic có Collider (3D), dùng Physics.Raycast
        if (targetCollider != null)
        {
            if (Physics.Raycast(ray, out hit, 100f, physicsLayerMask))
            {
                if (hit.collider.transform.IsChildOf(micTarget.transform) || hit.collider.transform == micTarget.transform) return true;
            }
            return false;
        }

        if (targetRect != null && graphicRaycaster != null && eventSystem != null)
        {
            PointerEventData ped = new PointerEventData(eventSystem);
            Vector2 screenCenter = new Vector2(Screen.width / 2f, Screen.height / 2f);
            if (vrCamera != null)
            {
                screenCenter = vrCamera.ViewportToScreenPoint(new Vector3(0.5f, 0.5f, 0f));
            }
            ped.position = screenCenter;

            List<RaycastResult> results = new List<RaycastResult>();
            graphicRaycaster.Raycast(ped, results);
            // Nếu có một UI blocker đang hiển thị thì vô hiệu hoá gaze
            if (uiBlockers != null)
            {
                foreach (var b in uiBlockers)
                {
                    if (b != null && b.activeInHierarchy) return false;
                }
            }

            if (results.Count == 0) return false;

            if (requireTopmostUI)
            {
                // Kiểm tra chỉ top-most hit
                var top = results[0];
                if (top.gameObject == micTarget || top.gameObject.transform.IsChildOf(micTarget.transform)) return true;
                return false;
            }
            else
            {
                // Mặc định: nếu bất kỳ hit nào là micTarget thì coi là nhìn vào mic
                foreach (var r in results)
                {
                    if (r.gameObject == micTarget || r.gameObject.transform.IsChildOf(micTarget.transform)) return true;
                }
                return false;
            }
        }

        return false;
    }
}
