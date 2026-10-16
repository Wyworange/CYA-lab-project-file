// Environment (Yuwen, 2026-10-13): glowing "neon bonsai" look for the trees and the feeding bush.
// The generated tree meshes have no UVs, so every pattern comes from the world position.
//  _Mode 0 (bark):    pale blue -> lavender -> pink iridescent bands, a few bright colour blocks.
//  _Mode 1 (foliage): deep blue with slowly twinkling cyan / magenta / orange dots.
// Every pass has a DOTS variant and shares one material cbuffer (GPU Resident Drawer / SRP Batcher).
Shader "DigiPhant/Neon Tree"
{
    Properties
    {
        [Enum(Bark,0,Foliage,1)] _Mode ("Part", Float) = 0
        _BaseColor ("Base colour", Color) = (0.05, 0.10, 0.38, 1)
        _ColorA ("Colour A", Color) = (0.62, 0.84, 1.00, 1)
        _ColorB ("Colour B", Color) = (0.78, 0.66, 1.00, 1)
        _ColorC ("Colour C", Color) = (1.00, 0.70, 0.90, 1)
        _Glow ("Self glow", Float) = 0.6
        _RimColor ("Rim colour", Color) = (0.45, 0.85, 1.00, 1)
        _RimGlow ("Rim glow", Float) = 1.5
        _PatternScale ("Pattern cells per metre", Float) = 2
        _SparkleScale ("Sparkle cells per metre", Float) = 3
        _SparkleAmount ("Sparkle amount", Range(0, 1)) = 0.3
        _SparkleGlow ("Sparkle glow", Float) = 4
        _TwinkleSpeed ("Twinkle speed", Float) = 1.5
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float _Mode;
            float4 _BaseColor, _ColorA, _ColorB, _ColorC, _RimColor;
            float _Glow, _RimGlow, _PatternScale, _SparkleScale, _SparkleAmount, _SparkleGlow, _TwinkleSpeed;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "NeonForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float fogFactor : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float3 Hash3(float3 p)
            {
                return float3(Hash(p), Hash(p + 19.19), Hash(p + 47.53));
            }

            // Smooth value noise, used to bend the bark bands.
            float Noise(float3 p)
            {
                float3 i = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(lerp(Hash(i), Hash(i + float3(1, 0, 0)), f.x),
                                 lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x),
                                 lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            float3 Iridescent(float t)
            {
                t = frac(t) * 3;
                return t < 1 ? lerp(_ColorA.rgb, _ColorB.rgb, t)
                     : t < 2 ? lerp(_ColorB.rgb, _ColorC.rgb, t - 1)
                     : lerp(_ColorC.rgb, _ColorA.rgb, t - 2);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 p = input.positionWS;
                float3 n = normalize(input.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(p));
                float rim = pow(1 - saturate(dot(n, v)), 3);

                float3 albedo, emission;
                if (_Mode < 0.5)
                {
                    // Bark: bands climbing the trunk, warped by noise, with a few bright blocks.
                    float warp = Noise(p * _PatternScale) * 0.6;
                    albedo = Iridescent(p.y * 0.25 + warp);
                    float3 cell = floor(p * _PatternScale * 1.5);
                    float block = step(0.88, Hash(cell));
                    albedo = lerp(albedo, Iridescent(Hash(cell + 3.7)) * 1.25, block);
                    emission = albedo * (_Glow + block * 0.8);
                }
                else
                {
                    // Foliage: dark blue with dots; each dot cell picks cyan, magenta or orange.
                    float3 q = p * _SparkleScale;
                    float3 cell = floor(q);
                    float3 rnd = Hash3(cell);
                    albedo = _BaseColor.rgb * (0.75 + 0.5 * Noise(p * 1.3));
                    float dist = length(frac(q) - (0.25 + 0.5 * rnd));
                    float spot = smoothstep(0.32, 0.08, dist) * step(rnd.x, _SparkleAmount);
                    float3 sparkle = rnd.y < 0.4 ? float3(0.2, 1.0, 0.95)
                                   : rnd.y < 0.8 ? float3(1.0, 0.3, 0.9)
                                   : float3(1.0, 0.6, 0.25);
                    float twinkle = 0.55 + 0.45 * sin(_Time.y * _TwinkleSpeed + rnd.z * 40);
                    emission = _BaseColor.rgb * _Glow + sparkle * spot * twinkle * _SparkleGlow;
                }

                Light light = GetMainLight(TransformWorldToShadowCoord(p), p, half4(1, 1, 1, 1));
                float3 diffuse = light.color * saturate(dot(n, light.direction)) * light.shadowAttenuation;
                float3 colour = albedo * (diffuse + SampleSH(n)) + emission + _RimColor.rgb * rim * _RimGlow;
                colour = MixFog(colour, input.fogFactor);
                return half4(colour, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment NullFrag
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };

            float4 ShadowVert(Attributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                return ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
            }

            half4 NullFrag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment NullFrag
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };

            float4 DepthVert(Attributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return TransformObjectToHClip(input.positionOS.xyz);
            }

            half4 NullFrag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex NormalsVert
            #pragma fragment NormalsFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };

            Varyings NormalsVert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 NormalsFrag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 oct = saturate(PackNormalOctQuadEncode(normalWS) * 0.5 + 0.5);
                return half4(PackFloat2To888(oct), 0);
            #else
                return half4(normalWS, 0);
            #endif
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
