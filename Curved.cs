using UnityEngine;

public class Distractor : MonoBehaviour
{
    [Header("References")]
    public Transform playerRig;
    public Curved curvedScript;
    public bool hasCalibrated = false;

    [Header("Obstruction Avoidance")]
    public LayerMask obstructionLayer;
    public float obstructionCheckRadius = 0.5f;
    public float pushOutStep = 0.5f;
    public int maxPushAttempts = 3;

    [Header("Distractor Objects")]
    public GameObject[] distractorPrefabs;
    public float[] distractorHeights;           // matched by index to distractorPrefabs
    public float distractorDistance = 2f;
    public float distractorHeight = 1.6f;       // fallback if no per-prefab height
    public float bobSpeed = 2f;
    public float bobAmount = 0.1f;

    [Header("Audio")]
    public AudioClip[] distractorSounds;        // matched by index to distractorPrefabs
    [Range(0f, 1f)]
    public float audioVolume = 0.7f;

    [Header("Outer Boundary Trigger")]
    [Range(0f, 1f)] public float outerTriggerThreshold = 0.7f;
    [Range(0f, 1f)] public float outerHideThreshold = 0.5f;
    public float outerCooldown = 5f;

    [Header("Inner Boundary Trigger")]
    [Range(0f, 1f)] public float innerTriggerThreshold = 0.3f;
    [Range(0f, 1f)] public float innerHideThreshold = 0.5f;
    public float innerCooldown = 5f;

    [Header("Lifetime")]
    public float minLifetime = 2f;

    [Header("Movement (optional)")]
    public bool enableMovement = false;
    public float moveSpeed = 1.3f;              // meters per second
    public float pathLength = 4f;               // total sideways travel across the spawn point
    public float arcAmount = 0.5f;              // how far the path bows off a straight line
    public float leaveDuration = 2f;            // keeps walking away this long after arriving / being cleared
    public bool faceTravelDirection = true;
    public bool bobWhileMoving = false;
    public bool useEnvironmentFrame = false;    // true = moves with the temple as it rotates
    public string animatorMoveParam = "isMoving";

    [Header("Debug")]
    public bool debugLogs = false;

    private GameObject activeDistractor;
    private float lastOuterTriggerTime = -999f;
    private float lastInnerTriggerTime = -999f;
    private float currentDistractorHeight;
    private float spawnTime;
    private bool activeIsOuter;

    // Debug distance tracking (only used when debugLogs is on)
    private float minDist = float.MaxValue;
    private float maxDist = 0f;
    private float lastLogTime;

    // Movement state. Positions are in "frame space": world space, or Environment-local if useEnvironmentFrame.
    private bool isMovingNow;
    private bool isLeaving;
    private Vector3 p0, p1, p2;
    private float pathT;
    private float approxPathLength;
    private Vector3 framePos;
    private Vector3 lastTravelDirFrame;
    private Vector3 leaveDir;
    private float leaveStartTime;

    void Update()
    {
        if (!hasCalibrated || playerRig == null || curvedScript == null) return;

        Vector3 center = new Vector3(curvedScript.trackingOffset.x, 0f, curvedScript.trackingOffset.z);
        Vector3 flatPlayerPos = new Vector3(playerRig.position.x, 0f, playerRig.position.z);
        float distance = (flatPlayerPos - center).magnitude;

        if (debugLogs)
        {
            minDist = Mathf.Min(minDist, distance);
            maxDist = Mathf.Max(maxDist, distance);
            if (Time.time - lastLogTime > 5f)
            {
                float outerTrig = curvedScript.innerRadius + outerTriggerThreshold * (curvedScript.outerRadius - curvedScript.innerRadius);
                float innerTrig = innerTriggerThreshold * curvedScript.innerRadius;
                Debug.Log($"[Distractor] dist {distance:F2} (min {minDist:F2}, max {maxDist:F2}) | outer trigger {outerTrig:F2}, inner trigger {innerTrig:F2}");
                lastLogTime = Time.time;
            }
        }

        float range = curvedScript.outerRadius - curvedScript.innerRadius;
        float normalizedOuterDistance = range > 0f
            ? Mathf.Clamp01((distance - curvedScript.innerRadius) / range)
            : 0f;
        float normalizedInnerDistance = curvedScript.innerRadius > 0f
            ? Mathf.Clamp01(distance / curvedScript.innerRadius)
            : 0f;

        bool shouldShowOuter = normalizedOuterDistance >= outerTriggerThreshold;
        bool shouldShowInner = normalizedInnerDistance <= innerTriggerThreshold;

        if (activeDistractor == null)
        {
            if (shouldShowOuter && Time.time - lastOuterTriggerTime > outerCooldown)
            {
                SpawnDistractor(center, pullTowardCenter: true);
                lastOuterTriggerTime = Time.time;
                spawnTime = Time.time;
                activeIsOuter = true;
                if (debugLogs) Debug.Log($"[Distractor] OUTER spawn, distance {distance:F2}, outerNorm {normalizedOuterDistance:F2}");
            }
            else if (shouldShowInner && Time.time - lastInnerTriggerTime > innerCooldown)
            {
                SpawnDistractor(center, pullTowardCenter: false);
                lastInnerTriggerTime = Time.time;
                spawnTime = Time.time;
                activeIsOuter = false;
                if (debugLogs) Debug.Log($"[Distractor] INNER spawn, distance {distance:F2}, innerNorm {normalizedInnerDistance:F2}");
            }
        }
        else if (!isLeaving && Time.time - spawnTime >= minLifetime)
        {
            bool cleared = activeIsOuter
                ? normalizedOuterDistance < outerHideThreshold
                : normalizedInnerDistance > innerHideThreshold;

            if (cleared)
            {
                if (debugLogs) Debug.Log($"[Distractor] cleared, distance {distance:F2}, moving={isMovingNow}");
                if (isMovingNow) BeginLeaving();   // walk off instead of vanishing
                else DestroyActive();
            }
        }

        if (activeDistractor != null) UpdateActive();
    }

