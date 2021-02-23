Shader "Coherence/Display Modes"
{
    Properties
    {
        _DisplayMode ("Display Mode", Int) = 0
        _ApplyTexture("Apply Texture", Int) = 0
        _MainTex ("UV Checker Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
                float2 uv3 : TEXCOORD2;
                float2 uv4 : TEXCOORD3;
            };

            struct v2f
            {
                float4 channel : TEXCOORD0;

                UNITY_FOG_COORDS(1)
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            int _ApplyTexture;
            int _DisplayMode;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);

                // Order matches ObjectDisplayMode enum, copying
                // the appropriate vertex channel into the FS
                if (_DisplayMode == 1) { // Normals
                    o.channel = float4(v.normal, 1.0);
                }
                else if (_DisplayMode == 2) { // Vertex Colors
                    o.channel = v.color;
                }
                else if (_DisplayMode == 3) { // UV
                    o.channel = float4(TRANSFORM_TEX(v.uv, _MainTex), 0, 1);
                }
                else if (_DisplayMode == 4) { // UV2
                    o.channel = float4(TRANSFORM_TEX(v.uv2, _MainTex), 0, 1);
                }
                else if (_DisplayMode == 5) { // UV3
                    o.channel = float4(TRANSFORM_TEX(v.uv3, _MainTex), 0, 1);
                }
                else if (_DisplayMode == 6) { // UV4
                    o.channel = float4(TRANSFORM_TEX(v.uv4, _MainTex), 0, 1);
                }
                else {
                    o.channel = float4(0.0, 0.0, 0.0, 1.0);
                }

                UNITY_TRANSFER_FOG(o, o.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                if (_ApplyTexture == 1) {
                    return tex2D(_MainTex, i.channel.xy);
                }

                return i.channel;
            }
            ENDCG
        }
    }
}
