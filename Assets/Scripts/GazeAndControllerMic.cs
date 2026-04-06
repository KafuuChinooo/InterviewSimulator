using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Quan ly mic trong VR bang nut tay cam va visual feedback cho mic dang active.
/// Nut O tren tay cam se click UI dang nam o tam man hinh.
/// Script tu dong chon mic dang active trong scene va khong tu ghi am bang gaze.
/// </summary>
public class GazeAndControllerMic : MonoBehaviour
{
    [Tooltip("Tham chieu toi AIAudioClient trong scene")]
    public AIAudioClient aiAudioClient;

    [Tooltip("Tham chieu toi UIManager trong scene. Neu de trong se tu tim.")]
    public UIManager uiManager;

    [Tooltip("Keo tat ca cac nut Mic vao day (Mic tieng Anh, Mic tieng Viet, v.v.).")]
    public GameObject[] micTargets;

    [Tooltip("Camera dung de raycast (de gaze). Mac dinh la Camera.main neu de trong")]
    public Camera vrCamera;

    [Tooltip("KeyCode nut controller de bat/tat mic - mac dinh JoystickButton0 (PS X)")]
    public KeyCode controllerButton = KeyCode.JoystickButton0;

    [Tooltip("KeyCode nut controller de click UI o tam man hinh - mac dinh JoystickButton1 (PS O)")]
    public KeyCode controllerClickButton = KeyCode.JoystickButton1;

    [Tooltip("KeyCode nut controller de quay ve man hinh truoc - mac dinh JoystickButton2 (PS Square)")]
    public KeyCode controllerBackButton = KeyCode.JoystickButton2;

    [Tooltip("Phim ban phim de test bat/tat ghi am trong editor")]
    public KeyCode keyboardToggleKey = KeyCode.P;

    [Tooltip("Layer mask cho Physics.Raycast (neu mic la doi tuong 3D co Collider)")]
    public LayerMask physicsLayerMask = Physics.DefaultRaycastLayers;

