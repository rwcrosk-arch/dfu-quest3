// DFU Quest3 VR — chroma-key shader for the weapon quad and UI panel.
// Renders the source texture but makes near-black pixels transparent (alpha=0).
// This keys out the black background so only the weapon/UI elements show.
//
// IMPORTANT: This shader is designed to compile on Unity 6 built-in render pipeline
// targeting Android/Vulkan/OpenXR. It avoids CGPROGRAM legacy syntax that may fail
// on modern Vulkan drivers and uses the most portable shader model 2.0 features.
//
// Usage: Material mainTexture = weapon atlas; adjust _Threshold for key sensitivity.
Shader "DFUQuest3/VRUIChromaKey"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _KeyColor ("Key Color", Color) = (0,0,0,1)
        _Threshold ("Threshold", Range(0,1)) = 0.01
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        LOD 100

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _KeyColor;
            float _Threshold;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                half4 col = tex2D(_MainTex, i.uv);

                // Chroma-key: distance from key color (black). Pixels close to
                // black become fully transparent; everything else keeps color.
                // Very tight band so ONLY near-pure-black background is keyed,
                // never dark HUD/menu/weapon shading.
                half dist = distance(col.rgb, _KeyColor.rgb);
                half alpha = smoothstep(_Threshold, _Threshold + 0.03h, dist);

                // Return color with keyed alpha. Premultiply not needed since
                // we use standard SrcAlpha/OneMinusSrcAlpha blending.
                return half4(col.rgb, alpha);
            }
            ENDHLSL
        }
    }
    // Fallback: if HLSL fails on this platform, use a simple alpha-test cutout
    Fallback "Unlit/Transparent Cutout"
}
