using UnityEngine;

public class UIManager : MonoBehaviour
{
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

    // Hàm dùng cho nút NEXT
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

    // Hàm dùng cho nút RETURN / BACK
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
}