    private GameObject _activeMic;
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
            aiAudioClient = AIAudioClient.FindPreferredInstance();
            if (aiAudioClient != null)
                Debug.Log("[Gaze] Auto-assigned aiAudioClient from " + aiAudioClient.gameObject.name);
            else
                Debug.LogError("[Gaze] Could not find AIAudioClient in scene.");
        }

        if (uiManager == null)
        {
            uiManager = FindObjectOfType<UIManager>(true);
            if (uiManager != null)
                Debug.Log("[Gaze] Auto-assigned uiManager from " + uiManager.gameObject.name);
            else
                Debug.LogWarning("[Gaze] Could not find UIManager in scene.");
        }
    }

    void Start()
    {
        if (micTargets == null || micTargets.Length == 0)
        {
            // Tự quét các object có tên bắt đầu bằng "mic" để giảm thao tác kéo thả trong scene test.
            var found = new List<GameObject>();
            foreach (var go in GameObject.FindObjectsOfType<GameObject>())
            {
                string lowerName = go.name.ToLower();
                if (lowerName == "mic" || lowerName.StartsWith("mic"))
                {
                    found.Add(go);
                }
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
        Debug.Log($"[Gaze] Config: micButton={controllerButton}, clickButton={controllerClickButton}, backButton={controllerBackButton}");
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
            return;
        }

        Debug.Log("[Gaze] Active mic switched to: " + mic.name);
    }

    void Update()
    {
        if (aiAudioClient == null)
        {
            aiAudioClient = AIAudioClient.FindPreferredInstance();
            if (aiAudioClient == null) return;
        }

        if (Input.GetKeyDown(keyboardToggleKey))
        {
            aiAudioClient.ToggleRecord();
        }

        GameObject currentMic = GetActiveMic();
        RefreshCacheIfNeeded(currentMic);

        if (Input.GetKeyDown(controllerButton))
        {
            HandleControllerMicToggle();
        }

        if (Input.GetKeyDown(controllerClickButton))
        {
            TryClickCenterTarget();
        }

        if (Input.GetKeyDown(controllerBackButton))
        {
            HandleControllerBack();
        }

        if (currentMic == null) return;

        UpdateVisualFeedback(currentMic);

    }

    void UpdateVisualFeedback(GameObject currentMic)
    {
        if (aiAudioClient == null) return;

        // Màu sắc ở đây chỉ đóng vai trò feedback trạng thái:
        // đỏ khi đang ghi, vàng khi AI đang xử lý, mặc định khi rảnh.
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

    void HandleControllerMicToggle()
    {
        Debug.Log("[Gaze] Controller mic button pressed.");

        if (aiAudioClient == null || aiAudioClient.IsBusy) return;
        aiAudioClient.ToggleRecord();
    }

    void HandleControllerBack()
    {
        Debug.Log("[Gaze] Controller back button pressed.");

        if (uiManager == null)
        {
            uiManager = FindObjectOfType<UIManager>(true);
        }

        if (uiManager == null)
        {
            Debug.LogWarning("[Gaze] Cannot go to previous screen because UIManager is missing.");
            return;
        }

        uiManager.PreviousScreen();
    }

    bool TryClickCenterTarget()
    {
        if (eventSystem == null) eventSystem = EventSystem.current;
        if (eventSystem == null)
        {
            Debug.LogWarning("[Gaze] Cannot click center UI because EventSystem is missing.");
            return false;
        }

        PointerEventData pointerData = new PointerEventData(eventSystem)
        {
            position = GetScreenCenter(),
            button = PointerEventData.InputButton.Left
        };

        GameObject clickTarget = FindCenterClickTarget(pointerData);
        if (clickTarget == null)
        {
            Debug.Log("[Gaze] Controller click button pressed but no center UI target was found.");
            return false;
        }

        Button uiButton = clickTarget.GetComponent<Button>() ?? clickTarget.GetComponentInParent<Button>();
        if (uiButton != null)
        {
            if (!uiButton.IsActive() || !uiButton.interactable)
            {
                Debug.Log("[Gaze] Center UI target is not interactable: " + uiButton.gameObject.name);
                return false;
            }

            uiButton.onClick.Invoke();
            Debug.Log("[Gaze] Invoked centered Button.onClick on: " + uiButton.gameObject.name);
            return true;
        }

        bool clicked = ExecuteEvents.Execute(clickTarget, pointerData, ExecuteEvents.pointerClickHandler);
        ExecuteEvents.Execute(clickTarget, pointerData, ExecuteEvents.submitHandler);
        if (clicked)
        {
            Debug.Log("[Gaze] Executed centered pointer click on: " + clickTarget.name);
        }
        else
        {
            Debug.Log("[Gaze] Center target did not accept pointer click: " + clickTarget.name);
        }

        return clicked;
    }

    GameObject FindCenterClickTarget(PointerEventData pointerData)
    {
        List<RaycastResult> raycastResults = new List<RaycastResult>();
        eventSystem.RaycastAll(pointerData, raycastResults);

        foreach (RaycastResult result in raycastResults)
        {
            GameObject clickHandler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(result.gameObject);
            if (clickHandler != null)
            {
                return clickHandler;
            }
        }

        if (vrCamera != null)
        {
            Ray ray = new Ray(vrCamera.transform.position, vrCamera.transform.forward);
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, 100f, physicsLayerMask))
            {
                GameObject hitObject = hit.collider.gameObject;
                Button uiButton = hitObject.GetComponent<Button>() ?? hitObject.GetComponentInParent<Button>();
                if (uiButton != null)
                {
                    return uiButton.gameObject;
                }

                return ExecuteEvents.GetEventHandler<IPointerClickHandler>(hitObject);
            }
        }

        return null;
    }

    Vector2 GetScreenCenter()
    {
        if (vrCamera != null)
        {
            return vrCamera.ViewportToScreenPoint(new Vector3(0.5f, 0.5f, 0f));
        }

        return new Vector2(Screen.width / 2f, Screen.height / 2f);
    }

    // /\_/\\
    // ( o.o )  [ kafuu ]
    //  > ^ <
}
