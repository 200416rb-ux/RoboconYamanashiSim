using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Keyboard controller for the three-wheel independent-steering golf robot.
/// Attach this component to the imported URDF root object.
/// </summary>
public class IndependentSteerGolfRobotController : MonoBehaviour
{
    [Header("走行")]
    [SerializeField] private float maximumLinearSpeed = 0.6f;
    [SerializeField] private float maximumYawSpeed = 1.5f;
    [SerializeField] private float linearAcceleration = 1.2f;
    [SerializeField] private float yawAcceleration = 3f;
    [SerializeField] private float wheelRadius = 0.03f;
    [Tooltip("モデル正面とUnityの青いZ軸が異なる場合に調整します。")]
    [SerializeField] private float forwardYawOffsetDegrees;

    [Header("ステアリング・車輪")]
    [SerializeField] private float steeringStiffness = 250f;
    [SerializeField] private float steeringDamping = 25f;
    [SerializeField] private float steeringForceLimit = 20f;
    [SerializeField] private float wheelDamping = 5f;
    [Tooltip("20:40歯車後の車輪側駆動力として調整します。")]
    [SerializeField] private float wheelForceLimit = 10f;
    [SerializeField] private float maximumWheelSpeedDegrees = 350f;

    [Header("カメラ・目")]
    [SerializeField] private float cameraSpeedDegrees = 60f;
    [SerializeField] private float cameraLimitDegrees = 80f;
    [SerializeField] private float eyeSpeedDegrees = 90f;
    [SerializeField] private float eyeLimitDegrees = 65f;

    [Header("クラブ動作（曲線レールの近似角度）")]
    [Tooltip("定荷重ばねによってクラブが引かれる、前側の位置です。")]
    [SerializeField] private float clubFrontDegrees = 110f;
    [Tooltip("ワイヤを緩め、キャッチが自重で降りてクラブに掛かる位置です。")]
    [SerializeField] private float catchPositionDegrees = 100f;
    [Tooltip("ワイヤを最大まで巻き取った位置です。")]
    [SerializeField] private float maximumPulledBackDegrees = -105f;
    [SerializeField] private float clawOpenDegrees = 50f;
    [Range(0.15f, 1f)]
    [SerializeField] private float strikeStrength = 0.7f;

    [Header("ポテンショメータ（モータと1:1）")]
    [SerializeField] private int potentiometerMinimum = 0;
    [SerializeField] private int potentiometerMaximum = 1023;

    [Header("表示")]
    [SerializeField] private bool showOperationGuide = true;

    private ArticulationBody rootBody;
    private ArticulationBody cameraTilt;
    private ArticulationBody leftEye;
    private ArticulationBody rightEye;
    private ArticulationBody tongueClub;
    private ArticulationBody catchCarriage;
    private ArticulationBody catchClaw;

    private readonly List<WheelModule> wheelModules = new List<WheelModule>();

    private Vector2 translationInput;
    private float yawInput;
    private Vector2 currentTranslation;
    private float currentYawSpeed;
    private float cameraTarget;
    private float eyeTarget;
    private float potentiometerValue;
    private Coroutine clubRoutine;
    private bool clubIsCaptured;
    private bool initialized;

    private sealed class WheelModule
    {
        public ArticulationBody Steering;
        public ArticulationBody Wheel;
        public float LastSteeringTarget;
        public float WheelDirection = 1f;
    }