    void UpdateActive()
    {
        Transform t = activeDistractor.transform;

        if (isMovingNow)
        {
            Transform frame = GetFrame();
            Vector3 travelDirFrame;

            if (!isLeaving)
            {
                pathT += moveSpeed * Time.deltaTime / Mathf.Max(approxPathLength, 0.01f);
                float u = Mathf.Clamp01(pathT);
                framePos = Bezier(p0, p1, p2, u);
                travelDirFrame = BezierTangent(p0, p1, p2, u);
                lastTravelDirFrame = travelDirFrame;
                if (pathT >= 1f) BeginLeaving();   // arrived: keep walking off
            }
            else
            {
                framePos += leaveDir * moveSpeed * Time.deltaTime;
                travelDirFrame = leaveDir;
                if (Time.time - leaveStartTime >= leaveDuration)
                {
                    DestroyActive();
                    return;
                }
            }

            float bob = bobWhileMoving ? Mathf.Sin(Time.time * bobSpeed) * bobAmount : 0f;
            t.position = FromFrame(new Vector3(framePos.x, framePos.y + bob, framePos.z), frame);

            if (faceTravelDirection)
            {
                Vector3 worldDir = DirFromFrame(travelDirFrame, frame);
                worldDir.y = 0f;
                if (worldDir.sqrMagnitude > 0.0001f)
                    t.rotation = Quaternion.LookRotation(worldDir.normalized, Vector3.up);
            }
            else
            {
                t.LookAt(playerRig);
            }
        }
        else
        {
            // Static behavior
            float bob = Mathf.Sin(Time.time * bobSpeed) * bobAmount;
            Vector3 pos = t.position;
            t.position = new Vector3(pos.x, currentDistractorHeight + bob, pos.z);
            t.LookAt(playerRig);
        }
    }

    void BeginLeaving()
    {
        isLeaving = true;
        leaveStartTime = Time.time;
        leaveDir = lastTravelDirFrame.sqrMagnitude > 0.0001f ? lastTravelDirFrame.normalized : Vector3.forward;
    }

    void DestroyActive()
    {
        if (activeDistractor != null) Destroy(activeDistractor);
        activeDistractor = null;
        isMovingNow = false;
        isLeaving = false;
    }

    // Hook this up to Setup's Calibrate Event (alongside Curved.Calibrate)
    public void OnCalibrated()
    {
        hasCalibrated = true;
        minDist = float.MaxValue;
        maxDist = 0f;
    }

