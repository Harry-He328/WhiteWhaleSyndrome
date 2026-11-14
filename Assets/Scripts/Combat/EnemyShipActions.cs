using System.Collections;
using UnityEngine;

namespace WhiteWhale.Combat
{
    /// <summary>敌船基础动作。只完成动作，不决定下一招。</summary>
    public sealed class EnemyShipActions : MonoBehaviour
    {
        [Header("移动设置（海洋单位）")]
        [Min(0f)] public float moveSpeed = 4f;
        [Min(1f)] public float turnSpeed = 90f;
        [Min(1f)] public float attackDistance = 8f;
        [Min(1f)] public float followDistance = 11f;

        [Header("动作限制")]
        [Min(0.1f)] public float approachTimeout = 8f;
        [Min(0.1f)] public float turnTimeout = 3f;
        [Range(1f, 20f)] public float fireAngle = 6f;

        private EnemyActor actor;
        private Rigidbody body;
        private MovementSpeedModifier speedModifier;
        private Cannon broadsideCannon;
        private bool isMoving;
        private bool isTurning;
        private Quaternion targetRotation;
        public float CurrentSpeed { get; private set; }
        public int FiredCount { get; private set; }
        public bool IsInRange => actor != null && actor.CanAct && DistanceToPlayer() <= Mathf.Max(attackDistance, followDistance);

        public void Initialize(EnemyActor owner)
        {
            actor = owner;
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            speedModifier = GetComponent<MovementSpeedModifier>();
            FiredCount = 0;
            StopAction();
        }

        #region Movement Actions

        public IEnumerator MoveToPlayer()
        {
            float elapsed = 0f;
            while (actor.CanAct && DistanceToPlayer() > attackDistance && elapsed < approachTimeout)
            {
                SetDirection(DirectionToPlayer());
                isMoving = true;
                elapsed += Time.deltaTime;
                yield return null;
            }
            StopAction();
        }

        public IEnumerator TurnBroadsideToPlayer()
        {
            broadsideCannon = null;
            // 选择所需转向最少的已安装侧舷炮，炮口方向仍固定在船上。
            float smallestAngle = float.PositiveInfinity;
            foreach (Cannon cannon in actor.Artillery.InstalledCannons)
            {
                float angle = Vector3.Angle(Flat(cannon.transform.forward), DirectionToPlayer());
                if (angle >= smallestAngle) continue;
                smallestAngle = angle;
                broadsideCannon = cannon;
            }
            if (broadsideCannon == null) yield break;

            float elapsed = 0f;
            while (actor.CanAct && IsInRange && elapsed < turnTimeout)
            {
                Vector3 direction = DirectionToPlayer();
                float difference = Vector3.SignedAngle(Flat(broadsideCannon.transform.forward), direction, Vector3.up);
                if (Mathf.Abs(difference) <= fireAngle) break;
                targetRotation = Quaternion.AngleAxis(difference, Vector3.up) * body.rotation;
                isTurning = true;
                elapsed += Time.deltaTime;
                yield return null;
            }
            StopAction();
        }

        public void StopAction()
        {
            isMoving = false;
            isTurning = false;
            CurrentSpeed = 0f;
            if (body != null)
            {
                // 取消尚未完成的 MovePosition，避免战斗结束后仍推进最后一步。
                body.position = body.position;
                body.rotation = body.rotation;
            }
        }

        #endregion

        #region Attack Actions

        public void FireBroadside()
        {
            if (!actor.CanAct || !IsInRange || !actor.Battle.EnemyAutoFire || broadsideCannon == null) return;
            if (Vector3.Angle(Flat(broadsideCannon.transform.forward), DirectionToPlayer()) > fireAngle) return;
            // 只击发面向玩家的一舷，不向另一侧额外生成无关炮弹。
            foreach (Cannon cannon in actor.Artillery.InstalledCannons)
                if (Vector3.Angle(Flat(cannon.transform.forward), DirectionToPlayer()) <= fireAngle && cannon.TryFire()) FiredCount++;
        }

        public IEnumerator Wait(float seconds, bool checkRange = false)
        {
            float elapsed = 0f;
            while (actor.CanAct && elapsed < seconds)
            {
                if (checkRange && !IsInRange) yield break;
                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        #endregion

        #region Physics

        public void StepPhysics(OceanSurface ocean)
        {
            Quaternion rotation = isTurning
                ? Quaternion.RotateTowards(body.rotation, targetRotation, turnSpeed * Time.fixedDeltaTime)
                : body.rotation;
            Vector3 position = body.position;
            CurrentSpeed = isMoving ? moveSpeed * (speedModifier != null ? speedModifier.Multiplier : 1f) : 0f;
            Vector3 direction = rotation * Vector3.forward;
            float distance = CurrentSpeed * Time.fixedDeltaTime;
            if (distance > 0f && body.SweepTest(direction, out RaycastHit hit, distance + 0.1f, QueryTriggerInteraction.Ignore))
                distance = Mathf.Min(distance, Mathf.Max(0f, hit.distance - 0.1f));
            position += direction * distance;
            if (ocean != null) position.y = ocean.GetHeight(position) - 0.15f;
            body.MoveRotation(rotation);
            body.MovePosition(position);
        }

        private void SetDirection(Vector3 direction)
        {
            if (direction.sqrMagnitude < 0.001f) return;
            targetRotation = Quaternion.LookRotation(direction, Vector3.up);
            isTurning = true;
        }

        private float DistanceToPlayer() => Flat(actor.Battle.Player.transform.position - body.position).magnitude;
        private Vector3 DirectionToPlayer() => Flat(actor.Battle.Player.transform.position - body.position).normalized;
        private static Vector3 Flat(Vector3 value) => Vector3.ProjectOnPlane(value, Vector3.up);

        private void OnDisable() => StopAction();

        #endregion
    }
}
