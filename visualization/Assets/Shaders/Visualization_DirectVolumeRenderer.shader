
// Upgrade NOTE: replaced 'defined USE_OAC_VESSEL_SEG' with 'defined (USE_OAC_VESSEL_SEG)'



// ================================================================================================
// 
// If not explicitly stated: Copyright (C) 2019, all rights reserved,
// Alexander Winkler
// Email alexander.winkler@tum.de
// Computer Aided Medical Procedures and Augmented Reality
// Technische Universität München
// Boltzmannstr. 3, 85748 Garching b. München, Germany
// 
// ================================================================================================
// Extended by Markus Hamberger (Render Mode Retina BLinn Phong)
Shader "VolumeRendering/Visualization_DVR" {
	Properties{

		VolumeTex("Volume", 3D) = "white" {}
		[Header(General Settings)]
		[PowerSlider(3.0)]StepSize("Step Size", Range(0.0001, 0.1)) = 0.1
		Multiplier("Result Multiplier", Range(0.0, 10)) = 1.0
		Center("Result Offset", Range(-3.0, 3)) = 0.0
		[Toggle(STOCHASTIC_JITTER)]STOCHASTIC_JITTER("Stochastic jitter", Float) = 0

		[Header(Rendering Mode Specific Settings)]
		[KeywordEnum(ACCUMULATE, MAXIMUM, BLINN_PHONG, COMPOSITING, SHADED_COMPOSITING, CUSTOM_TRANSFERFUNCTION, EXPERIMENTAL, RETINA_BLINN_PHONG , DEBUG_EXPERIMENTAL)] _Rendering("Rendering Mode", Float) = 0

		[HideIfDisabled(_RENDERING_BLINN_PHONG, _RENDERING_SHADED_COMPOSITING, _RENDERING_EXPERIMENTAL, _RENDERING_RETINA_BLINN_PHONG)]BlinnPhongLightPos("Blinn-Phong Light Position", Vector) = (1.0,0.0,0.0,1.0)
		[HideIfDisabled(_RENDERING_BLINN_PHONG, _RENDERING_SHADED_COMPOSITING, _RENDERING_EXPERIMENTAL)]BlinnPhongBaseColor("Blinn-Phong Base Color", Color) = (0.2,0.2,0.2,1.0)
		[HideIfDisabled(_RENDERING_BLINN_PHONG, _RENDERING_SHADED_COMPOSITING, _RENDERING_EXPERIMENTAL)]BlinnPhongAmbientColor("Blinn-Phong Ambient Color", Color) = (0.5,0.5,0.5,1.0)
		[HideIfDisabled(_RENDERING_BLINN_PHONG, _RENDERING_SHADED_COMPOSITING, _RENDERING_EXPERIMENTAL)]BlinnPhongOutlineColor("Blinn-Phong Outline Color", Color) = (0.8,0.7,0.6,1.0)
		[HideIfDisabled(_RENDERING_BLINN_PHONG, _RENDERING_SHADED_COMPOSITING, _RENDERING_EXPERIMENTAL, _RENDERING_RETINA_BLINN_PHONG)]IsoValue("Blinn-Phong Iso-Value", Range(0.0, 1)) = 0.1
		[HideIfDisabled(_RENDERING_BLINN_PHONG, _RENDERING_SHADED_COMPOSITING, _RENDERING_EXPERIMENTAL, _RENDERING_RETINA_BLINN_PHONG)]BlinnPhong_k_s("Blinn-Phong k_s", Range(0.0, 1)) = 0.2
		[HideIfDisabled(_RENDERING_BLINN_PHONG, _RENDERING_SHADED_COMPOSITING, _RENDERING_EXPERIMENTAL, _RENDERING_RETINA_BLINN_PHONG)]BlinnPhong_k_a("Blinn-Phong k_a", Range(0.0, 1)) = 0.2
		[HideIfDisabled(_RENDERING_BLINN_PHONG, _RENDERING_SHADED_COMPOSITING, _RENDERING_EXPERIMENTAL, _RENDERING_RETINA_BLINN_PHONG)]BlinnPhong_k_d("Blinn-Phong k_d", Range(0.0, 1)) = 0.6
		[HideIfDisabled(_RENDERING_BLINN_PHONG, _RENDERING_SHADED_COMPOSITING, _RENDERING_EXPERIMENTAL, _RENDERING_RETINA_BLINN_PHONG)]BlinnPhong_s("Blinn-Phong s", Range(1.0, 100)) = 5.0
		[HideIfDisabled(_RENDERING_EXPERIMENTAL)]_Crease("Crease", Range(0, 10)) = 0.0
		[HideIfDisabled(_RENDERING_EXPERIMENTAL)]v_Exponent("Exponent", Range(1, 10)) = 1.0
		[HideInInspector]BlinnPhongTextureX("BlinnPhongTextureX", Float) = 512//767.0
		[HideInInspector]BlinnPhongTextureY("BlinnPhongTextureY", Float) = 1024//496.0
		[HideInInspector]BlinnPhongTextureZ("BlinnPhongTextureZ", Float) = 128//61.0

		[HideIfDisabled(_RENDERING_COMPOSITING, _RENDERING_SHADED_COMPOSITING, _RENDERING_CUSTOM_TRANSFERFUNCTION, _RENDERING_EXPERIMENTAL)]CompositingAlphaFactor("Compositing Alpha Multiplier", Range(0, 1)) = 1.0
		[HideIfDisabled(_RENDERING_COMPOSITING, _RENDERING_EXPERIMENTAL)][NoScaleOffset]CompositingTransferFunction("Compositing Transfer Function", 2D) = "white" {}

		// for Retina Blinn Phong only
		//++++++++++++++++++++++++++++++++++++++++++++++++++
		[Header(Retina Blinn Phong Settings)]
		[KeywordEnum(ILM, RPE, INSTRUMENT, VESSEL, INBETWEENVESSELS ,ALL)] _Section("Sections", Float) = 0
	

		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)][Toggle(ENHANCE_SPECLES)] ENHANCE_SPECLES("Enhance Specles with via LAB Color Space", Float) = 0
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)][Toggle(ENHANCE_SPECLES_ENFACE_COLORING)] ENHANCE_SPECLES_ENFACE_COLORING("Enhance Specles Enface Coloring", Float) = 0
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)][PowerSlider(1.0)]SpeclesMultiplier("Multiplier", Range(0.0, 100000.0)) = 150.0
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)][PowerSlider(1.0)]SpeclesPower("Power", Range(0.0, 10.0)) = 4

		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)][PowerSlider(1.0)]MaxDistOffset("Max Distance Offset", Range(0.0, 50.0)) = 10.0
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)][PowerSlider(1.0)]GradientSmoothKernelSize("Blinn Phong Gradient Smoothness", Range(0.0, 50.0)) = 5.0

		[Header(Shadow Settings)]
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)][Toggle(ShowShadows)] ShowShadows("Show Shadows", Float) = 0
	
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]ShadowColor("Shadow Color", Color) = (0.0,0.0,0.0,1.0)
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]ShadowAlphaMul("Shadow Alpha Multiplier", Range(0.0, 300.0)) = 1.0

		[Header(ILM Settings)]
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)][Toggle(ILM_BP_OnlyFirstHit)] ILM_BP_OnlyFirstHit("Blinn Phong Only For Surface", Float) = 0
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]ILMBaseColor("ILM Base Color", Color) = (0.2,0.2,0.2,1.0)
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]BPILMAmbientColor("BP ILM Ambient Color", Color) = (0.5,0.5,0.5,1.0)
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]BPILMOutlineColor("BP ILM Outline Color", Color) = (0.5,0.5,0.5,1.0)
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]ILMAlphaMultiplier("ILM Alpha Multiplier", Range(0.0, 50)) = 1.0
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)][PowerSlider(1.0)]MaxDistILM("Max Distance ILM", Range(0.0, 100.0)) = 20.0
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]ILMIsoValue("Iso-Value", Range(0.0, 1)) = 0.1
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]ILMBlinnPhong_k_s("Blinn-Phong k_s", Range(0.0, 1)) = 0.2
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]ILMBlinnPhong_k_a("Blinn-Phong k_a", Range(0.0, 1)) = 0.2
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]ILMBlinnPhong_k_d("Blinn-Phong k_d", Range(0.0, 1)) = 0.6
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]ILMBlinnPhong_s1("Blinn-Phong s1", Range(1.0, 100)) = 5.0
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]ILMBlinnPhong_s2("Blinn-Phong s2", Range(1.0, 100)) = 5.0
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]ILMBlinnPhong_s1_percentage("Blinn-Phong s1 Percentage", Range(1.0, 100)) = 5.0

		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)][Toggle(ILM_Receive_Shadows)] ILM_Receive_Shadows("Receive Shadows", Float) = 0

		[Header(Vessel Settings)]
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)][Toggle(USE_OAC_VESSEL_SEG)] USE_OAC_VESSEL_SEG("Use 3D OAC Segmented Vessels", Float) = 0
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)][Toggle(USE_MANUAL_VESSEL_SEG)] USE_MANUAL_VESSEL_SEG("Use 3D Manual Segmented Vessels", Float) = 0
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)][Toggle(USE_ENFACE_VESSEL_SEG)] USE_ENFACE_VESSEL_SEG("Use 2D Ennface Segmented Vessels", Float) = 0
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)][Toggle(USE_VESSEL_SKELETON_SEG)] USE_VESSEL_SKELETON_SEG("Use 3D Vessel Skeleton", Float) = 0
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)][Toggle(USE_VESSEL_SKELETON_SEG_FROM_RADIUS_MAP)] USE_VESSEL_SKELETON_SEG_FROM_RADIUS_MAP("Use 3D Vessel Skeleton From Radius Map", Float) = 0
		[HideInInspector]OACSegmentedVesselVol("OACSegmentedVesselVol", 3D) = "black" {}
			
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)][Toggle(ReduceFraying)] ReduceFraying("Reduce Fraying of Vessel", Float) = 0
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)][Toggle(Vessel_BP_OnlyFirstHit)] Vessel_BP_OnlyFirstHit("Blinn Phong Only For Surface", Float) = 0
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]VesselBaseColor("Vessel Base Color", Color) = (0.8,0.0,0.0,1.0)
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]VesselColorEnhancement("Vessel Color Enhancement", Range(0.0, 1.0)) = 0

		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]BPVesselAmbientColor("BP Vessel Ambient Color", Color) = (0.5,0.5,0.5,1.0)
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]BPVesselOutlineColor("BP Vessel Outline Color", Color) = (0.5,0.5,0.5,1.0)
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]VesselAlphaMultiplier("Vessel Alpha Multiplier", Range(0.0, 1000)) = 1000.0
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]VesselIsoValue("Iso-Value", Range(0.0, 1)) = 0.1
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]VesselSegmentationIsoValue("Segmentation Iso-Value", Range(0.0, 1)) = 0.7
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)][PowerSlider(1.0)]MaxThicknessVessel("Max Thickness", Range(0.0, 100.0)) = 20.0
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]VesselBlinnPhong_k_s("Blinn-Phong k_s", Range(0.0, 1)) = 0.2
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]VesselBlinnPhong_k_a("Blinn-Phong k_a", Range(0.0, 1)) = 0.2
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]VesselBlinnPhong_k_d("Blinn-Phong k_d", Range(0.0, 1)) = 0.6
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]VesselBlinnPhong_s1("Blinn-Phong s1", Range(1.0, 100)) = 5.0
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]VesselBlinnPhong_s2("Blinn-Phong s2", Range(1.0, 100)) = 5.0
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]VesselBlinnPhong_s1_percentage("Blinn-Phong s1 Percentage", Range(1.0, 100)) = 5.0

		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)][Toggle(Vessel_Receive_Shadows)] Vessel_Receive_Shadows("Receive Shadows", Float) = 0

		[Header(Area In Between Vessel Settings)]
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)][Toggle(IBVessel_BP_OnlyFirstHit)] IBVessel_BP_OnlyFirstHit("Blinn Phong Only For Surface", Float) = 0
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]IBVesselColor("In Between Vessel Color", Color) = (0.5,0.0,0.0,1.0)
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]BPIBVesselAmbientColor("BP In Between Vessel Ambient Color", Color) = (0.5,0.5,0.5,1.0)
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]BPIBVesselOutlineColor("BP In Between Vessel Outline Color", Color) = (0.5,0.5,0.5,1.0)
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]IBVesselAlphaMultiplier("Alpha Multiplier", Range(0.0, 300)) = 1.0
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]IBVesselIsoValue("Iso-Value", Range(0.0, 1)) = 0.1
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]IBVesselBlinnPhong_k_s("Blinn-Phong k_s", Range(0.0, 1)) = 0.2
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]IBVesselBlinnPhong_k_a("Blinn-Phong k_a", Range(0.0, 1)) = 0.2
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]IBVesselBlinnPhong_k_d("Blinn-Phong k_d", Range(0.0, 1)) = 0.6
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]IBVesselBlinnPhong_s1("Blinn-Phong s1", Range(1.0, 100)) = 5.0
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]IBVesselBlinnPhong_s2("Blinn-Phong s2", Range(1.0, 100)) = 5.0
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]IBVesselBlinnPhong_s1_percentage("Blinn-Phong s1 Percentage", Range(1.0, 100)) = 5.0

		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)][Toggle(IBVessel_Receive_Shadows)] IBVessel_Receive_Shadows("Receive Shadows", Float) = 0

		[Header(RPE Settings)]
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)][Toggle(RPE_BP_OnlyFirstHit)] RPE_BP_OnlyFirstHit("Blinn Phong Only For Surface", Float) = 0
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]RPEBaseColor("RPE Base Color", Color) = (0.2,0.2,0.2,1.0)
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)][Toggle(USE_ENFACE_COLOR)] USE_ENFACE_COLOR("Use Enface Color as Base Color", Float) = 0
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]EnfaceHueShift("Enface Color Hue Shift", Range(0.0, 1)) = 0.0
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]BPRPEAmbientColor("BP RPE Ambient Color", Color) = (0.5,0.5,0.5,1.0)
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]BPRPEOutlineColor("BP RPE Outline Color", Color) = (0.5,0.5,0.5,1.0)
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]RPEAlphaMultiplier("RPE Alpha Multiplier", Range(0.0, 300)) = 1.0
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)][PowerSlider(1.0)]MaxDistRPE("Max Distance RPE", Range(0.0, 100.0)) = 20.0
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]RPEIsoValue("Iso-Value", Range(0.0, 1)) = 0.1
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]RPEBlinnPhong_k_s("Blinn-Phong k_s", Range(0.0, 1)) = 0.2
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]RPEBlinnPhong_k_a("Blinn-Phong k_a", Range(0.0, 1)) = 0.2
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]RPEBlinnPhong_k_d("Blinn-Phong k_d", Range(0.0, 1)) = 0.6
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]RPEBlinnPhong_s1("Blinn-Phong s1", Range(1.0, 100)) = 5.0
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]RPEBlinnPhong_s2("Blinn-Phong s2", Range(1.0, 100)) = 5.0
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]RPEBlinnPhong_s1_percentage("Blinn-Phong s1 Percentage", Range(1.0, 100)) = 5.0

		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)][Toggle(RPE_Receive_Shadows)] RPE_Receive_Shadows("Receive Shadows", Float) = 0

		[Header(Instrument Settings)]
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)][Toggle(Inst_BP_OnlyFirstHit)] Inst_BP_OnlyFirstHit("Blinn Phong Only For Surface", Float) = 0
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]InstBaseColor("Inst Base Color", Color) = (0.2,0.2,0.2,1.0)
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]BPInstAmbientColor("BP Instrument Ambient Color", Color) = (0.5,0.5,0.5,1.0)
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]BPInstOutlineColor("BP Instrument Outline Color", Color) = (0.5,0.5,0.5,1.0)
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]InstAlphaMultiplier("Instrument Alpha Multiplier", Range(0.0, 1000)) = 1.0
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)][PowerSlider(1.0)]MaxDistInst("Max Distance Instrument", Range(0.0, 100.0)) = 20.0
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]InstIsoValue("Iso-Value", Range(0.0, 1)) = 0.1
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]InstBlinnPhong_k_s("Blinn-Phong k_s", Range(0.0, 1)) = 0.2
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]InstBlinnPhong_k_a("Blinn-Phong k_a", Range(0.0, 1)) = 0.2
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]InstBlinnPhong_k_d("Blinn-Phong k_d", Range(0.0, 1)) = 0.6
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]InstBlinnPhong_s1("Blinn-Phong s1", Range(1.0, 100)) = 5.0
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]InstBlinnPhong_s2("Blinn-Phong s2", Range(1.0, 100)) = 5.0
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]InstBlinnPhong_s1_percentage("Blinn-Phong s1 Percentage", Range(1.0, 100)) = 5.0


		[Header(Above ILM Settings)]
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)][Toggle(AboveILM_BP_OnlyFirstHit)] AboveILM_BP_OnlyFirstHit("Blinn Phong Only For Surface", Float) = 0
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]AboveILMBaseColor("Base Color", Color) = (0.2, 0.2, 0.2, 1.0)
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]BPAboveILMAmbientColor("BP Ambient Color", Color) = (0.5, 0.5, 0.5, 1.0)
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]BPAboveILMOutlineColor("BP  Outline Color", Color) = (0.5, 0.5, 0.5, 1.0)
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]AboveILMAlphaMultiplier("Alpha Multiplier", Range(0.0, 50)) = 1.0
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]AboveILMIsoValue("Iso-Value", Range(0.0, 1)) = 0.1
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]AboveILMBlinnPhong_k_s("Blinn-Phong k_s", Range(0.0, 1)) = 0.2
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]AboveILMBlinnPhong_k_a("Blinn-Phong k_a", Range(0.0, 1)) = 0.2
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]AboveILMBlinnPhong_k_d("Blinn-Phong k_d", Range(0.0, 1)) = 0.6
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]AboveILMBlinnPhong_s1("Blinn-Phong s1", Range(1.0, 100)) = 5.0
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]AboveILMBlinnPhong_s2("Blinn-Phong s2", Range(1.0, 100)) = 5.0
		[HideIfDisabled(_RENDERING_RETINA_BLINN_PHONG)]vILMBlinnPhong_s1_percentage("Blinn-Phong s1 Percentage", Range(1.0, 100)) = 5.0

		[HideInInspector]EnfaceTex("Enface",2D) = "red" {}
		[HideInInspector]VesselMapTex("VesselMapTex",2D) = "red" {}
		[HideInInspector]SegmentationTex2D("SegmentationTex2D", 2D) = "red" {}
		[HideInInspector]SegmentationTex2DBottomUp("SegmentationTex2DBottomUp", 2D) = "red" {}

		[HideInInspector]VesselDepthTex("VesselDepthTex",2D) = "red" {}
		[HideInInspector]VesselDepthTexBottomUp("VesselDepthTexBottomUp",2D) = "red" {}

		[HideInInspector]ShadowMapTex("ShadowMapTex", 3D) = "red" {}
		[HideInInspector]FeatureVolumeTex("FeatureVolumeTex", 3D) = "red" {}
		[HideInInspector]VesselSkeletonVolumeTex("VesselSkeletonVolumeTex", 3D) = "red" {}
		[HideInInspector]VesselSkeletonVolumeTexFromRadiusMap("VesselSkeletonVolumeTexFromRadiusMap", 3D) = "red" {}

		//++++++++++++++++++++++++++++++++++++++++++++++++++
		[Header(Clipping Planes)]
		[Toggle(CLIPPING_PLANES)]ClippingPlanes("Activate Clipping Planes", Float) = 0
		[HideIfDisabled(CLIPPING_PLANES)]NumberOfCuttingPlanes("Number of Active Clipping Planes (max 4)", Float) = 0
		[HideIfDisabled(CLIPPING_PLANES)]ClippingPlane0("Clipping Plane 0", Vector) = (1.0,0.0,0.0,1.0)
		[HideIfDisabled(CLIPPING_PLANES)]ClippingPlane1("Clipping Plane 1", Vector) = (1.0,0.0,0.0,1.0)
		[HideIfDisabled(CLIPPING_PLANES)]ClippingPlane2("Clipping Plane 2", Vector) = (1.0,0.0,0.0,1.0)
		[HideIfDisabled(CLIPPING_PLANES)]ClippingPlane3("Clipping Plane 3", Vector) = (1.0,0.0,0.0,1.0)

		[HideIfDisabled(_RENDERING_CUSTOM_TRANSFERFUNCTION, _RENDERING_EXPERIMENTAL)]Val00("Range: 0.00 - 0.05", Color) = (0,0,0,0)
		[HideIfDisabled(_RENDERING_CUSTOM_TRANSFERFUNCTION, _RENDERING_EXPERIMENTAL)]Val01("Range: 0.05 - 0.10", Color) = (0,0,0,0)
		[HideIfDisabled(_RENDERING_CUSTOM_TRANSFERFUNCTION, _RENDERING_EXPERIMENTAL)]Val02("Range: 0.10 - 0.15", Color) = (0,0,0,0)
		[HideIfDisabled(_RENDERING_CUSTOM_TRANSFERFUNCTION, _RENDERING_EXPERIMENTAL)]Val03("Range: 0.15 - 0.20", Color) = (0,0,0,0)
		[HideIfDisabled(_RENDERING_CUSTOM_TRANSFERFUNCTION, _RENDERING_EXPERIMENTAL)]Val04("Range: 0.20 - 0.25", Color) = (0,0,0,0)
		[HideIfDisabled(_RENDERING_CUSTOM_TRANSFERFUNCTION, _RENDERING_EXPERIMENTAL)]Val05("Range: 0.25 - 0.30", Color) = (0,0,0,0)
		[HideIfDisabled(_RENDERING_CUSTOM_TRANSFERFUNCTION, _RENDERING_EXPERIMENTAL)]Val06("Range: 0.30 - 0.35", Color) = (0,0,0,0)
		[HideIfDisabled(_RENDERING_CUSTOM_TRANSFERFUNCTION, _RENDERING_EXPERIMENTAL)]Val07("Range: 0.35 - 0.40", Color) = (0,0,0,0)
		[HideIfDisabled(_RENDERING_CUSTOM_TRANSFERFUNCTION, _RENDERING_EXPERIMENTAL)]Val08("Range: 0.40 - 0.45", Color) = (0,0,0,0)
		[HideIfDisabled(_RENDERING_CUSTOM_TRANSFERFUNCTION, _RENDERING_EXPERIMENTAL)]Val09("Range: 0.45 - 0.50", Color) = (0,0,0,0)
		[HideIfDisabled(_RENDERING_CUSTOM_TRANSFERFUNCTION, _RENDERING_EXPERIMENTAL)]Val10("Range: 0.50 - 0.55", Color) = (0,0,0,0)
		[HideIfDisabled(_RENDERING_CUSTOM_TRANSFERFUNCTION, _RENDERING_EXPERIMENTAL)]Val11("Range: 0.55 - 0.60", Color) = (0,0,0,0)
		[HideIfDisabled(_RENDERING_CUSTOM_TRANSFERFUNCTION, _RENDERING_EXPERIMENTAL)]Val12("Range: 0.60 - 0.65", Color) = (0,0,0,0)
		[HideIfDisabled(_RENDERING_CUSTOM_TRANSFERFUNCTION, _RENDERING_EXPERIMENTAL)]Val13("Range: 0.65 - 0.70", Color) = (0,0,0,0)
		[HideIfDisabled(_RENDERING_CUSTOM_TRANSFERFUNCTION, _RENDERING_EXPERIMENTAL)]Val14("Range: 0.70 - 0.75", Color) = (0,0,0,0)
		[HideIfDisabled(_RENDERING_CUSTOM_TRANSFERFUNCTION, _RENDERING_EXPERIMENTAL)]Val15("Range: 0.75 - 0.80", Color) = (0,0,0,0)
		[HideIfDisabled(_RENDERING_CUSTOM_TRANSFERFUNCTION, _RENDERING_EXPERIMENTAL)]Val16("Range: 0.80 - 0.85", Color) = (0,0,0,0)
		[HideIfDisabled(_RENDERING_CUSTOM_TRANSFERFUNCTION, _RENDERING_EXPERIMENTAL)]Val17("Range: 0.85 - 0.90", Color) = (0,0,0,0)
		[HideIfDisabled(_RENDERING_CUSTOM_TRANSFERFUNCTION, _RENDERING_EXPERIMENTAL)]Val18("Range: 0.90 - 0.95", Color) = (0,0,0,0)
		[HideIfDisabled(_RENDERING_CUSTOM_TRANSFERFUNCTION, _RENDERING_EXPERIMENTAL)]Val19("Range: 0.95 - 1.00", Color) = (0,0,0,0)
		[HideIfDisabled(_RENDERING_CUSTOM_TRANSFERFUNCTION, _RENDERING_EXPERIMENTAL)]Val20("Range: 1.00", Color) = (0,0,0,0)


		[Enum(UnityEngine.Rendering.BlendMode)] _Blend("Blend mode", Float) = 0
	}

		CGINCLUDE