    void SpawnDistractor(Vector3 center, bool pullTowardCenter)
    {
        if (distractorPrefabs == null || distractorPrefabs.Length == 0) return;

        Vector3 flatPlayerPos = new Vector3(playerRig.position.x, 0f, playerRig.position.z);

        int index = Random.Range(0, distractorPrefabs.Length);
        GameObject prefabToUse = distractorPrefabs[index];
        if (prefabToUse == null) return;

        Vector3 direction = pullTowardCenter
            ? (center - flatPlayerPos).normalized
            : (flatPlayerPos - center).normalized;

        Vector3 spawnPos = flatPlayerPos + direction * distractorDistance;

        float heightToUse = (distractorHeights != null && index < distractorHeights.Length)
            ? distractorHeights[index]
            : distractorHeight;

        // Obstruction avoidance: if the spawn spot overlaps a collider, pull it back toward the player
        Physics.SyncTransforms();
        Vector3 flatSpawnPos = new Vector3(spawnPos.x, 0f, spawnPos.z);
        int attempts = 0;
        while (Physics.CheckSphere(new Vector3(flatSpawnPos.x, heightToUse, flatSpawnPos.z), obstructionCheckRadius, obstructionLayer)
               && attempts < maxPushAttempts)
        {
            flatSpawnPos -= direction * pushOutStep;
            attempts++;
        }
        spawnPos = new Vector3(flatSpawnPos.x, heightToUse, flatSpawnPos.z);

        currentDistractorHeight = heightToUse;
        isMovingNow = false;
        isLeaving = false;

        activeDistractor = Instantiate(prefabToUse, spawnPos, Quaternion.identity);

        if (enableMovement) SetupMovement(spawnPos, direction, heightToUse);

        if (distractorSounds != null && distractorSounds.Length > 0)
        {
            AudioClip clipToUse = index < distractorSounds.Length ? distractorSounds[index] : distractorSounds[0];
            if (clipToUse != null)
            {
                AudioSource source = activeDistractor.AddComponent<AudioSource>();
                source.clip = clipToUse;
                source.volume = audioVolume;
                source.spatialBlend = 1f;
                source.loop = true;
                source.Play();
            }
        }
    }

    void SetupMovement(Vector3 spawnWorld, Vector3 direction, float height)
    {
        Transform frame = GetFrame();

        Vector3 sideways = Vector3.Cross(Vector3.up, direction).normalized;
        if (Random.value > 0.5f) sideways = -sideways;

        // Try the full path, then shorter ones if the curve hits an obstruction collider
        float[] scales = { 1f, 0.6f, 0.35f };
        Vector3 startW = spawnWorld, endW = spawnWorld, controlW = spawnWorld;
        bool found = false;
        float bulgeSign = Random.value > 0.5f ? 1f : -1f;

        foreach (float s in scales)
        {
            float half = pathLength * s * 0.5f;
            startW = spawnWorld - sideways * half;
            endW = spawnWorld + sideways * half;
            controlW = spawnWorld + direction * (arcAmount * 2f * bulgeSign * s);

            if (!PathBlockedAlongCurve(startW, controlW, endW, height)) { found = true; break; }
        }
        if (!found) return; // stay a static distractor

        p0 = ToFrame(startW, frame);
        p1 = ToFrame(controlW, frame);
        p2 = ToFrame(endW, frame);
        approxPathLength = (2f * (p2 - p0).magnitude + (p1 - p0).magnitude + (p2 - p1).magnitude) / 3f;
        pathT = 0f;
        framePos = p0;
        lastTravelDirFrame = BezierTangent(p0, p1, p2, 0f);
        isMovingNow = true;

        activeDistractor.transform.position = startW;

        // Drive the model's animation, if it has an Animator with a matching bool parameter
        Animator anim = activeDistractor.GetComponentInChildren<Animator>();
        if (anim != null && anim.runtimeAnimatorController != null)
        {
            foreach (var param in anim.parameters)
            {
                if (param.name == animatorMoveParam && param.type == AnimatorControllerParameterType.Bool)
                {
                    anim.SetBool(animatorMoveParam, true);
                    break;
                }
            }
        }
    }

    bool PathBlocked(Vector3 a, Vector3 b, float height)
    {
        if (obstructionLayer.value == 0) return false;
        Physics.SyncTransforms();
        return Physics.CheckCapsule(new Vector3(a.x, height, a.z), new Vector3(b.x, height, b.z),
                                    obstructionCheckRadius, obstructionLayer);
    }

    bool PathBlockedAlongCurve(Vector3 a, Vector3 b, Vector3 c, float height, int samples = 5)
    {
        Vector3 prev = a;
        for (int i = 1; i <= samples; i++)
        {
            float t = i / (float)samples;
            Vector3 pt = Bezier(a, b, c, t);
            if (PathBlocked(prev, pt, height)) return true;
            prev = pt;
        }
        return false;
    }

    Transform GetFrame()
    {
        return (useEnvironmentFrame && curvedScript != null) ? curvedScript.environment : null;
    }

    Vector3 ToFrame(Vector3 world, Transform frame) { return frame != null ? frame.InverseTransformPoint(world) : world; }
    Vector3 FromFrame(Vector3 local, Transform frame) { return frame != null ? frame.TransformPoint(local) : local; }
    Vector3 DirFromFrame(Vector3 d, Transform frame) { return frame != null ? frame.TransformDirection(d) : d; }

    static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t)
    {
        float u = 1f - t;
        return u * u * a + 2f * u * t * b + t * t * c;
    }

    static Vector3 BezierTangent(Vector3 a, Vector3 b, Vector3 c, float t)
    {
        return 2f * (1f - t) * (b - a) + 2f * t * (c - b);
    }
}
