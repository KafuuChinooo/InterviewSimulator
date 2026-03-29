using UnityEngine;
using UnityEngine.UI;
using TMPro;

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

    [Header("Kéo thả các màn hình (Trang) vào đây theo thứ tự (VD: Trang 1 -> Trang 5)")]
    public GameObject[] screens;

    [Header("Chỉ số màn hình bắt đầu (Mặc định là 0 - Trang 1)")]
    public int currentScreenIndex = 0;

    void Start()
    {
        // Khi game bắt đầu, hệ thống sẽ tự động cập nhật hiển thị, 
        // chỉ bật màn hình ở vị trí currentScreenIndex, còn lại tắt hết
        UpdateScreenVisibility();
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
        Debug.Log("[UIManager] Đã đổi hệ thống sang Tiếng Anh.");
    }

    public void SelectVietnamese()
    {
        currentLanguage = "Viet";
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
        }
    }

    // 3. Ghi lại Chức vụ / Vị trí đã chọn và in lên màn hình 5
    public void SelectPosition(string positionName)
    {
        selectedPosition = positionName;
        Debug.Log("[UIManager] Chức vụ đã chọn: " + selectedPosition);

        // Hiển thị vị trí lên UI ở Màn 5 nếu có 
        // (Nếu màn hiện tại không có, nó sẽ tự im lặng không lỗi)
        if (getReadyPositionTextEng != null) 
            getReadyPositionTextEng.text = "Position: " + selectedPosition;
        
        if (getReadyPositionTextViet != null) 
            getReadyPositionTextViet.text = "Vị trí: " + selectedPosition;
    }
}
