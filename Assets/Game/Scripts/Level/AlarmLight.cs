using UnityEngine;

namespace StealthProto
{
    /// <summary>
    /// The lasting cost of an alert: this light is off until the room is first alerted, then
    /// stays on for good, shrinking the dark. Put it on a Light (with a StealthLight).
    /// </summary>
    [RequireComponent(typeof(Light))]
    public class AlarmLight : MonoBehaviour
    {
        Light lightComponent;

        void Awake()
        {
            lightComponent = GetComponent<Light>();
            lightComponent.enabled = GuardBrain.RoomAlerted;
        }

        void OnEnable() => GuardBrain.RoomAlertRaised += TurnOn;
        void OnDisable() => GuardBrain.RoomAlertRaised -= TurnOn;

        void TurnOn()
        {
            if (lightComponent != null) lightComponent.enabled = true;
        }
    }
}
