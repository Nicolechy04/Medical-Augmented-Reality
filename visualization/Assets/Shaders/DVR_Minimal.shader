Shader "VolumeRendering/DVR_Minimal" {
	Properties {
		[Header(Volume)]
		VolumeTex("Volume", 3D) = "white" {}

		[Header(Raymarching)]
		[PowerSlider(3.0)] StepSize("Step Size", Range(0.0001, 0.1)) = 0.01
		[Toggle(STOCHASTIC_JITTER)] STOCHASTIC_JITTER("Stochastic Jitter", Float) = 0

		[Header(Blinn Phong)]
		BlinnPhongLightPos("Light Position", Vector) = (1.0, 0.0, 0.0, 1.0)
		BaseColor("Base Color", Color) = (0.2, 0.2, 0.2, 1.0)
		AmbientColor("Ambient Color", Color) = (0.5, 0.5, 0.5, 1.0)
		OutlineColor("Outline Color", Color) = (0.8, 0.7, 0.6, 1.0)
		IsoValue("Iso-Value", Range(0.0, 1.0)) = 0.1
		AlphaMultiplier("Alpha Multiplier", Range(0.0, 300.0)) = 1.0
		BP_k_s("k_s (Specular)", Range(0.0, 1.0)) = 0.2
		BP_k_a("k_a (Ambient)", Range(0.0, 1.0)) = 0.2
		BP_k_d("k_d (Diffuse)", Range(0.0, 1.0)) = 0.6
		BP_s1("Shininess s1", Range(1.0, 100.0)) = 5.0
		BP_s2("Shininess s2", Range(1.0, 100.0)) = 5.0
		BP_s1_percentage("s1 Percentage", Range(1.0, 100.0)) = 50.0

		[Header(Volume Resolution)]
		BlinnPhongTextureX("Texture Resolution X", Float) = 512
		BlinnPhongTextureY("Texture Resolution Y", Float) = 1024
		BlinnPhongTextureZ("Texture Resolution Z", Float) = 128
		GradientSmoothKernelSize("Gradient Smoothness", Range(0.0, 50.0)) = 5.0

		[Header(Feature Volume)]
		[Toggle(SHOW_FEATURE_VOLUME)] SHOW_FEATURE_VOLUME("Show Feature Volume", Float) = 0
		[HideIfDisabled(SHOW_FEATURE_VOLUME)] FeatureVolumeTex("Feature Volume", 3D) = "black" {}
		[HideIfDisabled(SHOW_FEATURE_VOLUME)] FeatureBandLow("Feature Band Low", Range(0.0, 1.0)) = 0.1
		[HideIfDisabled(SHOW_FEATURE_VOLUME)] FeatureBandHigh("Feature Band High", Range(0.0, 1.0)) = 1.0
		[HideIfDisabled(SHOW_FEATURE_VOLUME)] FeatureLowColor("Feature Low Color", Color) = (0.0, 0.2, 1.0, 1.0)
		[HideIfDisabled(SHOW_FEATURE_VOLUME)] FeatureMidLowColor("Feature Mid-Low Color", Color) = (0.0, 0.9, 1.0, 1.0)
		[HideIfDisabled(SHOW_FEATURE_VOLUME)] FeatureMidColor("Feature Mid Color", Color) = (0.2, 1.0, 0.25, 1.0)
		[HideIfDisabled(SHOW_FEATURE_VOLUME)] FeatureMidHighColor("Feature Mid-High Color", Color) = (1.0, 0.95, 0.15, 1.0)
		[HideIfDisabled(SHOW_FEATURE_VOLUME)] FeatureHighColor("Feature High Color", Color) = (1.0, 0.15, 0.0, 1.0)
		[HideIfDisabled(SHOW_FEATURE_VOLUME)] FeatureAlphaMultiplier("Feature Global Alpha", Range(0.0, 300.0)) = 1.0
		[HideIfDisabled(SHOW_FEATURE_VOLUME)] FeatureLowAlphaMultiplier("Feature Low Alpha", Range(0.0, 300.0)) = 0.25
		[HideIfDisabled(SHOW_FEATURE_VOLUME)] FeatureHighAlphaMultiplier("Feature High Alpha", Range(0.0, 300.0)) = 1.0
		[HideIfDisabled(SHOW_FEATURE_VOLUME)] FeatureAlphaCurve("Feature Alpha Curve", Range(0.1, 8.0)) = 1.0
		[HideInInspector] FeatureIsoValue("Feature Iso-Value", Range(0.0, 1.0)) = 0.1
		[HideInInspector] FeatureTexResX("Feature Texture Resolution X", Float) = 128
		[HideInInspector] FeatureTexResY("Feature Texture Resolution Y", Float) = 128
		[HideInInspector] FeatureTexResZ("Feature Texture Resolution Z", Float) = 128
		[HideInInspector] FeatureIsoSoftness("Feature Iso Softness", Range(0.0, 6.0)) = 2.0
		[HideIfDisabled(SHOW_FEATURE_VOLUME)] FeatureSamplingDensity("Feature Sampling Density", Range(0.25, 40.0)) = 1.0

		[Header(Clipping Planes)]
		[Toggle(CLIPPING_PLANES)] ClippingPlanes("Activate Clipping Planes", Float) = 0
		[HideIfDisabled(CLIPPING_PLANES)] NumberOfCuttingPlanes("Number of Active Clipping Planes (max 4)", Float) = 0
		[HideIfDisabled(CLIPPING_PLANES)] ClippingPlane0("Clipping Plane 0", Vector) = (1.0, 0.0, 0.0, 1.0)
		[HideIfDisabled(CLIPPING_PLANES)] ClippingPlane1("Clipping Plane 1", Vector) = (1.0, 0.0, 0.0, 1.0)
		[HideIfDisabled(CLIPPING_PLANES)] ClippingPlane2("Clipping Plane 2", Vector) = (1.0, 0.0, 0.0, 1.0)
		[HideIfDisabled(CLIPPING_PLANES)] ClippingPlane3("Clipping Plane 3", Vector) = (1.0, 0.0, 0.0, 1.0)

		[Enum(UnityEngine.Rendering.BlendMode)] _Blend("Blend mode", Float) = 0
	}

	SubShader {
		Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }

		Pass {
			Blend SrcAlpha [_Blend]
			ZTest Always

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma shader_feature STOCHASTIC_JITTER
			#pragma shader_feature CLIPPING_PLANES
			#pragma shader_feature SHOW_FEATURE_VOLUME
			#pragma target 5.0
			#pragma require 3dtextures

			#include "UnityCG.cginc"

			// Volume
			sampler3D VolumeTex;

			// Raymarching
			float StepSize;

			// Blinn-Phong
			float3 BlinnPhongLightPos;
			fixed4 BaseColor;
			fixed4 AmbientColor;
			fixed4 OutlineColor;
			float IsoValue;
			float AlphaMultiplier;
			float BP_k_s;
			float BP_k_a;
			float BP_k_d;
			float BP_s1;
			float BP_s2;
			float BP_s1_percentage;

			// Volume resolution (for gradient computation)
			float BlinnPhongTextureX;
			float BlinnPhongTextureY;
			float BlinnPhongTextureZ;
			float GradientSmoothKernelSize;

			// Feature
			sampler3D FeatureVolumeTex;
			float FeatureBandLow;
			float FeatureBandHigh;
			fixed4 FeatureLowColor;
			fixed4 FeatureMidLowColor;
			fixed4 FeatureMidColor;
			fixed4 FeatureMidHighColor;
			fixed4 FeatureHighColor;
			float FeatureAlphaMultiplier;
			float FeatureLowAlphaMultiplier;
			float FeatureHighAlphaMultiplier;
			float FeatureAlphaCurve;
			float FeatureIsoValue;
			float FeatureTexResX;
			float FeatureTexResY;
			float FeatureTexResZ;
			float FeatureIsoSoftness;
			float FeatureSamplingDensity;

			// Clipping
			float NumberOfCuttingPlanes;
			float4 ClippingPlane0;
			float4 ClippingPlane1;
			float4 ClippingPlane2;
			float4 ClippingPlane3;

			struct v2f {
				float4 pos : SV_POSITION;
				float4 worldPos : TEXCOORD0;
				float3 cameraPosInObjectSpace : TEXCOORD1;
			};

			// --- Helpers inlined for self-contained minimal shader ---

			float rand(float3 v) {
				return frac(sin(dot(v, float3(12.9898, 78.233, 45.5432))) * 43758.5453);
			}

			float2 intersectBox(float3 dir, float3 origin, float3 aabbMin, float3 aabbMax) {
				float3 invR = 1.0 / dir;
				float3 tbot = invR * (aabbMin - origin);
				float3 ttop = invR * (aabbMax - origin);
				float3 tmin = min(ttop, tbot);
				float3 tmax = max(ttop, tbot);
				float2 t = max(tmin.xx, tmin.yz);
				float t0 = max(t.x, t.y);
				t = min(tmax.xx, tmax.yz);
				float t1 = min(t.x, t.y);
				return float2(t0, t1);
			}

			fixed4 blendUnder(fixed4 colorAccum, fixed4 colorBehind) {
				colorAccum.rgb += (1.0 - colorAccum.a) * colorBehind.rgb * colorBehind.a;
				colorAccum.a += (1.0 - colorAccum.a) * colorBehind.a;
				return colorAccum;
			}

			float3 computeGradient(float3 texCoords) {
				float dx  = tex3D(VolumeTex, texCoords + float3(1.0 / BlinnPhongTextureX, 0, 0)).r;
				float dy  = tex3D(VolumeTex, texCoords + float3(0, 1.0 / BlinnPhongTextureY, 0)).r;
				float dz  = tex3D(VolumeTex, texCoords + float3(0, 0, 1.0 / BlinnPhongTextureZ)).r;
				float mdx = tex3D(VolumeTex, texCoords + float3(-1.0 / BlinnPhongTextureX, 0, 0)).r;
				float mdy = tex3D(VolumeTex, texCoords + float3(0, -1.0 / BlinnPhongTextureY, 0)).r;
				float mdz = tex3D(VolumeTex, texCoords + float3(0, 0, -1.0 / BlinnPhongTextureZ)).r;
				return float3(dx - mdx, dy - mdy, dz - mdz) * 0.5;
			}

			float3 computeGradientSmooth(float3 position) {
				float3 volumeSize = float3(BlinnPhongTextureX, BlinnPhongTextureY, BlinnPhongTextureZ);
				float3 invVolumeSize = 1.0 / volumeSize;
				float3 delta = invVolumeSize;
				float kernelRadius = GradientSmoothKernelSize * invVolumeSize.x;
				float3 smoothGradient = float3(0, 0, 0);

				float3 kernelDelta = float3(kernelRadius, 0, 0);
				[loop]
				for (float x = -kernelRadius; x <= kernelRadius; x += delta.x) {
					float3 samplePos = position + float3(x, 0, 0);
					smoothGradient.x += (tex3D(VolumeTex, samplePos + kernelDelta).r - tex3D(VolumeTex, samplePos - kernelDelta).r) * 0.5;
				}
				smoothGradient.x /= (2.0 * kernelRadius / delta.x);

				kernelDelta = float3(0, kernelRadius, 0);
				[loop]
				for (float y = -kernelRadius; y <= kernelRadius; y += delta.y) {
					float3 samplePos = position + float3(0, y, 0);
					smoothGradient.y += (tex3D(VolumeTex, samplePos + kernelDelta).r - tex3D(VolumeTex, samplePos - kernelDelta).r) * 0.5;
				}
				smoothGradient.y /= (2.0 * kernelRadius / delta.y);

				kernelDelta = float3(0, 0, kernelRadius);
				[loop]
				for (float z = -kernelRadius; z <= kernelRadius; z += delta.z) {
					float3 samplePos = position + float3(0, 0, z);
					smoothGradient.z += (tex3D(VolumeTex, samplePos + kernelDelta).r - tex3D(VolumeTex, samplePos - kernelDelta).r) * 0.5;
				}
				smoothGradient.z /= (2.0 * kernelRadius / delta.z);

				return smoothGradient;
			}

			float3 getFeatureInvResolution() {
				float3 featureRes = max(float3(FeatureTexResX, FeatureTexResY, FeatureTexResZ), 1.0);
				return 1.0 / featureRes;
			}

			float sampleFeatureFiltered(float3 texCoords) {
				float3 texel = getFeatureInvResolution();
				float3 axisWeights = texel / max(texel.x + texel.y + texel.z, 1e-6);
				float center = tex3D(FeatureVolumeTex, texCoords).r;
				float xPair = tex3D(FeatureVolumeTex, texCoords + float3(texel.x, 0, 0)).r + tex3D(FeatureVolumeTex, texCoords - float3(texel.x, 0, 0)).r;
				float yPair = tex3D(FeatureVolumeTex, texCoords + float3(0, texel.y, 0)).r + tex3D(FeatureVolumeTex, texCoords - float3(0, texel.y, 0)).r;
				float zPair = tex3D(FeatureVolumeTex, texCoords + float3(0, 0, texel.z)).r + tex3D(FeatureVolumeTex, texCoords - float3(0, 0, texel.z)).r;
				float sideW = 0.5;
				float sum = center * 0.5 + sideW * (axisWeights.x * xPair + axisWeights.y * yPair + axisWeights.z * zPair);
				float norm = 0.5 + 2.0 * sideW * (axisWeights.x + axisWeights.y + axisWeights.z);
				return sum / max(norm, 1e-6);
			}

			int getFeatureSubstepCount(float3 stepDirTexSpace) {
				float3 featureRes = max(float3(FeatureTexResX, FeatureTexResY, FeatureTexResZ), 1.0);
				float3 voxelDelta = abs(stepDirTexSpace) * featureRes * max(FeatureSamplingDensity, 0.25);
				float maxVoxelDelta = max(voxelDelta.x, max(voxelDelta.y, voxelDelta.z));
				return clamp((int)ceil(maxVoxelDelta), 1, 8);
			}

			float2 getFeatureBandRange() {
				return float2(min(FeatureBandLow, FeatureBandHigh), max(FeatureBandLow, FeatureBandHigh));
			}

			float getFeatureBandMask(float sampleValue, float edgeWidth) {
				float2 bandRange = getFeatureBandRange();
				float enterBand = smoothstep(bandRange.x - edgeWidth, bandRange.x + edgeWidth, sampleValue);
				float leaveBand = 1.0 - smoothstep(bandRange.y - edgeWidth, bandRange.y + edgeWidth, sampleValue);
				return enterBand * leaveBand;
			}

			float getFeatureBandLerp(float sampleValue) {
				float2 bandRange = getFeatureBandRange();
				float bandWidth = max(bandRange.y - bandRange.x, 1e-5);
				return saturate((sampleValue - bandRange.x) / bandWidth);
			}

			float smoothTransferStep(float t) {
				float clamped = saturate(t);
				return clamped * clamped * (3.0 - 2.0 * clamped);
			}

			float3 getFeatureTransferColor(float t) {
				float scaled = saturate(t) * 4.0;

				if (scaled < 1.0) {
					return lerp(FeatureLowColor.rgb, FeatureMidLowColor.rgb, smoothTransferStep(scaled));
				}

				if (scaled < 2.0) {
					return lerp(FeatureMidLowColor.rgb, FeatureMidColor.rgb, smoothTransferStep(scaled - 1.0));
				}

				if (scaled < 3.0) {
					return lerp(FeatureMidColor.rgb, FeatureMidHighColor.rgb, smoothTransferStep(scaled - 2.0));
				}

				return lerp(FeatureMidHighColor.rgb, FeatureHighColor.rgb, smoothTransferStep(scaled - 3.0));
			}

			float getFeatureTransferAlpha(float t) {
				float alphaT = pow(saturate(t), max(FeatureAlphaCurve, 0.1));
				return lerp(FeatureLowAlphaMultiplier, FeatureHighAlphaMultiplier, alphaT);
			}

			fixed4 applyShadingRetina(fixed4 surfaceColor, float3 texPosition, float3 gradient, float3 rayDir) {
				float3 L = normalize(-BlinnPhongLightPos);
				float3 N = normalize(gradient);
				float3 V = normalize(rayDir);
				float3 H = normalize(L + V);

				fixed3 ambient  = BP_k_a * AmbientColor.rgb;
				fixed  ndotl    = max(dot(N, L), 0.0);
				fixed3 diffuse  = BP_k_d * ndotl * surfaceColor.rgb;

				fixed ndoth1 = pow(max(dot(N, H), 0.0), BP_s1);
				fixed ndoth2 = pow(max(dot(N, H), 0.0), BP_s2);
				fixed ndoth  = (BP_s1_percentage * 0.01) * ndoth1 + (1.0 - BP_s1_percentage * 0.01) * ndoth2;
				fixed3 specular = BP_k_s * ndoth * OutlineColor.rgb;

				fixed3 finalColor = ambient + diffuse + specular;
				return fixed4(finalColor, surfaceColor.a);
			}

			// --- Vertex ---

			v2f vert(appdata_base v) {
				v2f o = (v2f)0;
				o.pos = UnityObjectToClipPos(v.vertex);
				o.worldPos = v.vertex;
				o.cameraPosInObjectSpace = mul(unity_WorldToObject, _WorldSpaceCameraPos - mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz).xyz;
				return o;
			}

			// --- Fragment ---

			fixed4 frag(v2f i) : SV_Target {
				float4 entryPoint = i.worldPos;
				float3 dir = normalize(-ObjSpaceViewDir(entryPoint));
				float2 tnear_tfar = intersectBox(dir, i.cameraPosInObjectSpace,
					float3(-0.5, -0.5, -0.5), float3(0.5, 0.5, 0.5));

				if (tnear_tfar.x < 0.0) tnear_tfar.x = 0.0;

				float3 rayStart  = i.cameraPosInObjectSpace + dir * tnear_tfar.x + 0.5;
				float3 rayStop   = i.cameraPosInObjectSpace + dir * tnear_tfar.y + 0.5;
				float3 deltaDir  = normalize(rayStop - rayStart) * StepSize;
				float  travel    = distance(rayStop, rayStart);
				float3 voxelCoord = rayStart;

#if defined(STOCHASTIC_JITTER)
				voxelCoord += deltaDir * (rand(entryPoint.xyz) - 0.5);
#endif

				fixed4 rayColor = fixed4(0.0, 0.0, 0.0, 0.0);

				[loop]
				for (; travel > 0.0; travel -= StepSize, voxelCoord += deltaDir) {

#if defined(CLIPPING_PLANES)
					if (NumberOfCuttingPlanes > 0 && dot(ClippingPlane0, float4(voxelCoord, 1.0)) > 0) continue;
					if (NumberOfCuttingPlanes > 1 && dot(ClippingPlane1, float4(voxelCoord, 1.0)) > 0) continue;
					if (NumberOfCuttingPlanes > 2 && dot(ClippingPlane2, float4(voxelCoord, 1.0)) > 0) continue;
					if (NumberOfCuttingPlanes > 3 && dot(ClippingPlane3, float4(voxelCoord, 1.0)) > 0) continue;
#endif

					float sampleIntensity = tex3D(VolumeTex, voxelCoord).r;

					if (sampleIntensity > IsoValue) {
						float alpha = saturate(sampleIntensity * StepSize * AlphaMultiplier);
						BaseColor.a = alpha;

						float3 gradient = computeGradientSmooth(voxelCoord);
						fixed4 litColor = applyShadingRetina(BaseColor, voxelCoord, gradient, dir);
						rayColor = blendUnder(rayColor, litColor);
					}

					// Feature overlay
#if defined(SHOW_FEATURE_VOLUME)
					{
						int featureSubsteps = getFeatureSubstepCount(deltaDir);
						float featureMaxSample = 0.0;
						float featureAccumSample = 0.0;
						float featureNonZeroCount = 0.0;
						[unroll]
						for (int s = 0; s < 8; s++) {
							if (s >= featureSubsteps) break;
							float t = ((float)s + 0.5) / (float)featureSubsteps;
							float3 featureCoord = voxelCoord + deltaDir * t;
							float candidate = sampleFeatureFiltered(featureCoord);
							float nonZeroSample = step(1e-6, candidate);
							featureMaxSample = max(featureMaxSample, candidate);
							featureAccumSample += candidate * nonZeroSample;
							featureNonZeroCount += nonZeroSample;
						}

						float3 featureTexel = getFeatureInvResolution();
						float featureSampleFiltered = featureNonZeroCount > 0.0 ? (featureAccumSample / featureNonZeroCount) : 0.0;
						float bandEdgeWidth = max(max(featureTexel.x, max(featureTexel.y, featureTexel.z)) * max(FeatureIsoSoftness, 0.0), 1e-5);
						float featureMask = step(1e-6, featureMaxSample) * getFeatureBandMask(featureSampleFiltered, bandEdgeWidth);
						if (featureMask > 0.001) {
							float featureLerp = getFeatureBandLerp(featureSampleFiltered);
							float3 featureColorRGB = getFeatureTransferColor(featureLerp);
							float featureAlphaRamp = getFeatureTransferAlpha(featureLerp);
							float featureAlpha = saturate((1-sampleIntensity) * StepSize * FeatureAlphaMultiplier * featureAlphaRamp * featureMask);
							fixed4 featureColor = fixed4(featureColorRGB, featureAlpha);
							rayColor = blendUnder(rayColor, featureColor);
						}
					}
#endif

					// Early exit if nearly opaque
					if (rayColor.a > 0.99) break;
				}

				return rayColor;
			}

			ENDCG
		}
	}

	FallBack "Diffuse"
}
