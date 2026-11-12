Shader "Doppelganger/Prologue Plasma"
{
    Properties
    {
        [HDR] _Glow("Glow",Color)=(1,.5,.2,1)
        _GlowMap("Falloff",2D)="white"{}
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+8" }
        Cull Off ZWrite Off Blend One One
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_GlowMap); SAMPLER(sampler_GlowMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _Glow;
            CBUFFER_END
            struct Input { float3 position : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 position : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            Varyings Vert(Input input)
            {
                Varyings result;
                result.position=TransformObjectToHClip(input.position);
                result.uv=input.uv;
                result.color=input.color;
                return result;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half mask=SAMPLE_TEXTURE2D(_GlowMap,sampler_GlowMap,input.uv).r;
                return half4(input.color.rgb*_Glow.rgb*(input.color.a*_Glow.a*mask),0);
            }
            ENDHLSL
        }
    }
}