#include "UnityCG.cginc"

						ENDCG
						SubShader{
							Tags{ "Queue" = "Transparent" "RenderType" = "Transparent" }


							Pass{

								//raycasting	
								LOD 200

								Blend SrcAlpha[_Blend]
								ZTest Always

								CGPROGRAM

								#pragma vertex vert
								#pragma fragment frag
								#pragma shader_feature STOCHASTIC_JITTER
								#pragma shader_feature CLIPPING_PLANES
								#pragma shader_feature _RENDERING_ACCUMULATE _RENDERING_MAXIMUM _RENDERING_COMPOSITING _RENDERING_BLINN_PHONG _RENDERING_SHADED_COMPOSITING _RENDERING_CUSTOM_TRANSFERFUNCTION _RENDERING_EXPERIMENTAL _RENDERING_RETINA_BLINN_PHONG _RENDERING_DEBUG_EXPERIMENTAL
								#pragma shader_feature _SECTION_ILM _SECTION_RPE _SECTION_INSTRUMENT _SECTION_VESSEL _SECTION_INBETWEENVESSELS _SECTION_ALL
								#pragma shader_feature USE_ENFACE_COLOR
							//	#pragma shader_feature USE_VESSEL_BASE_COLOR
								#pragma shader_feature ILM_BP_OnlyFirstHit 
								#pragma shader_feature RPE_BP_OnlyFirstHit 
								#pragma shader_feature Inst_BP_OnlyFirstHit
								#pragma shader_feature Vessel_BP_OnlyFirstHit
								#pragma shader_feature IBVessel_BP_OnlyFirstHit
								#pragma shader_feature ILM_Receive_Shadows
								#pragma shader_feature RPE_Receive_Shadows
								#pragma shader_feature Vessel_Receive_Shadows
								#pragma shader_feature IBVessel_Receive_Shadows
								#pragma shader_feature ReduceFraying
								#pragma shader_feature ShowShadows
								#pragma shader_feature USE_OAC_VESSEL_SEG
								#pragma shader_feature USE_MANUAL_VESSEL_SEG
								#pragma shader_feature USE_ENFACE_VESSEL_SEG
								#pragma shader_feature USE_VESSEL_SKELETON_SEG
								#pragma shader_feature USE_VESSEL_SKELETON_SEG_FROM_RADIUS_MAP
								#pragma shader_feature UI_ILM_SECTION_ACTIVE
								#pragma shader_feature UI_RPE_SECTION_ACTIVE
								#pragma shader_feature UI_VESSEL_SECTION_ACTIVE
								#pragma shader_feature UI_IBVESSEL_SECTION_ACTIVE
								#pragma shader_feature UI_INST_SECTION_ACTIVE
								#pragma shader_feature ENHANCE_SPECLES
								#pragma shader_feature ENHANCE_SPECLES_ENFACE_COLORING
								#pragma target 3.0
						//					#pragma shader_feature ShowRTShadows

											//for Retina Blinn Phong only
											//++++++++++++++++++++++++++++



											sampler3D DistanceMap;
											sampler3D ZoneMap;
											sampler2D EnfaceTex;
											sampler2D VesselMapTex;
											sampler3D SegmentationTex;
											Texture2D SegmentationTex2D;
											Texture2D SegmentationTex2DBottomUp;
											sampler3D ShadowMapTex;
											sampler3D FeatureVolumeTex;
											
											//Vessel Depth Map
											sampler2D VesselDepthTex;
											sampler2D VesselDepthTexBottomUp;
											//Vessel Skeleton
											sampler3D VesselSkeletonVolumeTex;
											sampler3D VesselSkeletonVolumeTexFromRadiusMap;	
											float VesselSkeletonResX;
											float VesselSkeletonResY;
											float VesselSkeletonResZ;

											//oac volume 
											sampler3D OACSegmentedVesselVol;
											sampler3D OACVolumeRaw;

											float MaxDistILM;
											float MaxDistRPE;
											float MaxDistInst;
											float MaxDistArtifact;

											float MaxDistOffset;

											float GradientSmoothKernelSize;

											float ILMAlphaMultiplier;
											float AboveILMAlphaMultiplier;
											float RPEAlphaMultiplier;
											float InstAlphaMultiplier;
											float ArtifactAlphaMultiplier;
											float IBVesselAlphaMultiplier;
											float VesselAlphaMultiplier;

											//additional alpha multiplier which the user can control
											float ILMUserMultiplier;
											float RPEUserMultiplier;
											float InstUserMultiplier;
											float IBVesselUserMultiplier;
											float VesselUserMultiplier;

											//Specles Values
											float SpeclesMultiplier;
											float SpeclesPower;
											//ShadowValues
											fixed4 ShadowColor;
											float ShadowAlphaMul;

											//ILM values
											fixed4 ILMBaseColor;
											fixed4 BPILMAmbientColor;
											fixed4 BPILMOutlineColor;
											float ILMIsoValue;
											float ILMBlinnPhong_k_s;
											float ILMBlinnPhong_k_a;
											float ILMBlinnPhong_k_d;
											float ILMBlinnPhong_s1;
											float ILMBlinnPhong_s2;
											float ILMBlinnPhong_s1_percentage;

											//Area above ilm values
											fixed4 AboveILMBaseColor;
											fixed4 BPAboveILMAmbientColor;
											fixed4 BPAboveILMOutlineColor;
											float AboveILMIsoValue;
											float AboveILMBlinnPhong_k_s;
											float AboveILMBlinnPhong_k_a;
											float AboveILMBlinnPhong_k_d;
											float AboveILMBlinnPhong_s1;
											float AboveILMBlinnPhong_s2;
											float AboveILMBlinnPhong_s1_percentage;

											//Vessel values
											fixed4 VesselColor;
											fixed4 VesselBaseColor;
											//value between 0 and 1 for lerping between microscopic vessel color and vessel base color
											float VesselColorEnhancement;

											float MaxThicknessVessel;
											fixed4 BPVesselAmbientColor;
											fixed4 BPVesselOutlineColor;
											float VesselIsoValue;
											float VesselSegmentationIsoValue;
											float VesselBlinnPhong_k_s;
											float VesselBlinnPhong_k_a;
											float VesselBlinnPhong_k_d;
											float VesselBlinnPhong_s1;
											float VesselBlinnPhong_s2;
											float VesselBlinnPhong_s1_percentage;

											//in between vessel values
											fixed4 IBVesselColor;

											fixed4 BPIBVesselAmbientColor;
											fixed4 BPIBVesselOutlineColor;
											float IBVesselIsoValue;
											float IBVesselBlinnPhong_k_s;
											float IBVesselBlinnPhong_k_a;
											float IBVesselBlinnPhong_k_d;
											float IBVesselBlinnPhong_s1;
											float IBVesselBlinnPhong_s2;
											float IBVesselBlinnPhong_s1_percentage;
											//RPE values
											fixed4 RPEBaseColor;
											fixed4 RPEEnfaceColor;

											float EnfaceHueShift;

											fixed4 BPRPEAmbientColor;
											fixed4 BPRPEOutlineColor;
											float RPEIsoValue;
											float RPEBlinnPhong_k_s;
											float RPEBlinnPhong_k_a;
											float RPEBlinnPhong_k_d;
											float RPEBlinnPhong_s1;
											float RPEBlinnPhong_s2;
											float RPEBlinnPhong_s1_percentage;

											//Instrument values
											fixed4 InstBaseColor;
											fixed4 BPInstAmbientColor;
											fixed4 BPInstOutlineColor;
											float InstIsoValue;
											float InstBlinnPhong_k_s;
											float InstBlinnPhong_k_a;
											float InstBlinnPhong_k_d;
											float InstBlinnPhong_s1;
											float InstBlinnPhong_s2;
											float InstBlinnPhong_s1_percentage;

											//volume slicer settings
											float slicerDepth;
											float slicerWidth;
											fixed4 slicerColor;


											SamplerState myPointClampSampler
											{
												Filter = MIN_MAG_MIP_POINT;

												AddressU = Clamp;
												AddressV = Clamp;
											};

											SamplerState myLinearClampSampler
											{
												Filter = MIN_MAG_MIP_LINEAR;

												AddressU = Clamp;
												AddressV = Clamp;
											};

											SamplerState myLinearClampSampler3D
											{
												Filter = MIN_MAG_MIP_LINEAR;
												AddressU = Clamp;
												AddressV = Clamp;
												AddressW = Clamp;
											};

											SamplerState anisotropicSampler {
												Filter = Anisotropic;
												AddressU = Wrap;
												AddressV = Wrap;
												AddressW = Wrap;
												MaxAnisotropy = 16;
											};

											SamplerComparisonState myComparisonSampler
											{
												Filter = COMPARISON_MIN_MAG_LINEAR_MIP_POINT;
												AddressU = Clamp;
												AddressV = Clamp;
												ComparisonFunc = LESS_EQUAL;
											};


											//++++++++++++++++++++++++++++

											sampler3D VolumeTex;

											float StepSize;
											float Multiplier;
											float Center;

											float IsoValue;
											float3 BlinnPhongLightPos;
											fixed4 BlinnPhongBaseColor;
											fixed4 BlinnPhongAmbientColor;
											fixed4 BlinnPhongOutlineColor;
											float BlinnPhong_k_s;
											float BlinnPhong_k_a;
											float BlinnPhong_k_d;
											float BlinnPhong_s;
											float BlinnPhongTextureX;
											float BlinnPhongTextureY;
											float BlinnPhongTextureZ;

											float CompositingAlphaFactor;
											sampler2D CompositingTransferFunction;

											float NumberOfCuttingPlanes;
											float4 ClippingPlane0;
											float4 ClippingPlane1;
											float4 ClippingPlane2;
											float4 ClippingPlane3;

											float _Crease;
											float _Exponent;

								#if defined (_RENDERING_CUSTOM_TRANSFERFUNCTION) || defined(_RENDERING_EXPERIMENTAL)

											float3 Val00;
											float3 Val01;
											float3 Val02;
											float3 Val03;
											float3 Val04;
											float3 Val05;
											float3 Val06;
											float3 Val07;
											float3 Val08;
											float3 Val09;
											float3 Val10;
											float3 Val11;
											float3 Val12;
											float3 Val13;
											float3 Val14;
											float3 Val15;
											float3 Val16;
											float3 Val17;
											float3 Val18;
											float3 Val19;
											float3 Val20;


													float transferFunctionR(float val) {
														val = saturate(val);
														if (val <= 0.05) {
															return Val00.x * (1 - (val - 0.0) * 10) + Val01.x * ((val - 0.0) * 10);
														}
														if (val <= 0.10) {
															return Val01.x * (1 - (val - 0.05) * 10) + Val02.x * ((val - 0.05) * 10);
														}
														if (val <= 0.15) {
															return Val02.x * (1 - (val - 0.1) * 10) + Val03.x * ((val - 0.1) * 10);
														}
														if (val <= 0.20) {
															return Val03.x * (1 - (val - 0.15) * 10) + Val04.x * ((val - 0.15) * 10);
														}
														if (val <= 0.25) {
															return Val04.x * (1 - (val - 0.20) * 10) + Val05.x * ((val - 0.20) * 10);
														}
														if (val <= 0.30) {
															return Val05.x * (1 - (val - 0.25) * 10) + Val06.x * ((val - 0.25) * 10);
														}
														if (val <= 0.35) {
															return Val06.x * (1 - (val - 0.30) * 10) + Val07.x * ((val - 0.30) * 10);
														}
														if (val <= 0.40) {
															return Val07.x * (1 - (val - 0.35) * 10) + Val08.x * ((val - 0.35) * 10);
														}
														if (val <= 0.45) {
															return Val08.x * (1 - (val - 0.40) * 10) + Val09.x * ((val - 0.40) * 10);
														}
														if (val <= 0.50) {
															return Val09.x * (1 - (val - 0.45) * 10) + Val10.x * ((val - 0.45) * 10);
														}
														if (val <= 0.55) {
															return Val10.x * (1 - (val - 0.50) * 10) + Val11.x * ((val - 0.50) * 10);
														}
														if (val <= 0.60) {
															return Val11.x * (1 - (val - 0.55) * 10) + Val12.x * ((val - 0.55) * 10);
														}
														if (val <= 0.65) {
															return Val12.x * (1 - (val - 0.60) * 10) + Val13.x * ((val - 0.60) * 10);
														}
														if (val <= 0.70) {
															return Val13.x * (1 - (val - 0.65) * 10) + Val14.x * ((val - 0.65) * 10);
														}
														if (val <= 0.75) {
															return Val14.x * (1 - (val - 0.70) * 10) + Val15.x * ((val - 0.70) * 10);
														}
														if (val <= 0.80) {
															return Val15.x * (1 - (val - 0.75) * 10) + Val16.x * ((val - 0.75) * 10);
														}
														if (val <= 0.85) {
															return Val16.x * (1 - (val - 0.80) * 10) + Val17.x * ((val - 0.80) * 10);
														}
														if (val <= 0.90) {
															return Val17.x * (1 - (val - 0.85) * 10) + Val18.x * ((val - 0.85) * 10);
														}
														if (val <= 0.95) {
															return Val18.x * (1 - (val - 0.90) * 10) + Val19.x * ((val - 0.90) * 10);
														}
														if (val <= 1.0) {
															return Val19.x * (1 - (val - 0.95) * 10) + Val20.x * ((val - 0.95) * 10);
														}
														return 0.0;
													}

													float transferFunctionG(float val) {
														val = saturate(val);
														if (val <= 0.05) {
															return Val00.y * (1 - (val - 0.0) * 10) + Val01.y * ((val - 0.0) * 10);
														}
														if (val <= 0.10) {
															return Val01.y * (1 - (val - 0.05) * 10) + Val02.y * ((val - 0.05) * 10);
														}
														if (val <= 0.15) {
															return Val02.y * (1 - (val - 0.1) * 10) + Val03.y * ((val - 0.1) * 10);
														}
														if (val <= 0.20) {
															return Val03.y * (1 - (val - 0.15) * 10) + Val04.y * ((val - 0.15) * 10);
														}
														if (val <= 0.25) {
															return Val04.y * (1 - (val - 0.20) * 10) + Val05.y * ((val - 0.20) * 10);
														}
														if (val <= 0.30) {
															return Val05.y * (1 - (val - 0.25) * 10) + Val06.y * ((val - 0.25) * 10);
														}
														if (val <= 0.35) {
															return Val06.y * (1 - (val - 0.30) * 10) + Val07.y * ((val - 0.30) * 10);
														}
														if (val <= 0.40) {
															return Val07.y * (1 - (val - 0.35) * 10) + Val08.y * ((val - 0.35) * 10);
														}
														if (val <= 0.45) {
															return Val08.y * (1 - (val - 0.40) * 10) + Val09.y * ((val - 0.40) * 10);
														}
														if (val <= 0.50) {
															return Val09.y * (1 - (val - 0.45) * 10) + Val10.y * ((val - 0.45) * 10);
														}
														if (val <= 0.55) {
															return Val10.y * (1 - (val - 0.50) * 10) + Val11.y * ((val - 0.50) * 10);
														}
														if (val <= 0.60) {
															return Val11.y * (1 - (val - 0.55) * 10) + Val12.y * ((val - 0.55) * 10);
														}
														if (val <= 0.65) {
															return Val12.y * (1 - (val - 0.60) * 10) + Val13.y * ((val - 0.60) * 10);
														}
														if (val <= 0.70) {
															return Val13.y * (1 - (val - 0.65) * 10) + Val14.y * ((val - 0.65) * 10);
														}
														if (val <= 0.75) {
															return Val14.y * (1 - (val - 0.70) * 10) + Val15.y * ((val - 0.70) * 10);
														}
														if (val <= 0.80) {
															return Val15.y * (1 - (val - 0.75) * 10) + Val16.y * ((val - 0.75) * 10);
														}
														if (val <= 0.85) {
															return Val16.y * (1 - (val - 0.80) * 10) + Val17.y * ((val - 0.80) * 10);
														}
														if (val <= 0.90) {
															return Val17.y * (1 - (val - 0.85) * 10) + Val18.y * ((val - 0.85) * 10);
														}
														if (val <= 0.95) {
															return Val18.y * (1 - (val - 0.90) * 10) + Val19.y * ((val - 0.90) * 10);
														}
														if (val <= 1.0) {
															return Val19.y * (1 - (val - 0.95) * 10) + Val20.y * ((val - 0.95) * 10);
														}
														return 0.0;
													}

													float transferFunctionB(float val) {
														val = saturate(val);
														if (val <= 0.05) {
															return Val00.z * (1 - (val - 0.0) * 10) + Val01.z * ((val - 0.0) * 10);
														}
														if (val <= 0.10) {
															return Val01.z * (1 - (val - 0.05) * 10) + Val02.z * ((val - 0.05) * 10);
														}
														if (val <= 0.15) {
															return Val02.z * (1 - (val - 0.1) * 10) + Val03.z * ((val - 0.1) * 10);
														}
														if (val <= 0.20) {
															return Val03.z * (1 - (val - 0.15) * 10) + Val04.z * ((val - 0.15) * 10);
														}
														if (val <= 0.25) {
															return Val04.z * (1 - (val - 0.20) * 10) + Val05.z * ((val - 0.20) * 10);
														}
														if (val <= 0.30) {
															return Val05.z * (1 - (val - 0.25) * 10) + Val06.z * ((val - 0.25) * 10);
														}
														if (val <= 0.35) {
															return Val06.z * (1 - (val - 0.30) * 10) + Val07.z * ((val - 0.30) * 10);
														}
														if (val <= 0.40) {
															return Val07.z * (1 - (val - 0.35) * 10) + Val08.z * ((val - 0.35) * 10);
														}
														if (val <= 0.45) {
															return Val08.z * (1 - (val - 0.40) * 10) + Val09.z * ((val - 0.40) * 10);
														}
														if (val <= 0.50) {
															return Val09.z * (1 - (val - 0.45) * 10) + Val10.z * ((val - 0.45) * 10);
														}
														if (val <= 0.55) {
															return Val10.z * (1 - (val - 0.50) * 10) + Val11.z * ((val - 0.50) * 10);
														}
														if (val <= 0.60) {
															return Val11.z * (1 - (val - 0.55) * 10) + Val12.z * ((val - 0.55) * 10);
														}
														if (val <= 0.65) {
															return Val12.z * (1 - (val - 0.60) * 10) + Val13.z * ((val - 0.60) * 10);
														}
														if (val <= 0.70) {
															return Val13.z * (1 - (val - 0.65) * 10) + Val14.z * ((val - 0.65) * 10);
														}
														if (val <= 0.75) {
															return Val14.z * (1 - (val - 0.70) * 10) + Val15.z * ((val - 0.70) * 10);
														}
														if (val <= 0.80) {
															return Val15.z * (1 - (val - 0.75) * 10) + Val16.z * ((val - 0.75) * 10);
														}
														if (val <= 0.85) {
															return Val16.z * (1 - (val - 0.80) * 10) + Val17.z * ((val - 0.80) * 10);
														}
														if (val <= 0.90) {
															return Val17.z * (1 - (val - 0.85) * 10) + Val18.z * ((val - 0.85) * 10);
														}
														if (val <= 0.95) {
															return Val18.z * (1 - (val - 0.90) * 10) + Val19.z * ((val - 0.90) * 10);
														}
														if (val <= 1.0) {
															return Val19.z * (1 - (val - 0.95) * 10) + Val20.z * ((val - 0.95) * 10);
														}
														return 0.0;
													}
										#endif


													struct appdata {
														UNITY_VERTEX_INPUT_INSTANCE_ID
														float4 vertex : POSITION;
														float3 normal : NORMAL;
													};

													struct v2f {
														UNITY_VERTEX_INPUT_INSTANCE_ID
															UNITY_VERTEX_OUTPUT_STEREO
														float4 pos : SV_POSITION;
														float4 worldPos : TEXCOORD0;
														float3 cameraPosInObjectSpace: TEXCOORD1;
													};

													fixed4 blendUnder(fixed4 colorAccum, fixed4 colorBehind) {
														colorAccum.rgb += (1.0 - colorAccum.a) * colorBehind.rgb * colorBehind.a;
														colorAccum.a += (1.0 - colorAccum.a) * colorBehind.a;
														return colorAccum;
													}

													fixed4 blendShadow(fixed4 shadowColor, fixed4 color, int sectionValue, float sampleIntensity, float shadowIntensity) {
					
															float alphaIBVessel = shadowIntensity * StepSize  * sampleIntensity;
															float alphaILM = shadowIntensity * StepSize * sampleIntensity;
															float alphaRPE = shadowIntensity * StepSize * sampleIntensity;
															float alphaVessel = shadowIntensity * StepSize * sampleIntensity;
														
														
															[branch] switch (sectionValue)
															{
																//vessel
															case 0:
																shadowColor.a = alphaVessel;
																break;
																//ILM
															case 1:
																shadowColor.a = alphaILM;
																break;
																//RPE
															case 3:
																shadowColor.a = alphaRPE;
																break;
																//IBVessel
															case 6:
																shadowColor.a = alphaIBVessel;
																break;
															}

															//multiply with general shadow multiplier
														//	shadowColor.a = saturate(shadowColor.a * ShadowAlphaMul);
															shadowColor.a = shadowColor.a * shadowIntensity *100 *ShadowAlphaMul;

															
															shadowColor.rgb += (1.0 - shadowColor.a) * color.rgb;
															
															shadowColor.a = color.a;
															
															return shadowColor;

														}

														float3 computeVolumeGradient(sampler3D tex, float3 texCoords) {
															float dx = tex3D(tex, texCoords + float3(1.0 / BlinnPhongTextureX, 0, 0)).r;
															float dy = tex3D(tex, texCoords + float3(0, 1.0 / BlinnPhongTextureY, 0)).r;
															float dz = tex3D(tex, texCoords + float3(0, 0, 1.0 / BlinnPhongTextureZ)).r;
															float mdx = tex3D(tex, texCoords + float3(-1.0 / BlinnPhongTextureX, 0, 0)).r;
															float mdy = tex3D(tex, texCoords + float3(0, -1.0 / BlinnPhongTextureY, 0)).r;
															float mdz = tex3D(tex, texCoords + float3(0, 0, -1.0 / BlinnPhongTextureZ)).r;
															return float3(dx - mdx, dy - mdy, dz - mdz) * 0.5;
														}

														
														float3 computeVolumeGradientVesselSkeleton(sampler3D tex, float3 texCoords) {

													/*		float heightILM = 1 - SegmentationTex2D.Sample(myPointClampSampler, float2(texCoords.x, texCoords.z)).r;
															float maxDistILM = MaxDistILM / BlinnPhongTextureY;
													*/		float maxThicknessVessel = MaxThicknessVessel / BlinnPhongTextureY;
													/*		float centerY = heightILM - maxDistILM - (maxThicknessVessel * 0.5);
															float distanceCenterY = centerY - texCoords.y;
															float positionYInSkeleton = 0.5 + (distanceCenterY * -1.0);
													*/
															texCoords.y = texCoords.y + maxThicknessVessel;

															float dx = tex3D(tex, texCoords + float3(1.0 / VesselSkeletonResX, 0, 0)).r;
															float dy = tex3D(tex, texCoords + float3(0, 1.0 / VesselSkeletonResY, 0)).r;
															float dz = tex3D(tex, texCoords + float3(0, 0, 1.0 / VesselSkeletonResZ)).r;
															float mdx = tex3D(tex, texCoords + float3(-1.0 / VesselSkeletonResX, 0, 0)).r;
															float mdy = tex3D(tex, texCoords + float3(0, -1.0 / VesselSkeletonResY, 0)).r;
															float mdz = tex3D(tex, texCoords + float3(0, 0, -1.0 / VesselSkeletonResZ)).r;
															return float3(dx - mdx, dy - mdy, dz - mdz) * 0.5;

															//+++++++++++++++++++++++++++++++++++++++++++++++++++
															

														}

														

														


														//testing
														//compute gradient in the range of 2
														float3 computeVolumeGradientSmooth(sampler3D tex, float3 texCoords) {

															float x = BlinnPhongTextureX;
															float y = BlinnPhongTextureY;
															float z = BlinnPhongTextureZ;

															float dxdx = tex3D(tex, texCoords + float3(2.0 / x , 0 / y, 0 / z)).r;
															float dxdy = tex3D(tex, texCoords + float3(1.0 / x, 1.0 / y, 0 / z)).r;
															float dxdz = tex3D(tex, texCoords + float3(1.0 / x, 0 / y, 1.0 / z)).r;
															float dxmdx = tex3D(tex, texCoords + float3(0.0 / x, 0 / y, 0 / z)).r;
															float dxmdy = tex3D(tex, texCoords + float3(1.0 / x, -1.0 / y, 0 / z)).r;
															float dxmdz = tex3D(tex, texCoords + float3(1.0 / x, 0 / y, -1.0 / z)).r;

															float dydx = tex3D(tex, texCoords + float3(1.0 / x, 1.0 / y, 0 / z)).r;
															float dydy = tex3D(tex, texCoords + float3(0 / x, 2.0 / y, 0 / z)).r;
															float dydz = tex3D(tex, texCoords + float3(0 / x, 1.0 / y, 1.0 / z)).r;
															float dymdx = tex3D(tex, texCoords + float3(-1.0 / x, 1.0 / y, 0 / z)).r;
															float dymdy = tex3D(tex, texCoords + float3(0 / x, 0.0 / y, 0 / z)).r;
															float dymdz = tex3D(tex, texCoords + float3(0 / x, 1.0 / y, -1.0 / z)).r;

															float dzdx = tex3D(tex, texCoords + float3(1.0 / x, 0 / y, 1.0 / z)).r;
															float dzdy = tex3D(tex, texCoords + float3(0 / x, 1.0 / y, 1.0 / z)).r;
															float dzdz = tex3D(tex, texCoords + float3(0 / x, 0 / y, 2.0 / z)).r;
															float dzmdx = tex3D(tex, texCoords + float3(-1.0 / x, 0 / y, 1.0 / z)).r;
															float dzmdy = tex3D(tex, texCoords + float3(0 / x, -1.0 / y, 1.0 / z)).r;
															float dzmdz = tex3D(tex, texCoords + float3(0 / x, 0 / y, 0.0 / z)).r;

															float mdxdx = tex3D(tex, texCoords + float3(0.0 / x, 0 / y, 0 / z)).r;
															float mdxdy = tex3D(tex, texCoords + float3(-1.0 / x, 1.0 / y, 0 / z)).r;
															float mdxdz = tex3D(tex, texCoords + float3(-1.0 / x, 0 / y, 1.0 / z)).r;
															float mdxmdx = tex3D(tex, texCoords + float3(-2.0 / x, 0 / y, 0 / z)).r;
															float mdxmdy = tex3D(tex, texCoords + float3(-1.0 / x, -1.0 / y , 0 / z)).r;
															float mdxmdz = tex3D(tex, texCoords + float3(-1.0 / x, 0 / y, -1.0 / z)).r;

															float mdydx = tex3D(tex, texCoords + float3(1.0 / x, -1.0 / y, 0 / z)).r;
															float mdydy = tex3D(tex, texCoords + float3(0 / x, 0.0 / y, 0 / z)).r;
															float mdydz = tex3D(tex, texCoords + float3(0 / x, -1.0 / y, 1.0 / z)).r;
															float mdymdx = tex3D(tex, texCoords + float3(-1.0 / x, -1.0 / y, 0 / z)).r;
															float mdymdy = tex3D(tex, texCoords + float3(0 / x, -2.0 / y, 0 / z)).r;
															float mdymdz = tex3D(tex, texCoords + float3(0 / x, -1.0 / y, -1.0 / z)).r;

															float mdzdx = tex3D(tex, texCoords + float3(1.0 / x, 0 / y, -1.0 / z)).r;
															float mdzdy = tex3D(tex, texCoords + float3(0 / x, 1.0 / y, -1.0 / z)).r;
															float mdzdz = tex3D(tex, texCoords + float3(0 / x, 0 / y, 0.0 / z)).r;
															float mdzmdx = tex3D(tex, texCoords + float3(-1.0 / x, 0 / y, -1.0 / z)).r;
															float mdzmdy = tex3D(tex, texCoords + float3(0 / x, -1.0 / y, -1.0 / z)).r;
															float mdzmdz = tex3D(tex, texCoords + float3(0 / x, 0 / y, -2.0 / z)).r;

															float dx = (dxdx - dxmdx, dxdy - dxmdy, dxdz - dxmdz) * 0.5;
															float dy = (dydx - dymdx, dydy - dymdy, dydz - dymdz) * 0.5;
															float dz = (dzdx - dzmdx, dzdy - dzmdy, dzdz - dzmdz) * 0.5;

															float mdx = (mdxdx - mdxmdx, mdxdy - mdxmdy, mdxdz - mdxmdz) * 0.5;
															float mdy = (mdydx - mdymdx, mdydy - mdymdy, mdydz - mdymdz) * 0.5;
															float mdz = (mdzdx - mdzmdx, mdzdy - mdzmdy, mdzdz - mdzmdz) * 0.5;

															return float3(dx - mdx, dy - mdy, dz - mdz) * 0.5;

														}

														//testing
														float3 CalculateGradientSmooth(sampler3D volume, float3 position)
														{
															float3 volumeSize = float3(BlinnPhongTextureX, BlinnPhongTextureY, BlinnPhongTextureZ);

															float3 invVolumeSize = 1.0f / volumeSize;

															float3 gradient = float3(0, 0, 0);

															// Sample the volume at the current position
															float currentSample = tex3D(volume, position).r;

															// Sample the volume at a small offset in each axis
															float3 delta = invVolumeSize;


															// Smooth the gradient by averaging over a kernel
															float kernelRadius = GradientSmoothKernelSize * invVolumeSize.x;
															float3 kernelDelta = float3(kernelRadius, 0, 0);
															float3 smoothGradient = float3(0, 0, 0);

															[loop]
															for (float x = -kernelRadius; x <= kernelRadius; x += delta.x)
															{
																float3 samplePos = position + float3(x, 0, 0);
																smoothGradient.x += (tex3D(volume, samplePos + kernelDelta).r - tex3D(volume, samplePos - kernelDelta).r) * 0.5;
															}
															smoothGradient.x /= (2.0 * kernelRadius / delta.x);

															kernelDelta = float3(0, kernelRadius, 0);
															[loop]
															for (float y = -kernelRadius; y <= kernelRadius; y += delta.y)
															{
																float3 samplePos = position + float3(0, y, 0);
																smoothGradient.y += (tex3D(volume, samplePos + kernelDelta).r - tex3D(volume, samplePos - kernelDelta).r) * 0.5;
															}
															smoothGradient.y /= (2.0 * kernelRadius / delta.y);

															kernelDelta = float3(0, 0, kernelRadius);
															[loop]
															for (float z = -kernelRadius; z <= kernelRadius; z += delta.z)
															{
																float3 samplePos = position + float3(0, 0, z);
																smoothGradient.z += (tex3D(volume, samplePos + kernelDelta).r - tex3D(volume, samplePos - kernelDelta).r) * 0.5;
															}
															smoothGradient.z /= (2.0 * kernelRadius / delta.z);

															// Return the smoothed gradient
															return smoothGradient;
														}

														
														fixed4 applyShading(fixed4 surfaceColor, float3 texPosition, float3 gradient, float3 rayDir, float3 lightPos) {
															fixed4 C_ambient = BlinnPhongAmbientColor;
															float3 L = normalize(texPosition + lightPos);
															float3 e = normalize(-rayDir);
															float3 h = (L + e) / length(L + e);
															float3 n = normalize(-gradient);
															float4 C_diffuse = surfaceColor * dot(n, L);
															fixed power = max(0.0001, pow(dot(h, n), BlinnPhong_s));
															fixed4 C_specular = fixed4(power, power, power, 1.0);
															fixed4 color = BlinnPhong_k_d * C_diffuse + BlinnPhong_k_s * C_specular + BlinnPhong_k_a * C_ambient;

															// outline rendering
															float outlineStrength = 1.0 - saturate(dot(normalize(gradient), normalize(rayDir)));
															color.rgb = lerp(color.rgb, BlinnPhongOutlineColor.rgb, outlineStrength * BlinnPhongOutlineColor.a);
															color.a = 1.0;

															return color;
														}
 



														
														

														fixed4 applyShadingRetina(fixed4 surfaceColor, fixed4 ambientColor, fixed4 outlineColor, float3 texPosition, float3 gradient, float3 rayDir, float3 lightPos, float BP_k_s, float BP_k_a, float BP_k_d, float BP_s1, float BP_s2, float BP_s1_perc) {
														
															float3 L = normalize(-lightPos);
															float3 N = normalize(gradient);
															float3 V = normalize(rayDir);
															float3 H = normalize(L + V);

															fixed3 ambient = BP_k_a * ambientColor.rgb;
															fixed ndotl = max(dot(N, L), 0.0f);
															fixed3 diffuse = BP_k_d * ndotl * surfaceColor.rgb;

															fixed ndoth1 = pow(max(dot(N, H), 0.0f), BP_s1);
															fixed ndoth2 = pow(max(dot(N, H), 0.0f), BP_s2);
															fixed ndoth = (BP_s1_perc * 0.01f) * ndoth1 + (1 - (BP_s1_perc * 0.01f)) * ndoth2;

															fixed3 specular = BP_k_s * ndoth * outlineColor.rgb;
															fixed3 finalColor = ambient + diffuse + specular;

															//alpha value will be overwritten later on by the accumulation of alpha values depending on intensity values in the volume
															fixed4 finalColorWithAlpha = fixed4(finalColor, surfaceColor.a); // Explicitly constructing a fixed4
															return finalColorWithAlpha;
														}

														




														


														//calculate the vertical distance to the different segmentation lines stored in the segmentaton texture
														//save the four different distance values in the corresponding float index

													

														float4 getSegmentationDistanceValue(float3 position, Texture2D SegmentationTex)
														{
										
															
															float4 intensity = SegmentationTex.Sample(myPointClampSampler, float2(position.x, position.z));
															//testing 14.6
															intensity = SegmentationTex.Sample(myLinearClampSampler, float2(position.x, position.z));
															//testing end
															

															//set all values at 1.0 respectively 255 much higher such that no voxel will ever be mapped to that segmentation line since it means no segmentation line was found
															if (intensity.r >= 1.0) {
																intensity.r = -10.0;
															}
															if (intensity.g >= 1.0) {
																intensity.g = -10.0;
															}
															if (intensity.b >= 1.0) {
																intensity.b = -10.0;
															}
															if (intensity.a >= 1.0) {
																intensity.a = -10.0;
															}

															//invert intensity
															intensity = float4(1.0, 1.0, 1.0, 1.0) - intensity;

															//position.y = BlinnPhongTextureY - position.y;
															float distanceR = position.y - (intensity.r);
															float distanceG = position.y - (intensity.g);
															float distanceB = position.y - (intensity.b);
															float distanceA = position.y - (intensity.a);

															

															return float4(distanceR, distanceG, distanceB, distanceA);

														}

														//return the smallest positive value out of the four input values; supposed that all values are between -1,1
														float SmallestPositive(float x, float y, float z, float w)
														{
															float result = 10.0f;

															if (x > 0 && x < result) {
																result = x;
															}
															if (y > 0 && y < result) {
																result = y;
															}
															if (z > 0 && z < result) {
																result = z;
															}
															if (w > 0 && w < result) {
																result = w;
															}

															return result;
														}

				
														
														//checks if xz of xyz coordinate are part of a vessel in the 2D enface vessel map
														bool partOfVesselMap(float3 texPosition)
														{
															float intensity = tex2D(VesselMapTex, texPosition.xz).r;
															if (intensity > 0.0f)
															{
																return true;
															}
															else
															{
																return false;
															}
															
														}

														//checks if the current voxel position is segmented in the oac volume
														bool partOfOACSegmentation(float3 texPosition) 
														{
															float intensity = tex3D(OACSegmentedVesselVol, texPosition).r;
														
														/*	if (partOfVesselMap(texPosition) && intensity > VesselSegmentationIsoValue)
															{ 
																return true; 
															}
														*/
															if (intensity > 0.0) 
															{
																return true;
															}
														

															return false;
														}

													


														//checks if current voxel is part of a vessel depending on which kind of segmentation we are using
														float checkIfVoxelIsPartOfVessel(float3 texPosition, float sampleIntensity,float VesselIsoValue, float maxThicknessVessel,float VesselMapColor, float maxDistILM,float4 distanceValue,float4 distanceValueBottomUp, float vesselDepthMapDistance, float vesselDepthMapDistanceBottomUp) 
														{
#if (defined (USE_OAC_VESSEL_SEG))	
															//use oac vessel segmentation instead
															if (partOfOACSegmentation(texPosition) && sampleIntensity >= VesselIsoValue)
															{
																return 1.0;
															}

#endif


#if (defined (USE_MANUAL_VESSEL_SEG))
															//else use the segmented vessels in the 2D segmentation textures
														//	vesselDepthMapDistance = 1.0 - vesselDepthMapDistance; // invert distance
															
															
															vesselDepthMapDistance = 1.0 -vesselDepthMapDistance; //invert
															vesselDepthMapDistanceBottomUp = 1.0 - vesselDepthMapDistanceBottomUp; //invert
															
															


															if (sampleIntensity >= VesselIsoValue && vesselDepthMapDistance >= texPosition.y && texPosition.y >= vesselDepthMapDistanceBottomUp && vesselDepthMapDistance < 0.99 && vesselDepthMapDistanceBottomUp >0.01) //&& vesselDepthMapDistanceBottomUp >= vesselDepthMapDistanceBottomUp) 
															{
																return 1.0;
															}

															if (sampleIntensity >= VesselIsoValue && vesselDepthMapDistanceBottomUp >= texPosition.y && texPosition.y >= vesselDepthMapDistance && vesselDepthMapDistanceBottomUp < 0.99 && vesselDepthMapDistance >0.01) //&& vesselDepthMapDistanceBottomUp >= vesselDepthMapDistanceBottomUp) 
															{
																return 1.0;
															}
															float vesselOffset = 0.0025;
															//testing if it is minimal under or over vesselDepthMapDistance/vesselDepthMapBottomUp
															if (sampleIntensity >= VesselIsoValue && ((vesselDepthMapDistance >= texPosition.y && vesselDepthMapDistance - vesselOffset <= texPosition.y) || (texPosition.y >= vesselDepthMapDistanceBottomUp && texPosition.y <= vesselDepthMapDistanceBottomUp + vesselOffset)) && vesselDepthMapDistance < 0.90) //&& vesselDepthMapDistanceBottomUp >= vesselDepthMapDistanceBottomUp) 
															{
															//	return 1.0;
															}
															
#endif

#if (defined (USE_ENFACE_VESSEL_SEG))															
															//use segmentation soley based of the 2D vessel map 
															if (VesselMapColor > 0.0 && sampleIntensity > VesselIsoValue && (distanceValue.r) * -1.0 > maxDistILM && (distanceValue.r) * -1.0 <= maxThicknessVessel) 
															{	
																return 1.0;
															}
#endif
															//use the vessel skeleton texture in order to decide if the current pos is part of a vessel
#if (defined (USE_VESSEL_SKELETON_SEG))
															//++++++++++++++++++//crrently assuming vessel lay directly under the ilm since not depth information is available, 
															//also assuming for the vessel skeleton lookup, vessel lay at a constant depth of 0.5
															
												/*			float heightILM =1-  SegmentationTex2D.Sample(myPointClampSampler, float2(texPosition.x, texPosition.z)).r;
															float centerY = heightILM - maxDistILM - (maxThicknessVessel * 0.5);
															float distanceCenterY = centerY - texPosition.y;
												*/
												//			float positionYInSkeleton = 0.5 + (distanceCenterY * -1.0);
															float positionYInSkeleton = texPosition.y + maxThicknessVessel;

															float vesselSkeletonIntensity = tex3D(VesselSkeletonVolumeTex, float3(texPosition.x,positionYInSkeleton,texPosition.z)).r;
															//++++++++++++++++++++++

															if (vesselSkeletonIntensity > 0 && sampleIntensity >= VesselIsoValue)
															{
																return 1.0;
															}
#endif

#if (defined (USE_VESSEL_SKELETON_SEG_FROM_RADIUS_MAP))
															float vesselSkeletonIntensityFromRadius = tex3D(VesselSkeletonVolumeTexFromRadiusMap, float3(texPosition.x, texPosition.y, texPosition.z)).r;
															if (vesselSkeletonIntensityFromRadius > 0 && sampleIntensity >= VesselIsoValue)
															{
																return 1.0;
															}


#endif				
															return 0.0;
														}

												

														//return a value for each section if current voxel is in the defined section and has a max distance and has a min IsoValue, else error value 10
														
														int checkForSectionAndDistanceAndIsoValueNew(float3 texPosition, float sampleIntensity) {
															float maxDistILM = MaxDistILM / BlinnPhongTextureY;
															float maxDistRPE = MaxDistRPE / BlinnPhongTextureY;
															float maxDistInst = MaxDistInst / BlinnPhongTextureY;
															float maxDistOffset = MaxDistOffset / BlinnPhongTextureY;
															
															float maxThicknessVessel = MaxThicknessVessel / BlinnPhongTextureY;

															float VesselMapColor = tex2D(VesselMapTex, texPosition.xz).r;


															
															float4 distanceValue = getSegmentationDistanceValue(texPosition, SegmentationTex2D);
															
															float4 distanceValueBottomUp = getSegmentationDistanceValue(texPosition, SegmentationTex2DBottomUp);
															
															float vesselDepthMapDistance = 0.0;
															float vesselDepthMapDistanceBottomUp = 0.0;

															vesselDepthMapDistance = tex2D(VesselDepthTex, texPosition.xz).r;
															vesselDepthMapDistanceBottomUp = tex2D(VesselDepthTexBottomUp, texPosition.xz).r;

															float sampleIntensityTMP = sampleIntensity;
															//VESSEl in between ILM and RPE Section
/*
#if (defined (_SECTION_VESSEL)) ||  (defined (_SECTION_ALL))
				//if above  below ilm segmentation line + ilm thickness and above rpe segmentation

#if (defined (ReduceFraying))
						//before perform closing operation to reduce fraying of the vessels
															sampleIntensity = closing(texPosition, float3(BlinnPhongTextureX, BlinnPhongTextureY, BlinnPhongTextureZ), VesselIsoValue);
#endif
															if (VesselMapColor > 0.0 && sampleIntensity > VesselIsoValue && (distanceValue.r) * -1.0 > maxDistILM && distanceValue.b > maxDistOffset && (distanceValue.r) * -1.0 <= maxThicknessVessel) {
																return 0;
															}
															sampleIntensity = sampleIntensityTMP;
#endif
*/
															
#if (defined (_SECTION_VESSEL)) ||  (defined (_SECTION_ALL)) && (defined(UI_VESSEL_SECTION_ACTIVE))
															

															if(checkIfVoxelIsPartOfVessel(texPosition,sampleIntensity,VesselIsoValue,  maxThicknessVessel,VesselMapColor,  maxDistILM, distanceValue, distanceValueBottomUp, vesselDepthMapDistance, vesselDepthMapDistanceBottomUp))

															//if (checkIfVoxelIsPartOfVesselTrilinear(texPosition,sampleIntensity,VesselIsoValue, maxThicknessVessel, VesselMapColor, maxDistILM, distanceValue, distanceValueBottomUp) > 0.2)
														//	if (checkIfVoxelIsPartOfVesselTestLinear(texPosition) > 0.1)
															{
																//check if vessel is under the ilm, otherwise vessels are cut away
																if(distanceValue.r < 0)
																return 0;
															}
														
#endif															


#if (defined (_SECTION_INBETWEENVESSELS)) ||  (defined (_SECTION_ALL)) && (defined(UI_IBVESSEL_SECTION_ACTIVE))
															
															//if current voxel lays in between ilm and rpe and is not part of a vessel
															if (sampleIntensity >= IBVesselIsoValue && (distanceValue.r) * -1.0 > maxDistILM && distanceValue.g > maxDistOffset) // && !(checkIfVoxelIsPartOfVessel(texPosition, sampleIntensity, VesselIsoValue, maxThicknessVessel, VesselMapColor, maxDistILM, distanceValue, distanceValueBottomUp)))
															{
																return 6;
															}
													

#endif

														//ILM
#if (defined (_SECTION_ILM)) || (defined (_SECTION_ALL)) && (defined(UI_ILM_SECTION_ACTIVE))
														//if zone between rpe and ilm segmentation line or above ilm segmentation line and distance < maxDistOffset
															if (sampleIntensity > ILMIsoValue && distanceValue.r*-1 <= maxDistILM && distanceValue.r <= maxDistOffset) {
																return 1;
															}
#endif

/*														//Area above ILM
#if (defined (_SECTION_ALL)) && (defined(UI_ILM_SECTION_ACTIVE))
														
															//if voxel is above ilm segmentation line and not part of a instrument
															if (sampleIntensity > AboveILMIsoValue && distanceValue.r > 0  && (abs(distanceValue.a) > maxDistInst)) {
																return 7;
															}
#endif
*/														
															//RPE
#if (defined (_SECTION_RPE)) ||  (defined (_SECTION_ALL)) && (defined(UI_RPE_SECTION_ACTIVE))
														//if below rpe segmentation line and distance < maxDistRPE or above and distance < maxDistOffset
															
															if (sampleIntensity > RPEIsoValue && (distanceValue.g <= 0 && distanceValue.g * -1.0 <= maxDistRPE)) {
																return 3;
															}
																
															
															
#endif			

															//INSTRUMENT
#if (defined (_SECTION_INSTRUMENT)) ||  (defined (_SECTION_ALL)) && (defined(UI_INST_SECTION_ACTIVE))
				

															//if (sampleIntensity > InstIsoValue && (abs(distanceValue.a) <= maxDistInst))
															if (sampleIntensity > InstIsoValue && (abs(distanceValue.a) <= maxDistInst))

															{
																return 2;
															}
#endif

															//error value or not defined value
															return 10;


														}

														//helper function which return 1.0 if voxel at texCoords is part of the ilm segmentation
														//else return 0.0
														float isPartOfILM(float3 texCoords, float sampleIntensity)
														{
															if (checkForSectionAndDistanceAndIsoValueNew(texCoords, sampleIntensity) == 1)
															{
																return 1.0;
															}
															return 0.0;
														}

														float3 isPartOfILMGaussianSmooth(float3 texCoords, float sampleIntensity) {
															float kernelSize = 2 ;
															float3 sum = float3(0, 0, 0);
															float weightSum = 0;
															float sigma = kernelSize / 3.0;
															float PI = 3.1415926535897932384;
															for (float i = -kernelSize; i <= kernelSize; i++) {
																float weight = exp(-i * i / (2 * sigma * sigma)) / (sigma * sqrt(2 * PI));
																

																float3 offset = float3(i / BlinnPhongTextureX, 0, 0);
																sum.x += weight * isPartOfILM(texCoords + offset, sampleIntensity);

																offset = float3(0, i / BlinnPhongTextureY, 0);
																sum.y += weight * isPartOfILM(texCoords + offset, sampleIntensity);

																offset = float3(0, 0, i / BlinnPhongTextureZ);
																sum.z += weight * isPartOfILM(texCoords + offset, sampleIntensity);

																weightSum += weight;
															}
															
															return sum / weightSum;
														}

														float3 computeVolumeGradientILM(float3 texCoords, float sampleIntensity) {
															float delta = 1.0;

															float dx = isPartOfILM(texCoords + float3(delta / BlinnPhongTextureX, 0, 0), sampleIntensity);
															float dy = isPartOfILM(texCoords + float3(0, delta / BlinnPhongTextureY, 0), sampleIntensity);
															float dz = isPartOfILM(texCoords + float3(0, 0, delta / BlinnPhongTextureZ), sampleIntensity);
															float mdx = isPartOfILM(texCoords + float3(-delta / BlinnPhongTextureX, 0, 0), sampleIntensity);
															float mdy = isPartOfILM(texCoords + float3(0, -delta / BlinnPhongTextureY, 0), sampleIntensity);
															float mdz = isPartOfILM(texCoords + float3(0, 0, -delta / BlinnPhongTextureZ), sampleIntensity);
															return float3((dx - mdx)/BlinnPhongTextureX, (dy - mdy)/ BlinnPhongTextureY, (dz - mdz) / BlinnPhongTextureZ) * 0.5;

														}

														float3 computeVolumeGradientILMSmooth(float3 texCoords, float sampleIntensity) {

															float kernelSize = GradientSmoothKernelSize;
															float3 gradientSum = float3(0, 0, 0);
															float divisor = 0;

															[loop]
															for (float i = -kernelSize; i <= kernelSize; i++) {
																float3 offset = float3(i / BlinnPhongTextureX, 0, 0);
																gradientSum.x += computeVolumeGradientILM(texCoords + offset, sampleIntensity);

																offset = float3(0, i / BlinnPhongTextureY, 0);
																gradientSum.y += computeVolumeGradientILM(texCoords + offset, sampleIntensity);

																offset = float3(0, 0, i / BlinnPhongTextureZ);
																gradientSum.z += computeVolumeGradientILM(texCoords + offset, sampleIntensity);

																divisor++;
															}

															// Take the average
															float3 avgGradient = gradientSum / divisor;

															//debug
															return float3(1,0,0);
															//return avgGradient;
														}








														float3 RGBtoHSV(float3 c)
														{
															float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
															float4 p = c.g < c.b ? float4(c.bg, K.wz) : float4(c.gb, K.xy);
															float4 q = c.r < p.x ? float4(p.xyw, c.r) : float4(c.r, p.yzx);
															float d = q.x - min(q.w, q.y);
															float e = 1.0e-10;
															return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
														}

														float3 HSVtoRGB(float3 c)
														{
															float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
															float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
															return c.z * lerp(K.xxx, clamp(p - K.xxx, 0.0, 1.0), c.y);
														}

														//shifts the hue of an rgb value 
														float3 HueShift(float3 color, float hueShift)
														{
															float3 hsv = RGBtoHSV(color);
															hsv.r += hueShift;
															if (hsv.r > 1)
																hsv.r -= 1;
															else if (hsv.r < 0)
																hsv.r += 1;
															return HSVtoRGB(hsv);
														}

														

														float rand(float3 myVector) {
															return frac(sin(dot(myVector, float3(12.9898, 78.233, 45.5432))) * 43758.5453);
														}

														v2f vert(appdata_base v) {
															v2f o = (v2f)0;
															UNITY_SETUP_INSTANCE_ID(v);
															UNITY_TRANSFER_INSTANCE_ID(v, o);
															UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
															o.pos = UnityObjectToClipPos(v.vertex);
															o.worldPos = v.vertex;
															o.cameraPosInObjectSpace = mul(unity_WorldToObject, _WorldSpaceCameraPos - mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz).xyz;
															return o;
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

														

														//////////////////////////////////////////////
														///
														/// Color Space  Start
														///
														/////////////////////////////////////////////

														float3 RGBLinearToXYZ(float3 rgb)
														{
															float3x3 m = float3x3(
																0.41239080, 0.35758434, 0.18048079,
																0.21263901, 0.71516868, 0.07219232,
																0.01933082, 0.11919478, 0.95053215
																);

															return mul(m, rgb);
														}

														float3 XYZToRGBLinear(float3 xyz)
														{
															float3x3 m = float3x3(
																+3.24096994, -1.53738318, -0.49861076,
																-0.96924364, +1.8759675, +0.04155506,
																+0.05563008, -0.20397696, +1.05697151
																);

															return mul(m, xyz);
														}


														//------------------------------------------------------------
														// CIELAB
														// Note: the L* coordinate ranges from 0 to 100.
														//------------------------------------------------------------
														static const float LAB_Xn = 0.950489;
														static const float LAB_Yn = 1.0;
														static const float LAB_Zn = 1.088840;


														float _LABFunc(float t)
														{
															const float T = 0.00885645168; //pow(6/29,3);
															return t > T
																? pow(t, 1.0 / 3.0)
																: 7.78703704 * t + 4.0 / 29.0;
														}

														float _LABFuncInv(float t)
														{
															const float T = 6 / 29.0;
															return t > T
																? t * t * t
																: 3 * T * T * (t - 4 / 29.0);
														}


														float3 XYZToLAB(float3 xyz)
														{
															float fx = _LABFunc(xyz.x / LAB_Xn);
															float fy = _LABFunc(xyz.y / LAB_Yn);
															float fz = _LABFunc(xyz.z / LAB_Zn);

															return float3(
																116 * fy - 16,
																500 * (fx - fy),
																200 * (fy - fz)
																);
														}

														float3 LABToXYZ(float3 lab)
														{
															float ltmp = (lab.x + 16) / 116;
															return float3(
																LAB_Xn * _LABFuncInv(ltmp + lab.y / 500),
																LAB_Yn * _LABFuncInv(ltmp),
																LAB_Zn * _LABFuncInv(ltmp - lab.z / 200)
																);
														}


														//------------------------------------------------------------
														// sRGB(D65)
														//------------------------------------------------------------


														float3 SRGBToRGBLinear(float3 rgb)
														{
															const float t = 0.04045;
															float3 a = rgb / 12.92;
															float3 b = pow((rgb + 0.055) / 1.055, 2.4);
															return float3(
																rgb.r <= t ? a.r : b.r,
																rgb.g <= t ? a.g : b.g,
																rgb.b <= t ? a.b : b.b
																);
														}

														float3 RGBLinearToSRGB(float3 rgb)
														{
															const float t = 0.031308;
															float3 a = rgb * 12.92;
															float3 b = 1.055 * pow(rgb, 1 / 2.4) - 0.055;
															float3 srgb = float3(
																rgb.r <= t ? a.r : b.r,
																rgb.g <= t ? a.g : b.g,
																rgb.b <= t ? a.b : b.b
																);

															return saturate(srgb);
														}


														float3 RGBToXYZ(float3 rgb)
														{
															return RGBLinearToXYZ(SRGBToRGBLinear(rgb));
														}

														float3 XYZToRGB(float3 xyz)
														{
															float3 rgbl = XYZToRGBLinear(xyz);
															return RGBLinearToSRGB(rgbl);
														}


														// Note: the L* coordinate ranges from 0 to 100.
														float3 RGBToLAB(float3 rgb)
														{
															return XYZToLAB(RGBToXYZ(rgb));
														}

														float3 LABToRGB(float3 lab)
														{
															return XYZToRGB(LABToXYZ(lab));
														}

														/////////////////////////////////////////////
														///
														/// Color Space End
														///
														/////////////////////////////////////////////

														//method aims to enhance specles in the volume, meaning enhance the visability of spots with high intensity within the volume by changing the color
														float4 enhanceSpecles(float3 voxelCoord, float4 color, fixed4 enfaceColor, float intensity, float oacIntensity)
														{
															float4 speclesColor = float4(0,0,0,0);
#if defined(ENHANCE_SPECLES_ENFACE_COLORING)
															speclesColor.rgb = enfaceColor.rgb;
#else
															speclesColor.rgb = color.rgb;
#endif
															//transform to LAB color space
															speclesColor.rgb = RGBToLAB(speclesColor.rgb);
															//setting intensity value as L value in LAB color space
															//color.r = pow(intensity, SpeclesPower) * SpeclesMultiplier;
															speclesColor.r =pow(oacIntensity, SpeclesPower) * SpeclesMultiplier;

															//transform back to RBG color space
															speclesColor.rgb = LABToRGB(speclesColor.rgb);

															return speclesColor;														
														}

														fixed4 frag(v2f i) :COLOR{
															UNITY_SETUP_INSTANCE_ID(i);
															float4 entryPoint = i.worldPos;
															float3 dir = normalize(-ObjSpaceViewDir(entryPoint));
															float2 tnear_tfar;
															tnear_tfar = intersectBox(dir, i.cameraPosInObjectSpace, float3(-0.5, -0.5, -0.5), float3(0.5, 0.5, 0.5));
															if (tnear_tfar.x < 0.0) {
																tnear_tfar.x = 0.0;
															}
															float3 rayStart = i.cameraPosInObjectSpace + dir * tnear_tfar.x + .5;
															float3 rayStop = i.cameraPosInObjectSpace + dir * tnear_tfar.y + .5;
															float3 deltaDir = normalize(rayStop - rayStart) * StepSize;
															float travel = distance(rayStop, rayStart);
															float numberOfSteps = travel / StepSize + 0.5;
															float3 voxelCoord = rayStart;

											#if defined(STOCHASTIC_JITTER)
															voxelCoord += deltaDir * (rand(entryPoint) - 0.5);
											#endif
															half maximum = 0.0;
															float accu = 0.0;
															fixed4 rayColor = fixed4(0.0, 0.0, 0.0, 0.0);
															float prevVal = 0;

															

															//arbitrary start value
															int previousSectionValue = 100;

															//number of Voxel since we hit a vessel
															int numVoxelSincePreviousVessel = 0;

															// check if we already hit the first voxel of a Section
															// used to have the option to only apply blinn phong shading on the surface of each section
															bool firstHitVessel = false;
															bool firstHitILM = false;
															bool firstHitInst = false;
															bool firstHitRPE = false;
															bool firstHitIBVessel = false;
															bool firstHitAboveILM = false;



															// This loops over all samples along the ray
															[loop]
															for (; travel > 0.0; travel -= StepSize, voxelCoord += deltaDir) {
											#if defined(CLIPPING_PLANES)
																if (NumberOfCuttingPlanes > 0 && dot(ClippingPlane0, float4(voxelCoord.xyz, 1.0)) > 0) {
																	continue;
																}
																if (NumberOfCuttingPlanes > 1 && dot(ClippingPlane1, float4(voxelCoord.xyz, 1.0)) > 0) {
																	continue;
																}
																if (NumberOfCuttingPlanes > 2 && dot(ClippingPlane2, float4(voxelCoord.xyz, 1.0)) > 0) {
																	continue;
																}
																if (NumberOfCuttingPlanes > 3 && dot(ClippingPlane3, float4(voxelCoord.xyz, 1.0)) > 0) {
																	continue;
																}
											#endif
																// sample scalar intensity value from the volume
																float sampleIntensity = tex3D(VolumeTex, voxelCoord).r;
																
																//sample intensity value of the oac raw volume
																float sampleIntensityOACRaw = tex3D(OACVolumeRaw, voxelCoord).r;

																float shadowMapIntensity = tex3D(ShadowMapTex, voxelCoord).r;


											#if defined(_RENDERING_ACCUMULATE)
																accu += sampleIntensity;
											#elif defined (_RENDERING_MAXIMUM)
																maximum = max(sampleIntensity, maximum);
											#elif defined (_RENDERING_RETINA_BLINN_PHONG)
																int sectionValue = 10;

																sectionValue = checkForSectionAndDistanceAndIsoValueNew(voxelCoord, sampleIntensity);												
																//render depth slicer into volume
																if (voxelCoord.z <= slicerDepth + slicerWidth && voxelCoord.z >= slicerDepth - slicerWidth &&(voxelCoord.x < slicerWidth || voxelCoord.x > 1- slicerWidth || voxelCoord.y < slicerWidth || voxelCoord.y > 1 - slicerWidth))														
																{																
																	rayColor = blendUnder(rayColor, slicerColor);													
																}

																//if we hit a vessel set to 0, else increase
																//is used to counter z-fighting with vessel throwing a shadow onto itself
																if (sectionValue == 0) 
																{
																	numVoxelSincePreviousVessel = 0;
																}
																else 
																{
																	numVoxelSincePreviousVessel ++;
																}

																
																	

																	accu += sampleIntensity;

																	//calculate alpha values depending on stepsize and section
																	float alphaILM = saturate((sampleIntensity * StepSize) * ILMAlphaMultiplier)* ILMUserMultiplier;
																	float alphaRPE = saturate((sampleIntensity * StepSize) * RPEAlphaMultiplier) * RPEUserMultiplier;
																	float alphaInst = saturate((sampleIntensity * StepSize) * InstAlphaMultiplier) * InstUserMultiplier;
																	float alphaIBVessel = saturate((sampleIntensity * StepSize) * IBVesselAlphaMultiplier) * IBVesselUserMultiplier;
																	float alphaVessel = saturate((sampleIntensity * StepSize)) * VesselAlphaMultiplier * VesselUserMultiplier;

																	float alphaAboveILM = saturate((sampleIntensity * StepSize) * AboveILMAlphaMultiplier);
																	

															
																	AboveILMBaseColor.a = alphaAboveILM;
																	ILMBaseColor.a = alphaILM;
																	InstBaseColor.a = alphaInst;
																	RPEBaseColor.a = alphaRPE;
																	RPEEnfaceColor.rgb = tex2D(EnfaceTex, voxelCoord.xz).rgb;
																	IBVesselColor.a = alphaIBVessel;
																	VesselBaseColor.a = alphaVessel;
																	VesselColor.a = alphaVessel;


																	//perfom a hueshift
																	RPEEnfaceColor.rgb = HueShift(RPEEnfaceColor.rgb, EnfaceHueShift);
																	RPEEnfaceColor.a = alphaRPE;

																	//check if current voxel is marked with non zero in the vessel map, if so then it is part of a vessel and gets the color assigned from the enface texture 

																	VesselColor.rgb = RPEEnfaceColor.rgb;

																	//if we are using the vessel base color then we always lerp between this color an the microscopic enface color of the vessel with the value of VesselColorEnhancement
//#if defined(USE_VESSEL_BASE_COLOR)																																				
																	//lerp between base color an microscope enface color
																//	VesselBaseColor.rgb = RGBLinearToSRGB(lerp(SRGBToRGBLinear(VesselColor.rgb), SRGBToRGBLinear(VesselBaseColor.rgb), VesselColorEnhancement));
																	VesselBaseColor.rgb = RGBLinearToSRGB(lerp(SRGBToRGBLinear(VesselColor.rgb), SRGBToRGBLinear(VesselBaseColor.rgb), VesselColorEnhancement));
																			
																			
//#endif		
																	fixed4 blendingColor = fixed4(0, 0, 0, 0);
																	float3 gradient = computeVolumeGradient(VolumeTex, voxelCoord);
																	//testing 25.2.25
																	//float3 gradient = CalculateGradientSmooth(VolumeTex, voxelCoord);
																	
																	[branch] switch (sectionValue)
																	{
																	case 0:
																		float3 vesselSkeletonGradient = computeVolumeGradientVesselSkeleton(VesselSkeletonVolumeTex, voxelCoord);
																		previousSectionValue = 0;
																		//blend sample color with previous sample color
											// if we use blinn phong for every step along the ray we call apply shadingRetina otherwise we just use the base color without blinn phong shading and then later on blend it with the first hit for which blinn phon shading is used							
											#if defined(Vessel_BP_OnlyFirstHit)
																		if(!firstHitVessel){
																		blendingColor = VesselBaseColor;
																		}
																		else{blendingColor = applyShadingRetina(VesselBaseColor, BPVesselAmbientColor, BPVesselOutlineColor, voxelCoord, vesselSkeletonGradient, dir, BlinnPhongLightPos, VesselBlinnPhong_k_s, VesselBlinnPhong_k_a, VesselBlinnPhong_k_d, VesselBlinnPhong_s1, VesselBlinnPhong_s2, VesselBlinnPhong_s1_percentage);
																		}
																		firstHitVessel = true;
											#else
																						
																		blendingColor = applyShadingRetina(VesselBaseColor, BPVesselAmbientColor, BPVesselOutlineColor, voxelCoord, vesselSkeletonGradient, dir, BlinnPhongLightPos, VesselBlinnPhong_k_s, VesselBlinnPhong_k_a, VesselBlinnPhong_k_d, VesselBlinnPhong_s1, VesselBlinnPhong_s2, VesselBlinnPhong_s1_percentage);

																		//check if current voxel lays in the shadow, if so blend it with the shadow color
											#if (defined (ShowShadows) && defined (Vessel_Receive_Shadows))
																		if (shadowMapIntensity > 0.0 && numVoxelSincePreviousVessel > 3 ) //  numVoxelSincePreviousVessel used to counter z-fighting with shadow
																		{
																			//blendingColor = blendShadow(ShadowColor, blendingColor, 0, sampleIntensity, shadowMapIntensity);
																		}
											#endif
											#endif
																		
																		rayColor = blendUnder(rayColor, blendingColor);
																		break;
																	case 1:
																		//float3 ILMGradient = computeVolumeGradientILMSmooth(voxelCoord, sampleIntensity);
																		float3 ILMGradient = CalculateGradientSmooth(VolumeTex, voxelCoord);
																		previousSectionValue = 1;
																		//blend sample color with previous sample color
																		ILMBaseColor.a = alphaILM;
											#if defined(ILM_BP_OnlyFirstHit)
																		if(!firstHitILM){
																		blendingColor = ILMBaseColor;
																		}
																		else{blendingColor = applyShadingRetina(ILMBaseColor, BPILMAmbientColor, BPILMOutlineColor, voxelCoord, ILMGradient, dir, BlinnPhongLightPos, ILMBlinnPhong_k_s, ILMBlinnPhong_k_a, ILMBlinnPhong_k_d, ILMBlinnPhong_s1, ILMBlinnPhong_s2, ILMBlinnPhong_s1_percentage);
																		}
																		firstHitILM = true;
											#else
																		blendingColor = applyShadingRetina(ILMBaseColor, BPILMAmbientColor, BPILMOutlineColor, voxelCoord, ILMGradient, dir, BlinnPhongLightPos, ILMBlinnPhong_k_s, ILMBlinnPhong_k_a, ILMBlinnPhong_k_d, ILMBlinnPhong_s1, ILMBlinnPhong_s2, ILMBlinnPhong_s1_percentage);
											#endif
																		//check if current voxel lays in the shadow, if so blend it with the shadow color
											#if (defined (ShowShadows) && defined (ILM_Receive_Shadows))
																		if (shadowMapIntensity > 0.0 && numVoxelSincePreviousVessel > 3) //  numVoxelSincePreviousVessel used to counter z-fighting with shadow
																		{
																			//blendingColor = blendShadow(ShadowColor, blendingColor, 1, sampleIntensity, shadowMapIntensity);
																		}
											#endif
																		rayColor = blendUnder(rayColor, blendingColor);
																		break;
																	case 2:
																		previousSectionValue = 2;
																		//blend sample color with previous sample color
																		InstBaseColor.a = alphaInst;
											#if defined(Inst_BP_OnlyFirstHit)
																		if(!firstHitInst){
																		blendingColor = InstBaseColor;
																		}
																		else{blendingColor = blendUnder(rayColor,applyShadingRetina(InstBaseColor, BPInstAmbientColor, BPInstOutlineColor, voxelCoord, gradient, dir, BlinnPhongLightPos, InstBlinnPhong_k_s, InstBlinnPhong_k_a, InstBlinnPhong_k_d, InstBlinnPhong_s1, InstBlinnPhong_s2, InstBlinnPhong_s1_percentage));
																		}
																		firstHitInst = true;
											#else
																		blendingColor = blendUnder(rayColor,applyShadingRetina(InstBaseColor, BPInstAmbientColor, BPInstOutlineColor, voxelCoord, gradient, dir, BlinnPhongLightPos, InstBlinnPhong_k_s, InstBlinnPhong_k_a, InstBlinnPhong_k_d, InstBlinnPhong_s1, InstBlinnPhong_s2, InstBlinnPhong_s1_percentage));
											#endif
																		rayColor = blendUnder(rayColor, blendingColor);
																		break;
																	case 3:
																		previousSectionValue = 3;
																		//blend sample color with previous sample color						
																		RPEBaseColor.a = alphaRPE;

											#if defined(USE_ENFACE_COLOR)
											#if defined(RPE_BP_OnlyFirstHit)
																		if(!firstHitRPE){
																		blendingColor = RPEEnfaceColor;
																		}
																		else{blendingColor = applyShadingRetina(RPEEnfaceColor, BPRPEAmbientColor, BPRPEOutlineColor, voxelCoord, gradient, dir, BlinnPhongLightPos, RPEBlinnPhong_k_s, RPEBlinnPhong_k_a, RPEBlinnPhong_k_d, RPEBlinnPhong_s1, RPEBlinnPhong_s2, RPEBlinnPhong_s1_percentage);
																		}
																		firstHitRPE = true;
											#else
																		blendingColor = applyShadingRetina(RPEEnfaceColor, BPRPEAmbientColor, BPRPEOutlineColor, voxelCoord, gradient, dir, BlinnPhongLightPos, RPEBlinnPhong_k_s, RPEBlinnPhong_k_a, RPEBlinnPhong_k_d, RPEBlinnPhong_s1, RPEBlinnPhong_s2, RPEBlinnPhong_s1_percentage);
											#endif
											#else
											#if defined(RPE_BP_OnlyFirstHit)
																		if(!firstHitRPE){
																		blendingColor = RPEBaseColor;
																		}
																		else{blendingColor = applyShadingRetina(RPEBaseColor, BPRPEAmbientColor, BPRPEOutlineColor, voxelCoord, gradient, dir, BlinnPhongLightPos, RPEBlinnPhong_k_s, RPEBlinnPhong_k_a, RPEBlinnPhong_k_d, RPEBlinnPhong_s1, RPEBlinnPhong_s2, RPEBlinnPhong_s1_percentage);
																		}
																		firstHitRPE = true;
											#else
																		blendingColor = applyShadingRetina(RPEBaseColor, BPRPEAmbientColor, BPRPEOutlineColor, voxelCoord, gradient, dir, BlinnPhongLightPos, RPEBlinnPhong_k_s, RPEBlinnPhong_k_a, RPEBlinnPhong_k_d, RPEBlinnPhong_s1, RPEBlinnPhong_s2, RPEBlinnPhong_s1_percentage);
											#endif
											#endif
																		//check if current voxel lays in the shadow, if so blend it with the shadow color
											#if (defined (ShowShadows) && defined (RPE_Receive_Shadows))
																		if (shadowMapIntensity > 0.0 && numVoxelSincePreviousVessel > 3) //  numVoxelSincePreviousVessel used to counter z-fighting with shadow
																		{
																		// blendingColor = blendShadow(ShadowColor, blendingColor, 3, sampleIntensity, shadowMapIntensity);
																		}
											#endif
																		rayColor = blendUnder(rayColor, blendingColor);
																		break;

																	case 6:

																		IBVesselColor.a = alphaIBVessel;
											#if defined(IBVessel_BP_OnlyFirstHit)
																		if(!firstHitIBVessel){
																		blendingColor = IBVesselColor;
																		blendingColor.a = IBVesselColor.a;
																		}
																		else{blendingColor = applyShadingRetina(IBVesselColor, BPIBVesselAmbientColor, BPIBVesselOutlineColor, voxelCoord, gradient, dir, BlinnPhongLightPos, IBVesselBlinnPhong_k_s, IBVesselBlinnPhong_k_a, IBVesselBlinnPhong_k_d, IBVesselBlinnPhong_s1, IBVesselBlinnPhong_s2, IBVesselBlinnPhong_s1_percentage);
																		}
																		firstHitIBVessel = true;
																		
											#else
																		blendingColor = applyShadingRetina(IBVesselColor, BPIBVesselAmbientColor, BPIBVesselOutlineColor, voxelCoord, gradient, dir, BlinnPhongLightPos, IBVesselBlinnPhong_k_s, IBVesselBlinnPhong_k_a, IBVesselBlinnPhong_k_d, IBVesselBlinnPhong_s1, IBVesselBlinnPhong_s2, IBVesselBlinnPhong_s1_percentage);

											#endif
																		//check if current voxel lays in the shadow, if so blend it with the shadow color
											#if (defined (ShowShadows) && defined (IBVessel_Receive_Shadows))
																		if (shadowMapIntensity > 0.0 && numVoxelSincePreviousVessel > 3) //  numVoxelSincePreviousVessel used to counter z-fighting with shadow
																		{
																		//	blendingColor = blendShadow(ShadowColor, blendingColor, 6, sampleIntensity, shadowMapIntensity);
																		}
											#endif
											#if defined(ENHANCE_SPECLES)
																		blendingColor = enhanceSpecles(voxelCoord,blendingColor, RPEEnfaceColor, sampleIntensity, sampleIntensityOACRaw);
																		blendingColor.a = IBVesselColor.a;
											#endif
																		rayColor = blendUnder(rayColor, blendingColor);
																		break;

																	case 7:
																		previousSectionValue = 1;
																		//blend sample color with previous sample color
																		AboveILMBaseColor.a = alphaAboveILM;

																		blendingColor = applyShadingRetina(AboveILMBaseColor, BPAboveILMAmbientColor, BPAboveILMOutlineColor, voxelCoord, gradient, dir, BlinnPhongLightPos, AboveILMBlinnPhong_k_s, AboveILMBlinnPhong_k_a, AboveILMBlinnPhong_k_d, AboveILMBlinnPhong_s1, AboveILMBlinnPhong_s2, AboveILMBlinnPhong_s1_percentage);

																		rayColor = blendUnder(rayColor, blendingColor);
																		break;
																		//error case
																		case 10:
																			previousSectionValue = 10;
																			//blend sample color with previous sample color
																			//use inst color for artifact as well
																			InstBaseColor.a = alphaInst;
																			rayColor = blendUnder(rayColor, InstBaseColor);
																			break;

																		default:
																			break;
																	}

																			#elif defined (_RENDERING_BLINN_PHONG)
																								if (sampleIntensity > IsoValue) {
																									float3 gradient = computeVolumeGradient(VolumeTex, voxelCoord);
																									return applyShading(BlinnPhongBaseColor, voxelCoord, gradient, dir, BlinnPhongLightPos);
																								}

																			#elif defined (_RENDERING_COMPOSITING)
																								float4 sampleColor = tex2D(CompositingTransferFunction, sampleIntensity);
																								// modulate the sample opacity by the ray step size
																								sampleColor.a = 1.0 - pow(1.0 - sampleColor.a * CompositingAlphaFactor, StepSize * 100);
																								// blend the new sample under the previously accumulated color
																								rayColor = blendUnder(rayColor, sampleColor);
																			#elif defined (_RENDERING_CUSTOM_TRANSFERFUNCTION)
																								float4 sampleColor;
																								sampleColor.r = transferFunctionR(sampleIntensity);
																								sampleColor.g = transferFunctionG(sampleIntensity);
																								sampleColor.b = transferFunctionB(sampleIntensity);
																								// modulate the sample opacity by the ray step size
																								sampleColor.a = saturate(1.0 - pow(1.0 - sqrt(sampleColor.r * sampleColor.r + sampleColor.g * sampleColor.g + sampleColor.b * sampleColor.b) * CompositingAlphaFactor, StepSize * 100));
																								// blend the new sample under the previously accumulated color
																								rayColor = blendUnder(rayColor, sampleColor);
																			#elif defined (_RENDERING_SHADED_COMPOSITING)
																								float4 sampleColor = tex2D(CompositingTransferFunction, sampleIntensity);
																								if (sampleIntensity > IsoValue) {
																									if (firstHit) {
																										float3 gradient = computeVolumeGradient(VolumeTex, voxelCoord);
																										sampleColor.xyz = applyShading(sampleColor, voxelCoord, gradient, dir, BlinnPhongLightPos).xyz;
																										firstHit = false;
																										rayColor = blendUnder(rayColor, sampleColor);
																										continue;
																									}

																								}
																								// modulate the sample opacity by the ray step size
																								sampleColor.a = 1.0 - pow(1.0 - sampleColor.a * CompositingAlphaFactor, StepSize * 100);
																								// blend the new sample under the previously accumulated color
																								rayColor = blendUnder(rayColor, sampleColor);

																			#elif defined (_RENDERING_EXPERIMENTAL)			
																								float4 sampleColor;
																								sampleColor.r = transferFunctionR(sampleIntensity - prevVal);
																								sampleColor.g = transferFunctionG(sampleIntensity - prevVal);
																								sampleColor.b = transferFunctionB(sampleIntensity - prevVal);
																								// modulate the sample opacity by the ray step size
																								//sampleColor.a = saturate(1.0 - pow(1.0 - sqrt(sampleColor.r*sampleColor.r + sampleColor.g*sampleColor.g + sampleColor.b*sampleColor.b)*CompositingAlphaFactor, StepSize * 100));
																								// blend the new sample under the previously accumulated color
																								rayColor += sampleColor;
																								rayColor.a = 1.0;


																								prevVal = sampleIntensity;
																			#elif defined (_RENDERING_DEBUG_EXPERIMENTAL)
																								float firstSegValue = 0.003921569;
																								float secondSegValue = 0.07843138;
																								float thirdSegValue = 0.01176471;
																								float fourthSegValue = 0.015686276;
																								if (sampleIntensity.r == secondSegValue)
																								{
																									return (1, 1, 1, 1);
																								}
																			#endif	
																							}

																			#if defined(_RENDERING_ACCUMULATE)
																							accu = saturate((accu * StepSize) * Multiplier + Center);
																							return fixed4(1.0, 1.0, 1.0, accu);
																			#elif defined (_RENDERING_MAXIMUM)
																							return fixed4(1.0, 1.0, 1.0, saturate(maximum * Multiplier + Center));
																			#elif defined (_RENDERING_BLINN_PHONG)
																							return fixed4(0.0, 0.0, 0.0, 0.0);
																			#elif defined (_RENDERING_RETINA_BLINN_PHONG)


															



															return rayColor;

											#elif defined (_RENDERING_COMPOSITING)
															return rayColor;
											#elif defined (_RENDERING_SHADED_COMPOSITING)
															return rayColor;
											#elif defined (_RENDERING_CUSTOM_TRANSFERFUNCTION)
															return (rayColor * Multiplier);
															//return fixed4(rayColor.x, rayColor.y, rayColor.z, saturate(rayColor.w * Multiplier));
											#elif defined (_RENDERING_EXPERIMENTAL)
															return rayColor / 10;
											#elif defined (_RENDERING_DEBUG_EXPERIMENTAL)
															return (0, 0, 0, 0);
											#endif
														}
															ENDCG
													}

					}

						FallBack "Diffuse"
}