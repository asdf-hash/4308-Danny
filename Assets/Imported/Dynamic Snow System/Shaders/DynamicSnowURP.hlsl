// Dynamic Snow System: shared URP code for all snow shaders.
// Ported from the Built-in (Shader Forge) versions. Each .shader file sets these defines per pass:
//   SNOW_PASS_FORWARD / SNOW_PASS_SHADOW / SNOW_PASS_DEPTH / SNOW_PASS_DEPTHNORMALS / SNOW_PASS_META
//   SNOW_TESS      tessellated height displacement from the Ground Map
//   SNOW_DISTANCE  fade to the plain base colour with camera distance
//   SNOW_ONTOP     "snow on top of a rock" surface instead of the ground/footprint surface
#ifndef DYNAMIC_SNOW_URP_INCLUDED
#define DYNAMIC_SNOW_URP_INCLUDED

#define _SPECULAR_SETUP 1
#define _NORMALMAP 1
#define REQUIRES_WORLD_SPACE_TANGENT_INTERPOLATOR 1

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#if defined(SNOW_PASS_FORWARD)
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#elif defined(SNOW_PASS_SHADOW)
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
#elif defined(SNOW_PASS_META)
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MetaInput.hlsl"
#endif

// ---------------------------------------------------------------------------------------------
// Material data (one UnityPerMaterial buffer for every pass, so the SRP Batcher can batch)
// ---------------------------------------------------------------------------------------------
TEXTURE2D(_BumpMap);  SAMPLER(sampler_BumpMap);

#if defined(SNOW_ONTOP)
    TEXTURE2D(_Texture);         SAMPLER(sampler_Texture);
    TEXTURE2D(_SnowTexture);     SAMPLER(sampler_SnowTexture);
    TEXTURE2D(_SpecularTexture); SAMPLER(sampler_SpecularTexture);
    TEXTURE2D(_SnowSpecular);    SAMPLER(sampler_SnowSpecular);
    TEXTURE2D(_GlossTexture);    SAMPLER(sampler_GlossTexture);
    TEXTURE2D(_SnowGloss);       SAMPLER(sampler_SnowGloss);

    CBUFFER_START(UnityPerMaterial)
        float4 _Color;
        float4 _Texture_ST;
        float4 _SnowColor;
        float4 _SnowTexture_ST;
        float4 _SpecularTexture_ST;
        float  _Specular;
        float4 _SnowSpecular_ST;
        float4 _GlossTexture_ST;
        float  _Gloss;
        float4 _SnowGloss_ST;
        float4 _BumpMap_ST;
    CBUFFER_END
#else
    TEXTURE2D(_MainTex);   SAMPLER(sampler_MainTex);
    TEXTURE2D(_SpecMap);   SAMPLER(sampler_SpecMap);
    TEXTURE2D(_GlossMap);  SAMPLER(sampler_GlossMap);
    TEXTURE2D(_Heigth);    SAMPLER(sampler_Heigth);
    TEXTURE2D(_GroundMap); SAMPLER(sampler_GroundMap);

    CBUFFER_START(UnityPerMaterial)
        float4 _BaseColor;
        float4 _MainTex_ST;
        float4 _LowerLayer;
        float4 _SpecularColor;
        float4 _SpecMap_ST;
        float4 _GlossColor;
        float4 _GlossMap_ST;
        float4 _BumpMap_ST;
        float4 _Heigth_ST;
        float4 _GroundMap_ST;
        float  _DisplacementStrength;
        #if defined(SNOW_DISTANCE)
            float _UseLowerLayer;
            float _DistanceBlend;
        #endif
        #if defined(SNOW_TESS)
            float _TesselationEdgeLength;
        #endif
    CBUFFER_END
#endif

#if defined(SNOW_PASS_SHADOW)
    // Set by URP for the shadow-casting light (see ShadowCasterPass.hlsl in the URP package).
    float3 _LightDirection;
    float3 _LightPosition;
#endif

