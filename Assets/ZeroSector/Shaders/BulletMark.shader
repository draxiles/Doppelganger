Shader "Doppelganger/Bullet Mark"
{
    Properties { _BaseMap("Impact pattern", 2D) = "white" {} }
    SubShader
    {
        Tags
        {
            "Queue"="Transparent-40" "RenderType"="Transparent"
            "RenderPipeline"="UniversalPipeline" "DisableBatching"="True"
        }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            float4 Vert(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS.xyz);
            }

            half4 Frag(float4 screenPosition : SV_POSITION) : SV_Target
            {
                float2 screenUV = screenPosition.xy * _ScreenSize.zw;
                float depth = LoadSceneDepth(uint2(screenPosition.xy));
                #if UNITY_REVERSED_Z
                    clip(depth - 0.000001);
                #else
                    clip(0.999999 - depth);
                    depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, depth);
                #endif

                float3 worldPosition = ComputeWorldSpacePosition(screenUV, depth, UNITY_MATRIX_I_VP);
                float3 localPosition = TransformWorldToObject(worldPosition);
                float3 surface = cross(ddx(worldPosition), ddy(worldPosition));
                float3 forward = TransformObjectToWorldDir(float3(0, 0, 1));
                float facing = abs(dot(surface, forward)) * rsqrt(max(dot(surface, surface), 1e-15));
                clip(0.5 - max(max(abs(localPosition.x), abs(localPosition.y)), abs(localPosition.z)));
                half4 mark = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, localPosition.xy + 0.5);
                mark.a *= smoothstep(0.28, 0.68, facing);
                return mark;
            }
            ENDHLSL
        }
    }
}
