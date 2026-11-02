using UnityEngine;

namespace StealthProto
{
    /// <summary>
    /// Readability cue: the player's body darkens to a cold silhouette when hidden in shadow
    /// and brightens when lit, so you can read your own visibility at a glance.
    /// </summary>
    public class PlayerShadowTint : MonoBehaviour
    {
        public Renderer[] renderers;
        public Color litColor = new Color(0.25f, 0.9f, 1f);
        public Color hiddenColor = new Color(0.06f, 0.1f, 0.22f);

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        LightExposure exposure;
        Health health;
        MaterialPropertyBlock block;

        void Awake()
        {
            exposure = GetComponent<LightExposure>();
            health = GetComponent<Health>();
            block = new MaterialPropertyBlock();
        }

        void LateUpdate()
        {
            if (exposure == null || renderers == null) return;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.1f, exposure.hiddenThreshold + 0.15f, exposure.Exposure));
            Color c = Color.Lerp(hiddenColor, litColor, t);
            if (health != null && health.Invulnerable) c = Color.Lerp(c, Color.white, 0.5f);
            foreach (var r in renderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor(BaseColorId, c);
                r.SetPropertyBlock(block);
            }
        }
    }
}