    private void Awake()
    {
        DisableConflictingControllers();
        initialized = FindRobotBodies();

        if (!initialized)
        {
            enabled = false;
            return;
        }

        ConfigureRobotDrives();
        ResetMechanismsImmediately();
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        translationInput = Vector2.zero;
        yawInput = 0f;

        // Imported model orientation: its visual front is opposite Unity local +Z.
        if (keyboard.wKey.isPressed) translationInput.y -= 1f;
        if (keyboard.sKey.isPressed) translationInput.y += 1f;
        if (keyboard.dKey.isPressed) translationInput.x += 1f;
        if (keyboard.aKey.isPressed) translationInput.x -= 1f;
        if (keyboard.eKey.isPressed) yawInput += 1f;
        if (keyboard.qKey.isPressed) yawInput -= 1f;

        translationInput = Vector2.ClampMagnitude(translationInput, 1f);

        float cameraInput = 0f;
        if (keyboard.upArrowKey.isPressed) cameraInput += 1f;
        if (keyboard.downArrowKey.isPressed) cameraInput -= 1f;
        cameraTarget = Mathf.Clamp(
            cameraTarget + cameraInput * cameraSpeedDegrees * Time.deltaTime,
            -cameraLimitDegrees,
            cameraLimitDegrees);

        float eyeInput = 0f;
        if (keyboard.rightArrowKey.isPressed) eyeInput += 1f;
        if (keyboard.leftArrowKey.isPressed) eyeInput -= 1f;
        eyeTarget = Mathf.Clamp(
            eyeTarget + eyeInput * eyeSpeedDegrees * Time.deltaTime,
            -eyeLimitDegrees,
            eyeLimitDegrees);

        SetPositionTarget(cameraTilt, cameraTarget);
        SetPositionTarget(leftEye, eyeTarget);
        SetPositionTarget(rightEye, eyeTarget);

        if (keyboard.spaceKey.wasPressedThisFrame && clubRoutine == null)
            clubRoutine = StartCoroutine(PlayClubSequence());

        if (keyboard.zKey.isPressed)
            strikeStrength = Mathf.Max(0.15f, strikeStrength - 0.35f * Time.deltaTime);
        if (keyboard.xKey.isPressed)
            strikeStrength = Mathf.Min(1f, strikeStrength + 0.35f * Time.deltaTime);

        if (keyboard.rKey.wasPressedThisFrame)
        {
            if (clubRoutine != null)
                StopCoroutine(clubRoutine);

            clubRoutine = null;
            ResetMechanismsImmediately();
        }

        if (keyboard.f1Key.wasPressedThisFrame)
            showOperationGuide = !showOperationGuide;
    }

    private void FixedUpdate()
    {
        if (!initialized || rootBody == null)
            return;

        Vector2 desiredTranslation = translationInput * maximumLinearSpeed;
        currentTranslation = Vector2.MoveTowards(
            currentTranslation,
            desiredTranslation,
            linearAcceleration * Time.fixedDeltaTime);

        currentYawSpeed = Mathf.MoveTowards(
            currentYawSpeed,
            yawInput * maximumYawSpeed,
            yawAcceleration * Time.fixedDeltaTime);

        // A constant-force spring is approximated by continuously commanding
        // the club toward the front end whenever the claw is not holding it.
        if (!clubIsCaptured)
            SetPositionTarget(tongueClub, clubFrontDegrees);

        potentiometerValue = ReadPotentiometerValue();

        DriveWheelModules(currentTranslation, currentYawSpeed);
        MoveArticulationRoot(currentTranslation, currentYawSpeed);
    }

    private bool FindRobotBodies()
    {
        Dictionary<string, ArticulationBody> bodies = new Dictionary<string, ArticulationBody>();
        foreach (ArticulationBody body in GetComponentsInChildren<ArticulationBody>(true))
        {
            bodies[body.gameObject.name] = body;
            if (body.isRoot || body.gameObject.name == "base_link")
                rootBody = body;
        }

        AddWheelModule(bodies, "front_steer_link", "front_wheel_link");
        AddWheelModule(bodies, "rear_left_steer_link", "rear_left_wheel_link");
        AddWheelModule(bodies, "rear_right_steer_link", "rear_right_wheel_link");

        bodies.TryGetValue("camera_tilt_link", out cameraTilt);
        bodies.TryGetValue("left_eye_link", out leftEye);
        bodies.TryGetValue("right_eye_link", out rightEye);
        bodies.TryGetValue("tongue_club_link", out tongueClub);
        bodies.TryGetValue("catch_carriage_link", out catchCarriage);
        bodies.TryGetValue("catch_claw_link", out catchClaw);

        if (rootBody == null || wheelModules.Count != 3)
        {
            Debug.LogError(
                "ロボットのArticulationBodyが見つかりません。" +
                "このスクリプトを independent_steer_golf_robot のルートへ追加してください。",
                this);
            return false;
        }

        return true;
    }

    private void AddWheelModule(
        Dictionary<string, ArticulationBody> bodies,
        string steeringName,
        string wheelName)
    {
        if (!bodies.TryGetValue(steeringName, out ArticulationBody steering) ||
            !bodies.TryGetValue(wheelName, out ArticulationBody wheel))
        {
            Debug.LogWarning($"車輪ユニット {steeringName} / {wheelName} が見つかりません。", this);
            return;
        }

        wheelModules.Add(new WheelModule
        {
            Steering = steering,
            Wheel = wheel,
            LastSteeringTarget = 0f
        });
    }

