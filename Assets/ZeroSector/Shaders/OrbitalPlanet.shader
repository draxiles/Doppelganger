Shader "Doppelganger/OrbitalPlanet"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct V { float4 positionCS:SV_POSITION; float3 n:TEXCOORD0; float3 world:TEXCOORD1; };
            V vert(A i) { V o; o.positionCS=TransformObjectToHClip(i.positionOS.xyz); o.n=TransformObjectToWorldNormal(i.normalOS); o.world=TransformObjectToWorld(i.positionOS.xyz); return o; }
            float hash(float3 p) { return frac(sin(dot(p,float3(127.1,311.7,74.7)))*43758.5453); }
            float noise(float3 p)
            {
                float3 a=floor(p),f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(lerp(hash(a),hash(a+float3(1,0,0)),f.x),lerp(hash(a+float3(0,1,0)),hash(a+float3(1,1,0)),f.x),f.y),lerp(lerp(hash(a+float3(0,0,1)),hash(a+float3(1,0,1)),f.x),lerp(hash(a+float3(0,1,1)),hash(a+1),f.x),f.y),f.z);
            }
            half4 frag(V i):SV_Target
            {
                float3 n=normalize(i.n), sun=normalize(float3(-.7,.4,-.6));
                float land=noise(n*5)*.6+noise(n*12)*.3+noise(n*30)*.1;
                float3 sea=float3(.012,.075,.18), ground=lerp(float3(.045,.13,.09),float3(.27,.23,.12),smoothstep(.55,.7,land));
                float3 c=lerp(sea,ground,smoothstep(.50,.53,land));
                float clouds=smoothstep(.52,.73,noise(n*15+5)*.7+noise(n*45)*.3);
                c=lerp(c,float3(.8,.88,.96),clouds*.85);
                c=lerp(c,float3(.85,.9,.96),smoothstep(.83,.97,abs(n.y)));
                float light=saturate(dot(n,sun));
                float rim=pow(1-saturate(dot(n,normalize(_WorldSpaceCameraPos-i.world))),4);
                return half4(c*(.035+light*1.5)+rim*float3(.025,.20,.48)*(.2+light),1);
            }
            ENDHLSL
        }
    }
}
