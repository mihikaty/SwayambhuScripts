using UnityEngine;

public class Curved : MonoBehaviour
{
    [Header("References")]
    public Transform playerRig;
    public Transform environment;

    [Header("Tracking Area")]
    public Vector3 trackingOffset = new Vector3(0f, 0f, 7f);
    [Range(0f, 16f)]
    public float innerRadius = 5f;
    [Range(0f, 16f)]
    public float outerRadius = 8f;
    public float towerRadius = 10f;

    [Header("Steering Settings")]
    [Range(0f, 2f)]
    public float assistance = 0f;

    public bool showDebug = true;

    [Header("Calibration Validation (off by default)")]
    // Leave off until referenceCalibrationPosition / referenceCalibrationForward are set to a known-good spot.
    // With the defaults below and snapToReferenceIfInvalid on, calibration would snap to (0,0,0).
    public bool useCalibrationValidation = false;
    public Vector3 referenceCalibrationPosition;
    public Vector3 referenceCalibrationForward = Vector3.forward;
    public float positionTolerance = 1.5f;        // meters allowed away from the reference spot
    public float facingToleranceDegrees = 30f;    // degrees allowed away from the reference facing
    public bool snapToReferenceIfInvalid = true;  // true = use reference values; false = block calibration

    [Header("Start Point Lock")]
    // Resets the Environment's rotation on every calibration so the starting point in the model
    // depends on facing direction only, not on how you walked before calibrating.
    public bool lockStartPoint = false;
    public float startFacingYaw = 0f;             // facing (degrees) of the calibration that gave the good start
    public bool logCalibrationYaw = true;         // prints the facing yaw each time you calibrate

    private Vector3 lastPosition;
    private Quaternion startEnvRotation;

    void Start()
    {
        if (!playerRig || !environment)
        {
            Debug.LogError("Missing references");
            enabled = false;
            return;
        }

        startEnvRotation = environment.rotation; // the saved rotation is the clean baseline
        lastPosition = playerRig.position;
    }

    void Update()
    {
        // All distance/direction math is flattened to the horizontal (X/Z) plane.
        Vector3 center = new Vector3(trackingOffset.x, 0f, trackingOffset.z);
        Vector3 flatPlayerPos = new Vector3(playerRig.position.x, 0f, playerRig.position.z);
        Vector3 centerToPlayer = flatPlayerPos - center;
        float distance = centerToPlayer.magnitude;
        Vector3 dir = centerToPlayer.normalized;

        // Player motion
        Vector3 playerDelta = playerRig.position - lastPosition;
        Vector3 flatDelta = new Vector3(playerDelta.x, 0, playerDelta.z);
        float speed = flatDelta.magnitude / Mathf.Max(Time.deltaTime, 0.0001f);

        if (speed > 0.01f) // Movement threshold
        {
            Vector3 flatLastPosition = new Vector3(lastPosition.x, 0f, lastPosition.z);
            Vector3 prevDir = (flatLastPosition - center).normalized;
            Vector3 newDir = (flatPlayerPos - center).normalized;
            float angle = Vector3.SignedAngle(prevDir, newDir, Vector3.up);

            float rotationGain = ((innerRadius / towerRadius) - 1f) * -(assistance - 1f);
            environment.RotateAround(center, Vector3.up, -angle * rotationGain);
        }

        // Align environment along inner radius
        Vector3 innerClosestPos = center + dir * innerRadius;
        Vector3 desiredEnvPos = innerClosestPos - dir * towerRadius;
        environment.position = new Vector3(desiredEnvPos.x, 0, desiredEnvPos.z);

        lastPosition = playerRig.position;
    }

    public void Calibrate()
    {
        Vector3 playerPos = playerRig.position;

        Vector3 forward = playerRig.forward;
        forward.y = 0f;
        forward.Normalize();

        if (useCalibrationValidation)
        {
            bool isValid = ValidateCalibration(playerPos, forward, out string reason);

            if (!isValid)
            {
                Debug.LogWarning("Calibration rejected: " + reason);

                if (snapToReferenceIfInvalid)
                {
                    Debug.LogWarning("Falling back to reference calibration position/direction.");
                    playerPos = referenceCalibrationPosition;
                    forward = referenceCalibrationForward.normalized;
                }
                else
                {
                    return; // trackingOffset stays whatever it was before
                }
            }
        }

        trackingOffset = playerPos + forward * outerRadius;

        float yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        if (logCalibrationYaw) Debug.Log($"[Curved] calibrated facing yaw: {yaw:F1}");

        if (lockStartPoint)
        {
            float delta = Mathf.DeltaAngle(startFacingYaw, yaw);
            environment.rotation = Quaternion.AngleAxis(delta, Vector3.up) * startEnvRotation;
        }

        lastPosition = playerRig.position;
    }

    private bool ValidateCalibration(Vector3 position, Vector3 forward, out string reason)
    {
        float positionDeviation = Vector3.Distance(
            new Vector3(position.x, 0, position.z),
            new Vector3(referenceCalibrationPosition.x, 0, referenceCalibrationPosition.z)
        );

        if (positionDeviation > positionTolerance)
        {
            reason = $"Position deviates {positionDeviation:F2}m from reference (tolerance {positionTolerance}m).";
            return false;
        }

        float facingAngle = Vector3.Angle(forward, referenceCalibrationForward.normalized);

        if (facingAngle > facingToleranceDegrees)
        {
            reason = $"Facing direction deviates {facingAngle:F1} degrees from reference (tolerance {facingToleranceDegrees}).";
            return false;
        }

        reason = "Valid.";
        return true;
    }

    public void Assistance(float factor)
    {
        assistance = factor;
    }

    public void InnerRadius(float radius)
    {
        innerRadius = radius;
    }

    public void OuterRadius(float radius)
    {
        outerRadius = radius;
    }

    void OnDrawGizmos()
    {
        if (!showDebug) { return; }

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(trackingOffset, outerRadius);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(trackingOffset, innerRadius);

        if (environment)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawSphere(environment.position, 0.25f);

            Gizmos.color = Color.gray;
            Gizmos.DrawWireSphere(environment.position, towerRadius);
        }

        if (playerRig)
        {
            Vector3 flatPlayerPosGizmo = new Vector3(playerRig.position.x, 0f, playerRig.position.z);
            Vector3 flatOffset = new Vector3(trackingOffset.x, 0f, trackingOffset.z);
            Vector3 dir = (flatPlayerPosGizmo - flatOffset).normalized;
            Vector3 closestInner = trackingOffset + dir * innerRadius;

            Gizmos.color = Color.red;
            Gizmos.DrawLine(playerRig.position, closestInner);
            Gizmos.DrawSphere(closestInner, 0.1f);
        }

        Gizmos.color = Color.green;
        Gizmos.DrawSphere(trackingOffset, 0.1f);

        // Reference calibration spot (blue), only when validation is on
        if (useCalibrationValidation)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(referenceCalibrationPosition, 0.3f);
            Gizmos.DrawLine(referenceCalibrationPosition, referenceCalibrationPosition + referenceCalibrationForward.normalized * 1.5f);
        }
    }
}
