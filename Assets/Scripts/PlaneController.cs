using UnityEngine;
using Unity.Cinemachine;
using System.Collections.Generic;

public class PlaneController : MonoBehaviour
{
    public enum ControlScheme { Mouse, WASD, AI }

    [Header("Control Settings")]
    public ControlScheme controlScheme = ControlScheme.Mouse;

    [Header("Flight Settings")]
    public float baseSpeed = 30f;
    public float maxSpeed = 60f;
    public float minSpeed = 10f;
    public float acceleration = 20f;

    [Header("Rotation Settings")]
    public float pitchSpeed = 90f;
    public float rollSpeed = 90f;
    public float rotationSmoothing = 5f;
    public float wasdSensitivityFactor = 0.5f;

    [Header("Combat Settings")]
    public GameObject projectilePrefab;
    public List<Transform> projectileSpawnPoints = new List<Transform>();
    public float projectileSpeed = 500f;
    public float fireRate = 0.1f;

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

    private float fireCooldown = 0f;
    private Rigidbody rb;
    private float currentSpeed;
    private CinemachineBasicMultiChannelPerlin noise;
    private Camera fallbackCam;
    private float aiFireTimer;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        currentSpeed = baseSpeed;

        if (vcam == null) fallbackCam = Camera.main;
        if (vcam == null && fallbackCam != null) vcam = fallbackCam.GetComponent<CinemachineCamera>();
        if (vcam != null) noise = vcam.GetComponent<CinemachineBasicMultiChannelPerlin>();

        if (player == null && controlScheme == ControlScheme.AI)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
        }
    }

    void Update()
    {
    }

    void FixedUpdate()
    {
        if (controlScheme == ControlScheme.AI)
        {
            aiControls();
        }
        else
        {
            flightControls();
            throttle();
            shooting();
            updateEngineParticles();
            updateCameraEffects();
        }
        rb.linearVelocity = transform.forward * currentSpeed;
    }

    void flightControls()
    {
        float pitch = 0f;
        float roll = 0f;

        if (controlScheme == ControlScheme.Mouse)
        {
            float mouseX = Input.GetAxis("Mouse X");
            float mouseY = Input.GetAxis("Mouse Y");
            pitch = -mouseY * pitchSpeed;
            roll = -mouseX * rollSpeed;
        }
        else if (controlScheme == ControlScheme.WASD)
        {
            if (Input.GetKey(KeyCode.W)) pitch = -pitchSpeed * wasdSensitivityFactor;
            if (Input.GetKey(KeyCode.S)) pitch = pitchSpeed * wasdSensitivityFactor;
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

    void shooting()
    {
        fireCooldown -= Time.deltaTime;
        if (Input.GetKey(KeyCode.Space) && fireCooldown <= 0f) //&& controlScheme!=ControlScheme.AI)
        {
            fireProjectiles();
            fireCooldown = fireRate;
        }
    }

    void aiControls()
    {
        if (player == null) return;

        float distance = Vector3.Distance(transform.position, player.position);
        //if (distance > detectionRange) return;

        Vector3 dirToPlayer = (player.position - transform.position).normalized;
        Quaternion targetRot = Quaternion.LookRotation(dirToPlayer, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSmoothing * Time.deltaTime);

        if (distance > 100f)
            currentSpeed = Mathf.MoveTowards(currentSpeed, maxSpeed, acceleration * Time.deltaTime);
        else
            currentSpeed = Mathf.MoveTowards(currentSpeed, baseSpeed, acceleration * Time.deltaTime);

        float dot = Vector3.Dot(transform.forward, dirToPlayer);
        aiFireTimer -= Time.deltaTime;
        if (distance <= fireRange && dot >= aimThreshold && aiFireTimer <= 0f)
        {
            fireProjectiles();
            aiFireTimer = aiFireCooldown;
        }
    }

    void fireProjectiles()
    {
        if (projectilePrefab == null || projectileSpawnPoints.Count == 0) return;
        foreach (Transform spawn in projectileSpawnPoints)
        {
            if (spawn == null) continue;
            Vector3 shootDir = transform.forward;
            GameObject projectile = Instantiate(projectilePrefab, spawn.position, Quaternion.LookRotation(shootDir));
            Collider projCol = projectile.GetComponent<Collider>();
            Collider planeCol = GetComponent<Collider>();
            if (projCol != null && planeCol != null) Physics.IgnoreCollision(projCol, planeCol);
            Rigidbody projRb = projectile.GetComponent<Rigidbody>();
            if (projRb != null) projRb.linearVelocity = shootDir * projectileSpeed + rb.linearVelocity;
        }
    }

    void updateEngineParticles()
    {
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

    void updateCameraEffects()
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
    }

    void OnDestroy()
    {
        foreach (var ps in engineParticles)
        {
            if (ps != null) Destroy(ps.gameObject);
        }
    }
}
