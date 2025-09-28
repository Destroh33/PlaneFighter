using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.UIElements;
using Unity.VisualScripting;

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

    [Header("Health UI (Scale-X)")]
    public RectTransform healthBarPanel;
    public TMP_Text healthTMP;
    public float maxHealthScaleX = 5f;

    [Header("Health Source")]
    public ShipHealthNet shipHealth;

    private float _healthScaleY = 1f;
    private float _healthScaleZ = 1f;

    public GameObject settingsPanel;
    public UnityEngine.UI.Slider pitchSlider;
    public UnityEngine.UI.Slider rollSlider;
    private bool settingsPanelActive = false;
    void Awake()
    {
        if (shipHealth == null && plane != null) shipHealth = plane.GetComponentInParent<ShipHealthNet>();
        if (healthBarPanel != null)
        {
            var s = healthBarPanel.localScale;
            _healthScaleY = s.y;
            _healthScaleZ = s.z;
        }
        SetHealthUIVisible(false);
    }

    void Update()
    {
        if (mainCamera == null || plane == null)
        {
            if (arrowUI) arrowUI.gameObject.SetActive(false);
            if (reticleUI) reticleUI.gameObject.SetActive(false);
            SetHealthUIVisible(false);
            return;
        }

        if (shipHealth == null && plane != null) shipHealth = plane.GetComponentInParent<ShipHealthNet>();
        if(plane!=null && pitchSlider!=null && rollSlider!=null)
        {
            plane.GetComponent<PlaneController>().pitchSense = pitchSlider.value;
            plane.GetComponent<PlaneController>().rollSense = rollSlider.value;
        }
        SetHealthUIVisible(true);
        UpdateEnemyArrow();
        UpdateForwardReticle();
        UpdateHealthUI();
    }
    public void ControlSchemeMouse()
    {
        if(plane!=null)
        {
            plane.GetComponent<PlaneController>().controlScheme = PlaneController.ControlScheme.Mouse;
        }
    }
    public void ControlSchemeWASDS()
    {
        if (plane != null)
        {
            plane.GetComponent<PlaneController>().controlScheme = PlaneController.ControlScheme.WASD;
        }
    }

    public void SwitchControlScheme()
    {
        if (plane != null)
        {
            if(plane.GetComponent<PlaneController>().controlScheme == PlaneController.ControlScheme.Mouse)
            {
                plane.GetComponent<PlaneController>().controlScheme = PlaneController.ControlScheme.WASD;
            }
            else
            {
                plane.GetComponent<PlaneController>().controlScheme = PlaneController.ControlScheme.Mouse;
            }
        }
    }
    public void SettingsPanel()
    {
        if(settingsPanelActive==false)
        {
            settingsPanel.SetActive(true);
            settingsPanelActive = true;
        }
        else
        {
            settingsPanel.SetActive(false);
            settingsPanelActive = false;
        }
    }
    void UpdateEnemyArrow()
    {
        if (enemy == null || arrowUI == null) return;

        Vector3 vp = mainCamera.WorldToViewportPoint(enemy.position);
        bool onScreen = (vp.z > 0f && vp.x > 0f && vp.x < 1f && vp.y > 0f && vp.y < 1f);

        arrowUI.gameObject.SetActive(!onScreen);
        if (onScreen) return;

        Vector3 screenPos = mainCamera.WorldToScreenPoint(enemy.position);
        Vector3 screenCenter = new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);
        if (screenPos.z < 0f) screenPos = screenCenter - (new Vector3(screenPos.x, screenPos.y, 0f) - screenCenter);

        Vector2 edgePos = GetPointOnScreenEdge(screenCenter, new Vector2(screenPos.x, screenPos.y), edgeBuffer);

        arrowUI.position = new Vector3(edgePos.x, edgePos.y, 0f);
        Vector3 dir = (arrowUI.position - screenCenter).normalized;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        arrowUI.rotation = Quaternion.Euler(0f, 0f, angle - 90f);
    }

    void UpdateForwardReticle()
    {
        if (reticleUI == null) return;

        Vector3 forwardPoint = plane.position + plane.forward * 1000f;
        Vector3 sp = mainCamera.WorldToScreenPoint(forwardPoint);

        if (sp.z < 0f)
        {
            reticleUI.gameObject.SetActive(false);
            return;
        }

        reticleUI.gameObject.SetActive(true);
        reticleUI.position = new Vector3(sp.x, sp.y, 0f);

        float roll = plane.localEulerAngles.z;
        if (roll > 180f) roll -= 360f;
        reticleUI.rotation = Quaternion.Euler(0f, 0f, roll);
    }

    void UpdateHealthUI()
    {
        if (shipHealth == null || healthBarPanel == null) return;

        float current = Mathf.Max(0f, shipHealth.CurrentHealth());
        float max = Mathf.Max(1f, shipHealth.MaxHealth());
        float pct = Mathf.Clamp01(current / max);
        float x = pct * maxHealthScaleX;

        healthBarPanel.localScale = new Vector3(x, _healthScaleY, _healthScaleZ);

        if (healthTMP != null)
            healthTMP.text = Mathf.CeilToInt(current).ToString();
    }

    static Vector2 GetPointOnScreenEdge(Vector3 screenCenter, Vector2 target, float edgeBuffer)
    {
        float minX = edgeBuffer;
        float maxX = Screen.width - edgeBuffer;
        float minY = edgeBuffer;
        float maxY = Screen.height - edgeBuffer;

        Vector2 dir = (target - (Vector2)screenCenter);
        if (dir.sqrMagnitude < 0.0001f) dir = Vector2.right;

        float tMin = float.PositiveInfinity;
        Vector2 best = (Vector2)screenCenter;

        if (!Mathf.Approximately(dir.x, 0f))
        {
            float t = (minX - screenCenter.x) / dir.x;
            if (t > 0f)
            {
                float y = screenCenter.y + t * dir.y;
                if (y >= minY && y <= maxY && t < tMin) { tMin = t; best = new Vector2(minX, y); }
            }
            t = (maxX - screenCenter.x) / dir.x;
            if (t > 0f)
            {
                float y = screenCenter.y + t * dir.y;
                if (y >= minY && y <= maxY && t < tMin) { tMin = t; best = new Vector2(maxX, y); }
            }
        }

        if (!Mathf.Approximately(dir.y, 0f))
        {
            float t = (minY - screenCenter.y) / dir.y;
            if (t > 0f)
            {
                float x = screenCenter.x + t * dir.x;
                if (x >= minX && x <= maxX && t < tMin) { tMin = t; best = new Vector2(x, minY); }
            }
            t = (maxY - screenCenter.y) / dir.y;
            if (t > 0f)
            {
                float x = screenCenter.x + t * dir.x;
                if (x >= minX && x <= maxX && t < tMin) { tMin = t; best = new Vector2(x, maxY); }
            }
        }

        best.x = Mathf.Clamp(best.x, minX, maxX);
        best.y = Mathf.Clamp(best.y, minY, maxY);
        return best;
    }

    void SetHealthUIVisible(bool visible)
    {
        if (healthBarPanel) healthBarPanel.gameObject.SetActive(visible);
        if (healthTMP) healthTMP.gameObject.SetActive(visible);
    }
}
