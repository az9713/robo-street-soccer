using UnityEngine;

namespace RoboStreetSoccer
{
    /// <summary>
    /// Visual-only, generic-rig leg correction. Attach to Robot Visual and call
    /// Configure after its Animator and the rigid-body root have been created.
    /// It never moves the rigid body, colliders, ball, or the striking right
    /// foot during an authored action.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public sealed class SoccerFootPlant : MonoBehaviour
    {
        sealed class Leg
        {
            public Transform thigh;
            public Transform shin;
            public Transform foot;
            public float upperLength;
            public float lowerLength;
            public bool planted;
            public bool plantedAsAction;
            public Vector3 target;
            public float replantAfter;
            public float plantedSince;
            public Quaternion authoredThighLocalRotation;
            public Quaternion authoredShinLocalRotation;
            public Quaternion authoredFootLocalRotation;
        }

        [SerializeField] float groundHeightSlack = .025f;
        [SerializeField] float maximumPlantOffset = .35f;
        [SerializeField] float softenAfterOffset = .18f;
        [SerializeField] float replantDelay = .06f;

        [SerializeField] Animator animator;
        [SerializeField] Transform locomotionRoot;
        SoccerRobot robot;
        Leg left;
        Leg right;
        int lastStateHash;
        float lastNormalizedTime = float.NaN;

        public bool IsConfigured { get; private set; }
        public string CurrentPlantedFoot { get; private set; } = "None";
        public float CurrentStanceDrift { get; private set; }
        public float MaximumUncorrectedStanceDrift { get; private set; }
        public float MaximumResidualStanceDrift { get; private set; }
        public float CurrentActionNormalizedTime { get; private set; }
        public float CurrentPlantDuration { get; private set; }
        public float LongestContinuousPlantSeconds { get; private set; }
        public int PlantAcquisitionCount { get; private set; }
        public int LocomotionPlantAcquisitionCount { get; private set; }
        public float LongestContinuousLocomotionPlantSeconds { get; private set; }
        public int ActionPlantAcquisitionCount { get; private set; }
        public float LongestContinuousActionPlantSeconds { get; private set; }
        public float MaximumLocomotionResidualDrift { get; private set; }
        public float MaximumActionResidualDrift { get; private set; }
        public string MaximumActionResidualName { get; private set; } = "None";
        public float MaximumActionResidualNormalizedTime { get; private set; }

        public void ResetTelemetry()
        {
            CurrentStanceDrift = 0f;
            MaximumUncorrectedStanceDrift = 0f;
            MaximumResidualStanceDrift = 0f;
            CurrentPlantDuration = 0f;
            LongestContinuousPlantSeconds = 0f;
            PlantAcquisitionCount = 0;
            LocomotionPlantAcquisitionCount = 0;
            LongestContinuousLocomotionPlantSeconds = 0f;
            ActionPlantAcquisitionCount = 0;
            LongestContinuousActionPlantSeconds = 0f;
            MaximumLocomotionResidualDrift = 0f;
            MaximumActionResidualDrift = 0f;
            MaximumActionResidualName = "None";
            MaximumActionResidualNormalizedTime = 0f;
            if (left != null && left.planted) left.plantedSince = Time.time;
            if (right != null && right.planted) right.plantedSince = Time.time;
        }

        /// <summary>
        /// Drops visual stance anchors after an external rigid-body teleport.
        /// This retains accumulated telemetry, but prevents the next rendered
        /// pose from measuring the teleport distance as foot sliding.
        /// </summary>
        public void ResetAnchorsAfterTeleport()
        {
            Release(left);
            Release(right);
            ResetAnchor(left);
            ResetAnchor(right);
            CurrentPlantedFoot = "None";
            CurrentPlantDuration = 0f;
            CurrentStanceDrift = 0f;
            lastStateHash = int.MinValue;
            lastNormalizedTime = float.NaN;
            CaptureAuthoredPose();
        }

        void Awake()
        {
            if (!animator) animator = GetComponent<Animator>();
            if (!locomotionRoot) locomotionRoot = transform.parent ? transform.parent : transform;
            Configure(animator, locomotionRoot);
        }

        /// <summary>Configures this visual component without modifying gameplay state.</summary>
        public bool Configure(Animator targetAnimator, Transform targetLocomotionRoot)
        {
            animator = targetAnimator;
            locomotionRoot = targetLocomotionRoot;
            robot = locomotionRoot ? locomotionRoot.GetComponent<SoccerRobot>() : GetComponentInParent<SoccerRobot>();
            if (!animator || !locomotionRoot) return false;

            Transform hips = FindBone(animator.transform, "Hips");
            left = CreateLeg(FindBone(animator.transform, "Thigh.L"), FindBone(animator.transform, "Shin.L"), FindBone(animator.transform, "Foot.L"));
            right = CreateLeg(FindBone(animator.transform, "Thigh.R"), FindBone(animator.transform, "Shin.R"), FindBone(animator.transform, "Foot.R"));
            IsConfigured = hips && left != null && right != null;
            lastStateHash = int.MinValue;
            lastNormalizedTime = float.NaN;
            if (!IsConfigured)
            {
                Release(left);
                Release(right);
            }
            return IsConfigured;
        }

        void LateUpdate()
        {
            if (!IsConfigured || !animator || !locomotionRoot) return;
            CurrentStanceDrift = 0f;
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            CurrentActionNormalizedTime = Mathf.Repeat(state.normalizedTime, 1f);

            // AnimatePhysics can leave several rendered LateUpdate calls between
            // animator evaluations. Restore the captured authored pose before
            // measuring each one, otherwise a previous IK result would conceal
            // true root-relative stance drift.
            if (state.fullPathHash != lastStateHash || !Mathf.Approximately(state.normalizedTime, lastNormalizedTime))
            {
                CaptureAuthoredPose();
                lastStateHash = state.fullPathHash;
                lastNormalizedTime = state.normalizedTime;
            }
            RestoreAuthoredPose();

            // Do not correct through a transition: a blend can temporarily make
            // either leg appear grounded and would create a visual snap.
            if (animator.IsInTransition(0))
            {
                Release(left);
                Release(right);
                CurrentPlantedFoot = "None";
                CurrentPlantDuration = 0f;
                return;
            }

            RobotAction action = robot ? robot.CurrentAction : RobotAction.None;
            if (action != RobotAction.None)
            {
                // Soccer actions strike with the authored right foot. Correct
                // only the left support leg, never the striking leg in wind-up,
                // contact, or recovery.
                if (action == RobotAction.Dribble || action == RobotAction.Receive || action == RobotAction.Pass
                    || action == RobotAction.Shoot || action == RobotAction.Tackle)
                    UpdateLeg(left, ActionSupportPhase(action), true, action.ToString());
                else Release(left);
                Release(right);
                UpdatePlantedLabel();
                return;
            }

            bool locomoting = state.IsName("Run") || state.IsName("Turn");
            if (!locomoting)
            {
                Release(left);
                Release(right);
                CurrentPlantedFoot = "None";
                CurrentPlantDuration = 0f;
                return;
            }

            // The lower animated ankle is the stance candidate. A small slack
            // keeps a stable double-support instant at a stride transition.
            bool leftCandidate = left.foot.position.y <= right.foot.position.y + groundHeightSlack;
            bool rightCandidate = right.foot.position.y <= left.foot.position.y + groundHeightSlack;
            UpdateLeg(left, leftCandidate, false, "None");
            UpdateLeg(right, rightCandidate, false, "None");
            UpdatePlantedLabel();
        }

        void UpdateLeg(Leg leg, bool candidate, bool actionPlant, string actionName)
        {
            if (leg == null || !candidate)
            {
                Release(leg);
                return;
            }

            if (!leg.planted)
            {
                if (Time.time < leg.replantAfter) return;
                leg.target = leg.foot.position;
                leg.planted = true;
                leg.plantedAsAction = actionPlant;
                leg.plantedSince = Time.time;
                PlantAcquisitionCount++;
                if (actionPlant) ActionPlantAcquisitionCount++;
                else LocomotionPlantAcquisitionCount++;
            }
            else if (leg.plantedAsAction != actionPlant)
            {
                // Do not carry a locomotion acquisition into an action (or
                // vice versa): the two telemetry buckets represent distinct
                // visual support intervals.
                Release(leg);
                if (Time.time < leg.replantAfter) return;
                leg.target = leg.foot.position;
                leg.planted = true;
                leg.plantedAsAction = actionPlant;
                leg.plantedSince = Time.time;
                PlantAcquisitionCount++;
                if (actionPlant) ActionPlantAcquisitionCount++;
                else LocomotionPlantAcquisitionCount++;
            }

            float uncorrected = PlanarDistance(leg.foot.position, leg.target);
            CurrentStanceDrift = Mathf.Max(CurrentStanceDrift, uncorrected);
            MaximumUncorrectedStanceDrift = Mathf.Max(MaximumUncorrectedStanceDrift, uncorrected);
            if (uncorrected > maximumPlantOffset)
            {
                // A bounded solution is preferable to an inverted or stretched
                // knee. Re-anchor on the next safe stance sample.
                MaximumResidualStanceDrift = Mathf.Max(MaximumResidualStanceDrift, uncorrected);
                RecordResidual(uncorrected, actionPlant, actionName);
                Release(leg);
                leg.replantAfter = Time.time + replantDelay;
                return;
            }

            float weight = 1f - Mathf.InverseLerp(softenAfterOffset, maximumPlantOffset, uncorrected);
            if (weight <= .001f)
            {
                MaximumResidualStanceDrift = Mathf.Max(MaximumResidualStanceDrift, uncorrected);
                RecordResidual(uncorrected, actionPlant, actionName);
                return;
            }
            Vector3 correctedTarget = Vector3.Lerp(leg.foot.position, leg.target, weight);
            SolveTwoBone(leg, correctedTarget);
            float residual = PlanarDistance(leg.foot.position, leg.target);
            MaximumResidualStanceDrift = Mathf.Max(MaximumResidualStanceDrift, residual);
            RecordResidual(residual, actionPlant, actionName);
        }

        bool ActionSupportPhase(RobotAction action)
        {
            // The left support foot is meaningful only around its authored
            // strike/cushion beat. Releasing it outside this narrow interval
            // avoids pretending a full moving wind-up is a stationary plant.
            float contact = action == RobotAction.Dribble || action == RobotAction.Receive ? .3667f
                : action == RobotAction.Pass ? .5333f
                : action == RobotAction.Shoot ? .6333f : .4667f;
            float duration = action == RobotAction.Dribble ? .8f : action == RobotAction.Receive ? .8667f
                : action == RobotAction.Pass ? 1.1333f : action == RobotAction.Shoot ? 1.4f : 1.0667f;
            float normalizedContact = contact / duration;
            return Mathf.Abs(CurrentActionNormalizedTime - normalizedContact) <= .10f;
        }

        void RecordResidual(float residual, bool actionPlant, string actionName)
        {
            if (!actionPlant)
            {
                MaximumLocomotionResidualDrift = Mathf.Max(MaximumLocomotionResidualDrift, residual);
                return;
            }
            if (residual <= MaximumActionResidualDrift) return;
            MaximumActionResidualDrift = residual;
            MaximumActionResidualName = actionName;
            MaximumActionResidualNormalizedTime = CurrentActionNormalizedTime;
        }

        static void SolveTwoBone(Leg leg, Vector3 target)
        {
            Vector3 hip = leg.thigh.position;
            Vector3 knee = leg.shin.position;
            Vector3 ankle = leg.foot.position;
            Vector3 toTarget = target - hip;
            float distance = toTarget.magnitude;
            if (distance < .0001f) return;

            float minimum = Mathf.Abs(leg.upperLength - leg.lowerLength) + .005f;
            float maximum = leg.upperLength + leg.lowerLength - .005f;
            distance = Mathf.Clamp(distance, minimum, maximum);
            Vector3 direction = toTarget / toTarget.magnitude;

            Vector3 currentBend = knee - hip;
            Vector3 normal = Vector3.Cross(direction, currentBend);
            if (normal.sqrMagnitude < .00001f) normal = Vector3.Cross(direction, leg.thigh.right);
            if (normal.sqrMagnitude < .00001f) return;
            normal.Normalize();
            Vector3 bendDirection = Vector3.Cross(normal, direction).normalized;
            if (Vector3.Dot(bendDirection, currentBend) < 0f) bendDirection = -bendDirection;

            float along = (leg.upperLength * leg.upperLength - leg.lowerLength * leg.lowerLength + distance * distance) / (2f * distance);
            float height = Mathf.Sqrt(Mathf.Max(0f, leg.upperLength * leg.upperLength - along * along));
            Vector3 desiredKnee = hip + direction * along + bendDirection * height;
            Quaternion animatedFootRotation = leg.foot.rotation;

            Vector3 upperVector = knee - hip;
            Vector3 desiredUpper = desiredKnee - hip;
            if (upperVector.sqrMagnitude > .000001f && desiredUpper.sqrMagnitude > .000001f)
                leg.thigh.rotation = Quaternion.FromToRotation(upperVector, desiredUpper) * leg.thigh.rotation;

            Vector3 lowerVector = leg.foot.position - leg.shin.position;
            Vector3 desiredLower = target - leg.shin.position;
            if (lowerVector.sqrMagnitude > .000001f && desiredLower.sqrMagnitude > .000001f)
                leg.shin.rotation = Quaternion.FromToRotation(lowerVector, desiredLower) * leg.shin.rotation;

            // Preserve the authored foot orientation; only the two leg segments
            // rotate, which avoids turning an instep contact into a different pose.
            leg.foot.rotation = animatedFootRotation;
        }

        static Leg CreateLeg(Transform thigh, Transform shin, Transform foot)
        {
            if (!thigh || !shin || !foot) return null;
            return new Leg
            {
                thigh = thigh,
                shin = shin,
                foot = foot,
                upperLength = Vector3.Distance(thigh.position, shin.position),
                lowerLength = Vector3.Distance(shin.position, foot.position),
            };
        }

        static Transform FindBone(Transform root, string boneName)
        {
            if (root.name == boneName) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindBone(root.GetChild(i), boneName);
                if (found) return found;
            }
            return null;
        }

