Shader "Doppelganger/Earth Surface"
{
    Properties
    {
        _DayTex("Day surface",2D)="white"{}
        _NightTex("Night cities",2D)="black"{}
        _SunDirection("Sun direction",Vector)=(-.55,.35,-.8,0)
        _OrbitalGrade("Orbital color grade",Vector)=(1,1,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_DayTex); SAMPLER(sampler_DayTex);
            TEXTURE2D(_NightTex); SAMPLER(sampler_NightTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _SunDirection;
                float4 _OrbitalGrade;
            CBUFFER_END
            struct A { float4 p:POSITION; float3 n:NORMAL; float2 uv:TEXCOORD0; };
            struct V { float4 p:SV_POSITION; float3 world:TEXCOORD0; float3 n:TEXCOORD1; float2 uv:TEXCOORD2; };
            V vert(A i) { V o; o.p=TransformObjectToHClip(i.p.xyz); o.world=TransformObjectToWorld(i.p.xyz); o.n=TransformObjectToWorldNormal(i.n); o.uv=i.uv; return o; }
            half4 frag(V i):SV_Target
            {
                float3 n=normalize(i.n),v=normalize(_WorldSpaceCameraPos-i.world),sun=normalize(_SunDirection.xyz);
                float light=dot(n,sun),day=smoothstep(-.12,.25,light);
                half3 surface=SAMPLE_TEXTURE2D(_DayTex,sampler_DayTex,i.uv).rgb;
                half3 cities=SAMPLE_TEXTURE2D(_NightTex,sampler_NightTex,i.uv).rgb;
                float ocean=saturate((surface.b-surface.r)*5);
                float specular=pow(saturate(dot(n,normalize(sun+v))),120)*ocean*saturate(light);
                float rim=pow(1-saturate(dot(n,v)),4);
                half3 color=surface*(.008+saturate(light)*1.45)+cities*(1-day)*2.6;
                color+=specular*float3(.6,.75,.95)*.35;
                color+=rim*float3(.015,.10,.25)*(.2+day*.6);
                float warmth=smoothstep(.22,.82,light)*_OrbitalGrade.z;
                float luminance=dot(color,float3(.2126,.7152,.0722));
                color=lerp(float3(luminance,luminance,luminance),color,_OrbitalGrade.x)*_OrbitalGrade.y;
                color*=lerp(float3(1,1,1),float3(1.09,.98,.82),warmth);
                return half4(color,1);
            }
            ENDHLSL
        }
    }
}
