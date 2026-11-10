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
        /// <summary>The SoundGrid space the sound was made in. Only that space hears it.</summary>
        public int space;
    }

    /// <summary>
    /// Hearing, plus a feed of every sound for Focus mode.
    /// How noise travels: a noise is a straight-line circle of its radius. A listener inside the
    /// circle hears it if they're in the same space (SoundGrid): any open way round counts, however
    /// long, and only a fully sealed space doesn't hear it. Open doorways and open air connect
    /// spaces; closed doors, vents and grates don't. A noise made inside a vent comes out at the
    /// vent's openings (full range from each); inside a floor grate it's heard from the floor above
    /// (SoundDuct).
    /// Rule: sound is always shown, never only heard. Critical sounds (loudness 0.9+) always get a
    /// visible ring; the rest are shown by Focus, or by F2 for debugging. Rings stop at walls that
    /// seal the sound in.
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

        static readonly List<Vector3> Origins = new List<Vector3>();
        static readonly List<INoiseListener> Heard = new List<INoiseListener>();

        /// <summary>A sound guards within radius hear, if they're in the same space.</summary>
        public static void Emit(Vector3 position, float radius, float loudness = 0.5f, INoiseListener ignore = null,
            SoundKind kind = SoundKind.Movement, GameObject source = null)
        {
            if (radius <= 0f) return;
            float r2 = radius * radius;
            SoundDuct.ResolveOrigins(position, Origins);
            Heard.Clear();
            foreach (var origin in Origins)
            {
                int space = SoundGrid.SpaceAt(origin);
                for (int i = Listeners.Count - 1; i >= 0; i--)
                {
                    var listener = Listeners[i];
                    if (listener == ignore || Heard.Contains(listener)) continue;
                    Vector3 at = listener.ListenPosition;
                    if ((at - origin).sqrMagnitude > r2) continue;
                    if (!SoundGrid.Connected(space, SoundGrid.SpaceAt(at))) continue;
                    Heard.Add(listener);
                    listener.HearNoise(origin, loudness);
                }
                Publish(origin, radius, loudness, kind, source, true, space);
            }
        }

        /// <summary>A sound guards ignore (e.g. guard footsteps). Only Focus shows it.</summary>
        public static void Ambient(Vector3 position, float radius, float loudness, SoundKind kind, GameObject source)
        {
            SoundDuct.ResolveOrigins(position, Origins);
            foreach (var origin in Origins)
                Publish(origin, radius, loudness, kind, source, false, SoundGrid.SpaceAt(origin));
        }

        /// <summary>Would a listener at this point hear the sound (in range and in the same space)?</summary>
        public static bool Reaches(SoundEvent e, Vector3 listener)
        {
            if ((listener - e.position).sqrMagnitude > e.radius * e.radius) return false;
            return SoundGrid.Connected(e.space, SoundGrid.SpaceAt(listener));
        }

        static void Publish(Vector3 position, float radius, float loudness, SoundKind kind, GameObject source, bool alerts, int space)
        {
            if (ShowNoise || loudness >= CriticalLoudness)
                FX.NoiseRing(position + Vector3.up * 0.05f, radius, loudness, space);
            SoundMade?.Invoke(new SoundEvent
            {
                position = position,
                radius = radius,
                loudness = loudness,
                kind = kind,
                source = source,
                alertsGuards = alerts,
                time = Time.time,
                space = space
            });
        }
    }
}
