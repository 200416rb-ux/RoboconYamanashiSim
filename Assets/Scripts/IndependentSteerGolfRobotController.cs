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
    [SerializeField] private float maximumLinearSpeed = 0.09f;
    [SerializeField] private float maximumYawSpeed = 0.7f;
    [SerializeField] private float linearAcceleration = 0.25f;
    [SerializeField] private float yawAcceleration = 1.5f;
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
    [Tooltip("20:40減速後の目視確認用上限。180 deg/s = 30 rpmです。")]
    [SerializeField] private float maximumWheelSpeedDegrees = 180f;

    [Header("カメラ・目")]
    [SerializeField] private float cameraSpeedDegrees = 60f;
    [SerializeField] private float cameraLimitDegrees = 80f;
    [Tooltip("カメラ歯車は1:1なので通常は1です。")]
    [SerializeField] private float cameraDriveGearRatio = 1f;
    [SerializeField] private float eyeSpeedDegrees = 45f;
    [SerializeField] private float eyeLimitDegrees = 30f;
    [Tooltip("サーボ側歯車角 / 目側歯車角。歯数が判明したら設定します。")]
    [SerializeField] private float eyeDriveGearRatio = 1f;

    [Header("クラブ動作（曲線レールの近似角度）")]
    [Tooltip("クラブの前端位置。part_334から求めた円弧上の初期姿勢です。")]
    [SerializeField] private float clubFrontDegrees = 0f;
    [Tooltip("キャッチを保管する円弧上端です。")]
    [SerializeField] private float catchStoredDegrees = 0f;
    [Tooltip("キャッチが自重で下降してクラブに掛かる位置です。")]
    [SerializeField] private float catchAtClubDegrees = 90f;
    [Tooltip("クラブを前端から引き上げる最大角度です。")]
    [SerializeField] private float maximumPullDegrees = -75f;
    [SerializeField] private float clawOpenDegrees = 50f;
    [SerializeField] private float railRadius = 0.221f;
    [SerializeField] private float bearingRadius = 0.005f;
    [Header("定荷重ばね（左右2本）")]
    [Tooltip("ばね1本がクラブをレール接線方向へ引く力です。実測値があれば置き換えてください。")]
    [SerializeField] private float constantForcePerSpringNewtons = 3f;
    [Tooltip("一定力を作る速度ドライブの目標速度です。実速度は力上限で決まります。")]
    [SerializeField] private float springReturnVelocityDegrees = 360f;
    [SerializeField] private float springDriveDamping = 4f;
    [Tooltip("base_link_visual_245/247 のばね巻取り半径です。")]
    [SerializeField] private float springReelRadius = 0.01f;
    [Range(0.15f, 1f)]
    [SerializeField] private float strikeStrength = 0.7f;

    [Header("ポテンショメータ（モータと1:1）")]
    [SerializeField] private int potentiometerMinimum = 0;
    [SerializeField] private int potentiometerMaximum = 1023;

    [Header("表示")]
    [SerializeField] private bool showOperationGuide = true;
    [SerializeField, HideInInspector] private int configurationVersion;

    private ArticulationBody rootBody;
    private ArticulationBody cameraTilt;
    private ArticulationBody cameraDriveGear;
    private ArticulationBody leftEyeOutput;
    private ArticulationBody leftEyeDriveGear;
    private ArticulationBody rightEyeOutput;
    private ArticulationBody rightEyeDriveGear;
    private ArticulationBody rightClubSpringReel;
    private ArticulationBody leftClubSpringReel;
    private ArticulationBody tongueClub;
    private ArticulationBody catchCarriage;
    private ArticulationBody catchClaw;
    private readonly List<ArticulationBody> tongueBearings = new List<ArticulationBody>();
    private readonly List<ArticulationBody> catchBearings = new List<ArticulationBody>();

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
        public float SteeringOffsetDegrees;
    }

    private void OnValidate()
    {
        UpgradeSerializedSettings();
    }

    private void Awake()
    {
        UpgradeSerializedSettings();
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

    private void UpgradeSerializedSettings()
    {
        // Unity keeps values serialized by older component versions. Version 5
        // adds the two constant-force springs and their visible winding reels.
        if (configurationVersion >= 5)
            return;

        maximumLinearSpeed = 0.09f;
        maximumYawSpeed = 0.7f;
        linearAcceleration = 0.25f;
        yawAcceleration = 1.5f;
        maximumWheelSpeedDegrees = 180f;
        eyeSpeedDegrees = 45f;
        eyeLimitDegrees = 30f;
        clubFrontDegrees = 0f;
        catchStoredDegrees = 0f;
        catchAtClubDegrees = 90f;
        maximumPullDegrees = -75f;
        railRadius = 0.221f;
        bearingRadius = 0.005f;
        constantForcePerSpringNewtons = 3f;
        springReturnVelocityDegrees = 360f;
        springDriveDamping = 4f;
        springReelRadius = 0.01f;
        configurationVersion = 5;
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
        // The CAD model's lateral axis is opposite Unity local X.
        if (keyboard.dKey.isPressed) translationInput.x -= 1f;
        if (keyboard.aKey.isPressed) translationInput.x += 1f;
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
        SetPositionTarget(cameraDriveGear, -cameraTarget * cameraDriveGearRatio);
        SetPositionTarget(leftEyeOutput, eyeTarget);
        SetPositionTarget(rightEyeOutput, eyeTarget);
        SetPositionTarget(leftEyeDriveGear, -eyeTarget * eyeDriveGearRatio);
        SetPositionTarget(rightEyeDriveGear, -eyeTarget * eyeDriveGearRatio);

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

        float clubDegrees = ReadJointDegrees(tongueClub);
        UpdateBearingTargets(tongueBearings, clubDegrees);
        UpdateBearingTargets(catchBearings, ReadJointDegrees(catchCarriage));
        UpdateSpringReelTargets(clubDegrees);

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

        // CAD link names describe the old assumed layout. On the actual robot,
        // front_wheel_link is the central rear wheel, while rear_left/right are
        // the two front wheels. Their mounting-angle signs are therefore opposite.
        AddWheelModule(bodies, "front_steer_link", "front_wheel_link", 90f);
        AddWheelModule(bodies, "rear_left_steer_link", "rear_left_wheel_link", -30f);
        AddWheelModule(bodies, "rear_right_steer_link", "rear_right_wheel_link", 30f);

        bodies.TryGetValue("camera_tilt_link", out cameraTilt);
        bodies.TryGetValue("camera_drive_gear_link", out cameraDriveGear);
        bodies.TryGetValue("left_eye_output_link", out leftEyeOutput);
        bodies.TryGetValue("left_eye_drive_gear_link", out leftEyeDriveGear);
        bodies.TryGetValue("right_eye_output_link", out rightEyeOutput);
        bodies.TryGetValue("right_eye_drive_gear_link", out rightEyeDriveGear);
        bodies.TryGetValue("right_club_spring_reel_link", out rightClubSpringReel);
        bodies.TryGetValue("left_club_spring_reel_link", out leftClubSpringReel);
        bodies.TryGetValue("tongue_club_link", out tongueClub);
        bodies.TryGetValue("catch_carriage_link", out catchCarriage);
        bodies.TryGetValue("catch_claw_link", out catchClaw);

        for (int index = 0; index < 6; index++)
        {
            if (bodies.TryGetValue($"tongue_bearing_{index}_link", out ArticulationBody tongueBearing))
                tongueBearings.Add(tongueBearing);
            if (bodies.TryGetValue($"catch_bearing_{index}_link", out ArticulationBody catchBearing))
                catchBearings.Add(catchBearing);
        }

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
        string wheelName,
        float steeringOffsetDegrees)
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
            LastSteeringTarget = 0f,
            SteeringOffsetDegrees = steeringOffsetDegrees
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
        ConfigurePositionDrive(cameraDriveGear, 40f, 6f, 2f);
        ConfigurePositionDrive(leftEyeOutput, 60f, 8f, 3f);
        ConfigurePositionDrive(rightEyeOutput, 60f, 8f, 3f);
        ConfigurePositionDrive(leftEyeDriveGear, 30f, 5f, 1f);
        ConfigurePositionDrive(rightEyeDriveGear, 30f, 5f, 1f);
        ConfigurePositionDrive(rightClubSpringReel, 20f, 2f, 0.10f);
        ConfigurePositionDrive(leftClubSpringReel, 20f, 2f, 0.10f);
        ConfigureClubSpringDrive();
        ConfigurePositionDrive(catchCarriage, 150f, 18f, 20f);
        ConfigurePositionDrive(catchClaw, 80f, 8f, 5f);

        foreach (ArticulationBody bearing in tongueBearings)
            ConfigurePositionDrive(bearing, 8f, 1f, 0.08f);
        foreach (ArticulationBody bearing in catchBearings)
            ConfigurePositionDrive(bearing, 8f, 1f, 0.08f);
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
        drive.targetVelocity = 0f;
        body.xDrive = drive;
    }

    private void ConfigureClubSpringDrive()
    {
        if (tongueClub == null)
            return;

        // The two spring forces act tangentially to the circular rail. Their
        // combined linear force therefore becomes an almost constant torque.
        float constantTorque =
            2f * Mathf.Max(0f, constantForcePerSpringNewtons) * Mathf.Max(railRadius, 0.001f);
        ArticulationDrive drive = tongueClub.xDrive;
        drive.stiffness = 0f;
        drive.damping = Mathf.Max(0.01f, springDriveDamping);
        drive.forceLimit = Mathf.Max(0.01f, constantTorque);
        drive.target = clubFrontDegrees;
        drive.targetVelocity = Mathf.Abs(springReturnVelocityDegrees);
        tongueClub.xDrive = drive;
        tongueClub.WakeUp();
    }

    private void ConfigureClubPullDrive()
    {
        ConfigurePositionDrive(tongueClub, 420f, 24f, 45f);
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
                float requestedAngle =
                    Mathf.Atan2(moduleVelocity.x, moduleVelocity.y) * Mathf.Rad2Deg +
                    module.SteeringOffsetDegrees;
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
        SetPositionTarget(catchCarriage, catchAtClubDegrees);
        yield return new WaitForSeconds(1.4f);
        clubIsCaptured = true;
        ConfigureClubPullDrive();

        // 2. Wind the wire. The 1:1 potentiometer follows this winding angle.
        // Pull-back distance determines the striking strength.
        float clubPullTarget = clubFrontDegrees + maximumPullDegrees * strikeStrength;
        float catchPullTarget = catchAtClubDegrees + maximumPullDegrees * strikeStrength;
        SetPositionTarget(catchCarriage, catchPullTarget);
        SetPositionTarget(tongueClub, clubPullTarget);
        yield return new WaitForSeconds(Mathf.Lerp(0.7f, 1.8f, strikeStrength));

        // 3. Open only the claw. The catch remains held by the wire, while the
        // constant-force spring sends the released club toward the front.
        SetPositionTarget(catchClaw, clawOpenDegrees);
        yield return new WaitForSeconds(0.12f);
        clubIsCaptured = false;
        ConfigureClubSpringDrive();
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
        SetPositionTarget(cameraDriveGear, 0f);
        SetPositionTarget(leftEyeOutput, 0f);
        SetPositionTarget(rightEyeOutput, 0f);
        SetPositionTarget(leftEyeDriveGear, 0f);
        SetPositionTarget(rightEyeDriveGear, 0f);
        clubIsCaptured = false;
        ConfigureClubSpringDrive();
        SetPositionTarget(catchCarriage, catchStoredDegrees);
        SetPositionTarget(catchClaw, 0f);
        potentiometerValue = potentiometerMaximum;
    }

    private float ReadPotentiometerValue()
    {
        float carriageDegrees = ReadJointDegrees(catchCarriage);

        float winding01 = Mathf.InverseLerp(
            catchAtClubDegrees,
            catchStoredDegrees,
            carriageDegrees);

        return Mathf.Lerp(potentiometerMinimum, potentiometerMaximum, winding01);
    }

    private static float ReadJointDegrees(ArticulationBody body)
    {
        if (body == null || body.jointPosition.dofCount == 0)
            return 0f;

        return body.jointPosition[0] * Mathf.Rad2Deg;
    }

    private void UpdateBearingTargets(List<ArticulationBody> bearings, float carrierDegrees)
    {
        float rollingRatio = railRadius / Mathf.Max(bearingRadius, 0.001f);
        float bearingDegrees = -carrierDegrees * rollingRatio;

        foreach (ArticulationBody bearing in bearings)
            SetPositionTarget(bearing, bearingDegrees);
    }

    private void UpdateSpringReelTargets(float clubDegrees)
    {
        float windingRatio = railRadius / Mathf.Max(springReelRadius, 0.001f);
        float reelDegrees = -(clubDegrees - clubFrontDegrees) * windingRatio;

        // The two reels are mirrored, so their visible winding directions are
        // opposite although both springs pull the club toward the front.
        SetPositionTarget(rightClubSpringReel, reelDegrees);
        SetPositionTarget(leftClubSpringReel, -reelDegrees);
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

        float targetWinding = Mathf.Clamp01(
            Mathf.Abs(maximumPullDegrees) * strikeStrength /
            Mathf.Max(Mathf.Abs(catchStoredDegrees - catchAtClubDegrees), 0.001f));
        int targetPotentiometer = Mathf.RoundToInt(Mathf.Lerp(
            potentiometerMinimum,
            potentiometerMaximum,
            targetWinding));

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
