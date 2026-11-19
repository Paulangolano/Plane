// Opaque animated ocean (port of webxr/src/world/ocean.js).
// Vertex colour alpha holds "shallowness" baked from terrain depth: turquoise lagoons + shore foam,
// with no transparency cost on Quest.
Shader "Skyreach/Ocean"
{
    Properties
    {
        _Deep ("Deep", Color) = (0.04, 0.31, 0.49, 1)
        _Shallow ("Shallow", Color) = (0.21, 0.79, 0.75, 1)
        _SkyReflect ("Sky reflection", Color) = (0.66, 0.84, 0.96, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _Deep, _Shallow, _SkyReflect;
            float3 _SkyreachSunDir;
            fixed4 _SkyreachFogColor;
            float4 _SkyreachFog;

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 normal : TEXCOORD1;
                float shallow : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float t = _Time.y;
                float3 p = mul(unity_ObjectToWorld, v.vertex).xyz;
                float shallow = v.color.a;
                float w1 = sin(p.x * 0.021 + t * 0.9) * cos(p.z * 0.017 + t * 0.7);
                float w2 = sin((p.x + p.z) * 0.043 + t * 1.6);
                p.y += (w1 * 1.1 + w2 * 0.35) * (1.0 - shallow * 0.7);
                float dx = cos(p.x * 0.021 + t * 0.9) * 0.021 * 1.1 + cos((p.x + p.z) * 0.043 + t * 1.6) * 0.043 * 0.35;
                float dz = -sin(p.z * 0.017 + t * 0.7) * 0.017 * 1.1 + cos((p.x + p.z) * 0.043 + t * 1.6) * 0.043 * 0.35;
                o.normal = normalize(float3(-dx * 2.0, 1.0, -dz * 2.0));
                o.worldPos = p;
                o.shallow = shallow;
                o.pos = mul(UNITY_MATRIX_VP, float4(p, 1.0));
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
                float3 n = normalize(i.normal);
                float fresnel = pow(1.0 - saturate(dot(viewDir, n)), 4.0);
                float3 col = lerp(_Deep.rgb, _Shallow.rgb, i.shallow);
                float foam = smoothstep(0.82, 1.0, i.shallow) * (0.6 + 0.4 * sin(_Time.y * 2.0 + i.worldPos.x * 0.15 + i.worldPos.z * 0.11));
                col = lerp(col, _SkyReflect.rgb, fresnel * 0.65);
                col = lerp(col, float3(1, 1, 1), foam * 0.45);
                float3 h = normalize(normalize(_SkyreachSunDir) + viewDir);
                col += float3(1.0, 0.95, 0.85) * pow(saturate(dot(n, h)), 180.0) * 1.4;
                float dist = distance(i.worldPos, _WorldSpaceCameraPos);
                float fog = saturate((dist - _SkyreachFog.x) / max(_SkyreachFog.y - _SkyreachFog.x, 1.0));
                col = lerp(col, _SkyreachFogColor.rgb, fog);
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
