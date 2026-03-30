using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Kích hoạt ghi âm khi người dùng nhìn vào bất kỳ mic nào trong `micTargets` trong `gazeTriggerTime` giây
/// hoặc nhấn `controllerButton`. Script tự động chọn mic đang active trong scene.
/// CÓ VISUAL FEEDBACK: icon Mic sẽ nhấp nháy đỏ khi đang ghi âm, vàng khi AI xử lý.
/// </summary>
public class GazeAndControllerMic : MonoBehaviour
{
    [Tooltip("Tham chiếu tới AIAudioClient trong scene")]
    public AIAudioClient aiAudioClient;

    [Tooltip("Kéo TẤT CẢ các nút Mic vào đây (Mic tiếng Anh, Mic tiếng Việt, v.v.).")]
    public GameObject[] micTargets;

    [HideInInspector] // Giữ lại để tương thích ngược nếu có
    public GameObject micTarget;

    [Tooltip("Camera dùng để raycast (để gaze). Mặc định là Camera.main nếu để trống")]
    public Camera vrCamera;

    [Tooltip("Thời gian (giây) nhìn liên tục vào mic để kích hoạt")]
    public float gazeTriggerTime = 1.5f;

    [Tooltip("Thời gian (giây) ghi âm sau khi kích hoạt")]
    public float recordDuration = 10f;

    [Tooltip("KeyCode nút controller để kích hoạt (một lần) - mặc định JoystickButton0")]
    public KeyCode controllerButton = KeyCode.JoystickButton0;

    [Tooltip("Layer mask cho Physics.Raycast (nếu mic là đối tượng 3D có Collider)")]
    public LayerMask physicsLayerMask = Physics.DefaultRaycastLayers;
    
    [Tooltip("Nếu có UI panel mở nên vô hiệu hoá gaze, kéo các panel đó vào đây")]
    public GameObject[] uiBlockers;

    [Tooltip("Chỉ nhận gaze khi target là top-most UI (ổn với world-space UI)")]
    public bool requireTopmostUI = true;

    float gazeTimer = 0f;
    bool gazeTriggered = false;

    // Cache cho mic đang active hiện tại
    GameObject _activeMic;
    Collider targetCollider;
    RectTransform targetRect;
    GraphicRaycaster graphicRaycaster;
    EventSystem eventSystem;

    // --- Visual Feedback ---
    private Dictionary<GameObject, Color> _originalColors = new Dictionary<GameObject, Color>();
    private float _pulseTimer = 0f;
    private bool _wasRecording = false;
    private bool _wasBusy = false;

    // Dùng Awake thay vì Start để đảm bảo auto-find chạy sớm nhất
    void Awake()
    {
        if (vrCamera == null) vrCamera = Camera.main;

        // Auto-assign AIAudioClient nếu người dùng quên gán
        if (aiAudioClient == null)
        {
            aiAudioClient = FindObjectOfType<AIAudioClient>();
            if (aiAudioClient != null)
                Debug.Log("[Gaze] ✅ Auto-assigned aiAudioClient từ " + aiAudioClient.gameObject.name);
            else
                Debug.LogError("[Gaze] ❌ KHÔNG TÌM THẤY AIAudioClient trong scene! Gaze sẽ KHÔNG hoạt động!");
        }
        else
        {
            Debug.Log("[Gaze] ✅ aiAudioClient đã được gán sẵn: " + aiAudioClient.gameObject.name);
        }
    }

