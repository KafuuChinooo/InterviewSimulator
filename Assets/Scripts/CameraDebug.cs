using UnityEngine;

public class CameraDebug : MonoBehaviour
{
    public float sensitivity = 2f;
    private float pitch = 0f;
    private float yaw = 0f;

    void Update()
    {
        // Giữ chuột phải và di chuyển để xoay góc nhìn camera
        if (Input.GetMouseButton(1)) 
        {
            yaw += sensitivity * Input.GetAxis("Mouse X");
            pitch -= sensitivity * Input.GetAxis("Mouse Y");
            transform.eulerAngles = new Vector3(pitch, yaw, 0f);
        }
    }
}