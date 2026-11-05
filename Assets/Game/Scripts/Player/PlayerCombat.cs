using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StealthProto
{
    /// <summary>
    /// Melee (lunge + arc) and a hitscan gun
    /// while aiming. In first-person mode the gun fires along the camera.
    /// </summary>
    [RequireComponent(typeof(PlayerInputReader), typeof(PlayerMotor))]
    public class PlayerCombat : MonoBehaviour
    {
        [Header("Melee")]
        public int meleeDamage = 1;
        public float meleeRange = 1.6f;
        public float meleeArc = 120f;
        public float meleeWindup = 0.06f;
        public float meleeRecovery = 0.22f;
        public float meleeLunge = 8f;
        public float meleeHitStop = 0.07f;

        [Header("Gun")]
        public int fireDamage = 1;
        public float fireRange = 35f;
        public float fireCooldown = 0.28f;
        public float gunNoiseRadius = 18f;

        [Header("References")]
        public Camera aimCamera;
        public CameraRig rig;

        PlayerInputReader input;
        PlayerMotor motor;
        float busyUntil;
        float fireReadyAt;
        readonly Collider[] overlap = new Collider[32];
        readonly HashSet<Health> hitSet = new HashSet<Health>();

        public bool IsAiming { get; private set; }
        public Vector3 AimDirection { get; private set; }

        void Awake()
        {
            input = GetComponent<PlayerInputReader>();
            motor = GetComponent<PlayerMotor>();
            if (aimCamera == null) aimCamera = Camera.main;
        }

        void OnDisable()
        {
            if (motor != null)
            {
                motor.SetFacingOverride(null);
                motor.AttackLock = false;
            }
        }

        void Update()
        {
            bool firstPerson = rig != null && rig.IsFirstPerson;
            IsAiming = firstPerson || input.AimHeld;
            AimDirection = ComputeAim(firstPerson);

            if (!firstPerson && IsAiming) motor.SetFacingOverride(Flat(AimDirection));
            else motor.SetFacingOverride(null);

            if (Time.time < busyUntil || motor.IsDashing || motor.ExternalControl) return;

            if (IsAiming)
            {
                if (input.FirePressed && Time.time >= fireReadyAt) Fire(firstPerson);
            }
            else if (input.MeleePressed && !motor.IsProne)
            {
                StartCoroutine(MeleeRoutine());
            }
        }

        Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 0.0001f ? v.normalized : transform.forward;
        }

        Vector3 ComputeAim(bool firstPerson)
        {
            if (firstPerson && aimCamera != null) return aimCamera.transform.forward;

            Vector3 chest = transform.position + Vector3.up * 1.2f;
            if (input.UsingGamepad)
            {
                Vector2 stick = input.AimStick;
                if (stick.sqrMagnitude > 0.04f) return motor.CameraRelative(stick).normalized;
                Vector2 move = input.Move;
                if (move.sqrMagnitude > 0.04f) return motor.CameraRelative(move).normalized;
                return transform.forward;
            }

            if (aimCamera == null) return transform.forward;
            Ray ray = aimCamera.ScreenPointToRay(input.PointerPosition);

            // Cursor directly over an enemy (e.g. on a catwalk above): aim at them in 3D.
            if (Physics.Raycast(ray, out RaycastHit hit, 200f, ~0, QueryTriggerInteraction.Ignore))
            {
                var h = hit.collider.GetComponentInParent<Health>();
                if (h != null && h.transform != transform && !h.IsDead)
                    return ((h.transform.position + Vector3.up * 1.2f) - chest).normalized;
            }

            var plane = new Plane(Vector3.up, chest);
            if (plane.Raycast(ray, out float t))
            {
                Vector3 d = ray.GetPoint(t) - chest;
                d.y = 0f;
                if (d.sqrMagnitude > 0.01f) return d.normalized;
            }
            return transform.forward;
        }

        Vector3 MeleeDirection()
        {
            if (!input.UsingGamepad) return Flat(ComputeAim(false));
            Vector2 move = input.Move;
            if (move.sqrMagnitude > 0.04f) return Flat(motor.CameraRelative(move));
            return transform.forward;
        }

        IEnumerator MeleeRoutine()
        {
            Vector3 dir = MeleeDirection();
            transform.rotation = Quaternion.LookRotation(dir);
            busyUntil = Time.time + meleeWindup + meleeRecovery;
            motor.AttackLock = true;
            motor.AddLunge(dir * meleeLunge);

            yield return new WaitForSeconds(meleeWindup);

            Vector3 center = transform.position + Vector3.up + dir * 0.6f;
            int count = Physics.OverlapSphereNonAlloc(center, meleeRange, overlap, ~0, QueryTriggerInteraction.Ignore);
            bool hitAny = false;
            for (int i = 0; i < count; i++)
            {
                var h = overlap[i].GetComponentInParent<Health>();
                if (h == null || h.transform == transform || h.IsDead || !hitSet.Add(h)) continue;

                Vector3 to = h.transform.position - transform.position;
                if (Mathf.Abs(to.y) > 1.3f) continue;
                to.y = 0f;
                if (to.sqrMagnitude > 0.01f && Vector3.Angle(dir, to) > meleeArc * 0.5f) continue;

                var info = new DamageInfo
                {
                    amount = meleeDamage,
                    point = h.transform.position + Vector3.up,
                    direction = to.sqrMagnitude > 0.01f ? to.normalized : dir,
                    source = gameObject
                };
                if (h.TakeDamage(info)) hitAny = true;
            }
            hitSet.Clear();

            FX.Arc(transform.position + Vector3.up, dir, meleeRange + 0.6f, meleeArc,
                hitAny ? Color.white : new Color(1f, 1f, 1f, 0.35f), 0.08f);
            if (hitAny)
            {
                HitStop.Do(meleeHitStop);
                CameraRig.Shake(0.15f);
            }
            NoiseSystem.Emit(transform.position, hitAny ? 5f : 1.5f, 0.4f, null, SoundKind.Impact, gameObject);

            yield return new WaitForSeconds(meleeRecovery * 0.5f);
            motor.AttackLock = false;
        }

        void Fire(bool firstPerson)
        {
            fireReadyAt = Time.time + fireCooldown;
            Vector3 dir = AimDirection;
            Vector3 origin = firstPerson && aimCamera != null
                ? aimCamera.transform.position
                : transform.position + Vector3.up * (motor.IsProne ? 0.3f : 1.2f) + Flat(dir) * 0.45f;

            Vector3 end = origin + dir * fireRange;
            if (Occlusion.Raycast(origin, dir, fireRange, transform, null, out RaycastHit hit))
            {
                end = hit.point;
                var h = hit.collider.GetComponentInParent<Health>();
                if (h != null && h.transform != transform)
                {
                    var info = new DamageInfo { amount = fireDamage, point = hit.point, direction = dir, source = gameObject };
                    if (h.TakeDamage(info)) HitStop.Do(0.035f);
                }
            }

            Vector3 tracerStart = firstPerson ? origin + dir * 0.6f - Vector3.up * 0.15f : origin;
            FX.Line(tracerStart, end, new Color(1f, 0.9f, 0.4f, 0.9f), 0.04f, 0.06f);
            CameraRig.Shake(0.08f);
            NoiseSystem.Emit(transform.position, gunNoiseRadius, 1f, null, SoundKind.Gunshot, gameObject);
        }
    }
}
