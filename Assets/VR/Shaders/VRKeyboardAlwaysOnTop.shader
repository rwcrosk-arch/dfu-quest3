// DFU Quest3 VR — always-on-top texture shader for the VR keyboard keys.
// Renders the baked key-label texture with ZTest Always + ZWrite Off + Overlay
// queue, so the keys are GUARANTEED to draw after (on top of) the DFU UI panel
// quad and the save window's dark NativePanel backdrop in gameplay.
//
// WHY THIS EXISTS: the previous approach set renderQueue=4000 on Unlit/Texture,
// but Unlit/Texture does NOT expose _ZTest/_ZWrite as public properties, so the
// keys were never reliably forced on top — the save window's opaque black
// mainPanel (drawn into the world-space overlay panel) buried the letter rows
// while the darker special row escaped below the panel edge. This shader makes
// the draw-after explicit and robust.
//
// IMPORTANT: same portability rules as VRUIChromaKey — Unity 6 built-in RP,
// Android/Vulkan/OpenXR, HLSLPROGRAM, shader model 2.0.
Shader "DFUQuest3/VRKeyboardAlwaysOnTop"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        // Overlay queue = drawn after everything else (post-UI). ZTest Always
        // ignores depth so the panel/backdrop can never occlude the keys.
        Tags { "Queue"="Overlay" "RenderType"="Opaque" "IgnoreProjector"="True" "PreviewType"="Plane" }
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

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                return tex2D(_MainTex, i.uv);
            }
            ENDHLSL
        }
    }
    Fallback "Unlit/Texture"
}
