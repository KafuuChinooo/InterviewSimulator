using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// AI Voice Assistant Client cho Unity.
/// Hỗ trợ 2 chế độ: gõ text hoặc ghi âm giọng nói.
/// 
/// === SETUP GUIDE ===
/// 1. Tạo một GameObject (ví dụ: "AIManager")
/// 2. Gắn script này vào GameObject đó
/// 3. Kéo các UI element vào các slot trong Inspector (xem bên dưới)
/// 4. Đảm bảo FastAPI server đang chạy: python main.py
/// </summary>
public class AIAudioClient : MonoBehaviour
{
    [Header("=== SERVER ===")]
    [Tooltip("URL của FastAPI server (không có dấu / ở cuối). Nếu chạy trên điện thoại, dùng IP LAN của PC thay vì 127.0.0.1.")]
    public string serverUrl = "http://127.0.0.1:8000";
    [Tooltip("Session id gui sang backend. Neu de trong se tu sinh.")]
    public string sessionId = "";

    [Header("=== AUDIO ===")]
    [Tooltip("AudioSource để phát câu trả lời của AI")]
    public AudioSource audioSource;

    [Header("=== TEXT MODE UI ===")]
    [Tooltip("InputField để gõ câu hỏi (TMP_InputField)")]
    public TMP_InputField textInput;
    [Tooltip("Button 'Gửi' cho chế độ text")]
    public Button sendTextButton;

    [Header("=== VOICE MODE UI ===")]
    [Tooltip("Button 'Bắt đầu ghi âm'")]
    public Button startRecordButton;
    [Tooltip("Button 'Dừng và Gửi'")]
    public Button stopSendButton;
    [Tooltip("Số giây ghi âm tối đa")]
    public int maxRecordSeconds = 10;

    [Header("=== DEFAULT SCRIPT MODE UI ===")]
    [Tooltip("Button để đọc đoạn text mặc định")]
    public Button readDefaultScriptButton;
    [Tooltip("Đoạn script mặc định để TTS đọc")]
    [TextArea(3, 5)]
    public string defaultScript = "Hello, this is automatically generated text.";

    [Header("=== VR SCRIPT CONFIG ===")]
    [Tooltip("Vị trí công việc (VD: Data Analyst)")]
    public string jobTitle = "Data Analyst";
    [Tooltip("Loại phỏng vấn (VD: Attitude Interview, Role-Specific Interview)")]
    public string interviewType = "Attitude Interview";
    [Tooltip("Ngôn ngữ (VD: Vietnamese, English)")]
    public string language = "Vietnamese";
    [Tooltip("Button để gọi API sinh kịch bản JSON")]
    public Button generateScriptButton;

    [Header("=== STATUS UI ===")]
    [Tooltip("Label hiển thị trạng thái (ví dụ: 'Đang ghi âm...', 'AI đang trả lời...')")]
    public TMP_Text statusLabel;
    [Tooltip("Label hiển thị lời AI chuẩn bị phát ra")]
    public TMP_Text transcriptLabel;

    [Header("=== GREETING SCRIPT ===")]
    [Tooltip("Gắn UI Text chứa lời chào vào đây")]
    public TMP_Text greetingTextUI;
    [TextArea(3, 5)]
    public string englishGreeting = "Hi there, welcome to VirtuHire! My name is Phuong Hang. I'll be your interviewer for today's session. This is a safe space for you to practice and get comfortable with interviews. Just relax and do your best. Let's get started!";
    [TextArea(3, 5)]
    public string vietnameseGreeting = "Chào bạn, chào mừng đến với VirtuHire! Tôi tên là Phương Hằng. Tôi sẽ là người phỏng vấn bạn trong buổi hôm nay. Đây là một không gian an toàn để bạn luyện tập và làm quen với các cuộc phỏng vấn. Hãy cứ thư giãn và thể hiện hết mình nhé. Chúng ta bắt đầu nào!";

    // --- Private state ---
    private AudioClip _recordingClip;
    private bool _isRecording = false;
    private bool _isBusy = false;
    private StatusKey _currentStatusKey = StatusKey.Ready;
    private string _currentStatusDetail = "";
    private const int SttTimeoutSeconds = 60;
    private const int ChatTimeoutSeconds = 90;
    private const int TtsTimeoutSeconds = 60;
    private const int AdminPollTimeoutSeconds = 120;
    private const int AdminPollRequestTimeoutSeconds = 15;
    private const float AdminPollIntervalSeconds = 2f;
    private const int TtsChunkMaxCharacters = 140;
    private const int WavConversionChunkFrames = 4096;
    private const float AudioPlaybackGraceSeconds = 2f;
    private const int MaxConversationHistoryMessages = 12;
    // Public read-only accessors for other scripts (e.g., gaze/controller helper)
    public bool IsRecording { get { return _isRecording; } }
    public bool IsBusy { get { return _isBusy; } }
    private readonly List<ChatHistoryItem> _conversationHistory = new List<ChatHistoryItem>();

    public static AIAudioClient FindPreferredInstance()
    {
        AIAudioClient[] clients = UnityEngine.Object.FindObjectsOfType<AIAudioClient>(true);
        AIAudioClient best = null;
        int bestScore = int.MinValue;

        foreach (AIAudioClient client in clients)
        {
            if (client == null || !client.gameObject.scene.IsValid()) continue;

            int score = client.GetAutoBindScore();
            if (best == null || score > bestScore)
            {
                best = client;
                bestScore = score;
            }
        }

        return best;
    }

    private enum StatusKey
    {
        Ready,
        EmptyQuestion,
        DefaultScriptEmpty,
        NoMicrophone,
        MicRecording,
        MicStopped,
        MicStoppedShort,
        MicSendingAudio,
        RecognizingSpeech,
        AwaitingAdmin,
        AdminOverrideReceived,
        AdminWaitTimedOut,
        SpeechNotRecognized,
        SpeechRecognizedSendingToAi,
        AiProcessing,
        ConnectionError,
        AiAnswering,
        AiAudioUnreadable,
        GeneratingScript,
        ScriptGenerationFailed,
        ScriptGenerated,
        PreparingVoice,
        TtsError,
        AiSpeaking,
        AiVoiceUnreadable
    }

