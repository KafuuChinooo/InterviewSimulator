using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Script dùng để gọi API Chat của FastAPI, lấy trực tiếp file audio về Unity.
/// </summary>
public class AIAudioClient : MonoBehaviour
{
    [Header("API Settings")]
    [Tooltip("URL của FastAPI server")]
    public string serverUrl = "http://127.0.0.1:8000";

    [Header("Audio Component")]
    [Tooltip("AudioSource để phát âm thanh. Nếu để trống sẽ tự động được thêm vào Game Object này.")]
    public AudioSource audioSource;

    [Header("Testing")]
    [Tooltip("Tin nhắn để gửi cho AI test")]
    public string testMessage = "Hello, how are you today?";

    // Serialize object để chuyển sang JSON gửi đi
    [System.Serializable]
    private class ChatRequestPayload
    {
        public string message;
        // Pydantic bên FastAPI sẽ tự động map các giá trị mặc định cho job_title và history
    }

    private void Start()
    {
        // Tự động tìm hoặc thêm AudioSource nếu chưa có
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
    }

    /// <summary>
    /// Gắn hàm này vào sự kiện OnClick() của UI Button trong Unity
    /// </summary>
    public void OnClickTestSendToAI()
    {
        if (string.IsNullOrWhiteSpace(testMessage)) return;
        
        // Ngừng audio đang chạy nếu có
        if (audioSource.isPlaying)
        {
            audioSource.Stop();
        }

        Debug.Log("[AIAudioClient] Bắt đầu gửi tin nhắn (Chat -> Voice direct): " + testMessage);
        StartCoroutine(SendChatAndPlayVoiceDirect(testMessage));
    }

    private IEnumerator SendChatAndPlayVoiceDirect(string message)
    {
        // Gọi API /api/chat_voice để vừa chat, vừa lấy audio trả về trực tiếp
        string endpoint = serverUrl + "/api/chat_voice";
        
        ChatRequestPayload chatReq = new ChatRequestPayload { message = message };
        string jsonPayload = JsonUtility.ToJson(chatReq);
        
        using (UnityWebRequest request = new UnityWebRequest(endpoint, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            
            // Dùng DownloadHandlerAudioClip để nhận kết quả là file WAV
            request.downloadHandler = new DownloadHandlerAudioClip(endpoint, AudioType.WAV);
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                // Có thể server trả về lỗi dạng JSON {"error": "..."} nhưng Unity sẽ báo lỗi HTTP.
                // Ta có thể in ra nội dung lỗi chi tiết hơn nếu có.
                Debug.LogError("[AIAudioClient] Lỗi khi gọi API chat_voice: " + request.error);
                if (request.downloadHandler != null && !string.IsNullOrEmpty(request.downloadHandler.text))
                {
                    Debug.LogError("[AIAudioClient] Chi tiết lỗi từ server: " + request.downloadHandler.text);
                }
                yield break;
            }

            // Lấy AudioClip và gán vào AudioSource để phát
            AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
            if (clip != null)
            {
                audioSource.clip = clip;
                audioSource.Play();
                Debug.Log("[AIAudioClient] Đã nhận và phát Audio thành công!");
            }
            else
            {
                Debug.LogError("[AIAudioClient] Không thể tải AudioClip, định dạng có thể không được hỗ trợ hoặc bị lỗi.");
            }
        }
    }
}
