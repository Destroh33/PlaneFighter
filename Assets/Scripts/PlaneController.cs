using UnityEngine;
using Unity.Netcode;
using Unity.Cinemachine;
using System.Collections.Generic;

public class PlaneController : NetworkBehaviour
{
    public enum ControlScheme { Mouse, WASD, AI }

    [Header("Control Settings")]
    public ControlScheme controlScheme = ControlScheme.WASD;

    [Header("Flight Settings")]
    public float baseSpeed = 40f;
    public float maxSpeed = 90f;
    public float minSpeed = 20f;
    public float acceleration = 35f;

    [Header("Rotation Settings")]
    public float pitchSpeed = 240f;
    public float rollSpeed = 320f;
    public float rotationSmoothing = 3f;
    public float wasdSensitivityFactor = 50f;

    [Header("Combat Settings")]
    public GameObject projectilePrefab;
    public List<Transform> projectileSpawnPoints = new List<Transform>();
    public float projectileSpeed = 600f;
    public float fireRate = 0.12f;

    [Header("Engine Particles")]
    public List<ParticleSystem> engineParticles = new List<ParticleSystem>();
    public float zVelBoost = 2f;
    public float zVelCruise = 1f;
    public float zVelBrake = 0.5f;

    [Header("Boost Camera Settings")]
    public CinemachineCamera vcam;
    public float boostFov = 70f;
    public float normalFov = 60f;
    public float fovLerpSpeed = 5f;
    public float shakeAmp = 2f;
    public float shakeFreq = 3f;

    [Header("AI Settings")]
    public Transform player;
    public float detectionRange = 200f;
    public float fireRange = 150f;
    public float aimThreshold = 0.95f;
    public float aiFireCooldown = 0.5f;

    Rigidbody rb;
    float currentSpeed;
    float fireCooldown;
    float aiFireTimer;
    CinemachineBasicMultiChannelPerlin noise;
    Camera fallbackCam;

    // server-authoritative input sample (owned by server)
    float inPitch, inRoll;
    bool inBoost, inBrake, inFire;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;
        //Cursor.lockState = CursorLockMode.Locked;
        //Cursor.visible = false;
        currentSpeed = baseSpeed;

        if (vcam == null) fallbackCam = Camera.main;
        if (vcam == null && fallbackCam != null) vcam = fallbackCam.GetComponent<CinemachineCamera>();
        if (vcam != null) noise = vcam.GetComponent<CinemachineBasicMultiChannelPerlin>();

        if (!IsOwner && vcam != null) vcam.gameObject.SetActive(false);

