// Cheap, mobile-friendly lit shader used for almost everything in Skyreach:
// Lambert sun + sky/ground hemisphere ambient + distance fog, optional vertex colours and texture.
// Lighting values come from globals set by WorldBuilder.SetupSky, so it needs no Unity lights.
// Supports single-pass instanced stereo (required for Quest).
Shader "Skyreach/Lit"
{
    Properties
    {
        _Color ("Colour", Color) = (1,1,1,1)
        _MainTex ("Texture", 2D) = "white" {}
        _Emission ("Emission", Color) = (0,0,0,0)
        _VertexColorWeight ("Use vertex colours", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            fixed4 _Emission;
            float _VertexColorWeight;

            float3 _SkyreachSunDir;
            fixed4 _SkyreachSunColor;
            fixed4 _SkyreachSkyAmbient;
            fixed4 _SkyreachGroundAmbient;
            fixed4 _SkyreachFogColor;
            float4 _SkyreachFog; // x = near, y = far

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : TEXCOORD1;
                float3 worldPos : TEXCOORD2;
                fixed4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                fixed4 vc = v.color;
                #ifndef UNITY_COLORSPACE_GAMMA
                vc.rgb = GammaToLinearSpace(vc.rgb); // vertex colours are authored in sRGB
                #endif
                o.color = lerp(fixed4(1,1,1,1), vc, _VertexColorWeight) * _Color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 n = normalize(i.normal);
                float ndl = saturate(dot(n, normalize(_SkyreachSunDir)));
                float3 ambient = lerp(_SkyreachGroundAmbient.rgb, _SkyreachSkyAmbient.rgb, n.y * 0.5 + 0.5);
                float3 albedo = tex2D(_MainTex, i.uv).rgb * i.color.rgb;
                float3 col = albedo * (ambient + _SkyreachSunColor.rgb * ndl) + _Emission.rgb;
                float dist = distance(i.worldPos, _WorldSpaceCameraPos);
                float fog = saturate((dist - _SkyreachFog.x) / max(_SkyreachFog.y - _SkyreachFog.x, 1.0));
                col = lerp(col, _SkyreachFogColor.rgb, fog);
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
    Fallback "Unlit/Color"
}