// ---------------------------------------------------------------------------------------------
// Vertex data
// ---------------------------------------------------------------------------------------------
struct Attributes
{
    float4 positionOS        : POSITION;
    float3 normalOS          : NORMAL;
    float4 tangentOS         : TANGENT;
    float2 uv                : TEXCOORD0;
    float2 staticLightmapUV  : TEXCOORD1;
    float2 dynamicLightmapUV : TEXCOORD2;
};

struct Varyings
{
#if defined(SNOW_PASS_FORWARD)
    float2 uv                     : TEXCOORD0;
    float3 positionWS             : TEXCOORD1;
    float3 normalWS               : TEXCOORD2;
    half4  tangentWS              : TEXCOORD3; // xyz: tangent, w: sign
    #ifdef _ADDITIONAL_LIGHTS_VERTEX
        half4 fogFactorAndVertexLight : TEXCOORD5; // x: fog, yzw: vertex light
    #else
        half fogFactor            : TEXCOORD5;
    #endif
    #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
        float4 shadowCoord        : TEXCOORD6;
    #endif
    DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 8);
    #ifdef DYNAMICLIGHTMAP_ON
        float2 dynamicLightmapUV  : TEXCOORD9;
    #endif
    #ifdef USE_APV_PROBE_OCCLUSION
        float4 probeOcclusion     : TEXCOORD10;
    #endif
#elif defined(SNOW_PASS_DEPTHNORMALS)
    float3 normalWS               : TEXCOORD2;
#elif defined(SNOW_PASS_META)
    float2 uv                     : TEXCOORD0;
#endif
    float4 positionCS             : SV_POSITION;
};

// ---------------------------------------------------------------------------------------------
// Height displacement (tessellated variants only, as in the original shaders)
// ---------------------------------------------------------------------------------------------
#if defined(SNOW_TESS)
void SnowDisplace(inout Attributes v)
{
    float3 height = SAMPLE_TEXTURE2D_LOD(_Heigth,    sampler_Heigth,    TRANSFORM_TEX(v.uv, _Heigth),    0).rgb;
    float3 ground = SAMPLE_TEXTURE2D_LOD(_GroundMap, sampler_GroundMap, TRANSFORM_TEX(v.uv, _GroundMap), 0).rgb;
    v.positionOS.xyz += (height * ground) * (_DisplacementStrength * v.normalOS);
}
#endif

// ---------------------------------------------------------------------------------------------
// Surface
// ---------------------------------------------------------------------------------------------
half3 SnowSampleNormalTS(float2 uv)
{
    return UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, TRANSFORM_TEX(uv, _BumpMap)));
}

