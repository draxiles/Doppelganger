Shader "Doppelganger/Earth Clouds"
{
    Properties
    {
        _CloudTex("Cloud coverage",2D)="black"{}
        _SunDirection("Sun direction",Vector)=(-.55,.35,-.8,0)
        _OrbitalGrade("Orbital color grade",Vector)=(1,1,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_CloudTex); SAMPLER(sampler_CloudTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _SunDirection;
                float4 _OrbitalGrade;
            CBUFFER_END
            struct A { float4 p:POSITION; float3 n:NORMAL; float2 uv:TEXCOORD0; };
            struct V { float4 p:SV_POSITION; float3 n:TEXCOORD0; float2 uv:TEXCOORD1; };
            V vert(A i) { V o; o.p=TransformObjectToHClip(i.p.xyz); o.n=TransformObjectToWorldNormal(i.n); o.uv=i.uv; return o; }
            half4 frag(V i):SV_Target
            {
                float coverage=SAMPLE_TEXTURE2D(_CloudTex,sampler_CloudTex,i.uv+float2(_Time.y*.00001,0)).r;
                float light=dot(normalize(i.n),normalize(_SunDirection.xyz));
                float lighting=.018+saturate(light)*1.4;
                float warmth=smoothstep(.22,.82,light)*_OrbitalGrade.z;
                float3 tint=lerp(float3(.9,.96,1),float3(1.04,.94,.81),warmth);
                return half4(tint*lighting*_OrbitalGrade.y,smoothstep(.08,.8,coverage)*.94);
            }
            ENDHLSL
        }
    }
}
