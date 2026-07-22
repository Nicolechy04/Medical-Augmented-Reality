Shader "TensionViz/InjectionAngleOverlay"
{
    Properties
    {
        _BscanTex      ("B-Scan",             2D)               = "black" {}
        _NeedleTipUV   ("Needle Tip UV",      Vector)           = (0.56, 0.53, 0, 0)
        _SurfaceNormal ("Surface Normal UV",  Vector)           = (0, 1, 0, 0)
        _NeedleDir     ("Needle Direction",   Vector)           = (0.1, -1, 0, 0)
        _AngleDeviation("Deviation (deg)",    Float)            = 0
        _SafeMaxDeg    ("Safe Max (deg)",     Float)            = 10
        _WarnMaxDeg    ("Warn Max (deg)",     Float)            = 20
        _ArcRadius     ("Arc Radius (UV)",    Range(0.02,0.25)) = 0.12
        _ArcThickness  ("Arc Thickness",      Range(0.005,0.05)) = 0.020
        _NormalLineLen ("Normal Line Length", Range(0.02,0.30)) = 0.18
        _LineWidth     ("Line Width",         Range(0.001,0.015)) = 0.004
        _DashPeriod    ("Dash Period",        Range(0.005,0.05)) = 0.018
        _DashFill      ("Dash Fill",          Range(0.1,0.9))   = 0.45
        _AspectRatio   ("B-Scan W/H",         Float)            = 0.977
        _FlipBscanY    ("Flip B-Scan Y",      Float)            = 0
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

            sampler2D _BscanTex;
            float4 _NeedleTipUV;
            float4 _SurfaceNormal;
            float4 _NeedleDir;
            float  _AngleDeviation;
            float  _SafeMaxDeg;
            float  _WarnMaxDeg;
            float  _ArcRadius;
            float  _ArcThickness;
            float  _NormalLineLen;
            float  _LineWidth;
            float  _DashPeriod;
            float  _DashFill;
            float  _AspectRatio;
            float  _FlipBscanY;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f     { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            float2 AspectUV(float2 v) { return float2(v.x * _AspectRatio, v.y); }
            float  Angle2(float2 v)   { return atan2(v.y, v.x); }

            // Wrap to (-PI, PI]
            float WrapAngle(float a)
            {
                return a - 6.28318530 * floor((a + 3.14159265) / 6.28318530);
            }

            // #639922 green -> #EF9F27 orange -> #E24B4A red
            float3 ArcColor(float dev)
            {
                float3 green  = float3(0.388, 0.600, 0.133);
                float3 orange = float3(0.937, 0.624, 0.153);
                float3 red    = float3(0.886, 0.294, 0.290);
                if (dev <= _SafeMaxDeg)
                    return green;
                float t = saturate((dev - _SafeMaxDeg) / max(_WarnMaxDeg - _SafeMaxDeg, 0.001));
                return lerp(orange, red, t);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;

                // B-scan background (greyscale)
                float2 bscanUV = uv;
                if (_FlipBscanY > 0.5) bscanUV.y = 1.0 - bscanUV.y;
                float  gs    = tex2D(_BscanTex, bscanUV).r;
                float3 color = float3(gs, gs, gs);

                // Aspect-corrected offset from contact point
                float2 tip   = _NeedleTipUV.xy;
                float2 delta = AspectUV(uv - tip);
                float  dist  = length(delta);

                // Direction vectors in aspect-corrected UV space
                float2 nVec = normalize(AspectUV(_SurfaceNormal.xy) + float2(0.0, 1e-5));
                float2 dVec = normalize(AspectUV(_NeedleDir.xy)     + float2(1e-5, 0.0));

                // --- Element 1: Surface normal dotted line (blue #4a9eff) ---
                {
                    float proj   = dot(delta, nVec);
                    float perp   = abs(delta.x * nVec.y - delta.y * nVec.x);
                    float onSeg  = step(0.001, proj)
                                 * step(proj, _NormalLineLen)
                                 * step(perp, _LineWidth);
                    float dashOn = step(_DashFill, fmod(proj / _DashPeriod, 1.0));
                    // #4a9eff = (0.290, 0.620, 1.0)
                    color = lerp(color, float3(0.290, 0.620, 1.0), onSeg * dashOn * 0.88);
                }

                // --- Element 2: Deviation arc (normal -> needle span) ---
                {
                    float inRing = step(dist, _ArcRadius + _ArcThickness * 0.5)
                                 * step(_ArcRadius - _ArcThickness * 0.5, dist);

                    if (inRing > 0.5 && dist > 1e-5)
                    {
                        float2 dir     = delta / dist;
                        float  angN    = Angle2(nVec);
                        float  angD    = Angle2(dVec);
                        float  fragAng = Angle2(dir);

                        // Signed arc span from normal to needle (shorter path)
                        float span    = WrapAngle(angD - angN);
                        float fragRel = WrapAngle(fragAng - angN);

                        // Fragment is inside arc: same sign as span, not past needle
                        float inArc = step(1e-4, abs(span))
                                    * step(0.0, span * fragRel)
                                    * step(abs(fragRel), abs(span));

                        if (inArc > 0.5)
                        {
                            float3 arcCol  = ArcColor(_AngleDeviation);
                            float edgeFade = smoothstep(_ArcThickness * 0.5,
                                                        _ArcThickness * 0.5 - 0.003,
                                                        abs(dist - _ArcRadius));
                            // opacity ~0.45 (semi-transparent, higher transp. per user request)
                            color = lerp(color, arcCol, edgeFade * 0.45);
                        }
                    }
                }

                return fixed4(color, 1);
            }
            ENDCG
        }
    }
    FallBack "Unlit/Color"
}
