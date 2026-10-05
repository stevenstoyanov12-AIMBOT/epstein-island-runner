Shader "Custom/FountainWater"
{
    Properties
    {
        _Color ("Tint", Color) = (0.55,0.68,0.75,1)
        _DeepColor ("Deep", Color) = (0.05,0.1,0.13,1)
        _Mode ("Mode 0=surface 1=curtain", Float) = 0
        _Speed ("Flow Speed", Float) = 1.6
        _Opacity ("Opacity", Range(0,1)) = 0.55
        _RippleCenterR ("Ripple ring radius", Float) = 1
        _Radius ("Surface radius", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _Color; float4 _DeepColor; float _Mode; float _Speed; float _Opacity; float _RippleCenterR; float _Radius;
            CBUFFER_END
            struct A { float4 pos:POSITION; float3 n:NORMAL; float2 uv:TEXCOORD0; };
            struct V { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float3 wp:TEXCOORD1; float3 wn:TEXCOORD2; float3 lp:TEXCOORD3; };
            float h21(float2 p){ p=frac(p*float2(123.34,456.21)); p+=dot(p,p+45.32); return frac(p.x*p.y); }
            float vn(float2 p){ float2 i=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(h21(i),h21(i+float2(1,0)),f.x), lerp(h21(i+float2(0,1)),h21(i+float2(1,1)),f.x), f.y); }
            float fbm(float2 p){ float s=0,a=0.5; for(int k=0;k<4;k++){ s+=a*vn(p); p*=2.03; a*=0.5;} return s; }
            V vert(A v){ V o; o.wp=TransformObjectToWorld(v.pos.xyz); o.pos=TransformWorldToHClip(o.wp); o.uv=v.uv; o.wn=TransformObjectToWorldNormal(v.n); o.lp=v.pos.xyz; return o; }
            float surfH(float2 xz, float t){
                float r=length(xz);
                float rings = sin((r-_RippleCenterR)*38.0 - t*9.0) * exp(-abs(r-_RippleCenterR)*3.0) * 0.5;
                float n = fbm(xz*3.0 + float2(t*0.35, -t*0.28)) + fbm(xz*7.0 - float2(t*0.6, t*0.5))*0.5;
                return rings*0.6 + n;
            }
            half4 frag(V i, bool front:SV_IsFrontFace):SV_Target
            {
                float t=_Time.y;
                float3 V_ = normalize(GetWorldSpaceViewDir(i.wp));
                float3 N; float alpha; float foam=0;
                if (_Mode < 0.5)
                {
                    float2 xz=i.lp.xz; float e=0.01;
                    float h=surfH(xz,t); float hx=surfH(xz+float2(e,0),t); float hz=surfH(xz+float2(0,e),t);
                    N=normalize(float3(-(hx-h)/e*0.035, 1, -(hz-h)/e*0.035));
                    float r=length(xz);
                    foam = saturate(1-abs(r-_RippleCenterR)*6) * smoothstep(0.55,0.8, fbm(xz*9+float2(t*1.3,-t)));
                    alpha=_Opacity;
                }
                else
                {
                    // uv.x around, uv.y down the fall (0 top, 1 bottom)
                    float2 p=float2(i.uv.x*140.0, i.uv.y*3.0 - t*_Speed);
                    float streak = fbm(float2(p.x, p.y*1.0));
                    float streak2 = vn(float2(i.uv.x*420.0, i.uv.y*8.0 - t*_Speed*2.3));
                    float s = streak*0.7+streak2*0.3;
                    float breakup = smoothstep(0.35, 1.0, i.uv.y) ;
                    alpha = _Opacity * saturate(s*1.6-0.25) * (1-breakup*smoothstep(0.35,0.75,1-s)) ;
                    alpha = saturate(alpha * lerp(2.2, 1.0, saturate(i.uv.y*2.5))) * smoothstep(0.0,0.04,i.uv.y);
                    float3 wn=normalize(i.wn); if(!front) wn=-wn;
                    float ds = ddx(s)+ddy(s);
                    N=normalize(wn + float3(ds,0,ds)*6.0);
                    foam = smoothstep(0.7,1.0,i.uv.y) * streak2 * 0.6;
                }
                Light L=GetMainLight();
                float3 H=normalize(L.direction+V_);
                float fres = pow(1-saturate(abs(dot(N,V_))),4);
                float3 refl = SampleSH(reflect(-V_,N));
                float3 col = lerp(_DeepColor.rgb, _Color.rgb, 0.35) * (SampleSH(N)*0.8 + L.color*saturate(dot(N,L.direction))*0.3);
                col += refl*fres*1.2;
                col += L.color * pow(saturate(dot(N,H)),180) * 2.0;
                #if defined(_ADDITIONAL_LIGHTS)
                uint cnt=GetAdditionalLightsCount();
                for(uint li=0; li<cnt; li++){ Light al=GetAdditionalLight(li,i.wp); float3 ah=normalize(al.direction+V_);
                    col += al.color*al.distanceAttenuation*(pow(saturate(dot(N,ah)),120)*3.0 + saturate(dot(N,al.direction))*0.15); }
                #endif
                col = lerp(col, (SampleSH(float3(0,1,0))+L.color*0.3)*1.4+0.15, foam);
                alpha = saturate(alpha + fres*0.35*(_Mode<0.5) + foam*0.5);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}