void SnowSurface(float2 uv, float3 positionWS, float3 normalWS, out half3 albedo, out half3 specular, out half smoothness)
{
#if defined(SNOW_ONTOP)
    // 0 on up-facing surfaces (snow), 1 on steep/underside surfaces (rock)
    float rockAmount = 1.0 - saturate(normalWS.y);

    float3 snowAlbedo = SAMPLE_TEXTURE2D(_SnowTexture, sampler_SnowTexture, TRANSFORM_TEX(uv, _SnowTexture)).rgb * _SnowColor.rgb;
    float3 rockAlbedo = SAMPLE_TEXTURE2D(_Texture, sampler_Texture, TRANSFORM_TEX(uv, _Texture)).rgb * _Color.rgb;
    albedo = lerp(snowAlbedo, rockAlbedo, rockAmount);

    float3 snowSpec = SAMPLE_TEXTURE2D(_SnowSpecular, sampler_SnowSpecular, TRANSFORM_TEX(uv, _SnowSpecular)).rgb;
    float3 rockSpec = SAMPLE_TEXTURE2D(_SpecularTexture, sampler_SpecularTexture, TRANSFORM_TEX(uv, _SpecularTexture)).rgb * _Specular;
    specular = lerp(snowSpec, rockSpec, rockAmount);

    float snowGloss = SAMPLE_TEXTURE2D(_SnowGloss, sampler_SnowGloss, TRANSFORM_TEX(uv, _SnowGloss)).r;
    float rockGloss = SAMPLE_TEXTURE2D(_GlossTexture, sampler_GlossTexture, TRANSFORM_TEX(uv, _GlossTexture)).r * _Gloss;
    smoothness = lerp(snowGloss, rockGloss, rockAmount);
#else
    float3 baseColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, TRANSFORM_TEX(uv, _MainTex)).rgb * _BaseColor.rgb;
    float3 groundMask = SAMPLE_TEXTURE2D(_GroundMap, sampler_GroundMap, TRANSFORM_TEX(uv, _GroundMap)).rgb;
    // Ground Map: white = untouched snow (base colour), black = trodden (lower layer)
    float3 withLowerLayer = lerp(_LowerLayer.rgb, baseColor, groundMask);

    #if defined(SNOW_DISTANCE)
        float3 near = (_UseLowerLayer > 0.5) ? withLowerLayer : baseColor;
        float farAmount = saturate(pow(distance(positionWS, _WorldSpaceCameraPos) / max(_DistanceBlend, 1e-5), 8.0));
        albedo = lerp(near, baseColor, farAmount);
    #else
        albedo = withLowerLayer;
    #endif

    specular = SAMPLE_TEXTURE2D(_SpecMap, sampler_SpecMap, TRANSFORM_TEX(uv, _SpecMap)).rgb * _SpecularColor.rgb;
    smoothness = SAMPLE_TEXTURE2D(_GlossMap, sampler_GlossMap, TRANSFORM_TEX(uv, _GlossMap)).r * _GlossColor.r;
#endif
}

// ---------------------------------------------------------------------------------------------
// Vertex stage (after optional displacement)
// ---------------------------------------------------------------------------------------------
Varyings SnowBuildVaryings(Attributes input)
{
    Varyings output = (Varyings)0;

#if defined(SNOW_PASS_FORWARD)
    VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
    VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);
    half3 vertexLight = VertexLighting(vertexInput.positionWS, normalInput.normalWS);

    half fogFactor = 0;
    #if !defined(_FOG_FRAGMENT)
        fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
    #endif

    output.uv = input.uv;
    output.positionWS = vertexInput.positionWS;
    output.normalWS = normalInput.normalWS;
    output.tangentWS = half4(normalInput.tangentWS.xyz, input.tangentOS.w * GetOddNegativeScale());

    OUTPUT_LIGHTMAP_UV(input.staticLightmapUV, unity_LightmapST, output.staticLightmapUV);
    #ifdef DYNAMICLIGHTMAP_ON
        output.dynamicLightmapUV = input.dynamicLightmapUV.xy * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
    #endif
    OUTPUT_SH4(vertexInput.positionWS, output.normalWS.xyz, GetWorldSpaceNormalizeViewDir(vertexInput.positionWS), output.vertexSH, output.probeOcclusion);

    #ifdef _ADDITIONAL_LIGHTS_VERTEX
        output.fogFactorAndVertexLight = half4(fogFactor, vertexLight);
    #else
        output.fogFactor = fogFactor;
    #endif
    #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
        output.shadowCoord = GetShadowCoord(vertexInput);
    #endif
    output.positionCS = vertexInput.positionCS;

#elif defined(SNOW_PASS_SHADOW)
    float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
    float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
    #if _CASTING_PUNCTUAL_LIGHT_SHADOW
        float3 lightDirectionWS = normalize(_LightPosition - positionWS);
    #else
        float3 lightDirectionWS = _LightDirection;
    #endif
    output.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));

#elif defined(SNOW_PASS_DEPTH)
    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);

#elif defined(SNOW_PASS_DEPTHNORMALS)
    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
    output.normalWS = NormalizeNormalPerVertex(TransformObjectToWorldNormal(input.normalOS));

#elif defined(SNOW_PASS_META)
    output.positionCS = MetaVertexPosition(input.positionOS, input.staticLightmapUV, input.dynamicLightmapUV, unity_LightmapST, unity_DynamicLightmapST);
    output.uv = input.uv;
