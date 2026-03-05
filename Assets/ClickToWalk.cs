using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ClickToWalk : MonoBehaviour
{
    public float speed = 3.0F;
    private bool moveForward;
    private Transform vrCamera;
    private Rigidbody rigid;

    void Start()
    {
        rigid = GetComponent<Rigidbody>();
        vrCamera = Camera.main.transform;
    }

    void Update()
    {
        if (Input.GetButtonDown("Fire1"))
        {
            moveForward = !moveForward;
        }
    }

    void FixedUpdate() // Dùng FixedUpdate để làm việc với Rigidbody
    {
        if (moveForward)
        {
            Vector3 forward = vrCamera.TransformDirection(Vector3.forward);
            forward.y = 0; // Tránh di chuyển theo trục Y (bay lên trời)
            forward.Normalize();
            rigid.MovePosition(rigid.position + forward * speed * Time.fixedDeltaTime);
        }
    }
}
