using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Quản lý flow chuyển màn hình, lựa chọn ngôn ngữ/chức vụ và đồng bộ dữ liệu sang AIAudioClient.
/// </summary>
public class UIManager : MonoBehaviour
{
    [Header("==== Dữ liệu ngôn ngữ và vị trí ====")]
    public string currentLanguage = "Eng"; 
    public string selectedPosition = "";

    [Header("==== Tham chiếu giao diện (Kéo từ Inspector) ====")]
    [Tooltip("Kéo chữ hiển thị chức vụ ở Trang 5 của tiếng Anh vào đây")]
    public TextMeshProUGUI getReadyPositionTextEng;
    [Tooltip("Kéo chữ hiển thị chức vụ ở Trang 5 của tiếng Việt vào đây")]
    public TextMeshProUGUI getReadyPositionTextViet;
    [Tooltip("Tham chieu toi AIAudioClient de dong bo language/job title")]
    public AIAudioClient aiAudioClient;

    [Header("Kéo thả các màn hình (Trang) vào đây theo thứ tự (VD: Trang 1 -> Trang 5)")]
    public GameObject[] screens;

    [Header("Chỉ số màn hình bắt đầu (Mặc định là 0 - Trang 1)")]
    public int currentScreenIndex = 0;
    [Header("Tu dong bat dau phong van")]
    [Tooltip("Screen index se lam AI chu dong hoi cau dau tien. Screen 6 = index 5")]
    public int autoAskScreenIndex = 5;

    void Start()
    {
        if (aiAudioClient == null)
        {
            aiAudioClient = AIAudioClient.FindPreferredInstance();
        }

        // Khi game bắt đầu, hệ thống sẽ tự động cập nhật hiển thị, 
        // chỉ bật màn hình ở vị trí currentScreenIndex, còn lại tắt hết
        UpdateScreenVisibility();
        SyncAIAudioClient();
        HandleScreenEntered();
    }

    // === HÀM DỌN DẸP TUYỆT ĐỐI (KHÔNG CẦN ARRAY) ===
    private void HideAllScreens()
    {
        // 1. Tắt danh sách cũ (nếu có)
        if (screens != null)
        {
            foreach (var s in screens)
            {
                if (s != null && s.activeSelf) 
                    s.SetActive(false);
            }
        }

        // 2. Chế độ quét rác: Tìm TẤT CẢ các object bắt đầu bằng "Trang_" đang mở và TẮT HẾT.
        // Giúp miễn nhiễm với lỗi rác giao diện, sai array, hoặc kéo màn hình lung tung.
        // Quét thêm object tên "Trang_" để tránh còn sót UI active do cấu hình scene hoặc quên gắn vào mảng.
        var allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
        foreach (var obj in allObjects)
        {
            if (obj != null && obj.activeInHierarchy && obj.name.StartsWith("Trang_"))
            {
                obj.SetActive(false);
            }
        }
    }

    // Hàm dùng cho nút NEXT (Cũ)
    public void NextScreen()
    {
        if (currentScreenIndex < screens.Length - 1)
        {
            HideAllScreens();
            currentScreenIndex++;
            if (screens[currentScreenIndex] != null)
                screens[currentScreenIndex].SetActive(true);

            HandleScreenEntered();
        }
    }

    // Hàm dùng cho nút RETURN / BACK (Cũ)
    public void PreviousScreen()
    {
        if (currentScreenIndex > 0)
        {
            HideAllScreens();
            currentScreenIndex--;
            if (screens[currentScreenIndex] != null)
                screens[currentScreenIndex].SetActive(true);

            HandleScreenEntered();
        }
    }

    // Hàm an toàn để đảm bảo ẩn / hiện đúng màn hình khi bắt đầu hoặc được gọi từ đâu đó
    private void UpdateScreenVisibility()
    {
        HideAllScreens();
        if (currentScreenIndex >= 0 && currentScreenIndex < screens.Length && screens[currentScreenIndex] != null)
        {
            screens[currentScreenIndex].SetActive(true);
        }
    }

    // ============================================
    // CÁC HÀM MỚI (DÙNG ĐỂ CHUYỂN TRANG THEO NHÁNH)
    // ============================================

    // 1. Lưu Ngôn Ngữ
    public void SelectEnglish()
    {
        currentLanguage = "Eng";
        if (aiAudioClient != null) aiAudioClient.SetLanguageEnglish();
        Debug.Log("[UIManager] Đã đổi hệ thống sang Tiếng Anh.");
    }

    public void SelectVietnamese()
    {
        currentLanguage = "Viet";
        if (aiAudioClient != null) aiAudioClient.SetLanguageVietnamese();
        Debug.Log("[UIManager] Đã đổi hệ thống sang Tiếng Việt.");
    }

    // 2. Chuyển sang bất kỳ màn hình nào bất chấp thứ tự (Tự động dọn dẹp)
    public void GoToScreen(GameObject screenToOpen)
    {
        HideAllScreens();
        
        // Cuối cùng mới bật màn hình được chỉ định lên
        if (screenToOpen != null)
        {
            screenToOpen.SetActive(true);

            if (screens != null)
            {
                for (int i = 0; i < screens.Length; i++)
                {
                    if (screens[i] == screenToOpen)
                    {
                        currentScreenIndex = i;
                        break;
                    }
                }
            }

            HandleScreenEntered();
        }
    }

    // 3. Ghi lại Chức vụ / Vị trí đã chọn và in lên màn hình 5
    public void SelectPosition(string positionName)
    {
        selectedPosition = positionName;
        if (aiAudioClient != null) aiAudioClient.SetJobTitle(positionName);
        Debug.Log("[UIManager] Chức vụ đã chọn: " + selectedPosition);

        // Hiển thị vị trí lên UI ở Màn 5 nếu có 
        // (Nếu màn hiện tại không có, nó sẽ tự im lặng không lỗi)
        if (getReadyPositionTextEng != null) 
            getReadyPositionTextEng.text = "Position: " + selectedPosition;
        
        if (getReadyPositionTextViet != null) 
            getReadyPositionTextViet.text = "Vị trí: " + selectedPosition;
    }

    private void SyncAIAudioClient()
    {
        if (aiAudioClient == null) return;

        if (currentLanguage == "Viet") aiAudioClient.SetLanguageVietnamese();
        else aiAudioClient.SetLanguageEnglish();

        if (!string.IsNullOrWhiteSpace(selectedPosition))
        {
            aiAudioClient.SetJobTitle(selectedPosition);
        }
    }

    private void HandleScreenEntered()
    {
        if (aiAudioClient == null) return;
        if (currentScreenIndex != autoAskScreenIndex) return;

        // Chỉ màn hình phỏng vấn chính mới tự kích hoạt câu mở đầu từ AI.
        aiAudioClient.AskOpeningQuestion();
    }

    // /\_/\\
    // ( o.o )  [ kafuu ]
    //  > ^ <
}