#endif

    return output;
}

#if defined(SNOW_TESS) && !defined(SNOW_PASS_META)
// ---------------------------------------------------------------------------------------------
// Tessellation (edge-length based, same maths as Built-in Tessellation.cginc)
// ---------------------------------------------------------------------------------------------
struct TessControlPoint
{
    float4 positionOS        : INTERNALTESSPOS;
    float3 normalOS          : NORMAL;
    float4 tangentOS         : TANGENT;
    float2 uv                : TEXCOORD0;
    float2 staticLightmapUV  : TEXCOORD1;
    float2 dynamicLightmapUV : TEXCOORD2;
};

struct TessFactors
{
    float edge[3] : SV_TessFactor;
    float inside  : SV_InsideTessFactor;
};

TessControlPoint TessVert(Attributes v)
{
    TessControlPoint o;
    o.positionOS = v.positionOS;
    o.normalOS = v.normalOS;
    o.tangentOS = v.tangentOS;
    o.uv = v.uv;
    o.staticLightmapUV = v.staticLightmapUV;
    o.dynamicLightmapUV = v.dynamicLightmapUV;
    return o;
}

float SnowEdgeTessFactor(float3 positionAOS, float3 positionBOS)
{
    float3 a = TransformObjectToWorld(positionAOS);
    float3 b = TransformObjectToWorld(positionBOS);
    float dist = distance(0.5 * (a + b), _WorldSpaceCameraPos);
    float len = distance(a, b);
    return clamp(len * _ScreenParams.y / (max(_TesselationEdgeLength, 0.001) * max(dist, 0.001)), 1.0, 64.0);
}

TessFactors SnowPatchConstants(InputPatch<TessControlPoint, 3> p)
{
    TessFactors f;
    f.edge[0] = SnowEdgeTessFactor(p[1].positionOS.xyz, p[2].positionOS.xyz);
    f.edge[1] = SnowEdgeTessFactor(p[2].positionOS.xyz, p[0].positionOS.xyz);
    f.edge[2] = SnowEdgeTessFactor(p[0].positionOS.xyz, p[1].positionOS.xyz);
    f.inside = (f.edge[0] + f.edge[1] + f.edge[2]) / 3.0;
    return f;
}

[domain("tri")]
[partitioning("fractional_odd")]
[outputtopology("triangle_cw")]
[patchconstantfunc("SnowPatchConstants")]
[outputcontrolpoints(3)]
TessControlPoint Hull(InputPatch<TessControlPoint, 3> p, uint id : SV_OutputControlPointID)
{
    return p[id];
}

[domain("tri")]
Varyings Domain(TessFactors f, const OutputPatch<TessControlPoint, 3> p, float3 bary : SV_DomainLocation)
{
    Attributes v = (Attributes)0;
    #define SNOW_INTERP(field) v.field = p[0].field * bary.x + p[1].field * bary.y + p[2].field * bary.z;
    SNOW_INTERP(positionOS)
    SNOW_INTERP(normalOS)
    SNOW_INTERP(tangentOS)
    SNOW_INTERP(uv)
    SNOW_INTERP(staticLightmapUV)
    SNOW_INTERP(dynamicLightmapUV)
    #undef SNOW_INTERP
    SnowDisplace(v);
    return SnowBuildVaryings(v);
}
#else
Varyings Vert(Attributes v)
{
    return SnowBuildVaryings(v);
}
#endif

// ---------------------------------------------------------------------------------------------
// Fragment stage
// ---------------------------------------------------------------------------------------------
#if defined(SNOW_PASS_FORWARD)

