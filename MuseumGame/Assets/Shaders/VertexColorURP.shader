Shader "Custom/VertexColorURP"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            struct A { float4 pos:POSITION; float3 nor:NORMAL; float4 col:COLOR; };
            struct V { float4 pos:SV_POSITION; float3 nor:TEXCOORD0; float4 col:COLOR; };
            V vert(A i){ V o; o.pos=TransformObjectToHClip(i.pos.xyz); o.nor=TransformObjectToWorldNormal(i.nor); o.col=i.col; return o; }
            half4 frag(V i):SV_Target{ Light l=GetMainLight(); float d=saturate(dot(normalize(i.nor),l.direction)); float3 c=i.col.rgb*(0.62+0.5*d); return half4(c,1); }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
    }
}