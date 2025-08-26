using UnityEngine;
using Unity.Cinemachine;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Managing;
using FishNet.Connection;

public class PlaneController : NetworkBehaviour
{
    public enum ControlScheme { Mouse, WASD, AI }

    [Header("Managers (auto if null)")]
    public NetworkManager networkManager;

    [Header("Control Settings")]
    public ControlScheme controlScheme = ControlScheme.Mouse;

    [Header("Flight Settings")]
    public float baseSpeed = 30f, maxSpeed = 60f, minSpeed = 10f, acceleration = 20f;

    [Header("Rotation Settings")]
    public float pitchSpeed = 90f, rollSpeed = 90f, rotationSmoothing = 5f, wasdSensitivityFactor = 0.5f;

    [Header("Combat Settings")]
    public NetworkObject projectilePrefab; // registered in NetworkManager->Prefabs
    public List<Transform> projectileSpawnPoints = new();
    public float projectileSpeed = 500f;
    public float fireRate = 0.1f;

    [Header("Engine Particles")]
    public List<ParticleSystem> engineParticles = new();
    public float zVelBoost = 2f, zVelCruise = 1f, zVelBrake = 0.5f;

    [Header("Camera (Cinemachine v3)")]
    public CinemachineCamera vcam;
    public float boostFov = 70f, normalFov = 60f, fovLerpSpeed = 5f, shakeAmp = 2f, shakeFreq = 3f;

    [Header("AI Settings")]
    public Transform player;
    public float detectionRange = 200f, fireRange = 150f, aimThreshold = 0.95f, aiFireCooldown = 0.5f;

    [Header("UI (auto if null)")]
    public TargetingUI targetingUI;

    float fireCooldown, currentSpeed, aiFireTimer;
    Rigidbody rb;
    Camera fallbackCam;
    CinemachineBasicMultiChannelPerlin noise;
    Collider myCol;

    void Awake()
    {
        if (!networkManager) networkManager = FindFirstObjectByType<NetworkManager>();
        rb = GetComponent<Rigidbody>();
        myCol = GetComponent<Collider>();
        if (rb) rb.freezeRotation = true;
        currentSpeed = baseSpeed;
    }

    public void OwnerLocalSetup()
    {
        var vcam = FindFirstObjectByType<CinemachineCamera>();
        if (vcam)
        {
            var tgt = vcam.Target;
            tgt.TrackingTarget = transform;
            tgt.LookAtTarget = transform;
            vcam.Target = tgt;
        }

        var ui = FindFirstObjectByType<TargetingUI>();
        if (ui)
        {
            ui.plane = transform;
            ui.enemy = FindNearestEnemy();
            ui.mainCamera = Camera.main;
        }
    }

    Transform FindNearestEnemy()
    {
        var all = FindObjectsByType<PlaneController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Transform best = null;
        float bestD2 = float.MaxValue;

        foreach (var pc in all)
        {
            if (!pc || pc == this) continue;
            bool isEnemy = !pc.IsOwner || pc.controlScheme == ControlScheme.AI;
            if (!isEnemy) continue;

            float d2 = (pc.transform.position - transform.position).sqrMagnitude;
            if (d2 < bestD2) { bestD2 = d2; best = pc.transform; }
        }
        return best;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (IsOwner && controlScheme != ControlScheme.AI)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            if (!vcam) vcam = FindFirstObjectByType<CinemachineCamera>();
            if (vcam)
            {
                noise = vcam.GetComponent<CinemachineBasicMultiChannelPerlin>();
                var tgt = vcam.Target;
                tgt.TrackingTarget = transform;
                tgt.LookAtTarget = transform;
                vcam.Target = tgt;
            }

            if (!targetingUI) targetingUI = FindFirstObjectByType<TargetingUI>();
            WireTargetingUI();
        }

        if (!player && controlScheme == ControlScheme.AI)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p) player = p.transform;
        }
    }

    void FixedUpdate()
    {
        if (controlScheme == ControlScheme.AI)
        {
            if (!IsServerInitialized) return;
            aiControls();
            rb.linearVelocity = transform.forward * currentSpeed;
            return;
        }

        if (!IsOwner) return;

        flightControls();
        throttle();
        shootingLocal();
        updateEngineParticlesLocal();
        updateCameraEffectsLocal();
        MaintainTargetingEnemy();

        rb.linearVelocity = transform.forward * currentSpeed;
    }

    void WireTargetingUI()
    {
        if (!targetingUI) return;
        targetingUI.plane = transform;
        targetingUI.enemy = FindEnemyTransform();
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
        PlaneController[] all = FindObjectsByType<PlaneController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Transform best = null; float bestDistSq = float.MaxValue;
        foreach (var pc in all)
        {
            if (!pc || pc == this) continue;
            bool isEnemy = (!pc.IsOwner) || (pc.controlScheme == ControlScheme.AI);
            if (!isEnemy) continue;

            float d2 = (pc.transform.position - transform.position).sqrMagnitude;
            if (d2 < bestDistSq) { bestDistSq = d2; best = pc.transform; }
        }
        return best;
    }

    // ---- Flight & Input ----
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
        transform.rotation = Quaternion.Slerp(transform.rotation, transform.rotation * delta, rotationSmoothing * Time.deltaTime);
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

    // ---- Shooting (server authoritative) ----
    void shootingLocal()
    {
        fireCooldown -= Time.deltaTime;
        if (Input.GetKey(KeyCode.Space) && fireCooldown <= 0f)
        {
            fireCooldown = fireRate;

            foreach (var spawn in projectileSpawnPoints)
            {
                if (!spawn) continue;

                if (IsServerInitialized)
                {
                    ServerFire(spawn.position, spawn.forward);
                }
                else
                {
                    FireServerRpc(spawn.position, spawn.rotation * Vector3.forward);
                }
            }
        }
    }

    [ServerRpc]
    void FireServerRpc(Vector3 pos, Vector3 forward)
    {
        ServerFire(pos, forward);
    }

    [Server]
    void ServerFire(Vector3 pos, Vector3 forward)
    {
        if (!projectilePrefab)
        {
            Debug.LogWarning("[Shoot] ServerFire aborted: assign projectilePrefab");
            return;
        }

        var proj = Instantiate(projectilePrefab, pos, Quaternion.LookRotation(forward));

        var projCol = proj.GetComponent<Collider>();
        if (projCol && myCol) Physics.IgnoreCollision(projCol, myCol);

        if (!networkManager) networkManager = FindFirstObjectByType<NetworkManager>();
        networkManager.ServerManager.Spawn(proj);
        if (Owner != null) proj.GiveOwnership(Owner);

        var rbProj = proj.GetComponent<Rigidbody>();
        if (rbProj)
        {
            var shooterVel = rb ? rb.linearVelocity : Vector3.zero;
            rbProj.linearVelocity = forward * projectileSpeed + shooterVel;
            rbProj.useGravity = false;
            rbProj.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }
    }

    // ---- AI ----
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
            //ServerFire();
        }
    }

    // ---- Cosmetics (local only) ----
    void updateEngineParticlesLocal()
    {
        float zVel = zVelCruise;
        if (Input.GetKey(KeyCode.LeftShift)) zVel = zVelBoost;
        else if (Input.GetKey(KeyCode.LeftControl)) zVel = zVelBrake;

        foreach (var ps in engineParticles)
        {
            if (!ps) continue;
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.z = zVel;
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
