using UnityEngine;
using Unity.Netcode;
using Unity.Cinemachine;
using System.Linq;

public class PlayerLocalView : NetworkBehaviour
{
    [Header("Scene refs (leave null to auto-find)")]
    public CinemachineCamera sceneVcam;
    public TargetingUI sceneTargetingUI;

    [Header("Retargeting")]
    public float retargetEverySeconds = 1.0f;

    float retargetTimer;

    void Start()
    {
        if (!IsOwner) return;

        if (sceneVcam == null)
            sceneVcam = Object.FindFirstObjectByType<CinemachineCamera>(FindObjectsInactive.Exclude);

        if (sceneVcam != null)
        {
            sceneVcam.Follow = transform;
            sceneVcam.LookAt = transform;
            sceneVcam.gameObject.SetActive(true);
        }

        if (sceneTargetingUI == null)
            sceneTargetingUI = Object.FindFirstObjectByType<TargetingUI>(FindObjectsInactive.Include);

        if (sceneTargetingUI != null)
        {
            if (sceneTargetingUI.mainCamera == null) sceneTargetingUI.mainCamera = Camera.main;
            sceneTargetingUI.plane = transform;
            SelectNearestEnemy();
        }
    }

    void Update()
    {
        if (!IsOwner || sceneTargetingUI == null) return;

        bool needRetarget = sceneTargetingUI.enemy == null ||
                            !sceneTargetingUI.enemy.gameObject.activeInHierarchy;

        retargetTimer -= Time.deltaTime;
        if (needRetarget || retargetTimer <= 0f)
        {
            SelectNearestEnemy();
            retargetTimer = retargetEverySeconds;
        }
    }

    void SelectNearestEnemy()
    {
        var myPc = GetComponent<PlaneController>();
        if (myPc == null) return;

        var candidates = Object
            .FindObjectsByType<PlaneController>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(p => p != null && p.gameObject != gameObject)
            .Select(p => p.transform);

        Transform nearest = null;
        float best = float.MaxValue;

        foreach (var t in candidates)
        {
            float d = (t.position - transform.position).sqrMagnitude;
            if (d < best) { best = d; nearest = t; }
        }

        if (sceneTargetingUI != null)
            sceneTargetingUI.enemy = nearest;
    }
}