    void Start()
    {
        // Nếu không có micTargets nào được kéo vào, tự tìm tất cả GameObject tên chứa 'mic'
        if (micTargets == null || micTargets.Length == 0)
        {
            var found = new List<GameObject>();
            foreach (var go in GameObject.FindObjectsOfType<GameObject>())
            {
                if (go.name.ToLower() == "mic" || go.name.ToLower().StartsWith("mic"))
                    found.Add(go);
            }
            // Fallback: nếu có micTarget (slot cũ), thêm vào list
            if (micTarget != null && !found.Contains(micTarget)) found.Add(micTarget);
            micTargets = found.ToArray();
            Debug.Log("[Gaze] Auto-found " + micTargets.Length + " mic target(s).");
        }
        else
        {
            Debug.Log("[Gaze] Có " + micTargets.Length + " mic target(s) được gán trong Inspector.");
        }

        // Lưu màu gốc của tất cả mic targets
        foreach (var mic in micTargets)
        {
            if (mic != null)
            {
                Image img = mic.GetComponent<Image>();
                if (img != null)
                {
                    _originalColors[mic] = img.color;
                }
            }
        }

        eventSystem = EventSystem.current;

        // Log cấu hình để debug
        Debug.Log($"[Gaze] Config: gazeTriggerTime={gazeTriggerTime}s, recordDuration={recordDuration}s");
        Debug.Log($"[Gaze] aiAudioClient={(aiAudioClient != null ? aiAudioClient.gameObject.name : "NULL")}");
    }

    /// <summary>Tìm mic nào đang active (visible) trong scene lúc này</summary>
    GameObject GetActiveMic()
    {
        if (micTargets == null) return null;
        foreach (var m in micTargets)
        {
            if (m != null && m.activeInHierarchy) return m;
        }
        return null;
    }

