Shader "Doppelganger/DeepSpace"
{
    Properties
    {
        _Tint("Tint", Color) = (.01,.02,.04,1)
        _BackdropExposure("Backdrop exposure", Range(.1,1)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _BackdropExposure;
            CBUFFER_END
            struct A { float4 positionOS:POSITION; };
            struct V { float4 positionCS:SV_POSITION; float3 dir:TEXCOORD0; };
            V vert(A i) { V o; o.positionCS=TransformObjectToHClip(i.positionOS.xyz); o.dir=i.positionOS.xyz; return o; }
            float hash(float3 p) { p=frac(p*.3183099+.1); p*=17; return frac(p.x*p.y*p.z*(p.x+p.y+p.z)); }
            half4 frag(V i):SV_Target
            {
                float3 d=normalize(i.dir);
                float3 grid=d*370;
                float3 cell=floor(grid);
                float h=hash(cell);
                float star=pow(saturate(1-length(frac(grid)-.5)*2),12)*step(.985,h)*4;
                float band=pow(saturate(1-abs(d.y*.8+d.x*.3+.2)),18);
                float cloud=(sin(d.x*18+d.z*6)*sin(d.z*12-d.y*14)*.5+.5)*band;
                float3 backdrop=(float3(.003,.006,.018)+cloud*float3(.014,.025,.052))*_BackdropExposure;
                return half4(backdrop+star*float3(.72,.84,1),1);
            }
            ENDHLSL
        }
    }
}