    private void ConfigureRobotDrives()
    {
        foreach (WheelModule module in wheelModules)
        {
            ConfigurePositionDrive(
                module.Steering,
                steeringStiffness,
                steeringDamping,
                steeringForceLimit);

            ArticulationDrive wheelDrive = module.Wheel.xDrive;
            wheelDrive.stiffness = 0f;
            wheelDrive.damping = wheelDamping;
            wheelDrive.forceLimit = wheelForceLimit;
            module.Wheel.xDrive = wheelDrive;
        }

        ConfigurePositionDrive(cameraTilt, 80f, 10f, 5f);
        ConfigurePositionDrive(leftEye, 60f, 8f, 3f);
        ConfigurePositionDrive(rightEye, 60f, 8f, 3f);
        ConfigurePositionDrive(tongueClub, 420f, 24f, 45f);
        ConfigurePositionDrive(catchCarriage, 150f, 18f, 20f);
        ConfigurePositionDrive(catchClaw, 80f, 8f, 5f);
    }

    private static void ConfigurePositionDrive(
        ArticulationBody body,
        float stiffness,
        float damping,
        float forceLimit)
    {
        if (body == null)
            return;

        ArticulationDrive drive = body.xDrive;
        drive.stiffness = stiffness;
        drive.damping = damping;
        drive.forceLimit = forceLimit;
        body.xDrive = drive;
    }

    private void DriveWheelModules(Vector2 translation, float yawSpeed)
    {
        bool hasCommand = translation.sqrMagnitude > 0.0001f || Mathf.Abs(yawSpeed) > 0.001f;

        foreach (WheelModule module in wheelModules)
        {
            Vector3 localPosition = rootBody.transform.InverseTransformPoint(
                module.Steering.transform.position);

            // Vector2 uses X = robot right, Y = robot forward.
            Vector2 moduleVelocity = translation + new Vector2(
                yawSpeed * localPosition.z,
                -yawSpeed * localPosition.x);

            if (hasCommand && moduleVelocity.sqrMagnitude > 0.0001f)
            {
                float requestedAngle = Mathf.Atan2(moduleVelocity.x, moduleVelocity.y) * Mathf.Rad2Deg;
                requestedAngle = OptimizeSteeringAngle(
                    requestedAngle,
                    module.LastSteeringTarget,
                    out float wheelDirection);

                module.LastSteeringTarget = Mathf.Clamp(requestedAngle, -90f, 90f);
                module.WheelDirection = wheelDirection;
                SetPositionTarget(module.Steering, module.LastSteeringTarget);

                float linearSpeed = moduleVelocity.magnitude * module.WheelDirection;
                float wheelDegreesPerSecond = linearSpeed / Mathf.Max(wheelRadius, 0.001f) * Mathf.Rad2Deg;
                SetVelocityTarget(
                    module.Wheel,
                    Mathf.Clamp(wheelDegreesPerSecond, -maximumWheelSpeedDegrees, maximumWheelSpeedDegrees));
            }
            else
            {
                SetVelocityTarget(module.Wheel, 0f);
            }
        }
    }

    private static float OptimizeSteeringAngle(
        float requestedAngle,
        float currentAngle,
        out float wheelDirection)
    {
        requestedAngle = Mathf.DeltaAngle(0f, requestedAngle);
        float directDifference = Mathf.Abs(Mathf.DeltaAngle(currentAngle, requestedAngle));
        float reversedAngle = Mathf.DeltaAngle(0f, requestedAngle + 180f);
        float reversedDifference = Mathf.Abs(Mathf.DeltaAngle(currentAngle, reversedAngle));

        if (reversedDifference < directDifference)
        {
            wheelDirection = -1f;
            return reversedAngle;
        }

        wheelDirection = 1f;
        return requestedAngle;
    }