    /// <summary>Cập nhật cache collider/rect khi mic active thay đổi</summary>
    void RefreshCacheIfNeeded(GameObject mic)
    {
        if (mic == _activeMic) return;
        _activeMic = mic;
        if (mic == null) { targetCollider = null; targetRect = null; graphicRaycaster = null; return; }

        targetCollider = mic.GetComponent<Collider>() ?? mic.GetComponentInChildren<Collider>();
        targetRect     = mic.GetComponent<RectTransform>();
        if (targetRect != null)
        {
            Canvas canvas = mic.GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                graphicRaycaster = canvas.GetComponent<GraphicRaycaster>();
                if (graphicRaycaster == null) graphicRaycaster = canvas.gameObject.AddComponent<GraphicRaycaster>();
            }
        }
        Debug.Log("[Gaze] Active mic switched to: " + mic.name);
    }

    void Update()
    {
        // Retry tìm AIAudioClient nếu chưa có (phòng trường hợp chạy sau)
        if (aiAudioClient == null)
        {
            aiAudioClient = FindObjectOfType<AIAudioClient>();
            if (aiAudioClient != null)
                Debug.Log("[Gaze] ✅ Late-assigned aiAudioClient từ " + aiAudioClient.gameObject.name);
            else
                return; // Không có thì không làm gì
        }

        // Tìm mic đang hiển thị lúc này (Eng hoặc Viet)
        GameObject currentMic = GetActiveMic();
        RefreshCacheIfNeeded(currentMic);

        if (currentMic == null) return;

        // === VISUAL FEEDBACK ===
        UpdateVisualFeedback(currentMic);

        // Controller press
        if (Input.GetKeyDown(controllerButton))
        {
            Debug.Log("[Gaze] 🎮 Controller button pressed!");
            if (!aiAudioClient.IsRecording && !aiAudioClient.IsBusy)
            {
                Debug.Log("[Gaze] 🎙️ Bắt đầu ghi âm từ controller...");
                aiAudioClient.StartRecordForSeconds(Mathf.CeilToInt(recordDuration));
            }
            else if (aiAudioClient.IsRecording)
            {
                Debug.Log("[Gaze] ⏹️ Dừng ghi âm từ controller...");
                aiAudioClient.OnStopAndSendClicked();
            }
        }

        // Gaze detection
        bool looking = IsLookingAtTarget();
        if (looking)
        {
            gazeTimer += Time.deltaTime;

            // Log tiến trình gaze (mỗi 0.5 giây)
            if (Mathf.FloorToInt(gazeTimer * 2) != Mathf.FloorToInt((gazeTimer - Time.deltaTime) * 2))
            {
                Debug.Log($"[Gaze] 👁️ Đang nhìn vào Mic... {gazeTimer:F1}/{gazeTriggerTime:F1}s");
            }

            if (!gazeTriggered && gazeTimer >= gazeTriggerTime)
            {
                gazeTriggered = true;
                if (!aiAudioClient.IsRecording && !aiAudioClient.IsBusy)
                {
                    Debug.Log("[Gaze] ✅ GAZE TRIGGERED! Bắt đầu ghi âm " + Mathf.CeilToInt(recordDuration) + " giây...");
                    aiAudioClient.StartRecordForSeconds(Mathf.CeilToInt(recordDuration));
                }
                else
                {
                    Debug.Log($"[Gaze] ⚠️ Gaze triggered nhưng AI đang bận (Recording={aiAudioClient.IsRecording}, Busy={aiAudioClient.IsBusy})");
                }
            }
        }
        else
        {
            if (gazeTimer > 0.3f && !gazeTriggered)
            {
                Debug.Log("[Gaze] 👁️ Ngưng nhìn Mic (chưa đủ thời gian: " + gazeTimer.ToString("F1") + "s)");
            }
            gazeTimer = 0f;
            gazeTriggered = false;
        }
    }

    /// <summary>
    /// Visual feedback: Mic icon nhấp nháy ĐỎ khi recording, VÀNG khi AI xử lý, trả lại màu gốc khi idle
    /// </summary>
    void UpdateVisualFeedback(GameObject currentMic)
    {
        if (aiAudioClient == null) return;

        bool isRec = aiAudioClient.IsRecording;
        bool isBusy = aiAudioClient.IsBusy;

        // Phát hiện trạng thái thay đổi
        if (isRec && !_wasRecording)
        {
            Debug.Log("[Gaze] 🔴 === BẮT ĐẦU GHI ÂM === Icon Mic sẽ nhấp nháy đỏ");
        }
        if (!isRec && _wasRecording)
        {
            Debug.Log("[Gaze] ⏹️ === KẾT THÚC GHI ÂM ===");
        }
        if (isBusy && !_wasBusy && !isRec)
        {
            Debug.Log("[Gaze] 🟡 === AI ĐANG XỬ LÝ === Icon Mic chuyển vàng");
        }
        if (!isBusy && _wasBusy)
        {
            Debug.Log("[Gaze] ✅ === AI XỬ LÝ XONG === Icon Mic trả lại bình thường");
        }
        _wasRecording = isRec;
        _wasBusy = isBusy;

        // Áp dụng màu lên tất cả mic
        foreach (var mic in micTargets)
        {
            if (mic == null) continue;
            Image img = mic.GetComponent<Image>();
            if (img == null) continue;

            if (isRec)
            {
                // Nhấp nháy đỏ khi đang ghi âm
                _pulseTimer += Time.deltaTime * 4f;
                float alpha = 0.6f + 0.4f * Mathf.Sin(_pulseTimer);
                img.color = new Color(1f, 0.15f, 0.15f, alpha);
            }
            else if (isBusy)
            {
                // Nhấp nháy vàng khi AI đang xử lý
                _pulseTimer += Time.deltaTime * 2f;
                float alpha = 0.7f + 0.3f * Mathf.Sin(_pulseTimer);
                img.color = new Color(1f, 0.85f, 0f, alpha);
            }
            else
            {
                // Trả lại màu gốc
                _pulseTimer = 0f;
                if (_originalColors.ContainsKey(mic))
                    img.color = _originalColors[mic];
                else
                    img.color = Color.white;
            }
        }
    }

    bool IsLookingAtTarget()
    {
        if (vrCamera == null || _activeMic == null) return false;
        Ray ray = new Ray(vrCamera.transform.position, vrCamera.transform.forward);
        RaycastHit hit;

        // Nếu mic có Collider (3D), dùng Physics.Raycast
        if (targetCollider != null)
        {
            if (Physics.Raycast(ray, out hit, 100f, physicsLayerMask))
            {
                if (hit.collider.transform.IsChildOf(_activeMic.transform) || hit.collider.transform == _activeMic.transform) return true;
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
                var top = results[0];
                if (top.gameObject == _activeMic || top.gameObject.transform.IsChildOf(_activeMic.transform)) return true;
                return false;
            }
            else
            {
                foreach (var r in results)
                {
                    if (r.gameObject == _activeMic || r.gameObject.transform.IsChildOf(_activeMic.transform)) return true;
                }
                return false;
            }
        }

        return false;
    }
}
