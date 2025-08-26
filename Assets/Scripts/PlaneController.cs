using FishNet;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

public class PlaneController : NetworkBehaviour
{
    public enum ControlScheme { Mouse, WASD, AI }

    [Header("Control Settings")]
    public ControlScheme controlScheme = ControlScheme.Mouse;

    [Header("Flight Settings")]
    public float baseSpeed = 30f, maxSpeed = 60f, minSpeed = 10f, acceleration = 20f;

    [Header("Rotation Settings")]
    public float pitchSpeed = 90f, rollSpeed = 90f, rotationSmoothing = 5f, wasdSensitivityFactor = 0.5f;

    [Header("Combat Settings")]
    public NetworkObject projectilePrefab;                      // networked projectile
    public List<Transform> projectileSpawnPoints = new();
    public float projectileSpeed = 500f;
    public float fireRate = 0.1f;

    [Header("Engine Particles")]
    public List<ParticleSystem> engineParticles = new();
    public float zVelBoost = 2f, zVelCruise = 1f, zVelBrake = 0.5f;

    [Header("Camera (Cinemachine v3)")]
    public CinemachineCamera vcam;                              // assign in inspector or auto-find
    public float boostFov = 70f, normalFov = 60f, fovLerpSpeed = 5f, shakeAmp = 2f, shakeFreq = 3f;

    [Header("AI Settings")]
    public Transform player;
    public float detectionRange = 200f, fireRange = 150f, aimThreshold = 0.95f, aiFireCooldown = 0.5f;

    [Header("UI")]
    public TargetingUI targetingUI;                             // optional; will auto-find

    float fireCooldown, currentSpeed, aiFireTimer;
    Rigidbody rb; Camera fallbackCam; CinemachineBasicMultiChannelPerlin noise; Collider myCol;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        myCol = GetComponent<Collider>();
        if (rb) rb.freezeRotation = true;
        currentSpeed = baseSpeed;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        // Only owners drive input, camera, and HUD wiring.
        if (IsOwner && controlScheme != ControlScheme.AI)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            // Ensure we have a vcam reference (CM v3)
            if (!vcam)
            {
                vcam = FindFirstObjectByType<CinemachineCamera>();
            }
            if (vcam)
            {
                noise = vcam.GetComponent<CinemachineBasicMultiChannelPerlin>();
                SetVcamTargets(transform);     // <— Track & LookAt ME
            }

