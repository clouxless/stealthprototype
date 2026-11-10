using UnityEngine;
using UnityEngine.SceneManagement;

namespace StealthProto
{
    /// <summary>
    /// Exit trigger for test sandboxes. When the player walks in, it shows a [PH] result card
    /// (time, times a guard went alert, takedowns) and restarts the scene.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class SandboxGoal : MonoBehaviour
    {
        public string objective = "Reach the lit gate at the north end";
        public float restartDelay = 5f;

        float startTime, finishTime;
        int alerts, takedowns;
        bool reached;
        GUIStyle card, corner;

        void Reset() => GetComponent<BoxCollider>().isTrigger = true;

        void Start()
        {
            GetComponent<BoxCollider>().isTrigger = true;
            startTime = Time.time;
            foreach (var guard in GuardBrain.All) guard.StateChanged += OnGuardState;
        }

        void OnDestroy()
        {
            foreach (var guard in GuardBrain.All) guard.StateChanged -= OnGuardState;
        }

        void OnGuardState(GuardState state)
        {
            if (reached) return;
            if (state == GuardState.Alert) alerts++;
            else if (state == GuardState.Down) takedowns++;
        }

        void OnTriggerEnter(Collider other)
        {
            if (reached || other.GetComponentInParent<PlayerMotor>() == null) return;
            reached = true;
            finishTime = Time.time - startTime;

            var player = other.GetComponentInParent<PlayerMotor>().gameObject;
            var combat = player.GetComponent<PlayerCombat>();
            if (combat != null) combat.enabled = false;
            var context = player.GetComponent<PlayerContextActions>();
            if (context != null) context.enabled = false;
            Invoke(nameof(Restart), restartDelay);
        }

        void Restart()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        void OnGUI()
        {
            if (card == null)
            {
                card = new GUIStyle(GUI.skin.box) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                card.normal.textColor = Color.white;
                corner = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.UpperRight };
                corner.normal.textColor = Color.white;
            }

            float elapsed = reached ? finishTime : Time.time - startTime;
            string clock = $"{(int)(elapsed / 60f)}:{elapsed % 60f:00.0}";
            GUI.Label(new Rect(Screen.width - 412, 12, 400, 60),
                $"[PH] SANDBOX: {objective}\n{clock}   alerts {alerts}   takedowns {takedowns}", corner);

            if (!reached) return;
            string ghost = alerts == 0 ? "\nNo alerts: clean run" : "";
            GUI.Box(new Rect(Screen.width * 0.5f - 220, Screen.height * 0.35f, 440, 130),
                $"[PH] EXIT REACHED\nTime {clock}   Alerts {alerts}   Takedowns {takedowns}{ghost}\nRestarting...", card);
        }
    }
}
