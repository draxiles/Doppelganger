Shader "Doppelganger/Bridge Glass"
{
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 p:POSITION; float3 n:NORMAL; };
            struct V { float4 p:SV_POSITION; float3 world:TEXCOORD0; float3 n:TEXCOORD1; };
            V vert(A i) { V o; o.p=TransformObjectToHClip(i.p.xyz); o.world=TransformObjectToWorld(i.p.xyz); o.n=TransformObjectToWorldNormal(i.n); return o; }
            half4 frag(V i):SV_Target
            {
                float edge=pow(1-saturate(abs(dot(normalize(i.n),normalize(_WorldSpaceCameraPos-i.world)))),5);
                return half4(.09,.2,.32,.025+edge*.12);
            }
            ENDHLSL
        }
    }
}
