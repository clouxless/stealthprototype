using System;
using System.Collections.Generic;
using UnityEngine;

namespace StealthProto
{
    public interface INoiseListener
    {
        Vector3 ListenPosition { get; }
        /// <param name="loudness">0..1. Footsteps ~0.4, gunshots 1.</param>
        void HearNoise(Vector3 position, float loudness);
    }

    public enum SoundKind { Movement, Footstep, Impact, Gunshot, Shout, Knock, Struggle }

    public struct SoundEvent
    {
        public Vector3 position;
        public float radius;
        public float loudness;
        public SoundKind kind;
        /// <summary>Who made it (a guard, the player), if known.</summary>
        public GameObject source;
        /// <summary>False for sounds guards ignore (guard footsteps): they exist for Focus only.</summary>
        public bool alertsGuards;
        public float time;
    }

    /// <summary>
    /// Radius-based hearing (noise travels through walls for now), plus a feed of every sound for
    /// Focus mode. Rule: sound is always shown, never only heard. Critical sounds (loudness 0.9+)
    /// always get a visible ring; the rest are shown by Focus, or by F2 for debugging.
    /// </summary>
    public static class NoiseSystem
    {
        /// <summary>Debug: show rings for every sound, not just critical ones (F2).</summary>
        public static bool ShowNoise = false;
        public const float CriticalLoudness = 0.9f;

        /// <summary>Every sound, guard-audible or not.</summary>
        public static event Action<SoundEvent> SoundMade;

        static readonly List<INoiseListener> Listeners = new List<INoiseListener>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Listeners.Clear();
            SoundMade = null;
        }

        public static void Register(INoiseListener listener)
        {
            if (!Listeners.Contains(listener)) Listeners.Add(listener);
        }

        public static void Unregister(INoiseListener listener) => Listeners.Remove(listener);

        /// <summary>A sound guards within radius hear.</summary>
        public static void Emit(Vector3 position, float radius, float loudness = 0.5f, INoiseListener ignore = null,
            SoundKind kind = SoundKind.Movement, GameObject source = null)
        {
            if (radius <= 0f) return;
            float r2 = radius * radius;
            for (int i = Listeners.Count - 1; i >= 0; i--)
            {
                var listener = Listeners[i];
                if (listener == ignore) continue;
                if ((listener.ListenPosition - position).sqrMagnitude <= r2)
                    listener.HearNoise(position, loudness);
            }
            Publish(position, radius, loudness, kind, source, true);
        }

        /// <summary>A sound guards ignore (e.g. guard footsteps). Only Focus shows it.</summary>
        public static void Ambient(Vector3 position, float radius, float loudness, SoundKind kind, GameObject source)
        {
            Publish(position, radius, loudness, kind, source, false);
        }

        static void Publish(Vector3 position, float radius, float loudness, SoundKind kind, GameObject source, bool alerts)
        {
            if (ShowNoise || loudness >= CriticalLoudness)
                FX.NoiseRing(position + Vector3.up * 0.05f, radius, loudness);
            SoundMade?.Invoke(new SoundEvent
            {
                position = position,
                radius = radius,
                loudness = loudness,
                kind = kind,
                source = source,
                alertsGuards = alerts,
                time = Time.time
            });
        }
    }
}
