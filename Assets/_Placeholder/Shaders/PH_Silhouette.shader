// Placeholder x-ray silhouette (see ASSETS.md): draws a flat colour only where the object is
// hidden behind other geometry, so guards on other layers stay readable.
Shader "StealthProto/PH_Silhouette"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.6, 0.2, 0.55)
        [Toggle] _AlwaysOn ("Always on (ignore the x-ray toggle)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+50" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Silhouette"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZTest Greater
            ZWrite Off
            Cull Back
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _AlwaysOn;
            CBUFFER_END

            // Global testing toggle (DebugHUD, F4): 1 shows guards through walls. Unset = 0 = off.
            float _PH_XRay;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 c = _Color;
                c.a *= max(_AlwaysOn, _PH_XRay);
                clip(c.a - 0.001);
                return c;
            }
            ENDHLSL
        }
    }
}
