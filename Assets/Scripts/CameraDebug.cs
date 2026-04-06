using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// Script debug cho editor: xoay camera bằng chuột phải và giả lập gaze click ở tâm màn hình.
/// </summary>
public class CameraDebug : MonoBehaviour
{
    [Header("Camera Rotation")]
    public float sensitivity = 2f;
    private float pitch = 0f;
    private float yaw = 0f;

    [Header("Gaze Interaction")]
    [Tooltip("Thời gian nhìn vào nút để thực hiện nhấn (tính bằng giây)")]
    public float gazeTime = 2f;
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

        // 2. Logic kiểm tra và nhấn nút nếu nhìn đủ 2 giây
        CheckGazeClick();
    }

    private void CheckGazeClick()
    {
        if (EventSystem.current == null) return;

        // Khởi tạo vị trí tâm màn hình để chiếu tia (Raycast)
        // Dùng ViewportToScreenPoint(0.5, 0.5) để lấy chính xác tâm điểm của camera trong chế độ VR
        // (tránh lỗi lấy nhầm dải phân cách màu đen ở giữa màn hình điện thoại)
        Vector2 screenCenter = new Vector2(Screen.width / 2f, Screen.height / 2f);
        if (Camera.main != null)
        {
            screenCenter = Camera.main.ViewportToScreenPoint(new Vector3(0.5f, 0.5f, 0f));
        }

        PointerEventData pointerData = new PointerEventData(EventSystem.current)
        {
            position = screenCenter
        };

        List<RaycastResult> raycastResults = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointerData, raycastResults);

        GameObject hitObject = null;

        // Lọc để lấy đối tượng UI nào tương tác được (Button, EventTrigger, v.v.)
        foreach (RaycastResult result in raycastResults)
        {
            // Thay vì chỉ tìm Component Button, ta tìm object nào có khả năng xử lý Click
            GameObject clickHandler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(result.gameObject);
            
            if (clickHandler != null)
            {
                hitObject = clickHandler;
                break;
            }
        }

        // Nếu không tìm thấy handler nào thông qua EventSystem (ví dụ trên thiết bị VR split-screen),
        // thử fallback với Physics.Raycast từ Camera.forward — phù hợp cho world-space UI có collider.
        if (hitObject == null)
        {
            if (Camera.main != null)
            {
                Ray physRay = new Ray(Camera.main.transform.position, Camera.main.transform.forward);
                RaycastHit physHit;
                if (Physics.Raycast(physRay, out physHit, 100f))
                {
                    var hitGo = physHit.collider.gameObject;
                    // ưu tiên Button component nếu có
                    var btn = hitGo.GetComponent<UnityEngine.UI.Button>() ?? hitGo.GetComponentInParent<UnityEngine.UI.Button>();
                    if (btn != null)
                    {
                        hitObject = btn.gameObject;
                        Debug.Log("[Gaze] Physics fallback hit Button: " + hitObject.name);
                    }
                    else
                    {
                        var clickHandler2 = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hitGo);
                        if (clickHandler2 != null)
                        {
                            hitObject = clickHandler2;
                            Debug.Log("[Gaze] Physics fallback found click handler: " + hitObject.name);
                        }
                    }
                }
            }
        }

        // Xử lý khi vòng xoáy ánh mắt nhìn đúng 1 nút
        if (hitObject != null)
        {
            Debug.Log("[Gaze] Hit object for click: " + hitObject.name);
            if (hitObject == currentTarget)
            {
                // Tích lũy thời gian nếu vẫn nhìn vào nút đó
                gazeTimer += Time.deltaTime;

                if (gazeTimer >= gazeTime)
                {
                    // Đã nhìn đủ 2 giây -> Thực hiện lệnh nhấn (Click)
                    pointerData.button = PointerEventData.InputButton.Left;

                    // Nếu target (hoặc cha của nó) có component Button, invoke trực tiếp để đảm bảo OnClick chạy
                    var uiButton = currentTarget.GetComponent<UnityEngine.UI.Button>();
                    if (uiButton == null) uiButton = currentTarget.GetComponentInParent<UnityEngine.UI.Button>();

                    if (uiButton != null)
                    {
                        try
                        {
                            uiButton.onClick.Invoke();
                            Debug.Log("[Gaze] Invoked Button.onClick on: " + uiButton.gameObject.name);
                        }
                        catch (System.Exception ex)
                        {
                            Debug.LogWarning("[Gaze] Button.onClick invoke failed: " + ex.Message);
                            ExecuteEvents.Execute(currentTarget, pointerData, ExecuteEvents.pointerClickHandler);
                        }
                    }
                    else
                    {
                        ExecuteEvents.Execute(currentTarget, pointerData, ExecuteEvents.pointerClickHandler);
                        Debug.Log("[Gaze] Executed pointerClick on: " + (currentTarget ? currentTarget.name : "null"));
                    }

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
                Debug.Log("[Gaze] New gaze target: " + (currentTarget ? currentTarget.name : "null"));
                gazeTimer = 0f;
                // Bắn event báo hiệu trỏ chuột đang nằm trên nút mới
                ExecuteEvents.Execute(currentTarget, pointerData, ExecuteEvents.pointerEnterHandler);
            }
        }
        else
        {
            if (raycastResults.Count > 0)
            {
                // Nếu có kết quả raycast nhưng không tìm thấy click handler, log danh sách để debug
                string list = "";
                int count = 0;
                foreach (var r in raycastResults)
                {
                    if (count++ > 10) break;
                    list += r.gameObject.name + ", ";
                }
                Debug.Log("[Gaze] Raycast hits but no clickable handler found. Results (top..): " + list);
            }

            // Khi không nhìn vào nút nào cả thì reset
            if (currentTarget != null)
            {
                ExecuteEvents.Execute(currentTarget, pointerData, ExecuteEvents.pointerExitHandler);
                currentTarget = null;
            }
            gazeTimer = 0f;
        }
    }

    // /\_/\\
    // ( o.o )  [ kafuu ]
    //  > ^ <
}
