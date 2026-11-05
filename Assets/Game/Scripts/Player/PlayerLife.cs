using UnityEngine;
using UnityEngine.SceneManagement;

namespace StealthProto
{
    /// <summary>Hit feedback for the player and a scene restart on death.</summary>
    [RequireComponent(typeof(Health))]
    public class PlayerLife : MonoBehaviour
    {
        public float restartDelay = 2.5f;
        Health health;

        void Awake() => health = GetComponent<Health>();

        void OnEnable()
        {
            health.Damaged += OnDamaged;
            health.Died += OnDied;
        }

        void OnDisable()
        {
            health.Damaged -= OnDamaged;
            health.Died -= OnDied;
        }

        void OnDamaged(DamageInfo info)
        {
            HitStop.Do(0.06f);
            CameraRig.Shake(0.35f);
        }

        void OnDied(DamageInfo info)
        {
            var motor = GetComponent<PlayerMotor>();
            if (motor != null) motor.enabled = false;
            var combat = GetComponent<PlayerCombat>();
            if (combat != null) combat.enabled = false;
            var context = GetComponent<PlayerContextActions>();
            if (context != null) context.enabled = false;
            var focus = GetComponent<FocusMode>();
            if (focus != null) focus.enabled = false;
            var cover = GetComponent<PlayerCover>();
            if (cover != null) cover.enabled = false;
            Invoke(nameof(Restart), restartDelay);
        }

        void Restart()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }
    }
}
