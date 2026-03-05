using UnityEngine;

public class FaceCamera : MonoBehaviour
{
    private Camera mainCam;

    void Start()
    {
        mainCam = Camera.main;
    }

    void LateUpdate()
    {
        if (mainCam != null)
        {
            //transform.forward = mainCam.transform.forward;

            transform.LookAt(transform.position + mainCam.transform.rotation * Vector3.forward,
                              mainCam.transform.rotation * Vector3.up);
        }
    }
}
