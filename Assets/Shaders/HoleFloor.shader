// Floor shader that punches a real, growing hole through the ground.
// BlackHoleController feeds _HoleCenter / _HoleRadius as global shader properties
// every frame, and the fragment stage clips anything inside that circle, so the
// floor geometry genuinely disappears and you can see down into the pit below.
Shader "HoleStars/HoleFloor"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (0.17, 0.19, 0.27, 1)
        _RimColor("Hole Rim Color", Color) = (1, 0.4, 0.12, 1)
        _RimWidth("Hole Rim Width", Range(0, 3)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _RimColor;
                float _RimWidth;
            CBUFFER_END

            // Globals driven from script — kept out of UnityPerMaterial on purpose.
            float4 _HoleCenter;
            float _HoleRadius;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs positions = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = positions.positionCS;
                OUT.positionWS = positions.positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float distanceToHole = length(IN.positionWS.xz - _HoleCenter.xz);

                // Negative values are discarded: this is the hole itself.
                clip(distanceToHole - _HoleRadius);

                float3 normalWS = normalize(IN.normalWS);
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                half3 albedo = _BaseColor.rgb;

                // Hot rim just outside the lip, so the cut edge reads clearly.
                float rim = 1.0 - saturate((distanceToHole - _HoleRadius) / max(_RimWidth, 0.0001));
                albedo = lerp(albedo, _RimColor.rgb, rim * rim);

                half3 ambient = SampleSH(normalWS);
                half ndotl = saturate(dot(normalWS, mainLight.direction));
                half3 lighting = ambient + mainLight.color * ndotl * mainLight.shadowAttenuation;

                half3 color = albedo * lighting;

                // Keep the rim emissive so it stays visible in shadow.
                color += _RimColor.rgb * rim * rim * 0.6;

                return half4(color, 1);
            }
            ENDHLSL
        }

        // Depth-only variant, so the hole is also absent from the depth texture.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            float4 _HoleCenter;
            float _HoleRadius;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs positions = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = positions.positionCS;
                OUT.positionWS = positions.positionWS;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                clip(length(IN.positionWS.xz - _HoleCenter.xz) - _HoleRadius);
                return 0;
            }
            ENDHLSL
        }
    }
}