void SnowInitializeInputData(Varyings input, half3 normalTS, out InputData inputData)
{
    inputData = (InputData)0;
    inputData.positionWS = input.positionWS;

    float sgn = input.tangentWS.w;
    float3 bitangent = sgn * cross(input.normalWS.xyz, input.tangentWS.xyz);
    half3x3 tangentToWorld = half3x3(input.tangentWS.xyz, bitangent.xyz, input.normalWS.xyz);
    inputData.tangentToWorld = tangentToWorld;
    inputData.normalWS = NormalizeNormalPerPixel(TransformTangentToWorld(normalTS, tangentToWorld));
    inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);

    #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
        inputData.shadowCoord = input.shadowCoord;
    #elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
        inputData.shadowCoord = TransformWorldToShadowCoord(inputData.positionWS);
    #else
        inputData.shadowCoord = float4(0, 0, 0, 0);
    #endif

    #ifdef _ADDITIONAL_LIGHTS_VERTEX
        inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactorAndVertexLight.x);
        inputData.vertexLighting = input.fogFactorAndVertexLight.yzw;
    #else
        inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
    #endif

    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

    #if defined(_SCREEN_SPACE_IRRADIANCE)
        inputData.bakedGI = SAMPLE_GI(_ScreenSpaceIrradiance, input.positionCS.xy);
    #elif defined(DYNAMICLIGHTMAP_ON)
        inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.dynamicLightmapUV, input.vertexSH, inputData.normalWS);
        inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);
    #elif !defined(LIGHTMAP_ON) && (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2))
        inputData.bakedGI = SAMPLE_GI(input.vertexSH,
            GetAbsolutePositionWS(inputData.positionWS),
            inputData.normalWS,
            inputData.viewDirectionWS,
            input.positionCS.xy,
            input.probeOcclusion,
            inputData.shadowMask);
    #else
        inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.vertexSH, inputData.normalWS);
        inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);
    #endif
}

void SnowFragment(
    Varyings input
    , out half4 outColor : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out uint outRenderingLayers : SV_Target1
#endif
)
{
    half3 normalTS = SnowSampleNormalTS(input.uv);

    InputData inputData;
    SnowInitializeInputData(input, normalTS, inputData);

    SurfaceData surfaceData = (SurfaceData)0;
    SnowSurface(input.uv, input.positionWS, inputData.normalWS, surfaceData.albedo, surfaceData.specular, surfaceData.smoothness);
    surfaceData.metallic = 0;
    surfaceData.occlusion = 1;
    surfaceData.alpha = 1;
    surfaceData.normalTS = normalTS;
    surfaceData.emission = 0;

    half4 color = UniversalFragmentPBR(inputData, surfaceData);
    color.rgb = MixFog(color.rgb, inputData.fogCoord);
    color.a = 1;
    outColor = color;

    #ifdef _WRITE_RENDERING_LAYERS
        outRenderingLayers = EncodeMeshRenderingLayer();
    #endif
}

#elif defined(SNOW_PASS_SHADOW)

half4 SnowFragment(Varyings input) : SV_TARGET
{
    return 0;
}

#elif defined(SNOW_PASS_DEPTH)

half SnowFragment(Varyings input) : SV_TARGET
{
    return input.positionCS.z;
}

#elif defined(SNOW_PASS_DEPTHNORMALS)

void SnowFragment(
    Varyings input
    , out half4 outNormalWS : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out uint outRenderingLayers : SV_Target1
#endif
)
{
    #if defined(_GBUFFER_NORMALS_OCT)
        float3 normalWS = normalize(input.normalWS);
        float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
        float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
        half3 packedNormalWS = PackFloat2To888(remappedOctNormalWS);
        outNormalWS = half4(packedNormalWS, 0.0);
    #else
        outNormalWS = half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
    #endif
    #ifdef _WRITE_RENDERING_LAYERS
        outRenderingLayers = EncodeMeshRenderingLayer();
    #endif
}

#elif defined(SNOW_PASS_META)

half4 SnowFragment(Varyings input) : SV_TARGET
{
    half3 albedo, specular;
    half smoothness;
    SnowSurface(input.uv, float3(0, 0, 0), float3(0, 1, 0), albedo, specular, smoothness);

    MetaInput metaInput = (MetaInput)0;
    metaInput.Albedo = albedo;
    metaInput.Emission = 0;
    return MetaFragment(metaInput);
}
#endif

#endif // DYNAMIC_SNOW_URP_INCLUDED