        static float PlanarDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        static void ResetAnchor(Leg leg)
        {
            if (leg == null) return;
            leg.planted = false;
            leg.plantedAsAction = false;
            leg.target = Vector3.zero;
            leg.replantAfter = 0f;
            leg.plantedSince = 0f;
        }

        void Release(Leg leg)
        {
            if (leg == null || !leg.planted) return;
            RecordPlantDuration(leg, Time.time - leg.plantedSince);
            leg.planted = false;
        }

        void UpdatePlantedLabel()
        {
            if (left.planted && right.planted) CurrentPlantedFoot = "Both";
            else if (left.planted) CurrentPlantedFoot = "Left";
            else if (right.planted) CurrentPlantedFoot = "Right";
            else CurrentPlantedFoot = "None";
            float leftDuration = left.planted ? Time.time - left.plantedSince : 0f;
            float rightDuration = right.planted ? Time.time - right.plantedSince : 0f;
            CurrentPlantDuration = Mathf.Max(leftDuration, rightDuration);
            RecordLivePlantDuration(left);
            RecordLivePlantDuration(right);
        }

        void RecordLivePlantDuration(Leg leg)
        {
            if (leg != null && leg.planted)
                RecordPlantDuration(leg, Time.time - leg.plantedSince);
        }

        void RecordPlantDuration(Leg leg, float duration)
        {
            LongestContinuousPlantSeconds = Mathf.Max(LongestContinuousPlantSeconds, duration);
            if (leg.plantedAsAction)
                LongestContinuousActionPlantSeconds = Mathf.Max(LongestContinuousActionPlantSeconds, duration);
            else
                LongestContinuousLocomotionPlantSeconds = Mathf.Max(LongestContinuousLocomotionPlantSeconds, duration);
        }

        void CaptureAuthoredPose()
        {
            CaptureAuthoredPose(left);
            CaptureAuthoredPose(right);
        }

        static void CaptureAuthoredPose(Leg leg)
        {
            if (leg == null) return;
            leg.authoredThighLocalRotation = leg.thigh.localRotation;
            leg.authoredShinLocalRotation = leg.shin.localRotation;
            leg.authoredFootLocalRotation = leg.foot.localRotation;
        }

        void RestoreAuthoredPose()
        {
            RestoreAuthoredPose(left);
            RestoreAuthoredPose(right);
        }

        static void RestoreAuthoredPose(Leg leg)
        {
            if (leg == null) return;
            leg.thigh.localRotation = leg.authoredThighLocalRotation;
            leg.shin.localRotation = leg.authoredShinLocalRotation;
            leg.foot.localRotation = leg.authoredFootLocalRotation;
        }
    }
}
