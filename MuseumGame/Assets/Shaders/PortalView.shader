Shader "Custom/PortalView"
{
    Properties { _MainTex ("Tex", 2D) = "black" {} }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            struct A { float4 pos:POSITION; };
            struct V { float4 pos:SV_POSITION; float4 sp:TEXCOORD0; };
            V vert(A v){ V o; o.pos=TransformObjectToHClip(v.pos.xyz); o.sp=ComputeScreenPos(o.pos); return o; }
            half4 frag(V i):SV_Target { float2 uv=i.sp.xy/i.sp.w; return SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,uv); }
            ENDHLSL
        }
    }
}
