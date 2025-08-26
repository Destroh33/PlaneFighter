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

    [Header("Control Settings")] public ControlScheme controlScheme = ControlScheme.Mouse;

    [Header("Flight Settings")]
    public float baseSpeed = 30f, maxSpeed = 60f, minSpeed = 10f, acceleration = 20f;

    [Header("Rotation Settings")]
    public float pitchSpeed = 90f, rollSpeed = 90f, rotationSmoothing = 5f, wasdSensitivityFactor = 0.5f;

    [Header("Combat Settings")]
    public NetworkObject projectilePrefab;             // NetworkObject prefab (registered in NetworkManager->Prefabs)
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
    Rigidbody rb; Camera fallbackCam; CinemachineBasicMultiChannelPerlin noise; Collider myCol;

    [Header("Client-side local projectile ghost")]
    [SerializeField] bool spawnLocalReplicaForClient = true;
    [SerializeField] float localReplicaLifetime = 2.0f;
    [SerializeField] float localReplicaGravity = 0.0f; // 0 = straight, raise to arc
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
        // Bind Cinemachine v3 camera to THIS plane
        var vcam = FindFirstObjectByType<CinemachineCamera>();
        if (vcam)
        {
            var tgt = vcam.Target;
            tgt.TrackingTarget = transform;
            tgt.LookAtTarget = transform;
            vcam.Target = tgt;
        }

        // Wire TargetingUI to THIS plane
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
            // Treat any non-owned plane as an enemy; works for host vs clients
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
                var tgt = vcam.Target; tgt.TrackingTarget = transform; tgt.LookAtTarget = transform; vcam.Target = tgt;
            }

            if (!targetingUI) targetingUI = FindFirstObjectByType<TargetingUI>();
            WireTargetingUI();
        }

        if (!player && controlScheme == ControlScheme.AI)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p) player = p.transform;
        }

        //Debug.Log($"[Plane] OnStartClient -> IsOwner={IsOwner} IsServerInit={IsServerInitialized} NetId={NetworkObject?.NetworkObjectId}");
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

    // ---- Camera & UI wiring ----
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
                    Debug.Log("[Shoot] Host → ServerFire()");
                    ServerFire(spawn);
                }
                else
                {
                    Debug.Log("[Shoot] Client → FireServerRpc()");
                    FireServerRpc(spawn.position, spawn.rotation * Vector3.forward);

                    // 🔸 Spawn a local-only visual duplicate for this client
                    if (spawnLocalReplicaForClient)
                        SpawnLocalProjectileReplica(spawn);
                }
            }
        }
    }

    [ServerRpc]
    void FireServerRpc(Vector3 pos, Vector3 forward)
    {
        // server uses client's pose for best match
        ServerFire(pos, forward);
    }

    [Server]
    void ServerFire(Transform spawn)
    {
        ServerFire(spawn.position, spawn.forward);
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
        //proj.GetComponent<ProjectileNet>()?.ServerSetShooter(this.NetworkObject);
        var projCol = proj.GetComponent<Collider>();
        if (projCol && myCol) Physics.IgnoreCollision(projCol, myCol);

        // Spawn for everyone
        networkManager.ServerManager.Spawn(proj);
        if (Owner != null) proj.GiveOwnership(Owner);

        var rbProj = proj.GetComponent<Rigidbody>();
        if (rbProj)
        {
            // use shooter's current velocity if you want: rb.linearVelocity (plane's rb)
            var shooterVel = rb ? rb.linearVelocity : Vector3.zero;
            rbProj.linearVelocity = forward * projectileSpeed + shooterVel;
            rbProj.useGravity = false;
            rbProj.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }
    }

    // --- NEW: local-only visual duplicate for clients ---
    void SpawnLocalProjectileReplica(Transform spawn)
    {
        // Make an identical clone locally
        var clone = Instantiate(projectilePrefab.gameObject, spawn.position, spawn.rotation);

        // Disable network/logic/physics on the clone
        var projNet = clone.GetComponent<ProjectileNet>(); if (projNet) projNet.enabled = false;
        var nob = clone.GetComponent<FishNet.Object.NetworkObject>(); if (nob) nob.enabled = false;
        var rbLocal = clone.GetComponent<Rigidbody>(); if (rbLocal) { rbLocal.isKinematic = true; rbLocal.useGravity = false; }
        var col = clone.GetComponent<Collider>(); if (col) col.enabled = false;

        // Ensure its trails render immediately
        var trails = clone.GetComponentsInChildren<TrailRenderer>(true);
        foreach (var t in trails) { if (!t) continue; t.Clear(); t.emitting = true; }

        // Move it visually (no physics)
        var mover = clone.AddComponent<LocalProjectileGhost>();
        var shooterVel = rb ? rb.linearVelocity : Vector3.zero;
        mover.velocity = spawn.forward * projectileSpeed + shooterVel;
        mover.lifetime = localReplicaLifetime;
        mover.gravity = localReplicaGravity;
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
