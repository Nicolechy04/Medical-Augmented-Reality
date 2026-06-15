Shader "TensionViz/TensionOverlay2D"
{
    // Single-pass opaque composite:
    //   grayscale B-scan  +  tension heatmap overlay  +  needle-tip crosshair
    //
    // Assign to a Quad MeshRenderer.
    // PigEyeSequencePlayer drives _BscanTex, _TensionTex, _NeedleTipUV, _HighTensionPulse
    // at runtime; everything else is tunable in the Material Inspector.

    Properties
    {
        [Header(Textures)]
        _BscanTex   ("B-Scan (grayscale)",  2D) = "black" {}
        _TensionTex ("Tension Map (R ch)",  2D) = "black" {}

        [Header(Tension Color LUT)]
        _ColorLow     ("Low    (blue)",   Color) = (0.00, 0.20, 1.00, 1)
        _ColorMidLow  ("MidLow (cyan)",   Color) = (0.00, 0.90, 1.00, 1)
        _ColorMid     ("Mid    (green)",  Color) = (0.20, 1.00, 0.25, 1)
        _ColorMidHigh ("MidHigh(yellow)", Color) = (1.00, 0.95, 0.15, 1)
        _ColorHigh    ("High   (red)",    Color) = (1.00, 0.15, 0.00, 1)

        [Header(Heatmap Blend)]
        _TensionThreshold ("Visibility Threshold", Range(0.0, 0.5)) = 0.05
        _OverlayOpacity   ("Overlay Opacity",      Range(0.0, 1.0)) = 0.75
        _AlphaCurve       ("Alpha Curve (power)",  Range(0.5, 5.0)) = 2.0

        [Header(Needle Tip Marker)]
        [Toggle] _ShowNeedleTip  ("Show Needle Tip",       Float)  = 1
        _NeedleTipUV     ("Needle Tip (UV x,y)",    Vector) = (0.504, 0.641, 0, 0)
        _NeedleTipRadius ("Marker Radius (UV)",    Range(0.005, 0.08)) = 0.020
        _AspectRatio     ("B-Scan Aspect W/H",     Float)  = 0.977

        [Header(High Tension Warning)]
        // Set to 1 when HighTensionAlert is true; border pulses red
        _HighTensionPulse ("High Tension Pulse [0-1]", Range(0, 1)) = 0

        [Header(Flip)]
        [Toggle] _FlipBscanY ("Flip B-Scan Y (if upside-down)", Float) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" }
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            // ── Uniforms ───────────────────────────────────────────────

            sampler2D _BscanTex;
            sampler2D _TensionTex;

            fixed4 _ColorLow, _ColorMidLow, _ColorMid, _ColorMidHigh, _ColorHigh;
            float  _TensionThreshold, _OverlayOpacity, _AlphaCurve;

            float  _ShowNeedleTip;
            float4 _NeedleTipUV;
            float  _NeedleTipRadius, _AspectRatio;

            float  _HighTensionPulse;
            float  _FlipBscanY;

            // ── Vertex ────────────────────────────────────────────────

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f     { float4 pos    : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            // ── Helpers ───────────────────────────────────────────────

            // 5-stop colour ramp: blue → cyan → green → yellow → red
            float3 TensionColor(float t)
            {
                float s = saturate(t) * 4.0;
                if (s < 1.0) return lerp(_ColorLow.rgb,     _ColorMidLow.rgb,  s);
                if (s < 2.0) return lerp(_ColorMidLow.rgb,  _ColorMid.rgb,     s - 1.0);
                if (s < 3.0) return lerp(_ColorMid.rgb,     _ColorMidHigh.rgb, s - 2.0);
                              return lerp(_ColorMidHigh.rgb, _ColorHigh.rgb,    s - 3.0);
            }

            // ── Fragment ──────────────────────────────────────────────

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;

                // ① B-scan background ─────────────────────────────────
                float2 bscanUV = uv;
                if (_FlipBscanY > 0.5) bscanUV.y = 1.0 - bscanUV.y;
                float  bscan   = tex2D(_BscanTex, bscanUV).r;
                float3 color   = float3(bscan, bscan, bscan);

                // ② Tension heatmap overlay ───────────────────────────
                float tension = saturate(tex2D(_TensionTex, uv).r);
                if (tension > _TensionThreshold)
                {
                    float t     = saturate((tension - _TensionThreshold)
                                           / max(1.0 - _TensionThreshold, 1e-4));
                    float alpha = pow(t, max(_AlphaCurve, 0.1)) * _OverlayOpacity;
                    color = lerp(color, TensionColor(tension), alpha);
                }

                // ③ High-tension border warning (2 Hz pulse) ──────────
                if (_HighTensionPulse > 0.01)
                {
                    float pulse  = _HighTensionPulse
                                 * (0.6 + 0.4 * sin(_Time.y * 6.2832 * 2.0));
                    float2 edge  = min(uv, 1.0 - uv);
                    float  border = 1.0 - smoothstep(0.0, 0.025, min(edge.x, edge.y));
                    color = lerp(color, _ColorHigh.rgb, border * pulse);
                }

                // ④ Needle-tip crosshair ───────────────────────────────
                if (_ShowNeedleTip > 0.5)
                {
                    float2 tip = _NeedleTipUV.xy;
                    float  r   = _NeedleTipRadius;

                    // correct circular shape for non-square B-scan
                    float2 delta = (uv - tip) * float2(_AspectRatio, 1.0);
                    float  dist  = length(delta);

                    float ew = r * 0.12; // anti-alias width

                    // hollow ring
                    float ring = smoothstep(r + ew, r - ew, dist)
                               * (1.0 - smoothstep(r * 0.60 - ew, r * 0.60 + ew, dist));

                    // crosshair arms extending from ring outward
                    float  lw      = r * 0.09;          // line half-width
                    float  arm     = r * 2.5;            // arm length
                    float  outside = step(r + ew, dist); // 1 = outside ring

                    float hArm = outside
                               * step(abs(delta.x), arm)          // within extent
                               * smoothstep(lw, lw * 0.2, abs(delta.y)); // thin line

                    float vArm = outside
                               * step(abs(delta.y), arm)
                               * smoothstep(lw, lw * 0.2, abs(delta.x));

                    float3 markerCol = lerp(float3(1, 1, 1), _ColorHigh.rgb,
                                           saturate(_HighTensionPulse * 2.0));
                    float  marker    = saturate(ring + hArm + vArm);
                    color = lerp(color, markerCol, marker * 0.92);
                }

                return fixed4(color, 1.0);
            }
            ENDCG
        }
    }

    FallBack "Unlit/Color"
}
