Shader "Doppelganger/Earth Atmosphere"
{
    Properties
    {
        _SunDirection("Sun direction",Vector)=(-.55,.35,-.8,0)
        _OrbitalGrade("Orbital color grade",Vector)=(1,1,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+1" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Front
            Blend One One
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _SunDirection;
                float4 _OrbitalGrade;
            CBUFFER_END
            struct A { float4 p:POSITION; float3 n:NORMAL; };
            struct V { float4 p:SV_POSITION; float3 n:TEXCOORD0; float3 world:TEXCOORD1; };
            V vert(A i) { V o; o.p=TransformObjectToHClip(i.p.xyz); o.n=TransformObjectToWorldNormal(i.n); o.world=TransformObjectToWorld(i.p.xyz); return o; }
            half4 frag(V i):SV_Target
            {
                float3 n=normalize(i.n),v=normalize(_WorldSpaceCameraPos-i.world);
                float grazing=abs(dot(n,v));
                float rim=pow(saturate(1-grazing),3.2);
                rim*=lerp(1,smoothstep(0,.16,grazing),_OrbitalGrade.w);
                float sun=saturate(dot(n,normalize(_SunDirection.xyz))*.6+.4);
                return half4(float3(.018,.17,.55)*rim*sun*_OrbitalGrade.y,1);
            }
            ENDHLSL
        }
    }
}
