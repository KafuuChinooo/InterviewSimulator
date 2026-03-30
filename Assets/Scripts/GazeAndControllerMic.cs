using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Kich hoat ghi am khi nguoi dung nhin vao bat ky mic nao trong `micTargets`
/// trong `gazeTriggerTime` giay hoac nhan `controllerButton`.
/// Script tu dong chon mic dang active trong scene va hien thi visual feedback.
/// </summary>
public class GazeAndControllerMic : MonoBehaviour
{
    [Tooltip("Tham chieu toi AIAudioClient trong scene")]
    public AIAudioClient aiAudioClient;

    [Tooltip("Keo tat ca cac nut Mic vao day (Mic tieng Anh, Mic tieng Viet, v.v.).")]
    public GameObject[] micTargets;

    [HideInInspector]
    public GameObject micTarget;

    [Tooltip("Camera dung de raycast (de gaze). Mac dinh la Camera.main neu de trong")]
    public Camera vrCamera;

    [Tooltip("Thoi gian (giay) nhin lien tuc vao mic de kich hoat")]
    public float gazeTriggerTime = 2f;

    [Tooltip("Thoi gian (giay) ghi am sau khi kich hoat")]
    public float recordDuration = 10f;

    [Tooltip("KeyCode nut controller de kich hoat (mot lan) - mac dinh JoystickButton0")]
    public KeyCode controllerButton = KeyCode.JoystickButton0;

    [Tooltip("Phim ban phim de test bat/tat ghi am trong editor")]
    public KeyCode keyboardToggleKey = KeyCode.P;

    [Tooltip("Layer mask cho Physics.Raycast (neu mic la doi tuong 3D co Collider)")]
    public LayerMask physicsLayerMask = Physics.DefaultRaycastLayers;

    [Tooltip("Neu co UI panel mo nen vo hieu hoa gaze, keo cac panel do vao day")]
    public GameObject[] uiBlockers;

    [Tooltip("Chi nhan gaze khi target la top-most UI (on voi world-space UI)")]
    public bool requireTopmostUI = true;

    private float gazeTimer = 0f;
    private bool gazeTriggered = false;

    private GameObject _activeMic;
    private Collider targetCollider;
    private RectTransform targetRect;
    private GraphicRaycaster graphicRaycaster;
    private EventSystem eventSystem;

    private readonly Dictionary<GameObject, Color> _originalColors = new Dictionary<GameObject, Color>();
    private float _pulseTimer = 0f;
    private bool _wasRecording = false;
    private bool _wasBusy = false;

    void Awake()
    {
        if (vrCamera == null) vrCamera = Camera.main;

        if (aiAudioClient == null)
        {
            aiAudioClient = FindObjectOfType<AIAudioClient>();
            if (aiAudioClient != null)
                Debug.Log("[Gaze] Auto-assigned aiAudioClient from " + aiAudioClient.gameObject.name);
            else
                Debug.LogError("[Gaze] Could not find AIAudioClient in scene.");
        }
    }

    void Start()
    {
        if (micTargets == null || micTargets.Length == 0)
        {
            var found = new List<GameObject>();
            foreach (var go in GameObject.FindObjectsOfType<GameObject>())
            {
                string lowerName = go.name.ToLower();
                if (lowerName == "mic" || lowerName.StartsWith("mic"))
                {
                    found.Add(go);
                }
            }

            if (micTarget != null && !found.Contains(micTarget))
            {
                found.Add(micTarget);
            }

            micTargets = found.ToArray();
            Debug.Log("[Gaze] Auto-found " + micTargets.Length + " mic target(s).");
        }
        else
        {
            Debug.Log("[Gaze] Inspector assigned " + micTargets.Length + " mic target(s).");
        }

        foreach (var mic in micTargets)
        {
            if (mic == null) continue;

            Image img = mic.GetComponent<Image>();
            if (img != null)
            {
                _originalColors[mic] = img.color;
            }
        }

        eventSystem = EventSystem.current;
        Debug.Log($"[Gaze] Config: gazeTriggerTime={gazeTriggerTime}s, recordDuration={recordDuration}s");
    }

    GameObject GetActiveMic()
    {
        if (micTargets == null) return null;

        foreach (var mic in micTargets)
        {
            if (mic != null && mic.activeInHierarchy) return mic;
        }

        return null;
    }

    void RefreshCacheIfNeeded(GameObject mic)
    {
        if (mic == _activeMic) return;

        _activeMic = mic;
        if (mic == null)
        {
            targetCollider = null;
            targetRect = null;
            graphicRaycaster = null;
            return;
        }

        targetCollider = mic.GetComponent<Collider>() ?? mic.GetComponentInChildren<Collider>();
        targetRect = mic.GetComponent<RectTransform>();
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
        if (aiAudioClient == null)
        {
            aiAudioClient = FindObjectOfType<AIAudioClient>();
            if (aiAudioClient == null) return;
        }

        if (Input.GetKeyDown(keyboardToggleKey))
        {
            aiAudioClient.ToggleRecord();
        }

        GameObject currentMic = GetActiveMic();
        RefreshCacheIfNeeded(currentMic);
        if (currentMic == null) return;

        UpdateVisualFeedback(currentMic);

        if (Input.GetKeyDown(controllerButton))
        {
            Debug.Log("[Gaze] Controller button pressed.");
            if (!aiAudioClient.IsRecording && !aiAudioClient.IsBusy)
            {
                aiAudioClient.StartRecordForSeconds(Mathf.CeilToInt(recordDuration));
            }
            else if (aiAudioClient.IsRecording)
            {
                aiAudioClient.OnStopAndSendClicked();
            }
        }

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

    void UpdateVisualFeedback(GameObject currentMic)
    {
        if (aiAudioClient == null) return;

        bool isRecording = aiAudioClient.IsRecording;
        bool isBusy = aiAudioClient.IsBusy;

        if (isRecording && !_wasRecording)
        {
            Debug.Log("[Gaze] Recording started.");
        }
        if (!isRecording && _wasRecording)
        {
            Debug.Log("[Gaze] Recording stopped.");
        }
        if (isBusy && !_wasBusy && !isRecording)
        {
            Debug.Log("[Gaze] AI processing.");
        }
        if (!isBusy && _wasBusy)
        {
            Debug.Log("[Gaze] AI processing finished.");
        }

        _wasRecording = isRecording;
        _wasBusy = isBusy;

        foreach (var mic in micTargets)
        {
            if (mic == null) continue;

            Image img = mic.GetComponent<Image>();
            if (img == null) continue;

            if (isRecording)
            {
                _pulseTimer += Time.deltaTime * 4f;
                float alpha = 0.6f + 0.4f * Mathf.Sin(_pulseTimer);
                img.color = new Color(1f, 0.15f, 0.15f, alpha);
            }
            else if (isBusy)
            {
                _pulseTimer += Time.deltaTime * 2f;
                float alpha = 0.7f + 0.3f * Mathf.Sin(_pulseTimer);
                img.color = new Color(1f, 0.85f, 0f, alpha);
            }
            else
            {
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

            if (uiBlockers != null)
            {
                foreach (var blocker in uiBlockers)
                {
                    if (blocker != null && blocker.activeInHierarchy) return false;
                }
            }

            if (results.Count == 0) return false;

            if (requireTopmostUI)
            {
                var top = results[0];
                if (top.gameObject == _activeMic || top.gameObject.transform.IsChildOf(_activeMic.transform)) return true;
                return false;
            }

            foreach (var result in results)
            {
                if (result.gameObject == _activeMic || result.gameObject.transform.IsChildOf(_activeMic.transform)) return true;
            }
        }

        return false;
    }
}
