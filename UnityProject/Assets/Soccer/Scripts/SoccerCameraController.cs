using UnityEngine;

namespace RoboStreetSoccer
{
    [RequireComponent(typeof(Camera))]
    public sealed class SoccerCameraController : MonoBehaviour
    {
        static readonly Vector3 Pivot = new Vector3(0f, .5f, 0f);
        const float DefaultHorizontalDistance = 25.5f;
        const float DefaultHeight = 22f;
        const float NearAimOffset = 4f;
        const float DefaultFieldOfView = 62f;

        Camera viewCamera;
        float yaw;
        float pitch;
        float distance;

        void Awake()
        {
            viewCamera = GetComponent<Camera>();
            ResetView();
        }

        void Update()
        {
            bool changed = false;
            if (Input.GetMouseButton(1))
            {
                yaw += Input.GetAxisRaw("Mouse X") * 3.5f;
                pitch = Mathf.Clamp(pitch - Input.GetAxisRaw("Mouse Y") * 2.5f, 10f, 85f);
                changed = true;
            }

            float keyboardYaw = 0f;
            if (Input.GetKey(KeyCode.Q)) keyboardYaw -= 55f;
            if (Input.GetKey(KeyCode.E)) keyboardYaw += 55f;
            if (Mathf.Abs(keyboardYaw) > .01f)
            {
                yaw += keyboardYaw * Time.unscaledDeltaTime;
                changed = true;
            }

            float zoom = Input.mouseScrollDelta.y;
            if (Input.GetKey(KeyCode.Equals) || Input.GetKey(KeyCode.KeypadPlus)) zoom += 7f * Time.unscaledDeltaTime;
            if (Input.GetKey(KeyCode.Minus) || Input.GetKey(KeyCode.KeypadMinus)) zoom -= 7f * Time.unscaledDeltaTime;
            if (Mathf.Abs(zoom) > .001f)
            {
                distance = Mathf.Clamp(distance - zoom * 2.6f, 18f, 55f);
                changed = true;
            }

            if (Input.GetKeyDown(KeyCode.Home))
            {
                ResetView();
                return;
            }

            if (changed) ApplyView();
        }

        void ResetView()
        {
            yaw = 0f;
            pitch = Mathf.Atan2(DefaultHeight, DefaultHorizontalDistance) * Mathf.Rad2Deg;
            distance = Mathf.Sqrt(DefaultHorizontalDistance * DefaultHorizontalDistance + DefaultHeight * DefaultHeight);
            if (!viewCamera) viewCamera = GetComponent<Camera>();
            viewCamera.fieldOfView = DefaultFieldOfView;
            ApplyView();
        }

        void ApplyView()
        {
            Vector3 radialDirection = Quaternion.Euler(0f, yaw, 0f) * Vector3.back;
            float radians = pitch * Mathf.Deg2Rad;
            float horizontalDistance = Mathf.Cos(radians) * distance;
            float height = Mathf.Sin(radians) * distance;
            transform.position = Pivot + radialDirection * horizontalDistance + Vector3.up * height;
            transform.LookAt(Pivot + radialDirection * NearAimOffset);
        }
    }
}
