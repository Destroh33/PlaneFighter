using UnityEngine;

public class TargetingUI : MonoBehaviour
{
    [Header("References")]
    public Transform enemy;
    public Transform plane;
    public Camera mainCamera;

    [Header("UI Elements")]
    public RectTransform arrowUI;
    public RectTransform reticleUI;
    public float edgeBuffer = 50f;

    void Update()
    {
        if (mainCamera == null || plane == null)
        {
            arrowUI.gameObject.SetActive(false);
            return;
        }

        updateEnemyArrow();
        updateForwardReticle();
    }

    void updateEnemyArrow()
    {
        if (enemy == null || arrowUI == null) return;

        Vector3 viewportPos = mainCamera.WorldToViewportPoint(enemy.position);


        bool onScreen = (viewportPos.z > 0 &&
                         viewportPos.x > 0 && viewportPos.x < 1 &&
                         viewportPos.y > 0 && viewportPos.y < 1);


        arrowUI.gameObject.SetActive(!onScreen);
        if (onScreen) return;


        Vector3 screenPos = mainCamera.WorldToScreenPoint(enemy.position);
        if (screenPos.z < 0) screenPos *= -1;


        float clampedX = Mathf.Clamp(screenPos.x, edgeBuffer, Screen.width - edgeBuffer);
        float clampedY = Mathf.Clamp(screenPos.y, edgeBuffer, Screen.height - edgeBuffer);

        Vector3 clampedPos = new Vector3(clampedX, clampedY, 0f);
        arrowUI.position = clampedPos;


        Vector3 screenCenter = new Vector3(Screen.width / 2f, Screen.height / 2f, 0f);
        Vector3 dir = (clampedPos - screenCenter).normalized;

        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        arrowUI.rotation = Quaternion.Euler(0, 0, angle - 90f);
    }

    void updateForwardReticle()
    {
        if (reticleUI == null) return;

        Vector3 forwardPoint = plane.position + plane.forward * 1000f;
        Vector3 screenPos = mainCamera.WorldToScreenPoint(forwardPoint);

        if (screenPos.z < 0)
        {
            reticleUI.gameObject.SetActive(false);
            return;
        }

        reticleUI.gameObject.SetActive(true);
        reticleUI.position = new Vector3(screenPos.x, screenPos.y, 0f);

        float rollAngle = plane.localEulerAngles.z;

        if (rollAngle > 180f) rollAngle -= 360f;

        reticleUI.rotation = Quaternion.Euler(0, 0, rollAngle);
    }
}