        if (player == null && controlScheme == ControlScheme.AI)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
        }
    }
    public override void OnNetworkSpawn()
    {
        if (!IsOwner) return;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public override void OnNetworkDespawn()
    {
        if (!IsOwner) return;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void Update()
    {
        if (IsOwner && !IsServer && (controlScheme == ControlScheme.Mouse || controlScheme == ControlScheme.WASD))
        {
            SampleAndSendInput();
        }
        if (IsOwner && Input.GetKeyDown(KeyCode.Escape))
        {
            bool unlocked = Cursor.lockState == CursorLockMode.Locked;
            Cursor.lockState = unlocked ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = unlocked;
        }

        if (IsOwner && (controlScheme == ControlScheme.Mouse || controlScheme == ControlScheme.WASD))
        {
            UpdateLocalOnlyFX();
        }

        if (IsServer)
        {
            if (controlScheme == ControlScheme.AI) RunAI();
            else RunPlayerServer();
        }
    }

    void FixedUpdate()
    {
        if (!IsServer) return;
        rb.linearVelocity = transform.forward * currentSpeed;
    }

    void SampleAndSendInput()
    {
        float pitch = 0f, roll = 0f;
        bool boost = Input.GetKey(KeyCode.LeftShift);
        bool brake = Input.GetKey(KeyCode.LeftControl);
        bool fire = Input.GetKey(KeyCode.Space);
        if (controlScheme == ControlScheme.WASD)
        {
            float mouseX = Input.GetAxis("Mouse X");
            float mouseY = Input.GetAxis("Mouse Y");
            pitch = -mouseY;
            roll = -mouseX;
        }
        else
        {
            if (Input.GetKey(KeyCode.W))
            {
                pitch = -1f * wasdSensitivityFactor;
                Debug.Log("Pitch input: " + pitch);
            }
                if (Input.GetKey(KeyCode.S)) pitch = 1f * wasdSensitivityFactor;
            if (Input.GetKey(KeyCode.A)) roll = 1f * wasdSensitivityFactor;
            if (Input.GetKey(KeyCode.D)) roll = -1f * wasdSensitivityFactor;
        }

        SendInputServerRpc(pitch, roll, boost, brake, fire);
    }

    [ServerRpc]
    void SendInputServerRpc(float pitchAxis, float rollAxis, bool boost, bool brake, bool fire)
    {
        inPitch = pitchAxis;
        inRoll = rollAxis;
        inBoost = boost;
        inBrake = brake;
        inFire = fire;
    }

    void RunPlayerServer()
    {
        float pitch = 0f, roll = 0f;
        if (IsOwner && IsServer)
        {
            if (controlScheme == ControlScheme.Mouse)
            {
                float mouseX = Input.GetAxis("Mouse X");
                float mouseY = Input.GetAxis("Mouse Y");
                pitch = -mouseY;
                roll = -mouseX;
            }
            else if (controlScheme == ControlScheme.WASD)
            {
                //Debug.Log("Running wasd control scheme on server for owner");
                if (Input.GetKey(KeyCode.W)) pitch = -1f * wasdSensitivityFactor;
                if (Input.GetKey(KeyCode.S)) pitch = 1f * wasdSensitivityFactor;
                if (Input.GetKey(KeyCode.A)) roll = 1f * wasdSensitivityFactor;
                if (Input.GetKey(KeyCode.D)) roll = -1f * wasdSensitivityFactor;
            }
            //Debug.Log($"Server running player input: pitch {pitch}, roll {roll}");
            ApplyRotation(pitch, roll);
            ApplyThrottle(Input.GetKey(KeyCode.LeftShift), Input.GetKey(KeyCode.LeftControl));
            TryFire(Input.GetKey(KeyCode.Space));
        }
        else
        {
            ApplyRotation(inPitch, inRoll);
            ApplyThrottle(inBoost, inBrake);
            TryFire(inFire);
        }
    }

    void RunAI()
    {
        if (player == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
            else return;
        }

        float distance = Vector3.Distance(transform.position, player.position);
        Vector3 dir = (player.position - transform.position).normalized;
        Vector3 localDir = transform.InverseTransformDirection(dir);

        float pitchAxis = -localDir.y;
        float rollAxis = -localDir.x;

        ApplyRotation(pitchAxis, rollAxis);

        float targetSpeed = distance > 100f ? maxSpeed : baseSpeed;
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, acceleration * Time.deltaTime);

        float dot = Vector3.Dot(transform.forward, dir);
        aiFireTimer -= Time.deltaTime;
        if (distance <= fireRange && dot >= aimThreshold && aiFireTimer <= 0f)
        {
            ServerFireProjectiles();
            aiFireTimer = aiFireCooldown;
        }
    }

    void ApplyRotation(float pitchAxis, float rollAxis)
    {
        float pitch = pitchAxis * pitchSpeed * Time.deltaTime;
        float roll = rollAxis * rollSpeed * Time.deltaTime;
        Quaternion delta = Quaternion.Euler(pitch, 0f, roll);
        transform.rotation = Quaternion.Slerp(transform.rotation, transform.rotation * delta, rotationSmoothing * Time.deltaTime);
    }

    void ApplyThrottle(bool boost, bool brake)
    {
        if (boost)
            currentSpeed = Mathf.MoveTowards(currentSpeed, maxSpeed, acceleration * Time.deltaTime);
        else if (brake)
            currentSpeed = Mathf.MoveTowards(currentSpeed, minSpeed, acceleration * Time.deltaTime);
        else
            currentSpeed = Mathf.MoveTowards(currentSpeed, baseSpeed, acceleration * Time.deltaTime);
    }

    void TryFire(bool wantsFire)
    {
        fireCooldown -= Time.deltaTime;
        if (!wantsFire || fireCooldown > 0f) return;
        ServerFireProjectiles();
        fireCooldown = fireRate;
    }

    void ServerFireProjectiles()
    {
        if (projectilePrefab == null || projectileSpawnPoints.Count == 0) return;

        var planeCol = GetComponent<Collider>();
        foreach (var spawn in projectileSpawnPoints)
        {
            if (spawn == null) continue;
            GameObject go = Instantiate(projectilePrefab, spawn.position, Quaternion.LookRotation(transform.forward));
            var no = go.GetComponent<NetworkObject>();
            if (no != null) no.Spawn(true);

            var proj = go.GetComponent<Projectile>();
            if (proj != null)
            {
                proj.SetOwner(NetworkObjectId);
            }

            var projCol = go.GetComponent<Collider>();
            if (projCol != null && planeCol != null)
                Physics.IgnoreCollision(projCol, planeCol);

            var prb = go.GetComponent<Rigidbody>();
            if (prb != null)
                prb.linearVelocity = transform.forward * projectileSpeed + rb.linearVelocity;
        }
    }

    void UpdateLocalOnlyFX()
    {
        bool boosting = Input.GetKey(KeyCode.LeftShift);
        float targetFov = boosting ? boostFov : normalFov;

        if (vcam != null)
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
        else if (fallbackCam != null)
        {
            fallbackCam.fieldOfView = Mathf.Lerp(fallbackCam.fieldOfView, targetFov, fovLerpSpeed * Time.deltaTime);
        }

        float zVel = zVelCruise;
        if (Input.GetKey(KeyCode.LeftShift)) zVel = zVelBoost;
        else if (Input.GetKey(KeyCode.LeftControl)) zVel = zVelBrake;

        foreach (var ps in engineParticles)
        {
            if (ps == null) continue;
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.z = zVel;
        }
    }
}
