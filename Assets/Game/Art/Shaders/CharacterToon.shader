Shader "Game/Characters/Soft Toon"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1,1,1,1)
        _Brightness("Albedo Brightness", Range(0, 1.5)) = 0.8
        _ShadeColor("Shade Tint", Color) = (0.5,0.54,0.6,1)
        _ShadeThreshold("Shade Threshold", Range(0,1)) = 0.55
        _ShadeSoftness("Shade Softness", Range(0.01,0.5)) = 0.2
        _ShadowStrength("Cast Shadow Strength", Range(0,1)) = 0.3
        [Normal] _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Strength", Range(0,1)) = 0.35
        _SpecularStrength("Highlight Strength", Range(0,0.25)) = 0.02
        _SpecularPower("Highlight Sharpness", Range(8,128)) = 48
        _RimStrength("Rim Strength", Range(0,0.2)) = 0.015
        _Cutoff("Alpha Cutoff", Range(0,1)) = 0.35
        [Toggle] _AlphaClip("Alpha Clipping", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Render Face", Float) = 0
        // Surface state is retained from the source material during conversion.
        [HideInInspector] _Surface("Surface", Float) = 0
        [HideInInspector] _SrcBlend("Source Blend", Float) = 1
        [HideInInspector] _DstBlend("Destination Blend", Float) = 0
        [HideInInspector] _SrcBlendAlpha("Source Alpha Blend", Float) = 1
        [HideInInspector] _DstBlendAlpha("Destination Alpha Blend", Float) = 0
        [HideInInspector] _ZWrite("Depth Write", Float) = 1
        [HideInInspector] _AlphaToMask("Alpha To Coverage", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Cull [_Cull]

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _ShadeColor;
            half _Brightness, _ShadeThreshold, _ShadeSoftness, _ShadowStrength;
            half _BumpScale, _SpecularStrength, _SpecularPower, _RimStrength;
            half _Cutoff, _AlphaClip, _Surface;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 tangentOS : TANGENT;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            half3 normalWS : TEXCOORD1;
            half4 tangentWS : TEXCOORD2;
            float2 uv : TEXCOORD3;
            half fog : TEXCOORD4;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings ToonVertex(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
            VertexNormalInputs normal = GetVertexNormalInputs(input.normalOS, input.tangentOS);
            output.positionCS = position.positionCS;
            output.positionWS = position.positionWS;
            output.normalWS = normal.normalWS;
            output.tangentWS = half4(normal.tangentWS, input.tangentOS.w * GetOddNegativeScale());
            output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
            output.fog = ComputeFogFactor(position.positionCS.z);
            return output;
        }

        half4 SampleBase(float2 uv)
        {
            half4 base = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor;
            if (_AlphaClip > 0.5) clip(base.a - _Cutoff);
            return base;
        }

        half3 SurfaceNormal(Varyings input, half faceSign)
        {
            half3 normal = normalize(input.normalWS);
            if (_BumpScale > 0.001h)
            {
                half3 tangent = normalize(input.tangentWS.xyz);
                half3 bitangent = cross(normal, tangent) * input.tangentWS.w;
                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
                normal = normalize(TransformTangentToWorld(normalTS, half3x3(tangent, bitangent, normal)));
            }
            return normal * faceSign;
        }

        half4 ToonFragment(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            half4 base = SampleBase(input.uv);
            half3 normal = SurfaceNormal(input, IS_FRONT_VFACE(face, 1.0h, -1.0h));
            half3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
            Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
            half halfLambert = dot(normal, light.direction) * 0.5h + 0.5h;
            half ramp = smoothstep(_ShadeThreshold - _ShadeSoftness, _ShadeThreshold + _ShadeSoftness, halfLambert);
            half lightLevel = max(light.color.r, max(light.color.g, light.color.b));
            half visibility = lerp(1.0h, light.shadowAttenuation, _ShadowStrength);
            half lit = ramp * saturate(lightLevel) * visibility;
            half3 hue = light.color / max(lightLevel, 0.001h);
            half3 color = base.rgb * _Brightness * lerp(_ShadeColor.rgb, half3(1,1,1), lit);
            color *= lerp(half3(1,1,1), hue, 0.15h * lit);
            half3 halfDirection = SafeNormalize(light.direction + view);
            half highlight = pow(saturate(dot(normal, halfDirection)), _SpecularPower) * lit;
            color += base.rgb * highlight * _SpecularStrength;
            half rim = pow(1.0h - saturate(dot(normal, view)), 4.0h) * ramp;
            color += base.rgb * rim * _RimStrength;
            color = MixFog(color, input.fog);
            return half4(color, _Surface > 0.5 ? base.a : 1.0h);
        }

        half4 DepthFragment(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            SampleBase(input.uv);
            return 0;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForwardOnly" }
            Blend [_SrcBlend] [_DstBlend], [_SrcBlendAlpha] [_DstBlendAlpha]
            ZWrite [_ZWrite]
            AlphaToMask [_AlphaToMask]
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ToonVertex
            #pragma fragment ToonFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVertex
            #pragma fragment DepthFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection;
            float3 _LightPosition;
            Varyings ShadowVertex(Attributes input)
            {
                Varyings output = ToonVertex(input);
                float3 direction = _LightDirection;
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    direction = normalize(_LightPosition - output.positionWS);
                #endif
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(output.positionWS, output.normalWS, direction));
                #if UNITY_REVERSED_Z
                    output.positionCS.z = min(output.positionCS.z, UNITY_NEAR_CLIP_VALUE * output.positionCS.w);
                #else
                    output.positionCS.z = max(output.positionCS.z, UNITY_NEAR_CLIP_VALUE * output.positionCS.w);
                #endif
                return output;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormalsOnly"
            Tags { "LightMode"="DepthNormalsOnly" }
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ToonVertex
            #pragma fragment ToonDepthNormals
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            half4 ToonDepthNormals(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                SampleBase(input.uv);
                half3 normal = SurfaceNormal(input, IS_FRONT_VFACE(face, 1.0h, -1.0h));
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 packed = saturate(PackNormalOctQuadEncode(normal) * 0.5 + 0.5);
                    return half4(PackFloat2To888(packed), 0);
                #else
                    return half4(normal, 0);
                #endif
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ToonVertex
            #pragma fragment DepthFragment
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }
    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
