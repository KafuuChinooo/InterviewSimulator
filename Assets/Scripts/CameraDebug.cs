using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections.Generic;

public class CameraDebug : MonoBehaviour
{
    [Header("Camera Rotation")]
    public float sensitivity = 2f;
    private float pitch = 0f;
    private float yaw = 0f;

    [Header("Gaze Interaction")]
    [Tooltip("Thời gian nhìn vào nút để thực hiện nhấn (tính bằng giây)")]
    public float gazeTime = 3f;
    private float gazeTimer = 0f;
    private GameObject currentTarget;

    void Update()
    {
        // 1. Giữ chuột phải và di chuyển để xoay góc nhìn camera
        if (Input.GetMouseButton(1)) 
        {
            yaw += sensitivity * Input.GetAxis("Mouse X");
            pitch -= sensitivity * Input.GetAxis("Mouse Y");
            transform.eulerAngles = new Vector3(pitch, yaw, 0f);
        }

        // 2. Logic kiểm tra và nhấn nút nếu nhìn đủ 3 giây
        CheckGazeClick();
    }

    private void CheckGazeClick()
    {
        if (EventSystem.current == null) return;

        // Khởi tạo vị trí tâm màn hình để chiếu tia (Raycast)
        PointerEventData pointerData = new PointerEventData(EventSystem.current)
        {
            position = new Vector2(Screen.width / 2f, Screen.height / 2f)
        };

        List<RaycastResult> raycastResults = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointerData, raycastResults);

        GameObject hitObject = null;

        // Lọc để lấy đối tượng UI nào tương tác được (ví dụ Button)
        foreach (RaycastResult result in raycastResults)
        {
            Button btn = result.gameObject.GetComponentInParent<Button>();
            if (btn != null)
            {
                hitObject = btn.gameObject;
                break;
            }
        }

        // Xử lý khi vòng xoáy ánh mắt nhìn đúng 1 nút
        if (hitObject != null)
        {
            if (hitObject == currentTarget)
            {
                // Tích lũy thời gian nếu vẫn nhìn vào nút đó
                gazeTimer += Time.deltaTime;

                if (gazeTimer >= gazeTime)
                {
                    // Đã nhìn đủ 3 giây -> Thực hiện lệnh nhấn (Click)
                    ExecuteEvents.Execute(currentTarget, pointerData, ExecuteEvents.pointerClickHandler);
                    
                    // Giao tiếp qua Event Trigger nếu có
                    ExecuteEvents.Execute(currentTarget, pointerData, ExecuteEvents.submitHandler);

                    // Reset lại thời gian để không spam click liên tục
                    gazeTimer = 0f;
                }
            }
            else
            {
                // Chuyển sang nhìn một nút khác
                if (currentTarget != null)
                {
                    ExecuteEvents.Execute(currentTarget, pointerData, ExecuteEvents.pointerExitHandler);
                }
                
                currentTarget = hitObject;
                gazeTimer = 0f;
                // Bắn event báo hiệu trỏ chuột đang nằm trên nút mới
                ExecuteEvents.Execute(currentTarget, pointerData, ExecuteEvents.pointerEnterHandler);
            }
        }
        else
        {
            // Khi không nhìn vào nút nào cả thì reset
            if (currentTarget != null)
            {
                ExecuteEvents.Execute(currentTarget, pointerData, ExecuteEvents.pointerExitHandler);
                currentTarget = null;
            }
            gazeTimer = 0f;
        }
    }
}