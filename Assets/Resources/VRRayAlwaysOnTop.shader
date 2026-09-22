// DFU Quest3 VR — always-on-top unlit vertex-color shader for the pointer ray line
// and reticle. Renders above EVERYTHING: Queue 4100 (above the Overlay-4000 HUD/menu
// panels) + ZTest Always, so walls can't cut the ray and the always-on-top menus
// can't bury it either.
//
// WHY THIS EXISTS: with the menu panel promoted to Overlay-4000 (wall-immunity), the
// default-material ray line (Geometry queue, depth-tested) ended up BEHIND both the
// walls and the menu panel — the pointer vanished exactly where the user needs it.
// A pointer is UI, not world geometry; it always draws on top.
//
// Vertex color carries the LineRenderer's start/end color gradient (Unlit/Color
// semantics) so no material color plumbing is needed.
//
// IMPORTANT: same portability rules as VRUIChromaKey — Unity 6 built-in RP,
// Android/Vulkan/OpenXR, HLSLPROGRAM terminated with ENDHLSL, shader model 2.0.
Shader "DFUQuest3/VRRayAlwaysOnTop"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        // MUST sit above the Overlay(4000) HUD/menu panels — Overlay+100 = 4100.
        // (Transparent+100 would be 3100 = BELOW the panels, which is exactly the
        // bug this shader exists to fix.)
        Tags { "Queue"="Overlay+100" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZTest Always
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
                fixed4 color : COLOR0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR0;
            };

            fixed4 _Color;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return i.color;
            }
            ENDHLSL
        }
    }
    // Fallback if HLSL fails on a platform
    Fallback "Unlit/Color"
}