            // Wire TargetingUI
            if (!targetingUI)
            {
                targetingUI = FindFirstObjectByType<TargetingUI>();
            }
            WireTargetingUI();
        }

        // AI helper
        if (!player && controlScheme == ControlScheme.AI)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p) player = p.transform;
        }
    }

    void FixedUpdate()
    {
        // AI runs on server only
        if (controlScheme == ControlScheme.AI)
        {
            if (!IsServerInitialized) return;
            aiControls();
            rb.linearVelocity = transform.forward * currentSpeed;
            return;
        }

        // Human input only by owning client
        if (!IsOwner) return;

        flightControls();
        throttle();
        shootingLocal();                 // client asks server to fire
        updateEngineParticlesLocal();
        updateCameraEffectsLocal();

        // Keep UI enemy target fresh if it got destroyed
        MaintainTargetingEnemy();

        rb.linearVelocity = transform.forward * currentSpeed;
    }

    // -------------------- Camera & UI wiring --------------------

    void SetVcamTargets(Transform t)
    {
        if (!vcam || !t) return;
        var tgt = vcam.Target;               // CM v3 target struct
        tgt.TrackingTarget = t;
        tgt.LookAtTarget = t;
        vcam.Target = tgt;
    }

    void WireTargetingUI()
    {
        if (!targetingUI) return;

        // The plane the UI belongs to (this local player)
        targetingUI.plane = transform;

        // Enemy to track (pick nearest non-owned PlaneController)
        targetingUI.enemy = FindEnemyTransform();

        // Camera reference for UI
        targetingUI.mainCamera = Camera.main;
    }

    void MaintainTargetingEnemy()
    {
        if (!targetingUI) return;
        if (targetingUI.enemy == null)
            targetingUI.enemy = FindEnemyTransform();
    }

    Transform FindEnemyTransform()
    {
#if UNITY_2023_1_OR_NEWER
        PlaneController[] all = FindObjectsByType<PlaneController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
        PlaneController[] all = FindObjectsOfType<PlaneController>();
#endif
        Transform best = null; float bestDistSq = float.MaxValue;
        foreach (var pc in all)
        {
            if (!pc || pc == this) continue;

            // Prefer non-owners (other players) or AI as enemies
            bool isEnemy = (!pc.IsOwner) || (pc.controlScheme == ControlScheme.AI);
            if (!isEnemy) continue;

            float d2 = (pc.transform.position - transform.position).sqrMagnitude;
            if (d2 < bestDistSq) { bestDistSq = d2; best = pc.transform; }
        }
        return best;
    }

    // -------------------- Flight & Input --------------------

    void flightControls()
    {
        float pitch = 0f, roll = 0f;

        if (controlScheme == ControlScheme.Mouse)
        {
            float mx = Input.GetAxis("Mouse X");
            float my = Input.GetAxis("Mouse Y");
            pitch = -my * pitchSpeed;
            roll = -mx * rollSpeed;
        }
        else if (controlScheme == ControlScheme.WASD)
        {
            if (Input.GetKey(KeyCode.W)) pitch = pitchSpeed * wasdSensitivityFactor;
            if (Input.GetKey(KeyCode.S)) pitch = -pitchSpeed * wasdSensitivityFactor;
            if (Input.GetKey(KeyCode.A)) roll = rollSpeed * wasdSensitivityFactor;
            if (Input.GetKey(KeyCode.D)) roll = -rollSpeed * wasdSensitivityFactor;
        }

        Quaternion delta = Quaternion.Euler(pitch, 0f, roll);
        transform.rotation = Quaternion.Slerp(transform.rotation, transform.rotation * delta,
                                              rotationSmoothing * Time.deltaTime);
    }

    void throttle()
    {
        if (Input.GetKey(KeyCode.LeftShift))
            currentSpeed = Mathf.MoveTowards(currentSpeed, maxSpeed, acceleration * Time.deltaTime);
        else if (Input.GetKey(KeyCode.LeftControl))
            currentSpeed = Mathf.MoveTowards(currentSpeed, minSpeed, acceleration * Time.deltaTime);
        else
            currentSpeed = Mathf.MoveTowards(currentSpeed, baseSpeed, acceleration * Time.deltaTime);
    }

    // -------------------- Shooting (server authoritative) --------------------

    void shootingLocal()
    {
        fireCooldown -= Time.deltaTime;
        if (Input.GetKey(KeyCode.Space) && fireCooldown <= 0f)
        {
            fireCooldown = fireRate;
            Debug.Log("[Shoot] Owner pressed Space → FireServerRpc()");
            FireServerRpc();
        }
    }

    [ServerRpc]
    void FireServerRpc()
    {
        ServerFire();
    }

    [Server]
    void ServerFire()
    {
        if (!projectilePrefab || projectileSpawnPoints.Count == 0)
        {
            Debug.LogWarning("[Shoot] ServerFire aborted: assign projectilePrefab & spawn points");
            return;
        }

        foreach (var spawn in projectileSpawnPoints)
        {
            if (!spawn) continue;

            var proj = Instantiate(projectilePrefab, spawn.position, Quaternion.LookRotation(transform.forward));
            var projCol = proj.GetComponent<Collider>();
            if (projCol && myCol) Physics.IgnoreCollision(projCol, myCol);

            InstanceFinder.ServerManager.Spawn(proj, Owner); // credit owner
            //Debug.Log($"[Shoot] Server spawned proj NO={proj.NetworkObjectId} for owner {(Owner != null ? Owner.ClientId : (ushort)9999)}");

            var rbProj = proj.GetComponent<Rigidbody>();
            if (rbProj) rbProj.linearVelocity = transform.forward * projectileSpeed + rb.linearVelocity;
        }
    }

    // -------------------- AI --------------------

    void aiControls()
    {
        if (!player) return;

        Vector3 toPlayer = (player.position - transform.position);
        float distance = toPlayer.magnitude;
        Vector3 dirToPlayer = toPlayer.normalized;

        Quaternion targetRot = Quaternion.LookRotation(dirToPlayer, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSmoothing * Time.deltaTime);

        currentSpeed = (distance > 100f)
            ? Mathf.MoveTowards(currentSpeed, maxSpeed, acceleration * Time.deltaTime)
            : Mathf.MoveTowards(currentSpeed, baseSpeed, acceleration * Time.deltaTime);

        float dot = Vector3.Dot(transform.forward, dirToPlayer);
        aiFireTimer -= Time.deltaTime;
        if (distance <= fireRange && dot >= aimThreshold && aiFireTimer <= 0f)
        {
            aiFireTimer = aiFireCooldown;
            ServerFire(); // server-side
        }
    }

    // -------------------- Cosmetics (local only) --------------------

    void updateEngineParticlesLocal()
    {
        float zVel = zVelCruise;
        if (Input.GetKey(KeyCode.LeftShift)) zVel = zVelBoost;
        else if (Input.GetKey(KeyCode.LeftControl)) zVel = zVelBrake;

        foreach (var ps in engineParticles)
        {
            if (!ps) continue;
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.z = zVel;
        }
    }

    void updateCameraEffectsLocal()
    {
        bool boosting = Input.GetKey(KeyCode.LeftShift);
        float targetFov = boosting ? boostFov : normalFov;

        if (vcam)
        {
            var lens = vcam.Lens;
            lens.FieldOfView = Mathf.Lerp(lens.FieldOfView, targetFov, fovLerpSpeed * Time.deltaTime);
            vcam.Lens = lens;

            if (noise != null)
            {
                noise.AmplitudeGain = boosting ? shakeAmp : 0f;
                noise.FrequencyGain = boosting ? shakeFreq : 0f;
            }
        }
        else if (fallbackCam)
        {
            fallbackCam.fieldOfView = Mathf.Lerp(fallbackCam.fieldOfView, targetFov, fovLerpSpeed * Time.deltaTime);
        }
    }
}
