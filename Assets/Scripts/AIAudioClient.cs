using System;
using System.Collections;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class AIAudioClient : MonoBehaviour
{
    [Header("Server")]
    public string serverUrl = "http://127.0.0.1:8000";

    [Header("Session")]
    public string sessionId = "";

    [Header("Audio")]
    public AudioSource audioSource;
    public int maxRecordSeconds = 10;

    [Header("Text Mode UI")]
    public TMP_InputField textInput;
    public Button sendTextButton;

    [Header("Voice Mode UI")]
    public Button startRecordButton;
    public Button stopSendButton;

    [Header("Default Script UI")]
    public Button readDefaultScriptButton;
    [TextArea(3, 5)]
    public string defaultScript = "Hello, this is automatically generated text.";

    [Header("Interview Config")]
    public string jobTitle = "Data Analyst";
    public string interviewType = "Attitude Interview";
    public string language = "Vietnamese";
    public Button generateScriptButton;

    [Header("Status UI")]
    public TMP_Text statusLabel;
    public TMP_Text transcriptLabel;

    [Header("Greeting UI")]
    public TMP_Text greetingTextUI;
    [TextArea(3, 5)]
    public string englishGreeting = "Hi there, welcome to VirtuHire! My name is Phuong Hang. I will be your interviewer today.";
    [TextArea(3, 5)]
    public string vietnameseGreeting = "Chao ban, chao mung den voi VirtuHire! Toi se la nguoi phong van ban hom nay.";

    private AudioClip recordingClip;
    private bool isRecording;
    private bool isBusy;
    private const float AdminPollIntervalSeconds = 2f;

    private void Start()
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            sessionId = $"unity-{SystemInfo.deviceUniqueIdentifier}-{Guid.NewGuid():N}";
        }

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }
        }

        if (sendTextButton) sendTextButton.onClick.AddListener(OnSendTextClicked);
        if (startRecordButton) startRecordButton.onClick.AddListener(OnStartRecordClicked);
        if (stopSendButton) stopSendButton.onClick.AddListener(OnStopAndSendClicked);
        if (readDefaultScriptButton) readDefaultScriptButton.onClick.AddListener(OnReadDefaultScriptClicked);
        if (generateScriptButton) generateScriptButton.onClick.AddListener(OnGenerateScriptClicked);

        SetStopButtonInteractable(false);
        SetStatus($"Ready. Session: {sessionId}");
        StartCoroutine(PollAdminMessagesCoroutine());
    }

    public void OnSendTextClicked()
    {
        if (isBusy) return;
        string message = textInput ? textInput.text.Trim() : string.Empty;
        if (string.IsNullOrEmpty(message))
        {
            SetStatus("Please enter a question.");
            return;
        }

        StartCoroutine(ChatVoiceCoroutine(message));
    }

    public void OnReadDefaultScriptClicked()
    {
        if (isBusy) return;
        if (string.IsNullOrWhiteSpace(defaultScript))
        {
            SetStatus("Default script is empty.");
            return;
        }

        StartCoroutine(ReadTextCoroutine(defaultScript));
    }

    public void OnGenerateScriptClicked()
    {
        if (!isBusy)
        {
            StartCoroutine(GenerateVRScriptCoroutine());
        }
    }

    public void SetLanguageVietnamese() => language = "Vietnamese";
    public void SetLanguageEnglish() => language = "English";
    public void SetTypeAttitude() => interviewType = "Attitude Interview";
    public void SetTypeRoleSpecific() => interviewType = "Role-Specific Interview";

    public void PlayGreeting()
    {
        string textToPlay = language == "English" ? englishGreeting : vietnameseGreeting;
        if (greetingTextUI) greetingTextUI.text = textToPlay;
        SpeakText(textToPlay);
    }

    public void OnStartRecordClicked()
    {
        if (isBusy || isRecording) return;

        if (Microphone.devices.Length == 0)
        {
            SetStatus("Error: No microphone found.");
            return;
        }

        recordingClip = Microphone.Start(null, false, maxRecordSeconds, 16000);
        isRecording = true;
        SetStartButtonInteractable(false);
        SetStopButtonInteractable(true);
        SetStatus("Recording...");
    }

    public void OnStopAndSendClicked()
    {
        if (!isRecording) return;

        int recordedSamples = Microphone.GetPosition(null);
        Microphone.End(null);
        isRecording = false;
        SetStartButtonInteractable(true);
        SetStopButtonInteractable(false);

        if (recordingClip == null || recordedSamples < 100)
        {
            SetStatus("Recording too short. Try again.");
            return;
        }

        float[] data = new float[recordedSamples * recordingClip.channels];
        recordingClip.GetData(data, 0);
        AudioClip trimmed = AudioClip.Create("rec", recordedSamples, recordingClip.channels, recordingClip.frequency, false);
        trimmed.SetData(data, 0);
        Destroy(recordingClip);
        StartCoroutine(SttThenChatCoroutine(trimmed));
    }

    private IEnumerator SttThenChatCoroutine(AudioClip clip)
    {
        isBusy = true;
        SetStatus("Recognizing speech...");

        byte[] wavBytes = AudioClipToWav(clip);
        WWWForm form = new WWWForm();
        form.AddBinaryData("audio", wavBytes, "recording.wav", "audio/wav");
        form.AddField("session_id", sessionId);
        form.AddField("job_title", jobTitle);
        form.AddField("interview_type", interviewType);
        form.AddField("language", language);

        using (UnityWebRequest req = UnityWebRequest.Post(serverUrl + "/api/stt", form))
        {
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                SttErrorResponse errorResponse = TryParseJson<SttErrorResponse>(req.downloadHandler.text);
                if (errorResponse != null && errorResponse.need_admin)
                {
                    SetStatus("STT failed. Waiting for admin input from dashboard.");
                }
                else
                {
                    SetStatus("STT error: " + req.error);
                }
                isBusy = false;
                yield break;
            }

            SttResponse sttResponse = TryParseJson<SttResponse>(req.downloadHandler.text);
            string recognizedText = sttResponse != null ? sttResponse.text : string.Empty;
            if (string.IsNullOrWhiteSpace(recognizedText))
            {
                SetStatus("Could not recognize speech. Waiting for admin input.");
                isBusy = false;
                yield break;
            }

            if (transcriptLabel) transcriptLabel.text = "You: " + recognizedText;
            yield return ChatVoiceCoroutine(recognizedText.Trim());
        }
    }

    private IEnumerator ChatVoiceCoroutine(string message)
    {
        isBusy = true;
        SetStatus("AI is processing...");

        ChatPayload payload = new ChatPayload
        {
            session_id = sessionId,
            message = message,
            job_title = jobTitle,
            interview_type = interviewType,
            language = language,
        };

        byte[] bodyBytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload));
        using (UnityWebRequest req = new UnityWebRequest(serverUrl + "/api/chat_voice", "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(bodyBytes);
            req.downloadHandler = new DownloadHandlerAudioClip(serverUrl + "/api/chat_voice", AudioType.WAV);
            req.SetRequestHeader("Content-Type", "application/json");

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                string errorMessage = req.error;
                SttErrorResponse errorResponse = TryParseJson<SttErrorResponse>(req.downloadHandler.text);
                if (errorResponse != null && !string.IsNullOrWhiteSpace(errorResponse.error))
                {
                    errorMessage = errorResponse.error;
                }
                SetStatus("Chat error: " + errorMessage);
                isBusy = false;
                yield break;
            }

            string transcript = req.GetResponseHeader("X-Transcript");
            if (!string.IsNullOrEmpty(transcript))
            {
                string decoded = Uri.UnescapeDataString(transcript);
                if (transcriptLabel) transcriptLabel.text = "AI: " + decoded;
            }

            AudioClip aiClip = DownloadHandlerAudioClip.GetContent(req);
            if (!PlayAudio(aiClip))
            {
                SetStatus("Error: Could not play server audio.");
                isBusy = false;
                yield break;
            }

            SetStatus("AI is answering...");
        }

        isBusy = false;
    }

    private IEnumerator GenerateVRScriptCoroutine()
    {
        isBusy = true;
        SetStatus("Generating VR script...");

        ScriptRequestPayload payload = new ScriptRequestPayload
        {
            session_id = sessionId,
            message = "Generate interview script",
            job_title = jobTitle,
            interview_type = interviewType,
            language = language,
        };

        byte[] bodyBytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload));
        using (UnityWebRequest req = new UnityWebRequest(serverUrl + "/api/chat", "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(bodyBytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                SetStatus("Script error: " + req.error);
                isBusy = false;
                yield break;
            }

            Debug.Log("[AI] VR script: " + req.downloadHandler.text);
            SetStatus("Script generated. Check console.");
        }

        isBusy = false;
    }

    public void SpeakText(string text)
    {
        if (!isBusy)
        {
            StartCoroutine(ReadTextCoroutine(text));
        }
    }

    private IEnumerator ReadTextCoroutine(string text)
    {
        isBusy = true;
        SetStatus("Generating voice...");

        TtsPayload payload = new TtsPayload
        {
            session_id = sessionId,
            text = text,
            language = language,
        };

        byte[] bodyBytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload));
        using (UnityWebRequest req = new UnityWebRequest(serverUrl + "/api/tts", "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(bodyBytes);
            req.downloadHandler = new DownloadHandlerAudioClip(serverUrl + "/api/tts", AudioType.WAV);
            req.SetRequestHeader("Content-Type", "application/json");

            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                SetStatus("TTS error: " + req.error);
                isBusy = false;
                yield break;
            }

            AudioClip aiClip = DownloadHandlerAudioClip.GetContent(req);
            if (!PlayAudio(aiClip))
            {
                SetStatus("Error: Could not play TTS audio.");
                isBusy = false;
                yield break;
            }

            SetStatus("Reading text...");
        }

        isBusy = false;
    }

    private IEnumerator PollAdminMessagesCoroutine()
    {
        while (true)
        {
            string endpoint = serverUrl + "/api/poll_admin_message?session_id=" + UnityWebRequest.EscapeURL(sessionId);
            using (UnityWebRequest req = UnityWebRequest.Get(endpoint))
            {
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    PollResponse response = TryParseJson<PollResponse>(req.downloadHandler.text);
                    if (response != null && response.has_message && !string.IsNullOrWhiteSpace(response.message))
                    {
                        if (transcriptLabel) transcriptLabel.text = "Admin queued: " + response.message;
                        while (isBusy)
                        {
                            yield return null;
                        }
                        yield return ChatVoiceCoroutine(response.message.Trim());
                    }
                }
            }

            yield return new WaitForSeconds(AdminPollIntervalSeconds);
        }
    }

    private bool PlayAudio(AudioClip clip)
    {
        if (clip == null || audioSource == null)
        {
            return false;
        }

        if (audioSource.isPlaying)
        {
            audioSource.Stop();
        }

        if (audioSource.clip != null)
        {
            Destroy(audioSource.clip);
        }

        audioSource.clip = clip;
        audioSource.Play();
        return true;
    }

    private void SetStatus(string message)
    {
        if (statusLabel) statusLabel.text = message;
        Debug.Log("[AI] " + message);
    }

    private void SetStartButtonInteractable(bool value)
    {
        if (startRecordButton) startRecordButton.interactable = value;
    }

    private void SetStopButtonInteractable(bool value)
    {
        if (stopSendButton) stopSendButton.interactable = value;
    }

    private static T TryParseJson<T>(string json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonUtility.FromJson<T>(json);
        }
        catch
        {
            return null;
        }
    }

    private static byte[] AudioClipToWav(AudioClip clip)
    {
        float[] samples = new float[clip.samples * clip.channels];
        clip.GetData(samples, 0);

        byte[] bytesData = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short sample = (short)(samples[i] * 32767f);
            byte[] byteArr = BitConverter.GetBytes(sample);
            bytesData[i * 2] = byteArr[0];
            bytesData[i * 2 + 1] = byteArr[1];
        }

        using (MemoryStream stream = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            int hz = clip.frequency;
            int channels = clip.channels;
            int dataSize = bytesData.Length;

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
            writer.Write(bytesData);
            return stream.ToArray();
        }
    }

    [Serializable]
    private class ChatPayload
    {
        public string session_id;
        public string message;
        public string job_title;
        public string interview_type;
        public string language;
    }

    [Serializable]
    private class TtsPayload
    {
        public string session_id;
        public string text;
        public string language;
    }

    [Serializable]
    private class ScriptRequestPayload
    {
        public string session_id;
        public string message;
        public string job_title;
        public string interview_type;
        public string language;
    }

    [Serializable]
    private class SttResponse
    {
        public string text;
    }

    [Serializable]
    private class SttErrorResponse
    {
        public string error;
        public bool need_admin;
        public string session_id;
    }

    [Serializable]
    private class PollResponse
    {
        public bool has_message;
        public string message;
    }
}
