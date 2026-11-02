using System.Collections.Generic;
using UnityEngine;

namespace StealthProto
{
    public interface INoiseListener
    {
        Vector3 ListenPosition { get; }
        /// <param name="loudness">0..1. Footsteps ~0.5, gunshots 1.</param>
        void HearNoise(Vector3 position, float loudness);
    }

    /// <summary>Simple radius-based hearing. Noise travels through walls for now.</summary>
    public static class NoiseSystem
    {
        public static bool ShowNoise = true;
        static readonly List<INoiseListener> Listeners = new List<INoiseListener>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Listeners.Clear();

        public static void Register(INoiseListener listener)
        {
            if (!Listeners.Contains(listener)) Listeners.Add(listener);
        }

        public static void Unregister(INoiseListener listener) => Listeners.Remove(listener);

        public static void Emit(Vector3 position, float radius, float loudness = 0.5f, INoiseListener source = null)
        {
            if (radius <= 0f) return;
            float r2 = radius * radius;
            for (int i = Listeners.Count - 1; i >= 0; i--)
            {
                var listener = Listeners[i];
                if (listener == source) continue;
                if ((listener.ListenPosition - position).sqrMagnitude <= r2)
                    listener.HearNoise(position, loudness);
            }
            if (ShowNoise) FX.NoiseRing(position + Vector3.up * 0.05f, radius, loudness);
        }
    }
}
