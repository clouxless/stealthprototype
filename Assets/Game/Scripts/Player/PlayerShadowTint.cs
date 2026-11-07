using UnityEngine;

namespace StealthProto
{
    /// <summary>
    /// Shows the player's light tier on the character itself, no meter: a near-black silhouette
    /// in Dark, a muted body in Dim, full bright colour in Lit. Changes snap quickly so the tier
    /// boundary reads.
    /// </summary>
    public class PlayerShadowTint : MonoBehaviour
    {
        public Renderer[] renderers;
        public Color darkColor = new Color(0.04f, 0.06f, 0.14f);
        public Color dimColor = new Color(0.12f, 0.42f, 0.55f);
        public Color litColor = new Color(0.35f, 0.95f, 1f);
        [Tooltip("How fast the colour snaps to a new tier.")]
        public float snapSpeed = 25f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        LightExposure exposure;
        Health health;
        MaterialPropertyBlock block;
        Color current;

        void Awake()
        {
            exposure = GetComponent<LightExposure>();
            health = GetComponent<Health>();
            block = new MaterialPropertyBlock();
            current = darkColor;
        }

        public Color TierColor(LightTier tier) => tier switch
        {
            LightTier.Lit => litColor,
            LightTier.Dim => dimColor,
            _ => darkColor
        };

        void LateUpdate()
        {
            if (exposure == null || renderers == null) return;
            current = Color.Lerp(current, TierColor(exposure.SeenTier), 1f - Mathf.Exp(-snapSpeed * Time.deltaTime));
            Color c = current;
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
