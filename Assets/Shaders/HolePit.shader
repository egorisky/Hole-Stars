// Inside surface of the pit under the hole. Front faces are culled so the camera
// looks straight through the near wall into a tube that darkens with depth,
// which is what gives the hole its sense of volume instead of looking painted on.
Shader "HoleStars/HolePit"
{
    Properties
    {
        _TopColor("Top Color", Color) = (0.14, 0.11, 0.2, 1)
        _BottomColor("Bottom Color", Color) = (0, 0, 0, 1)
        _Falloff("Darkening Falloff", Range(0.2, 4)) = 1.1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+1" }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Front

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float depth01     : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _TopColor;
                float4 _BottomColor;
                float _Falloff;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);

                // Built-in cylinder spans y = -1 (bottom) to y = 1 (top) in object space,
                // so this stays correct however the pit is scaled.
                OUT.depth01 = saturate(0.5 - IN.positionOS.y * 0.5);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half t = pow(IN.depth01, _Falloff);
                return half4(lerp(_TopColor.rgb, _BottomColor.rgb, t), 1);
            }
            ENDHLSL
        }
    }
}