    private bool UseSingleRecordButton
    {
        get
        {
            if (startRecordButton != null && stopSendButton != null)
            {
                return startRecordButton == stopSendButton;
            }

            return startRecordButton != null || stopSendButton != null;
        }
    }

    private Button ActiveRecordButton
    {
        get
        {
            if (startRecordButton != null) return startRecordButton;
            return stopSendButton;
        }
    }

    private int GetAutoBindScore()
    {
        int score = 0;

        if (startRecordButton != null || stopSendButton != null) score += 4;
        if (statusLabel != null) score += 3;
        if (transcriptLabel != null) score += 3;
        if (greetingTextUI != null) score += 2;
        if (sendTextButton != null || textInput != null) score += 2;
        if (audioSource != null) score += 1;
        if (gameObject.activeInHierarchy) score += 1;

        return score;
    }

    // ─────────────────────────────────────────────────────────────
    // Unity Lifecycle
    // ─────────────────────────────────────────────────────────────

    private void Start()
    {
        serverUrl = NormalizeServerUrl(serverUrl);
        EnsureSessionId();

        // Tự thêm AudioSource nếu chưa có
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }
        }

        // Gắn sự kiện cho các button
        if (sendTextButton)   sendTextButton.onClick.AddListener(OnSendTextClicked);
        BindRecordButtons();
        if (readDefaultScriptButton) readDefaultScriptButton.onClick.AddListener(OnReadDefaultScriptClicked);
        if (generateScriptButton) generateScriptButton.onClick.AddListener(OnGenerateScriptClicked);

        // Trạng thái ban đầu
        RefreshRecordButtons();
        SetStatus("Sẵn sàng phỏng vấn.");

        // Kiểm tra và in danh sách microphone để dễ debug
        if (Microphone.devices.Length > 0) {
            foreach (var device in Microphone.devices) Debug.Log("Detected Mic: " + device);
        } else {
            Debug.LogError("No Microphone detected!");
        }

        // Nếu trong scene chưa có GazeAndControllerMic, tự động thêm để tiện test
        if (FindObjectOfType<GazeAndControllerMic>() == null)
        {
            try
            {
                var gaze = gameObject.AddComponent<GazeAndControllerMic>();
                gaze.aiAudioClient = this;

                // Cố gắng tự tìm micTarget theo tên
                GameObject micObj = GameObject.Find("Mic");
                if (micObj == null)
                {
                    foreach (var go in GameObject.FindObjectsOfType<GameObject>())
                    {
                        if (go.name.ToLower().Contains("mic")) { micObj = go; break; }
                    }
                }
                if (micObj != null) gaze.micTarget = micObj;

                // Gán các UI blocker (tìm các object có tên chứa 'trang_4' hoặc 'position')
                var blockers = new List<GameObject>();
                foreach (var go in GameObject.FindObjectsOfType<GameObject>())
                {
                    var n = go.name.ToLower();
                    if (n.Contains("trang_4") || n.Contains("position") || n.Contains("trang4")) blockers.Add(go);
                }
                if (blockers.Count > 0) gaze.uiBlockers = blockers.ToArray();

                Debug.Log("[AI] Auto-added GazeAndControllerMic to " + gameObject.name + (micObj != null ? " (mic: " + micObj.name + ")" : " (mic not found)"));
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[AI] Could not auto-add GazeAndControllerMic: " + ex.Message);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    // TEXT MODE & DEFAULT SCRIPT
    // ─────────────────────────────────────────────────────────────

    public void OnSendTextClicked()
    {
        if (_isBusy) return;
        string msg = textInput ? textInput.text.Trim() : "";
        if (string.IsNullOrEmpty(msg)) { SetStatus("Chưa nhập câu hỏi."); return; }
        StartCoroutine(ChatVoiceCoroutine(msg));
    }

    public void OnReadDefaultScriptClicked()
    {
        if (_isBusy) return;
        if (string.IsNullOrEmpty(defaultScript)) { SetStatus("Default script is empty."); return; }
        StartCoroutine(ReadTextCoroutine(defaultScript));
    }

    public void OnGenerateScriptClicked()
    {
        if (_isBusy) return;
        StartCoroutine(GenerateVRScriptCoroutine());
    }

    public void AskOpeningQuestion()
    {
        if (_isBusy) return;
        ResetConversationMemory(true);

        string openingPrompt =
            "Start the interview now. Give one short opening line, then ask the first interview question for the candidate based on the selected role and interview type.";
        StartCoroutine(ChatVoiceCoroutine(openingPrompt));
    }

    // ─────────────────────────────────────────────────────────────
    // UI SETTERS FOR VR CONFIG
    // ─────────────────────────────────────────────────────────────

    public void SetLanguageVietnamese()
    {
        language = "Vietnamese";
        ApplyCurrentStatus(false);
        Debug.Log("[AI] Language set to: Vietnamese");
    }

    public void SetLanguageEnglish()
    {
        language = "English";
        ApplyCurrentStatus(false);
        Debug.Log("[AI] Language set to: English");
    }

    public void SetTypeAttitude()
    {
        interviewType = "Attitude Interview";
        Debug.Log("[AI] Interview Type set to: Attitude Interview");
    }

    public void SetTypeRoleSpecific()
    {
        interviewType = "Role-Specific Interview";
        Debug.Log("[AI] Interview Type set to: Role-Specific Interview");
    }

    public void SetJobTitle(string value)
    {
        jobTitle = string.IsNullOrWhiteSpace(value) ? "Unknown" : value.Trim();
        Debug.Log("[AI] Job Title set to: " + jobTitle);
    }

    /// <summary>
    /// Phát âm thanh lời chào theo ngôn ngữ đã đặt. Gắn vào sự kiện OnClick() của nút Next.
    /// </summary>
    public void PlayGreeting()
    {
        string textToPlay = (language == "English") ? englishGreeting : vietnameseGreeting;
        
        if (greetingTextUI != null)
        {
            greetingTextUI.text = textToPlay;
        }

        Debug.Log($"[AI] Playing greeting in {language}...");
        SpeakText(textToPlay);
    }

    // ─────────────────────────────────────────────────────────────
    // VOICE MODE
    // ─────────────────────────────────────────────────────────────

    public void ToggleRecord()
    {
        if (_isBusy) return;
        
        if (!_isRecording)
        {
            OnStartRecordClicked();
            Debug.Log("[AI] Vừa bật ghi âm bằng nút Mic.");
        }
        else
        {
            OnStopAndSendClicked();
            Debug.Log("[AI] Vừa tắt và gửi ghi âm đi.");
        }
    }

    public void OnStartRecordClicked()
    {
        if (_isBusy || _isRecording) return;

        if (Microphone.devices.Length == 0)
        {
            SetStatus("Error: No microphone found!");
            return;
        }

        _recordingClip = Microphone.Start(null, false, maxRecordSeconds, 16000);
        _isRecording = true;
        SetStatus("Mic đang ghi âm...");
        RefreshRecordButtons();
    }

    public void OnStopAndSendClicked()
    {
        if (!_isRecording) return;

        int recordedSamples = Microphone.GetPosition(null);
        Microphone.End(null);
        _isRecording = false;
        RefreshRecordButtons();
        SetStatus("Mic đã dừng.");

        if (recordedSamples < 100)
        {
            SetStatus("Mic đã dừng. Bản ghi quá ngắn.");
            // Dọn dẹp clip ghi âm lỗi
            if (_recordingClip != null)
            {
                Destroy(_recordingClip);
                _recordingClip = null;
            }
            return;
        }

        // Cắt clip theo số sample thực tế
        float[] data = new float[recordedSamples * _recordingClip.channels];
        _recordingClip.GetData(data, 0);
        AudioClip trimmed = AudioClip.Create("rec", recordedSamples, _recordingClip.channels, _recordingClip.frequency, false);
        trimmed.SetData(data, 0);

        // Giải phóng đoạn ghi âm thô ban đầu sau khi đã cắt xong
        Destroy(_recordingClip);
        _recordingClip = null;

        SetStatus("Mic đã dừng. Đang gửi âm thanh...");
        StartCoroutine(SttThenChatCoroutine(trimmed));
    }

    /// <summary>
    /// Bắt đầu ghi âm trong một khoảng thời gian cố định (giây) rồi tự động dừng và gửi.
    /// Dùng cho kích hoạt bằng gaze hoặc controller.
    /// </summary>
    public void StartRecordForSeconds(int seconds)
    {
        StartCoroutine(StartRecordForSecondsCoroutine(seconds));
    }

    private IEnumerator StartRecordForSecondsCoroutine(int seconds)
    {
        if (_isBusy || _isRecording) yield break;
        int prevMax = maxRecordSeconds;
        maxRecordSeconds = Mathf.Max(maxRecordSeconds, seconds);
        OnStartRecordClicked();
        yield return new WaitForSeconds(seconds);
        OnStopAndSendClicked();
        maxRecordSeconds = prevMax;
    }

    // ─────────────────────────────────────────────────────────────
    // COROUTINES
    // ─────────────────────────────────────────────────────────────

    /// <summary>Bước 1: Gửi WAV lên /api/stt → lấy text → Bước 2</summary>
    private IEnumerator SttThenChatCoroutine(AudioClip clip)
    {
        _isBusy = true;
        RefreshRecordButtons();
        SetStatus("Đang nhận diện giọng nói...");

        byte[] wavBytes = null;
        try
        {
            yield return ConvertAudioClipToWavCoroutine(clip, bytes => wavBytes = bytes);
        }
        finally
        {
            if (clip != null)
            {
                Destroy(clip);
            }
        }
        if (wavBytes == null || wavBytes.Length == 0)
        {
            SetStatus("STT Error: Could not prepare audio.");
            Debug.LogError("[AI] Failed to convert recorded clip to WAV.");
            _isBusy = false;
            RefreshRecordButtons();
            yield break;
        }
        string endpoint = BuildEndpoint("/api/stt");

        WWWForm form = new WWWForm();
        form.AddBinaryData("audio", wavBytes, "recording.wav", "audio/wav");
        form.AddField("session_id", sessionId);
        form.AddField("job_title", jobTitle);
        form.AddField("interview_type", interviewType);
        form.AddField("language", language);

        using (UnityWebRequest req = UnityWebRequest.Post(endpoint, form))
        {
            req.timeout = SttTimeoutSeconds;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                string responseText = req.downloadHandler != null ? req.downloadHandler.text : "";
                SttResponse errorResp = null;
                if (!string.IsNullOrWhiteSpace(responseText))
                {
                    try
                    {
                        errorResp = JsonUtility.FromJson<SttResponse>(responseText);
                    }
                    catch (System.Exception)
                    {
                        errorResp = null;
                    }
                }

                if (errorResp != null && errorResp.need_admin)
                {
                    Debug.LogWarning("[AI] STT needs admin override: " + (string.IsNullOrWhiteSpace(errorResp.error) ? req.error : errorResp.error));
                    yield return WaitForAdminOverrideAndContinueCoroutine();
                    yield break;
                }

                SetStatus("STT Error: " + req.error);
                Debug.LogError("[AI] STT error: " + req.error);
                Debug.Log("[AI] Response Code: " + req.responseCode);
                _isBusy = false;
                RefreshRecordButtons();
                yield break;
            }

            string json = req.downloadHandler.text;
            Debug.Log("[AI] STT Raw JSON: " + json);
            SttResponse sttResp = JsonUtility.FromJson<SttResponse>(json);
            string recognizedText = sttResp?.text?.Trim();

            if (string.IsNullOrEmpty(recognizedText))
            {
                SetStatus("Không nhận diện được giọng nói. Hãy thử lại.");
                _isBusy = false;
                RefreshRecordButtons();
                yield break;
            }

            SetStatus("Đã nhận diện giọng nói. Đang gửi đến AI...");
            Debug.Log("[AI] Recognized: " + recognizedText);

            // Tiếp tục gửi transcript lên AI rồi tách riêng bước TTS
            yield return ChatVoiceCoroutine(recognizedText);
        }
    }

    private IEnumerator WaitForAdminOverrideAndContinueCoroutine()
    {
        SetStatus("STT needs admin help. Waiting...");
        float startedAt = Time.realtimeSinceStartup;

        while (Time.realtimeSinceStartup - startedAt < AdminPollTimeoutSeconds)
        {
            string endpoint = BuildEndpoint("/api/poll_admin_message?session_id=" + UnityWebRequest.EscapeURL(sessionId));

            using (UnityWebRequest req = UnityWebRequest.Get(endpoint))
            {
                req.timeout = AdminPollRequestTimeoutSeconds;
                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    string responseText = req.downloadHandler != null ? req.downloadHandler.text : "";
                    AdminPollResponse pollResp = null;
                    if (!string.IsNullOrWhiteSpace(responseText))
                    {
                        pollResp = JsonUtility.FromJson<AdminPollResponse>(responseText);
                    }

                    string adminMessage = pollResp != null ? pollResp.message : null;
                    adminMessage = string.IsNullOrWhiteSpace(adminMessage) ? null : adminMessage.Trim();
                    if (pollResp != null && pollResp.has_message && !string.IsNullOrWhiteSpace(adminMessage))
                    {
                        Debug.Log("[AI] Received admin override. Continuing interview.");
                        SetStatus("Received admin input. Sending to AI...");
                        yield return ChatVoiceCoroutine(adminMessage);
                        yield break;
                    }
                }
                else
                {
                    Debug.LogWarning("[AI] Poll admin message failed: " + req.error);
                }
            }

            yield return new WaitForSeconds(AdminPollIntervalSeconds);
        }

        SetStatus("Timed out waiting for admin. Please try again.");
        _isBusy = false;
        RefreshRecordButtons();
    }

    /// <summary>Bước 2: Gửi text lên /api/chat → nhận full text → Bước 3: TTS theo từng đoạn</summary>
    private IEnumerator ChatVoiceCoroutine(string message)
    {
        _isBusy = true;
        RefreshRecordButtons();
        SetStatus("AI đang xử lý...");

        string endpoint = BuildEndpoint("/api/chat");
        string jsonBody = JsonUtility.ToJson(new ChatPayload 
        { 
            session_id = sessionId,
            message = message,
            job_title = jobTitle,
            interview_type = interviewType,
            language = language,
            history = BuildHistorySnapshot()
        });
        byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);

        using (UnityWebRequest req = new UnityWebRequest(endpoint, "POST"))
        {
            req.uploadHandler   = new UploadHandlerRaw(bodyBytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = ChatTimeoutSeconds;

            yield return req.SendWebRequest();

            string responseText = req.downloadHandler != null ? req.downloadHandler.text : "";
            if (req.result != UnityWebRequest.Result.Success)
            {
                string serverError = ExtractErrorMessage(responseText);
                string errorDetail = string.IsNullOrWhiteSpace(serverError) ? req.error : serverError;
                ChatResponse errorResp = null;
                if (!string.IsNullOrWhiteSpace(responseText))
                {
                    try
                    {
                        errorResp = JsonUtility.FromJson<ChatResponse>(responseText);
                    }
                    catch (System.Exception)
                    {
                        errorResp = null;
                    }
                }

                if (errorResp != null && errorResp.need_admin)
                {
                    Debug.LogWarning("[AI] Chat needs admin override: " + errorDetail);
                    yield return WaitForAdminOverrideAndContinueCoroutine();
                    yield break;
                }

                SetStatus("Connection Error: " + errorDetail);
                Debug.LogError("[AI] Chat error: " + errorDetail);
                if (!string.IsNullOrWhiteSpace(responseText))
                    Debug.LogError("[AI] Chat error body: " + responseText);
                _isBusy = false;
                RefreshRecordButtons();
                yield break;
            }

            ChatResponse chatResp = null;
            if (!string.IsNullOrWhiteSpace(responseText))
            {
                chatResp = JsonUtility.FromJson<ChatResponse>(responseText);
            }

            string aiText = chatResp != null ? chatResp.response : null;
            aiText = string.IsNullOrWhiteSpace(aiText) ? null : aiText.Trim();

            if (string.IsNullOrWhiteSpace(aiText))
            {
                string errorDetail = ExtractErrorMessage(responseText);
                if (string.IsNullOrWhiteSpace(errorDetail))
                {
                    errorDetail = "AI returned an empty answer.";
                }

                SetStatus("Connection Error: " + errorDetail);
                Debug.LogError("[AI] Empty chat response: " + responseText);
                _isBusy = false;
                RefreshRecordButtons();
                yield break;
            }

            AppendConversationTurn(message, aiText);
            SetAiTranscriptText(aiText);
            yield return SpeakTextChunksCoroutine(aiText);
        }

        _isBusy = false;
        RefreshRecordButtons();
    }

    /// <summary>Gọi API sinh kịch bản VR JSON (không sinh audio)</summary>
    private IEnumerator GenerateVRScriptCoroutine()
    {
        _isBusy = true;
        RefreshRecordButtons();
        SetStatus("AI đang tạo kịch bản phỏng vấn...");

        string endpoint = BuildEndpoint("/api/chat");
        string jsonBody = JsonUtility.ToJson(new ScriptRequestPayload {
            session_id = sessionId,
            message = "Generate interview script",
            job_title = jobTitle,
            interview_type = interviewType,
            language = language
        });
        byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);

        using (UnityWebRequest req = new UnityWebRequest(endpoint, "POST"))
        {
            req.uploadHandler   = new UploadHandlerRaw(bodyBytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = ChatTimeoutSeconds;

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                SetStatus("Tạo kịch bản thất bại: " + req.error);
                Debug.LogError("[AI] Script error: " + req.error);
                _isBusy = false;
                RefreshRecordButtons();
                yield break;
            }

            string responseJson = req.downloadHandler.text;
            Debug.Log("[AI] VR Script Raw JSON:\n" + responseJson);
            
            // Nếu bạn cần parse JSON thành C# Object, hãy tạo các class tương ứng (như VrScriptResponse)
            // và gọi: var scriptObj = JsonUtility.FromJson<VrScriptResponse>(responseJson);
            
            SetStatus("Đã tạo kịch bản phỏng vấn.");
        }

        _isBusy = false;
        RefreshRecordButtons();
    }

    /// <summary>Gửi text trực tiếp lên /api/tts → nhận WAV → phát</summary>
    public void SpeakText(string text)
    {
        if (_isBusy) return;
        StartCoroutine(ReadTextCoroutine(text));
    }

    private IEnumerator ReadTextCoroutine(string text)
    {
        _isBusy = true;
        RefreshRecordButtons();
        SetAiTranscriptText(text);
        yield return SpeakTextChunksCoroutine(text);

        _isBusy = false;
        RefreshRecordButtons();
    }

    private IEnumerator SpeakTextChunksCoroutine(string fullText)
    {
        List<string> chunks = SplitTextForTts(fullText, TtsChunkMaxCharacters);
        if (chunks.Count == 0)
        {
            SetStatus("TTS Error: Empty text.");
            yield break;
        }

        for (int i = 0; i < chunks.Count; i++)
        {
            string chunk = chunks[i];
            SetStatus("AI đang chuẩn bị giọng nói...");

            string endpoint = BuildEndpoint("/api/tts");
            string jsonBody = JsonUtility.ToJson(new TtsPayload {
                session_id = sessionId,
                text = chunk,
                language = this.language
            });
            byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);

            using (UnityWebRequest req = new UnityWebRequest(endpoint, "POST"))
            {
                req.uploadHandler   = new UploadHandlerRaw(bodyBytes);
                req.downloadHandler = new DownloadHandlerAudioClip(endpoint, AudioType.WAV);
                req.SetRequestHeader("Content-Type", "application/json");
                req.timeout = TtsTimeoutSeconds;

                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    SetStatus("TTS Error: " + req.error);
                    Debug.LogError("[AI] TTS error: " + req.error);
                    byte[] errorBytes = req.downloadHandler != null ? req.downloadHandler.data : null;
                    if (errorBytes != null && errorBytes.Length > 0)
                    {
                        Debug.LogError("[AI] TTS error body: " + Encoding.UTF8.GetString(errorBytes));
                    }
                    yield break;
                }

                AudioClip aiClip = DownloadHandlerAudioClip.GetContent(req);
                if (aiClip == null)
                {
                    SetStatus("Không đọc được âm thanh giọng nói của AI.");
                    yield break;
                }

                if (audioSource.isPlaying) audioSource.Stop();
                if (audioSource.clip != null)
                {
                    Destroy(audioSource.clip);
                    audioSource.clip = null;
                }

                audioSource.clip = aiClip;
                audioSource.Play();
                SetStatus("AI đang nói...");
                Debug.Log($"[AI] Playing TTS chunk {i + 1}/{chunks.Count}.");

                yield return WaitForAudioPlaybackOrTimeout(aiClip, i + 1, chunks.Count);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────────────────────────

    private List<string> SplitTextForTts(string text, int maxCharacters)
    {
        var chunks = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return chunks;

        string normalized = Regex.Replace(text.Trim(), @"\s+", " ");
        string[] sentences = Regex.Split(normalized, @"(?<=[\.\!\?\;\:])\s+");
        var current = new StringBuilder();

        foreach (string rawSentence in sentences)
        {
            string sentence = rawSentence.Trim();
            if (string.IsNullOrWhiteSpace(sentence)) continue;

            if (sentence.Length > maxCharacters)
            {
                FlushChunk(chunks, current);
                AppendLongSentenceChunks(chunks, sentence, maxCharacters);
                continue;
            }

            int nextLength = current.Length == 0
                ? sentence.Length
                : current.Length + 1 + sentence.Length;

            if (nextLength > maxCharacters)
            {
                FlushChunk(chunks, current);
            }

            if (current.Length > 0) current.Append(' ');
            current.Append(sentence);
        }

        FlushChunk(chunks, current);
        return chunks;
    }

    private void AppendLongSentenceChunks(List<string> chunks, string sentence, int maxCharacters)
    {
        string[] words = sentence.Split(' ');
        var current = new StringBuilder();

        foreach (string word in words)
        {
            if (string.IsNullOrWhiteSpace(word)) continue;

            int nextLength = current.Length == 0
                ? word.Length
                : current.Length + 1 + word.Length;

            if (nextLength > maxCharacters && current.Length > 0)
            {
                chunks.Add(current.ToString());
                current.Length = 0;
            }

            if (current.Length > 0) current.Append(' ');
            current.Append(word);
        }

        if (current.Length > 0)
        {
            chunks.Add(current.ToString());
        }
    }

    private void FlushChunk(List<string> chunks, StringBuilder current)
    {
        if (current.Length == 0) return;
        chunks.Add(current.ToString());
        current.Length = 0;
    }

    private string ExtractErrorMessage(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) return null;

        try
        {
            ChatResponse chatResponse = JsonUtility.FromJson<ChatResponse>(rawJson);
            if (chatResponse != null && !string.IsNullOrWhiteSpace(chatResponse.error))
            {
                return chatResponse.error.Trim();
            }
        }
        catch (System.Exception)
        {
            // Nếu backend trả text thuần thay vì JSON thì fallback về raw body.
        }

        return rawJson.Trim();
    }

    private IEnumerator ConvertAudioClipToWavCoroutine(AudioClip clip, System.Action<byte[]> onCompleted)
    {
        if (clip == null)
        {
            onCompleted?.Invoke(null);
            yield break;
        }

        int hz = clip.frequency;
        int channels = clip.channels;
        int totalFrames = clip.samples;
        int dataSize = totalFrames * channels * 2;
        int fullChunkSampleCount = WavConversionChunkFrames * channels;

        float[] fullChunkSamples = new float[fullChunkSampleCount];
        byte[] fullChunkBytes = new byte[fullChunkSampleCount * 2];
        float[] tailChunkSamples = null;
        byte[] tailChunkBytes = null;

        using (MemoryStream stream = new MemoryStream(44 + dataSize))
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataSize);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)channels);
            writer.Write(hz);
            writer.Write(hz * channels * 2);
            writer.Write((short)(channels * 2));
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataSize);

            for (int frameOffset = 0; frameOffset < totalFrames; frameOffset += WavConversionChunkFrames)
            {
                int framesToRead = Mathf.Min(WavConversionChunkFrames, totalFrames - frameOffset);
                int sampleCount = framesToRead * channels;

                float[] sampleBuffer;
                byte[] byteBuffer;

                if (sampleCount == fullChunkSampleCount)
                {
                    sampleBuffer = fullChunkSamples;
                    byteBuffer = fullChunkBytes;
                }
                else
                {
                    if (tailChunkSamples == null || tailChunkSamples.Length != sampleCount)
                    {
                        tailChunkSamples = new float[sampleCount];
                        tailChunkBytes = new byte[sampleCount * 2];
                    }

                    sampleBuffer = tailChunkSamples;
                    byteBuffer = tailChunkBytes;
                }

                if (!clip.GetData(sampleBuffer, frameOffset))
                {
                    Debug.LogError("[AI] Failed to read recorded clip data for WAV conversion.");
                    onCompleted?.Invoke(null);
                    yield break;
                }

                for (int i = 0; i < sampleCount; i++)
                {
                    short pcmSample = (short)(Mathf.Clamp(sampleBuffer[i], -1f, 1f) * 32767f);
                    int byteIndex = i * 2;
                    byteBuffer[byteIndex] = (byte)(pcmSample & 0xFF);
                    byteBuffer[byteIndex + 1] = (byte)((pcmSample >> 8) & 0xFF);
                }

                writer.Write(byteBuffer, 0, sampleCount * 2);
                yield return null;
            }

            onCompleted?.Invoke(stream.ToArray());
        }
    }

    private IEnumerator WaitForAudioPlaybackOrTimeout(AudioClip clip, int chunkIndex, int chunkCount)
    {
        if (audioSource == null || clip == null) yield break;

        float startedAt = Time.realtimeSinceStartup;
        float maxWaitSeconds = Mathf.Max(clip.length + AudioPlaybackGraceSeconds, 5f);

        while (audioSource != null && audioSource.clip == clip)
        {
            float elapsed = Time.realtimeSinceStartup - startedAt;
            if (elapsed > maxWaitSeconds)
            {
                Debug.LogWarning($"[AI] TTS chunk {chunkIndex}/{chunkCount} exceeded playback wait ({elapsed:F1}s). Forcing next chunk.");
                if (audioSource.isPlaying)
                {
                    audioSource.Stop();
                }
                break;
            }

            if (!audioSource.isPlaying && elapsed > 0.1f)
            {
                break;
            }

            yield return null;
        }
    }

    private void SetStatus(string msg)
    {
        ParseStatus(msg, out _currentStatusKey, out _currentStatusDetail);
        ApplyCurrentStatus(true);
    }

    private void ApplyCurrentStatus(bool log)
    {
        string localized = GetLocalizedStatusText(_currentStatusKey, _currentStatusDetail);
        if (statusLabel) statusLabel.text = localized;
        if (log) Debug.Log("[AI] " + localized);
    }

    private void ParseStatus(string msg, out StatusKey key, out string detail)
    {
        detail = "";

        if (msg == "Sáºµn sÃ ng phá»ng váº¥n." || msg == "Ready for interview.")
        {
            key = StatusKey.Ready;
            return;
        }

        if (msg == "ChÆ°a nháº­p cÃ¢u há»i." || msg == "No question entered.")
        {
            key = StatusKey.EmptyQuestion;
            return;
        }

        if (msg == "Default script is empty." || msg == "Kịch bản mặc định đang trống.")
        {
            key = StatusKey.DefaultScriptEmpty;
            return;
        }

        if (msg == "Error: No microphone found!" || msg == "No microphone found.")
        {
            key = StatusKey.NoMicrophone;
            return;
        }

        if (msg == "Mic Ä‘ang ghi Ã¢m..." || msg == "Mic is recording...")
        {
            key = StatusKey.MicRecording;
            return;
        }

        if (msg == "Mic Ä‘Ã£ dá»«ng." || msg == "Mic stopped.")
        {
            key = StatusKey.MicStopped;
            return;
        }

        if (msg == "Mic Ä‘Ã£ dá»«ng. Báº£n ghi quÃ¡ ngáº¯n." || msg == "Mic stopped. Recording too short.")
        {
            key = StatusKey.MicStoppedShort;
            return;
        }

        if (msg == "Mic Ä‘Ã£ dá»«ng. Äang gá»­i Ã¢m thanh..." || msg == "Mic stopped. Sending audio...")
        {
            key = StatusKey.MicSendingAudio;
            return;
        }

        if (msg == "Äang nháº­n diá»‡n giá»ng nÃ³i..." || msg == "Recognizing speech...")
        {
            key = StatusKey.RecognizingSpeech;
            return;
        }

        if (msg == "STT needs admin help. Waiting...")
        {
            key = StatusKey.AwaitingAdmin;
            return;
        }

        if (msg == "Received admin input. Sending to AI...")
        {
            key = StatusKey.AdminOverrideReceived;
            return;
        }

        if (msg == "Timed out waiting for admin. Please try again.")
        {
            key = StatusKey.AdminWaitTimedOut;
            return;
        }

        if (msg.StartsWith("STT Error: "))
        {
            key = StatusKey.ConnectionError;
            detail = msg.Substring("STT Error: ".Length);
            return;
        }

        if (msg == "KhÃ´ng nháº­n diá»‡n Ä‘Æ°á»£c giá»ng nÃ³i. HÃ£y thá»­ láº¡i." || msg == "Speech was not recognized. Please try again.")
        {
            key = StatusKey.SpeechNotRecognized;
            return;
        }

        if (msg == "ÄÃ£ nháº­n diá»‡n giá»ng nÃ³i. Äang gá»­i Ä‘áº¿n AI..." || msg == "Speech recognized. Sending to AI...")
        {
            key = StatusKey.SpeechRecognizedSendingToAi;
            return;
        }

        if (msg == "AI Ä‘ang xá»­ lÃ½..." || msg == "AI is processing...")
        {
            key = StatusKey.AiProcessing;
            return;
        }

        if (msg.StartsWith("Connection Error: ") || msg.StartsWith("Lỗi kết nối: "))
        {
            key = StatusKey.ConnectionError;
            detail = msg.Substring(msg.IndexOf(": ") + 2);
            return;
        }

        if (msg == "AI Ä‘ang tráº£ lá»i..." || msg == "AI is answering...")
        {
            key = StatusKey.AiAnswering;
            return;
        }

        if (msg == "KhÃ´ng Ä‘á»c Ä‘Æ°á»£c Ã¢m thanh pháº£n há»“i tá»« AI." || msg == "Could not read AI response audio.")
        {
            key = StatusKey.AiAudioUnreadable;
            return;
        }

        if (msg == "AI Ä‘ang táº¡o ká»‹ch báº£n phá»ng váº¥n..." || msg == "AI is generating the interview script...")
        {
            key = StatusKey.GeneratingScript;
            return;
        }

        if (msg.StartsWith("Táº¡o ká»‹ch báº£n tháº¥t báº¡i: ") || msg.StartsWith("Script generation failed: "))
        {
            key = StatusKey.ScriptGenerationFailed;
            detail = msg.Substring(msg.IndexOf(": ") + 2);
            return;
        }

        if (msg == "ÄÃ£ táº¡o ká»‹ch báº£n phá»ng váº¥n." || msg == "Interview script generated.")
        {
            key = StatusKey.ScriptGenerated;
            return;
        }

        if (msg == "AI Ä‘ang chuáº©n bá»‹ giá»ng nÃ³i..." || msg == "AI is preparing voice...")
        {
            key = StatusKey.PreparingVoice;
            return;
        }

        if (msg.StartsWith("TTS Error: ") || msg.StartsWith("Lỗi TTS: "))
        {
            key = StatusKey.TtsError;
            detail = msg.Substring(msg.IndexOf(": ") + 2);
            return;
        }

        if (msg == "AI Ä‘ang nÃ³i..." || msg == "AI is speaking...")
        {
            key = StatusKey.AiSpeaking;
            return;
        }

        if (msg == "KhÃ´ng Ä‘á»c Ä‘Æ°á»£c Ã¢m thanh giá»ng nÃ³i cá»§a AI." || msg == "Could not read AI voice audio.")
        {
            key = StatusKey.AiVoiceUnreadable;
            return;
        }

        key = StatusKey.ConnectionError;
        detail = msg;
    }

    private string GetLocalizedStatusText(StatusKey key, string detail)
    {
        bool isEnglish = language == "English";

        switch (key)
        {
            case StatusKey.Ready:
                return isEnglish ? "Ready for interview." : "Sẵn sàng phỏng vấn.";
            case StatusKey.EmptyQuestion:
                return isEnglish ? "No question entered." : "Chưa nhập câu hỏi.";
            case StatusKey.DefaultScriptEmpty:
                return isEnglish ? "Default script is empty." : "Kịch bản mặc định đang trống.";
            case StatusKey.NoMicrophone:
                return isEnglish ? "No microphone found." : "Không tìm thấy microphone.";
            case StatusKey.MicRecording:
                return isEnglish ? "Mic is recording..." : "Mic đang ghi âm...";
            case StatusKey.MicStopped:
                return isEnglish ? "Mic stopped." : "Mic đã dừng.";
            case StatusKey.MicStoppedShort:
                return isEnglish ? "Mic stopped. Recording too short." : "Mic đã dừng. Bản ghi quá ngắn.";
            case StatusKey.MicSendingAudio:
                return isEnglish ? "Mic stopped. Sending audio..." : "Mic đã dừng. Đang gửi âm thanh...";
            case StatusKey.RecognizingSpeech:
                return isEnglish ? "Recognizing speech..." : "Đang nhận diện giọng nói...";
            case StatusKey.AwaitingAdmin:
                return isEnglish ? "STT needs admin help. Waiting..." : "STT cần admin hỗ trợ. Đang chờ...";
            case StatusKey.AdminOverrideReceived:
                return isEnglish ? "Received admin input. Sending to AI..." : "Đã nhận nội dung từ admin. Đang gửi đến AI...";
            case StatusKey.AdminWaitTimedOut:
                return isEnglish ? "Timed out waiting for admin. Please try again." : "Hết thời gian chờ admin. Hãy thử lại.";
            case StatusKey.SpeechNotRecognized:
                return isEnglish ? "Speech was not recognized. Please try again." : "Không nhận diện được giọng nói. Hãy thử lại.";
            case StatusKey.SpeechRecognizedSendingToAi:
                return isEnglish ? "Speech recognized. Sending to AI..." : "Đã nhận diện giọng nói. Đang gửi đến AI...";
            case StatusKey.AiProcessing:
                return isEnglish ? "AI is processing..." : "AI đang xử lý...";
            case StatusKey.ConnectionError:
                return isEnglish ? "Connection error: " + detail : "Lỗi kết nối: " + detail;
            case StatusKey.AiAnswering:
                return isEnglish ? "AI is answering..." : "AI đang trả lời...";
            case StatusKey.AiAudioUnreadable:
                return isEnglish ? "Could not read AI response audio." : "Không đọc được âm thanh phản hồi từ AI.";
            case StatusKey.GeneratingScript:
                return isEnglish ? "AI is generating the interview script..." : "AI đang tạo kịch bản phỏng vấn...";
            case StatusKey.ScriptGenerationFailed:
                return isEnglish ? "Script generation failed: " + detail : "Tạo kịch bản thất bại: " + detail;
            case StatusKey.ScriptGenerated:
                return isEnglish ? "Interview script generated." : "Đã tạo kịch bản phỏng vấn.";
            case StatusKey.PreparingVoice:
                return isEnglish ? "AI is preparing voice..." : "AI đang chuẩn bị giọng nói...";
            case StatusKey.TtsError:
                return isEnglish ? "TTS error: " + detail : "Lỗi TTS: " + detail;
            case StatusKey.AiSpeaking:
                return isEnglish ? "AI is speaking..." : "AI đang nói...";
            case StatusKey.AiVoiceUnreadable:
                return isEnglish ? "Could not read AI voice audio." : "Không đọc được âm thanh giọng nói của AI.";
            default:
                return detail;
        }
    }

    private void SetAiTranscriptText(string transcript)
    {
        if (!transcriptLabel) return;

        string displayText = string.IsNullOrWhiteSpace(transcript)
            ? GetLocalizedTranscriptFallback()
            : transcript.Trim();

        transcriptLabel.text = "🤖 AI: " + displayText;
    }

    private string GetLocalizedTranscriptFallback()
    {
        return language == "English"
            ? "(audio response only)"
            : "(chỉ có phản hồi bằng giọng nói)";
    }

    private string NormalizeServerUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "http://127.0.0.1:8000";
        }

        return value.Trim().TrimEnd('/');
    }

    private string BuildEndpoint(string path)
    {
        return NormalizeServerUrl(serverUrl) + path;
    }

    private void ResetConversationMemory(bool renewSessionId)
    {
        _conversationHistory.Clear();

        if (!renewSessionId) return;

        sessionId = CreateSessionId();
        Debug.Log("[AI] Reset conversation memory. New session id: " + sessionId);
    }

    private ChatHistoryItem[] BuildHistorySnapshot()
    {
        int count = Mathf.Min(_conversationHistory.Count, MaxConversationHistoryMessages);
        ChatHistoryItem[] snapshot = new ChatHistoryItem[count];
        int startIndex = _conversationHistory.Count - count;

        for (int i = 0; i < count; i++)
        {
            ChatHistoryItem item = _conversationHistory[startIndex + i];
            snapshot[i] = new ChatHistoryItem
            {
                role = item.role,
                content = item.content
            };
        }

        return snapshot;
    }

    private void AppendConversationTurn(string userMessage, string aiMessage)
    {
        AppendHistoryItem("user", userMessage);
        AppendHistoryItem("assistant", aiMessage);
    }

    private void AppendHistoryItem(string role, string content)
    {
        string normalizedContent = string.IsNullOrWhiteSpace(content) ? null : content.Trim();
        if (string.IsNullOrEmpty(normalizedContent)) return;

        _conversationHistory.Add(new ChatHistoryItem
        {
            role = role,
            content = normalizedContent
        });

        int overflow = _conversationHistory.Count - MaxConversationHistoryMessages;
        if (overflow > 0)
        {
            _conversationHistory.RemoveRange(0, overflow);
        }
    }

    private string CreateSessionId()
    {
        return "unity-" + System.Guid.NewGuid().ToString("N");
    }

    private void EnsureSessionId()
    {
        if (!string.IsNullOrWhiteSpace(sessionId)) return;

        sessionId = CreateSessionId();
        Debug.Log("[AI] Generated session id: " + sessionId);
    }

    private void SetStartButtonInteractable(bool v)
    {
        if (startRecordButton) startRecordButton.interactable = v;
    }

    private void SetStopButtonInteractable(bool v)
    {
        if (stopSendButton) stopSendButton.interactable = v;
    }

    // ─────────────────────────────────────────────────────────────
    // AudioClip → WAV bytes (PCM 16-bit)
    // ─────────────────────────────────────────────────────────────

    private void BindRecordButtons()
    {
        if (UseSingleRecordButton)
        {
            Button recordButton = ActiveRecordButton;
            if (recordButton) recordButton.onClick.AddListener(ToggleRecord);
            return;
        }

        if (startRecordButton) startRecordButton.onClick.AddListener(OnStartRecordClicked);
        if (stopSendButton) stopSendButton.onClick.AddListener(OnStopAndSendClicked);
    }

    private void RefreshRecordButtons()
    {
        if (UseSingleRecordButton)
        {
            Button recordButton = ActiveRecordButton;
            if (recordButton) recordButton.interactable = !_isBusy;
            return;
        }

        SetStartButtonInteractable(!_isBusy && !_isRecording);
        SetStopButtonInteractable(!_isBusy && _isRecording);
    }

    // ─────────────────────────────────────────────────────────────
    // JSON Models
    // ─────────────────────────────────────────────────────────────
    [System.Serializable] private class ChatPayload  { 
        public string session_id;
        public string message; 
        public string job_title;
        public string interview_type;
        public string language;
        public ChatHistoryItem[] history;
    }
    [System.Serializable] private class ChatHistoryItem
    {
        public string role;
        public string content;
    }
    [System.Serializable] private class SttResponse
    {
        public string text;
        public string error;
        public bool need_admin;
        public string session_id;
    }
    [System.Serializable] private class AdminPollResponse
    {
        public bool has_message;
        public string message;
    }
    [System.Serializable] private class ChatResponse
    {
        public string response;
        public string role;
        public string error;
        public bool need_admin;
        public string session_id;
    }
    [System.Serializable] private class TtsPayload   { 
        public string session_id;
        public string text; 
        public string language;
    }
    [System.Serializable] private class ScriptRequestPayload { 
        public string session_id;
        public string message; 
        public string job_title; 
        public string interview_type; 
        public string language; 
    }
}
