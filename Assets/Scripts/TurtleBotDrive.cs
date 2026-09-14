using UnityEngine;
using UnityEngine.InputSystem;

public class TurtleBotDrive : MonoBehaviour
{
    [Header("TurtleBot3 Burger")]
    [SerializeField] private float maxLinearSpeed = 0.22f;
    [SerializeField] private float maxAngularSpeed = 1.5f;
    [SerializeField] private float linearAcceleration = 0.45f;
    [SerializeField] private float angularAcceleration = 3f;
    [SerializeField] private float wheelRadius = 0.033f;
    [SerializeField] private float wheelTrack = 0.160f;

    [Header("Wheel Drive")]
    [SerializeField] private float driveDamping = 10f;
    [SerializeField] private float torqueLimit = 10f;

    [Header("Ground Check")]
    [SerializeField] private float groundCheckExtraDistance = 0.04f;

    [Header("Stability")]
    [SerializeField] private float uprightStrength = 35f;
    [SerializeField] private float uprightDamping = 8f;

    private ArticulationBody baseBody;
    private ArticulationBody leftWheel;
    private ArticulationBody rightWheel;

    private float forwardInput;
    private float turnInput;
    private float currentLinearSpeed;
    private float currentAngularSpeed;

    private void Start()
    {
        ArticulationBody[] bodies =
            GetComponentsInChildren<ArticulationBody>();

        foreach (ArticulationBody body in bodies)
        {
            if (body.name.Contains("base_link") && body.isRoot)
                baseBody = body;

            if (body.name.Contains("wheel_left_link"))
                leftWheel = body;

            if (body.name.Contains("wheel_right_link"))
                rightWheel = body;
        }

        ConfigureWheel(leftWheel);
        ConfigureWheel(rightWheel);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard == null)
            return;

        forwardInput = 0f;
        turnInput = 0f;

        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
            forwardInput += 1f;

        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
            forwardInput -= 1f;

        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
            turnInput -= 1f;

        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
            turnInput += 1f;
    }

    private void FixedUpdate()
    {
        baseBody.automaticCenterOfMass = false;
        baseBody.centerOfMass = new Vector3(0f, -0.02f, 0f);

        UpdateCommandSpeeds();
        //RotateWheels();

        bool leftGrounded = IsWheelGrounded(leftWheel);
        bool rightGrounded = IsWheelGrounded(rightWheel);

        StabilizeBody();

        if (leftGrounded && rightGrounded)
            MoveBody();
    }

    private void UpdateCommandSpeeds()
    {
        float targetLinear = forwardInput * maxLinearSpeed;
        float targetAngular = turnInput * maxAngularSpeed;

        currentLinearSpeed = Mathf.MoveTowards(
            currentLinearSpeed,
            targetLinear,
            linearAcceleration * Time.fixedDeltaTime);

        currentAngularSpeed = Mathf.MoveTowards(
            currentAngularSpeed,
            targetAngular,
            angularAcceleration * Time.fixedDeltaTime);
    }

    private void RotateWheels()
    {
        float halfTrack = wheelTrack * 0.5f;

        float leftLinear =
            currentLinearSpeed - currentAngularSpeed * halfTrack;

        float rightLinear =
            currentLinearSpeed + currentAngularSpeed * halfTrack;

        float leftDegrees =
            leftLinear / wheelRadius * Mathf.Rad2Deg;

        float rightDegrees =
            -rightLinear / wheelRadius * Mathf.Rad2Deg;

        SetWheelVelocity(leftWheel, leftDegrees);
        SetWheelVelocity(rightWheel, rightDegrees);
    }

    private bool IsWheelGrounded(ArticulationBody wheel)
    {
        if (wheel == null)
            return false;

        Vector3 origin =
            wheel.transform.position + Vector3.up * 0.01f;

        float distance =
            wheelRadius + groundCheckExtraDistance + 0.01f;

        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            Vector3.down,
            distance,
            ~0,
            QueryTriggerInteraction.Ignore);

        foreach (RaycastHit hit in hits)
        {
            if (!hit.collider.transform.IsChildOf(transform))
                return true;
        }

        return false;
    }

    private void MoveBody()
    {
        if (baseBody == null)
            return;

        baseBody.WakeUp();

        Vector3 forward = Vector3.ProjectOnPlane(
            baseBody.transform.forward,
            Vector3.up).normalized;

        Vector3 velocity = baseBody.linearVelocity;
        Vector3 desired = forward * currentLinearSpeed;

        baseBody.linearVelocity = new Vector3(
            desired.x,
            velocity.y,
            desired.z);

        Vector3 angular = baseBody.angularVelocity;
        angular.y = currentAngularSpeed;
        baseBody.angularVelocity = angular;
    }

    private void StabilizeBody()
    {
        if (baseBody == null)
            return;

        Vector3 tiltAxis = Vector3.Cross(
            baseBody.transform.up,
            Vector3.up);

        Vector3 tiltVelocity = Vector3.ProjectOnPlane(
            baseBody.angularVelocity,
            Vector3.up);

        Vector3 correction =
            tiltAxis * uprightStrength -
            tiltVelocity * uprightDamping;

        baseBody.AddTorque(
            correction,
            ForceMode.Acceleration);
    }

    private void ConfigureWheel(ArticulationBody wheel)
    {
        if (wheel == null)
            return;

        ArticulationDrive drive = wheel.xDrive;
        drive.stiffness = 0f;
        drive.damping = driveDamping;
        drive.forceLimit = torqueLimit;
        wheel.xDrive = drive;
    }

    private void SetWheelVelocity(
        ArticulationBody wheel,
        float velocity)
    {
        if (wheel == null)
            return;

        wheel.WakeUp();

        ArticulationDrive drive = wheel.xDrive;
        drive.targetVelocity = velocity;
        wheel.xDrive = drive;
    }
}