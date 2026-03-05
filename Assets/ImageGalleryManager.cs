using UnityEngine;
using TMPro; // Bắt buộc phải có cái này để điều khiển TextMeshPro

public class ImageGalleryManager : MonoBehaviour
{
    public TextMeshProUGUI titleText; // Kéo object Text (TMP) vào đây
    public Texture[] images;         // Danh sách các ảnh 360 của ông
    private int currentIndex = 0;

    void Start()
    {
        UpdateDisplay();
    }

    public void NextImage()
    {
        currentIndex = (currentIndex + 1) % images.Length;
        UpdateDisplay();
    }

    public void PreviousImage()
    {
        currentIndex--;
        if (currentIndex < 0) currentIndex = images.Length - 1;
        UpdateDisplay();
    }

    void UpdateDisplay()
    {
        if (images.Length > 0)
        {
            // Đổi text thành tên của file ảnh
            titleText.text = images[currentIndex].name;

            // Ở đây ông có thể thêm code để đổi texture của Sphere/Skybox luôn
            // Ví dụ: sphereMaterial.mainTexture = images[currentIndex];
        }
    }
}