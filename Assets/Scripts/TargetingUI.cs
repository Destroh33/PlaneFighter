using UnityEngine;

public class TargetingUI : MonoBehaviour
{
    public Transform enemy;
    public Transform plane;
    public Camera mainCamera;

    public RectTransform arrowUI;
    public RectTransform reticleUI;
    public float edgeBuffer = 50f;

    void Update()
    {
        if (mainCamera == null || plane == null)
        {
            if (arrowUI != null) arrowUI.gameObject.SetActive(false);
            if (reticleUI != null) reticleUI.gameObject.SetActive(false);
            return;
        }

        updateEnemyArrow();
        updateForwardReticle();
    }

    void updateEnemyArrow()
    {
        if (enemy == null || arrowUI == null) return;

        Vector3 vp = mainCamera.WorldToViewportPoint(enemy.position);
        bool onScreen = (vp.z > 0 && vp.x > 0 && vp.x < 1 && vp.y > 0 && vp.y < 1);

        arrowUI.gameObject.SetActive(!onScreen);
        if (onScreen) return;

        Vector3 sp = mainCamera.WorldToScreenPoint(enemy.position);
        if (sp.z < 0) sp *= -1;

        float x = Mathf.Clamp(sp.x, edgeBuffer, Screen.width - edgeBuffer);
        float y = Mathf.Clamp(sp.y, edgeBuffer, Screen.height - edgeBuffer);

        Vector3 clamped = new Vector3(x, y, 0f);
        arrowUI.position = clamped;

        Vector3 center = new Vector3(Screen.width / 2f, Screen.height / 2f, 0f);
        Vector3 dir = (clamped - center).normalized;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        arrowUI.rotation = Quaternion.Euler(0, 0, angle - 90f);
    }

    void updateForwardReticle()
    {
        if (reticleUI == null) return;

        Vector3 forwardPoint = plane.position + plane.forward * 1000f;
        Vector3 sp = mainCamera.WorldToScreenPoint(forwardPoint);

        if (sp.z < 0)
        {
            reticleUI.gameObject.SetActive(false);
            return;
        }

        reticleUI.gameObject.SetActive(true);
        reticleUI.position = new Vector3(sp.x, sp.y, 0f);

        float rollAngle = plane.localEulerAngles.z;
        if (rollAngle > 180f) rollAngle -= 360f;
        reticleUI.rotation = Quaternion.Euler(0, 0, rollAngle);
    }
}