    private void MoveArticulationRoot(Vector2 translation, float yawSpeed)
    {
        rootBody.WakeUp();

        Quaternion offset = Quaternion.AngleAxis(forwardYawOffsetDegrees, Vector3.up);
        Vector3 forward = Vector3.ProjectOnPlane(offset * rootBody.transform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(offset * rootBody.transform.right, Vector3.up).normalized;
        Vector3 desiredHorizontalVelocity =
            forward * translation.y + right * translation.x;

        Vector3 velocity = rootBody.linearVelocity;
        rootBody.linearVelocity = new Vector3(
            desiredHorizontalVelocity.x,
            velocity.y,
            desiredHorizontalVelocity.z);

        Vector3 angularVelocity = rootBody.angularVelocity;
        angularVelocity.y = yawSpeed;
        rootBody.angularVelocity = angularVelocity;
    }

    private IEnumerator PlayClubSequence()
    {
        // 1. Pay out the wire. The carriage runs down the curved frame under
        // its own weight. The free-moving claw rides over the club and latches.
        SetPositionTarget(catchClaw, 0f);
        SetPositionTarget(catchCarriage, catchPositionDegrees);
        yield return new WaitForSeconds(1.1f);

        SetPositionTarget(catchClaw, clawOpenDegrees * 0.35f);
        yield return new WaitForSeconds(0.12f);
        SetPositionTarget(catchClaw, 0f);
        yield return new WaitForSeconds(0.18f);
        clubIsCaptured = true;

        // 2. Wind the wire. The 1:1 potentiometer follows this winding angle.
        // Pull-back distance determines the striking strength.
        float pullTarget = Mathf.Lerp(
            catchPositionDegrees,
            maximumPulledBackDegrees,
            strikeStrength);
        SetPositionTarget(catchCarriage, pullTarget);
        SetPositionTarget(tongueClub, pullTarget);
        yield return new WaitForSeconds(Mathf.Lerp(0.7f, 1.8f, strikeStrength));

        // 3. Open only the claw. The catch remains held by the wire, while the
        // constant-force spring sends the released club toward the front.
        SetPositionTarget(catchClaw, clawOpenDegrees);
        yield return new WaitForSeconds(0.12f);
        clubIsCaptured = false;
        SetPositionTarget(tongueClub, clubFrontDegrees);
        yield return new WaitForSeconds(0.65f);

        // 4. Close the release servo. The catch stays at the pulled-back
        // position until the next cycle pays the wire out again.
        SetPositionTarget(catchClaw, 0f);
        clubRoutine = null;
    }

    private void ResetMechanismsImmediately()
    {
        cameraTarget = 0f;
        eyeTarget = 0f;
        SetPositionTarget(cameraTilt, 0f);
        SetPositionTarget(leftEye, 0f);
        SetPositionTarget(rightEye, 0f);
        clubIsCaptured = false;
        SetPositionTarget(tongueClub, clubFrontDegrees);
        SetPositionTarget(catchCarriage, maximumPulledBackDegrees);
        SetPositionTarget(catchClaw, 0f);
        potentiometerValue = potentiometerMaximum;
    }

    private float ReadPotentiometerValue()
    {
        float carriageDegrees = maximumPulledBackDegrees;

        if (catchCarriage != null && catchCarriage.jointPosition.dofCount > 0)
            carriageDegrees = catchCarriage.jointPosition[0] * Mathf.Rad2Deg;

        float winding01 = Mathf.InverseLerp(
            catchPositionDegrees,
            maximumPulledBackDegrees,
            carriageDegrees);

        return Mathf.Lerp(potentiometerMinimum, potentiometerMaximum, winding01);
    }

    private static void SetPositionTarget(ArticulationBody body, float degrees)
    {
        if (body == null)
            return;

        body.WakeUp();
        ArticulationDrive drive = body.xDrive;
        drive.target = degrees;
        body.xDrive = drive;
    }

    private static void SetVelocityTarget(ArticulationBody body, float degreesPerSecond)
    {
        if (body == null)
            return;

        body.WakeUp();
        ArticulationDrive drive = body.xDrive;
        drive.targetVelocity = degreesPerSecond;
        body.xDrive = drive;
    }

    private void DisableConflictingControllers()
    {
        foreach (MonoBehaviour behaviour in GetComponents<MonoBehaviour>())
        {
            if (behaviour == null || behaviour == this)
                continue;

            string fullName = behaviour.GetType().FullName;
            if (fullName == "Unity.Robotics.UrdfImporter.Control.Controller" ||
                behaviour.GetType().Name == "TurtleBotDrive")
            {
                behaviour.enabled = false;
                Debug.Log($"競合を避けるため {behaviour.GetType().Name} を停止しました。", this);
            }
        }
    }

    private void OnGUI()
    {
        if (!showOperationGuide)
            return;

        int targetPotentiometer = Mathf.RoundToInt(Mathf.Lerp(
            potentiometerMinimum,
            potentiometerMaximum,
            strikeStrength));

        string guide =
            "独立ステア・ゴルフロボット\n" +
            "W / S : 前進・後退    A / D : 横移動\n" +
            "Q / E : 左右旋回      ↑ / ↓ : カメラ\n" +
            "← / → : 目            Space : クラブ発射\n" +
            "Z / X : 打力を下げる・上げる\n" +
            $"打力 {strikeStrength * 100f:0}%  ポテンショメータ {potentiometerValue:0} / 目標 {targetPotentiometer}\n" +
            "R : 機構リセット      F1 : この表示を隠す";

        GUI.Box(new Rect(15f, 15f, 430f, 145f), guide);
    }
}
