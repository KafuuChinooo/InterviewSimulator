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

    // Hàm dùng cho nút NEXT (Cũ)
    public void NextScreen()
    {
        if (currentScreenIndex < screens.Length - 1)
        {
            if (screens[currentScreenIndex] != null)
                screens[currentScreenIndex].SetActive(false);

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
            if (screens[currentScreenIndex] != null)
                screens[currentScreenIndex].SetActive(false);

            currentScreenIndex--;

            if (screens[currentScreenIndex] != null)
                screens[currentScreenIndex].SetActive(true);
        }
    }

    // Hàm an toàn để đảm bảo ẩn / hiện đúng màn hình khi bắt đầu hoặc được gọi từ đâu đó
    private void UpdateScreenVisibility()
    {
        for (int i = 0; i < screens.Length; i++)
        {
            if (screens[i] != null)
            {
                screens[i].SetActive(i == currentScreenIndex);
            }
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
        // Cách 1: Tắt dựa trên danh sách có sẵn (Phòng hờ rủi ro)
        if (screens != null)
        {
            foreach (var s in screens)
            {
                if (s != null && s.activeSelf) 
                    s.SetActive(false);
            }
        }

        // Cách 2: Tìm gốc của màn hình (Giao_dien) và tắt hết các nhánh anh/em của nó
        // Việc này ngăn màn hình bị chồng chéo nếu anh quên thêm màn hình mới vào mảng 'screens'.
        if (screenToOpen != null && screenToOpen.transform.parent != null)
        {
            Transform parentObj = screenToOpen.transform.parent;
            for (int i = 0; i < parentObj.childCount; i++)
            {
                GameObject child = parentObj.GetChild(i).gameObject;
                // Chỉ tắt nếu nó đang bật (tối ưu hóa)
                if (child.activeSelf)
                {
                    child.SetActive(false);
                }
            }
            
            // Cuối cùng mới bật màn hình được chỉ định lên
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
