// DFU Quest3 VR — always-on-top chroma-key shader for the gameplay HUD panel.
// Chroma-keys near-black pixels to transparent (like VRUIChromaKey) AND ignores the
// depth buffer (ZTest Always) with an Overlay queue position, so dungeon walls close
// to the player can never occlude the HUD.
//
// WHY THIS EXISTS: the gameplay HUD panel renders 2m out in world space. In dungeons,
// standing near a wall puts the wall between the eye and the panel; the panel's
// depth test then fails and the HUD disappears. A HUD is UI, not world geometry —
// VR convention is that it draws on top of everything.
//
// IMPORTANT: same portability rules as VRUIChromaKey — Unity 6 built-in RP,
// Android/Vulkan/OpenXR, HLSLPROGRAM, shader model 2.0.
Shader "DFUQuest3/VRHUDAlwaysOnTop"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _KeyColor ("Key Color", Color) = (0,0,0,1)
        _Threshold ("Threshold", Range(0,1)) = 0.01
    }
    SubShader
    {
        // Overlay queue = drawn after everything else. ZTest Always ignores depth so
        // walls/enemies can never occlude the HUD. ZWrite Off + alpha blend for the
        // chroma-keyed transparency.
        Tags { "Queue"="Overlay" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
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
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _KeyColor;
            fixed _Threshold;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);
                // Near-black (keyed) pixels become fully transparent; everything else
                // draws on top of the world.
                fixed keyDist = abs(col.r - _KeyColor.r) + abs(col.g - _KeyColor.g) + abs(col.b - _KeyColor.b);
                col.a = (keyDist < _Threshold) ? 0.0 : 1.0;
                return col;
            }
            ENDHLSLPROGRAM
        }
    }
}