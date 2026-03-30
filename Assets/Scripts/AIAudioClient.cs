using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.IO;
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
    [Tooltip("URL của FastAPI server (không có dấu / ở cuối)")]
    public string serverUrl = "http://192.168.1.4:8000";

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
    [Tooltip("Label hiển thị text đã nhận dạng từ giọng nói")]
    public TMP_Text transcriptLabel;

    [Header("=== GREETING SCRIPT ===")]
    [Tooltip("Gắn UI Text chứa lời chào vào đây")]
    public TMP_Text greetingTextUI;
    [TextArea(3, 5)]
    public string englishGreeting = "Hi there, welcome to VirtuHire! My name is Phuong Hang. I'll be your interviewer for today's session. This is a safe space for you to practice and get comfortable with interviews. Just relax and do your best. Let's get started!";
    [TextArea(3, 5)]
    public string vietnameseGreeting = "Chào bạn, chào mừng đến với VirtuHire! Tôi tên là Phương Hằng. Tôi sẽ là người phỏng vấn bạn trong buổi hôm nay. Đây là một không gian an toàn để bạn luyện tập và làm quen với các cuộc phỏng vấn. Hãy cứ thư giãn và thể hiện hết mình nhé. Chúng ta bắt đầu nào!";

    [Header("=== INTERVIEW FLOW ===")]
    [Tooltip("Tự động bật mic ghi âm sau khi AI nói xong")]
    public bool autoContinueInterview = true;
    [Tooltip("Thời gian chờ (giây) sau khi AI nói xong để bật mic")]
    public float delayBeforeAutoRecord = 0.5f;
    [Tooltip("Tự động ngắt mic khi phát hiện im lặng")]
    public bool autoStopOnSilence = true;
    [Tooltip("Ngưỡng âm lượng để coi là im lặng (0 - 1)")]
    public float silenceThreshold = 0.015f;
    [Tooltip("Số giây im lặng liên tục để tự ngắt mic")]
    public float silenceDurationToStop = 2.5f;

    // --- Private state ---
    private AudioClip _recordingClip;
    private bool _isRecording = false;
    private bool _isBusy = false;

    // --- Flow states ---
    private bool _wasPlayingAudio = false;
    private float _autoRecordTimer = 0f;
    private bool _waitingToAutoRecord = false;
    private float _silenceTimer = 0f;
    // Public read-only accessors for other scripts (e.g., gaze/controller helper)
    public bool IsRecording { get { return _isRecording; } }
    public bool IsBusy { get { return _isBusy; } }

    // ─────────────────────────────────────────────────────────────
    // Unity Lifecycle
    // ─────────────────────────────────────────────────────────────

    private void Start()
    {
        Debug.Log($"[AI] ========== AIAudioClient KHỞI ĐỘNG ==========");
        Debug.Log($"[AI] Server URL: {serverUrl}");

        // === XIN QUYỀN MICROPHONE TRÊN ANDROID ===
        #if UNITY_ANDROID && !UNITY_EDITOR
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
        {
            Debug.Log("[AI] 📱 Đang xin quyền Microphone trên Android...");
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
        }
        else
        {
            Debug.Log("[AI] 📱 Quyền Microphone đã được cấp.");
        }
        #endif

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
        if (startRecordButton) startRecordButton.onClick.AddListener(OnStartRecordClicked);
        if (stopSendButton)   stopSendButton.onClick.AddListener(OnStopAndSendClicked);
        if (readDefaultScriptButton) readDefaultScriptButton.onClick.AddListener(OnReadDefaultScriptClicked);
        if (generateScriptButton) generateScriptButton.onClick.AddListener(OnGenerateScriptClicked);

        // Trạng thái ban đầu
        SetStopButtonInteractable(false);
        SetStatus("Ready.");

        // Kiểm tra và in danh sách microphone để dễ debug
        if (Microphone.devices.Length > 0) {
            Debug.Log("[AI] 🎤 Microphone(s) phát hiện:");
            foreach (var device in Microphone.devices) Debug.Log("  → " + device);
        } else {
            Debug.LogError("[AI] ❌ KHÔNG phát hiện Microphone! Trên Android: kiểm tra quyền RECORD_AUDIO.");
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

    private void Update()
    {
        // 1. AUTO-CONTINUE: Phát hiện AI vừa nói xong
        if (audioSource != null)
        {
            bool isPlaying = audioSource.isPlaying;
            if (_wasPlayingAudio && !isPlaying)
            {
                // Âm thanh vừa kết thúc
                if (autoContinueInterview && !_isBusy && !_isRecording)
                {
                    Debug.Log("[AI] 🎧 AI đã nói xong. Chuẩn bị bật Mic tự động...");
                    _waitingToAutoRecord = true;
                    _autoRecordTimer = 0f;
                }
            }
            _wasPlayingAudio = isPlaying;
        }

        // Đếm ngược để bật Mic
        if (_waitingToAutoRecord)
        {
            _autoRecordTimer += Time.deltaTime;
            if (_autoRecordTimer >= delayBeforeAutoRecord)
            {
                _waitingToAutoRecord = false;
                if (!_isBusy && !_isRecording)
                {
                    Debug.Log("[AI] 🎙️ AI FLOW: BẬT MIC TỰ ĐỘNG (Auto-Continue)");
                    StartRecordForSeconds(maxRecordSeconds);
                }
            }
        }

        // 2. SILENCE DETECTION (VAD): Tự ngắt mic khi im lặng
        if (_isRecording && autoStopOnSilence && _recordingClip != null)
        {
            float vol = GetMicrophoneVolume();
            if (vol < silenceThreshold)
            {
                _silenceTimer += Time.deltaTime;
                if (_silenceTimer >= silenceDurationToStop)
                {
                    Debug.Log($"[AI] 🔇 FLOW: Dừng mic do im lặng > {silenceDurationToStop}s");
                    _silenceTimer = 0f; // reset trước khi ngắt
                    OnStopAndSendClicked();
                }
            }
            else
            {
                _silenceTimer = 0f; // Reset do có tiếng động
            }
        }
    }

    private float GetMicrophoneVolume()
    {
        if (!_isRecording || _recordingClip == null) return 0f;
        int currentPosition = Microphone.GetPosition(null);
        if (currentPosition < 128) return 0f;

        float[] samples = new float[128];
        _recordingClip.GetData(samples, currentPosition - 128);
        float sum = 0f;
        foreach (float sample in samples)
        {
            sum += sample * sample;
        }
        return Mathf.Sqrt(sum / samples.Length);
    }

    // ─────────────────────────────────────────────────────────────
    // TEXT MODE & DEFAULT SCRIPT
    // ─────────────────────────────────────────────────────────────

    public void OnSendTextClicked()
    {
        if (_isBusy) return;
        string msg = textInput ? textInput.text.Trim() : "";
        if (string.IsNullOrEmpty(msg)) { SetStatus("Please enter a question."); return; }
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

    // ─────────────────────────────────────────────────────────────
    // UI SETTERS FOR VR CONFIG
    // ─────────────────────────────────────────────────────────────

    public void SetLanguageVietnamese()
    {
        language = "Vietnamese";
        Debug.Log("[AI] Language set to: Vietnamese");
    }

    public void SetLanguageEnglish()
    {
        language = "English";
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
        if (_isBusy || _isRecording)
        {
            Debug.Log($"[AI] OnStartRecordClicked blocked: isBusy={_isBusy}, isRecording={_isRecording}");
            return;
        }

        if (Microphone.devices.Length == 0)
        {
            SetStatus("Error: No microphone found!");
            Debug.LogError("[AI] ❌ Không tìm thấy microphone! Kiểm tra Android permission.");
            return;
        }

        Debug.Log($"[AI] 🎙️ BẮT ĐẦU GHI ÂM - Mic: {Microphone.devices[0]}, Max: {maxRecordSeconds}s, Freq: 16000Hz");
        _recordingClip = Microphone.Start(null, false, maxRecordSeconds, 16000);
        _isRecording = true;
        SetStatus("🔴 Đang ghi âm... Nhìn đi chỗ khác để dừng");
        SetStartButtonInteractable(false);
        SetStopButtonInteractable(true);
    }

    public void OnStopAndSendClicked()
    {
        if (!_isRecording) return;

        int recordedSamples = Microphone.GetPosition(null);
        Microphone.End(null);
        _isRecording = false;
        SetStopButtonInteractable(false);
        SetStartButtonInteractable(true);

        Debug.Log($"[AI] ⏹️ DỪNG GHI ÂM - Recorded {recordedSamples} samples ({(float)recordedSamples/16000f:F1}s)");

        if (recordedSamples < 100)
        {
            SetStatus("Ghi âm quá ngắn. Thử lại.");
            Debug.LogWarning("[AI] ⚠️ Recording quá ngắn (< 100 samples). Bỏ qua.");
            if (_recordingClip != null) Destroy(_recordingClip);
            return;
        }

        // Cắt clip theo số sample thực tế
        float[] data = new float[recordedSamples * _recordingClip.channels];
        _recordingClip.GetData(data, 0);
        AudioClip trimmed = AudioClip.Create("rec", recordedSamples, _recordingClip.channels, _recordingClip.frequency, false);
        trimmed.SetData(data, 0);

        // Giải phóng đoạn ghi âm thô ban đầu sau khi đã cắt xong
        Destroy(_recordingClip);

        Debug.Log($"[AI] 📤 Đang gửi audio lên server: {serverUrl}/api/stt ...");
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
        if (_isBusy || _isRecording)
        {
            Debug.Log($"[AI] StartRecordForSeconds blocked: isBusy={_isBusy}, isRecording={_isRecording}");
            yield break;
        }
        Debug.Log($"[AI] 🎙️ === GHI ÂM TỰ ĐỘNG {seconds}s BẮT ĐẦU ===");
        int prevMax = maxRecordSeconds;
        maxRecordSeconds = Mathf.Max(maxRecordSeconds, seconds);
        OnStartRecordClicked();
        yield return new WaitForSeconds(seconds);
        Debug.Log($"[AI] ⏹️ === GHI ÂM TỰ ĐỘNG {seconds}s KẾT THÚC ===");
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
        SetStatus("⏳ Đang nhận dạng giọng nói...");

        byte[] wavBytes = AudioClipToWav(clip);
        string endpoint = serverUrl + "/api/stt";
        Debug.Log($"[AI] 📤 Gửi {wavBytes.Length} bytes WAV tới {endpoint}");

        WWWForm form = new WWWForm();
        form.AddBinaryData("audio", wavBytes, "recording.wav", "audio/wav");

        using (UnityWebRequest req = UnityWebRequest.Post(endpoint, form))
        {
            req.timeout = 60; // timeout 60 giây (Whisper STT cần ~17s trên máy này)
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                string errorDetail = $"STT Error: {req.error} (Code: {req.responseCode})";
                SetStatus("❌ " + errorDetail);
                Debug.LogError($"[AI] ❌ STT THẤT BẠI: {errorDetail}");
                Debug.LogError($"[AI] Kiểm tra: Server {serverUrl} có đang chạy không? Đúng IP/Port không?");
                _isBusy = false;
                yield break;
            }

            string json = req.downloadHandler.text;
            Debug.Log("[AI] ✅ STT Response: " + json);
            SttResponse sttResp = JsonUtility.FromJson<SttResponse>(json);
            string recognizedText = sttResp?.text?.Trim();

            if (string.IsNullOrEmpty(recognizedText))
            {
                SetStatus("⚠️ Không nhận dạng được giọng nói. Thử lại.");
                Debug.LogWarning("[AI] ⚠️ STT trả về text rỗng!");
                _isBusy = false;
                yield break;
            }

            if (transcriptLabel) transcriptLabel.text = "🗣 You: " + recognizedText;
            SetStatus("✅ Nhận dạng: " + recognizedText);
            Debug.Log("[AI] 🗣 Recognized: " + recognizedText);

            // Tiếp tục gửi lên AI
            Debug.Log($"[AI] 🤖 Đang gửi cho AI Gemini xử lý: {serverUrl}/api/chat_voice");
            yield return ChatVoiceCoroutine(recognizedText);
        }
    }

    /// <summary>Bước 2: Gửi text lên /api/chat_voice → nhận WAV → phát</summary>
    private IEnumerator ChatVoiceCoroutine(string message)
    {
        _isBusy = true;
        SetStatus("🤖 AI đang suy nghĩ... Vui lòng chờ");

        string endpoint = serverUrl + "/api/chat_voice";
        string jsonBody = JsonUtility.ToJson(new ChatPayload 
        { 
            message = message,
            job_title = jobTitle,
            interview_type = interviewType,
            language = language
        });
        byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
        Debug.Log($"[AI] 🤖 Gửi tới {endpoint}: {jsonBody}");

        using (UnityWebRequest req = new UnityWebRequest(endpoint, "POST"))
        {
            req.uploadHandler   = new UploadHandlerRaw(bodyBytes);
            req.downloadHandler = new DownloadHandlerAudioClip(endpoint, AudioType.WAV);
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = 120; // Timeout 120 giây (STT + Gemini + TTS cần nhiều thời gian)

            Debug.Log("[AI] ⏳ Đang chờ AI phản hồi (timeout 120s)...");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                string errorDetail = $"Chat Error: {req.error} (Code: {req.responseCode})";
                SetStatus("❌ " + errorDetail);
                Debug.LogError($"[AI] ❌ CHAT_VOICE THẤT BẠI: {errorDetail}");
                Debug.LogError($"[AI] Kiểm tra server log để biết chi tiết.");
                _isBusy = false;
                yield break;
            }

            Debug.Log($"[AI] ✅ Nhận được phản hồi! Content-Length: {req.downloadHandler.data?.Length ?? 0} bytes");

            AudioClip aiClip = DownloadHandlerAudioClip.GetContent(req);
            if (aiClip != null)
            {
                if (audioSource.isPlaying) audioSource.Stop();

                // TIÊU DIỆT rác bộ nhớ trước khi gán clip mới
                if (audioSource.clip != null) 
                {
                    Destroy(audioSource.clip);
                }

                audioSource.clip = aiClip;
                audioSource.Play();
                SetStatus("🔊 AI is answering...");
                Debug.Log("[AI] Playing response audio.");

                // Lấy nội dung chữ mà AI vừa nói từ Header để hiện lên Transcript UI
                string aiText = req.GetResponseHeader("X-Transcript");
                if (!string.IsNullOrEmpty(aiText))
                {
                    aiText = System.Uri.UnescapeDataString(aiText);
                    if (transcriptLabel)
                    {
                        transcriptLabel.text += "\n\n🤖 AI: " + aiText;
                    }
                }
            }
            else
            {
                SetStatus("Error: Could not read audio from server.");
            }
        }

        _isBusy = false;
    }

    /// <summary>Gọi API sinh kịch bản VR JSON (không sinh audio)</summary>
    private IEnumerator GenerateVRScriptCoroutine()
    {
        _isBusy = true;
        SetStatus("⏳ Generating VR script...");

        string endpoint = serverUrl + "/api/chat";
        string jsonBody = JsonUtility.ToJson(new ScriptRequestPayload {
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

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                SetStatus("Connection Error: " + req.error);
                Debug.LogError("[AI] Script error: " + req.error);
                _isBusy = false;
                yield break;
            }

            string responseJson = req.downloadHandler.text;
            Debug.Log("[AI] VR Script Raw JSON:\n" + responseJson);
            
            // Nếu bạn cần parse JSON thành C# Object, hãy tạo các class tương ứng (như VrScriptResponse)
            // và gọi: var scriptObj = JsonUtility.FromJson<VrScriptResponse>(responseJson);
            
            SetStatus("✅ Script generated! (Check Console)");
        }

        _isBusy = false;
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
        SetStatus("⏳ Generating voice (TTS)...");

        string endpoint = serverUrl + "/api/tts";
        string jsonBody = JsonUtility.ToJson(new TtsPayload { text = text, language = this.language });
        byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);

        using (UnityWebRequest req = new UnityWebRequest(endpoint, "POST"))
        {
            req.uploadHandler   = new UploadHandlerRaw(bodyBytes);
            req.downloadHandler = new DownloadHandlerAudioClip(endpoint, AudioType.WAV);
            req.SetRequestHeader("Content-Type", "application/json");

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                SetStatus("TTS Error: " + req.error);
                Debug.LogError("[AI] TTS error: " + req.error);
                _isBusy = false;
                yield break;
            }

            AudioClip aiClip = DownloadHandlerAudioClip.GetContent(req);
            if (aiClip != null)
            {
                if (audioSource.isPlaying) audioSource.Stop();

                // TIÊU DIỆT rác bộ nhớ trước khi gán clip mới
                if (audioSource.clip != null) 
                {
                    Destroy(audioSource.clip);
                }

                audioSource.clip = aiClip;
                audioSource.Play();
                SetStatus("🔊 Reading text...");
                Debug.Log("[AI] Playing TTS audio.");
            }
            else
            {
                SetStatus("Error: Could not read audio from server.");
            }
        }

        _isBusy = false;
    }

    // ─────────────────────────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────────────────────────

    private void SetStatus(string msg)
    {
        if (statusLabel) statusLabel.text = msg;
        Debug.Log("[AI] " + msg);
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

    private static byte[] AudioClipToWav(AudioClip clip)
    {
        float[] samples = new float[clip.samples * clip.channels];
        clip.GetData(samples, 0);

        short[] intData = new short[samples.Length];
        byte[] bytesData = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            intData[i] = (short)(samples[i] * 32767f);
            byte[] byteArr = System.BitConverter.GetBytes(intData[i]);
            bytesData[i * 2]     = byteArr[0];
            bytesData[i * 2 + 1] = byteArr[1];
        }

        using (MemoryStream stream = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            int hz        = clip.frequency;
            int channels  = clip.channels;
            int dataSize  = bytesData.Length;

            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataSize);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);          // chunk size
            writer.Write((short)1);    // PCM
            writer.Write((short)channels);
            writer.Write(hz);
            writer.Write(hz * channels * 2);
            writer.Write((short)(channels * 2));
            writer.Write((short)16);   // bits per sample
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataSize);
            writer.Write(bytesData);

            return stream.ToArray();
        }
    }

    // ─────────────────────────────────────────────────────────────
    // JSON Models
    // ─────────────────────────────────────────────────────────────
    [System.Serializable] private class ChatPayload  { 
        public string message; 
        public string job_title;
        public string interview_type;
        public string language;
    }
    [System.Serializable] private class SttResponse  { public string text; }
    [System.Serializable] private class TtsPayload   { 
        public string text; 
        public string language;
    }
    [System.Serializable] private class ScriptRequestPayload { 
        public string message; 
        public string job_title; 
        public string interview_type; 
        public string language; 
    }
}
