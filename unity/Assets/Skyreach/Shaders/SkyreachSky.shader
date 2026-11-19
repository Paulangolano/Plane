// Gradient skybox with a soft sun disc. Assigned to RenderSettings.skybox at startup.
Shader "Skyreach/Sky"
{
    Properties
    {
        _Zenith ("Zenith", Color) = (0.18, 0.5, 0.85, 1)
        _Horizon ("Horizon", Color) = (0.75, 0.89, 0.97, 1)
        _SunColor ("Sun", Color) = (1, 0.95, 0.82, 1)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _Zenith, _Horizon, _SunColor;
            float3 _SkyreachSunDir;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 d = normalize(i.dir);
                float t = pow(saturate(d.y), 0.45);
                float3 col = lerp(_Horizon.rgb, _Zenith.rgb, t);
                float s = saturate(dot(d, normalize(_SkyreachSunDir)));
                col += _SunColor.rgb * (pow(s, 900.0) * 3.0 + pow(s, 12.0) * 0.25);
                if (d.y < 0.0) col = _Horizon.rgb;
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
