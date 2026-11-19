// Flat colour with optional alpha blending (lights, active ring, flame, glow, explosion).
Shader "Skyreach/Unlit"
{
    Properties
    {
        _Color ("Colour", Color) = (1,1,1,1)
        [HideInInspector] _SrcBlend ("Src", Float) = 1
        [HideInInspector] _DstBlend ("Dst", Float) = 0
        [HideInInspector] _ZWrite ("ZWrite", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite [_ZWrite]
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _Color;
            fixed4 _SkyreachFogColor;
            float4 _SkyreachFog;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float dist = distance(i.worldPos, _WorldSpaceCameraPos);
                float fog = saturate((dist - _SkyreachFog.x) / max(_SkyreachFog.y - _SkyreachFog.x, 1.0));
                return fixed4(lerp(_Color.rgb, _SkyreachFogColor.rgb, fog), _Color.a);
            }
            ENDCG
        }
    }
}
