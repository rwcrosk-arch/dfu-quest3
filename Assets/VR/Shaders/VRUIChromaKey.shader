// DFU Quest3 VR — chroma-key shader for the UI panel.
// Renders the DFU UI render target but makes near-black pixels transparent, so the
// opaque black panel background disappears and only the HUD/menu elements show.
Shader "DFUQuest3/VRUIChromaKey"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _KeyColor ("Key Color", Color) = (0,0,0,1)
        _Threshold ("Threshold", Range(0,1)) = 0.12
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float2 uv : TEXCOORD0; float4 vertex : SV_POSITION; };

            sampler2D _MainTex;
            float4 _KeyColor;
            float _Threshold;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);
                // Distance from the key color (black). Pixels close to black become
                // transparent; everything else keeps its color.
                float dist = distance(col.rgb, _KeyColor.rgb);
                float alpha = smoothstep(_Threshold, _Threshold + 0.15, dist);
                col.a = alpha;
                return col;
            }
            ENDCG
        }
    }
}
