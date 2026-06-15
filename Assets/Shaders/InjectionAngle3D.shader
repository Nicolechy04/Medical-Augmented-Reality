Shader "TensionViz/InjectionAngle3D"
{
    Properties
    {
        _VolumeTex      ("OCT Volume 3D",        3D)              = "" {}
        _SliceZ         ("Slice Z",              Range(0,1))      = 0.5
        _Brightness     ("Volume Brightness",    Range(0.5,6))    = 3.0
        _GradStep       ("Gradient Step",        Range(0.002,0.05)) = 0.012
        _NeedleTipUV    ("Needle Tip UV",        Vector)          = (0.5, 0.5, 0, 0)
        _NeedleDir      ("Needle Direction",     Vector)          = (0, 0.1, -1, 0)
        _SurfaceNormal  ("Surface Normal",       Vector)          = (0, 1, 0, 0)
        _AngleDeviation ("Deviation deg",        Float)           = 0
        _SafeMaxDeg     ("Safe Max deg",         Float)           = 10
        _CautionMaxDeg  ("Caution Max deg",      Float)           = 20
        _ArcRadius      ("Arc Radius",           Range(0.02,0.3)) = 0.12
        _LineWidth      ("Line Width",           Range(0.001,0.02)) = 0.005
        _AspectRatio    ("Aspect Ratio",         Float)           = 1.0
        _ShowGradField  ("Gradient Overlay",     Range(0,1))      = 0.25
    }

    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" }
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler3D _VolumeTex;
            float  _SliceZ;
            float  _Brightness;
            float  _GradStep;
            float4 _NeedleTipUV;
            float4 _NeedleDir;
            float4 _SurfaceNormal;
            float  _AngleDeviation;
            float  _SafeMaxDeg;
            float  _CautionMaxDeg;
            float  _ArcRadius;
            float  _LineWidth;
            float  _AspectRatio;
            float  _ShowGradField;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f     { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            float SampleVol(float3 uvw)
            {
                return tex3D(_VolumeTex, uvw).r;
            }

            float3 CentralDiff(float3 uvw, float h)
            {
                float dx = SampleVol(uvw + float3(h,0,0)) - SampleVol(uvw - float3(h,0,0));
                float dy = SampleVol(uvw + float3(0,h,0)) - SampleVol(uvw - float3(0,h,0));
                float dz = SampleVol(uvw + float3(0,0,h)) - SampleVol(uvw - float3(0,0,h));
                return float3(dx, dy, dz);
            }

            float2 AspectUV(float2 v) { return float2(v.x * _AspectRatio, v.y); }
            float  Angle2(float2 v)   { return atan2(v.y, v.x); }

            float3 AngleColor(float dev)
            {
                float t = saturate(dev / max(_CautionMaxDeg, 0.01));
                float3 g = float3(0.15, 0.95, 0.25);
                float3 y = float3(1.00, 0.85, 0.05);
                float3 r = float3(1.00, 0.10, 0.10);
                return t < 0.5 ? lerp(g, y, t * 2.0) : lerp(y, r, (t - 0.5) * 2.0);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv  = i.uv;
                float3 uvw = float3(uv.x, uv.y, _SliceZ);

                float gs    = saturate(SampleVol(uvw) * _Brightness);
                float3 color = float3(gs, gs, gs);

                float3 grad = CentralDiff(uvw, _GradStep);
                float  gMag = length(grad);
                float  surfaceHint = saturate(gMag * 8.0) * _ShowGradField;
                color = lerp(color, float3(1.0, 0.55, 0.1), surfaceHint * 0.35);

                float2 tip   = _NeedleTipUV.xy;
                float2 delta = AspectUV(uv - tip);
                float  dist  = length(delta);
                float2 dir   = dist > 1e-5 ? delta / dist : float2(0, 1);

                float2 n2D = normalize(_SurfaceNormal.xy + float2(0, 1e-4));

                float angN    = Angle2(n2D);
                float fragAng = Angle2(dir);
                float warnRad = _CautionMaxDeg * 0.01745;

                float inArc = step(dist, _ArcRadius + _LineWidth * 0.5)
                            * step(_ArcRadius - _LineWidth * 3.5, dist);
                float devFromN = abs(fragAng - angN);
                if (devFromN > 3.1416) devFromN = 6.2832 - devFromN;

                if (inArc * step(devFromN, warnRad) > 0.5)
                {
                    float arcDev = devFromN / warnRad * _CautionMaxDeg;
                    float edgeFade = smoothstep(_LineWidth * 3.5,
                                                _LineWidth * 3.5 - 0.003,
                                                abs(dist - _ArcRadius));
                    color = lerp(color, AngleColor(arcDev), edgeFade * 0.85);
                }

                if (_AngleDeviation > _CautionMaxDeg)
                {
                    float eu = min(uv.x, 1.0 - uv.x);
                    float ev = min(uv.y, 1.0 - uv.y);
                    float on = step(min(eu, ev), 0.013);
                    color = lerp(color, float3(1.0, 0.08, 0.08), on * 0.85);
                }

                return fixed4(color, 1);
            }
            ENDCG
        }
    }
    FallBack "Unlit/Color"
}
