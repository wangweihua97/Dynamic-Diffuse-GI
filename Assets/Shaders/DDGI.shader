Shader "Unlit/DDGI"
{
    Properties
    {
        [MainTexture]_BaseMap ("Base Texture",2D) = "white"{}
        [MainColor][HDR]_BaseColor("Base Color",Color)=(1,1,1,1)
    }
    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"//这是一个URP Shader！
            "Queue"="Geometry"
            "RenderType"="Opaque"
        }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "GridHelpers.hlsl"
        #define pi 3.1415926535
       
        //除了贴图外，要暴露在Inspector面板上的变量都需要缓存到CBUFFER中
        StructuredBuffer<IrradianceField> L;
        int probeSideLength;
        float energyPreservation;
        int baseColorMapSize_x;
        int baseColorMapSize_y;
        int irradianceMapSize_x;
        int irradianceMapSize_y;
        CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST;
        float4 irradianceMap_ST;
        half4 _BaseColor;
        half _IsSpecular;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Tags{"LightMode"="UniversalForward"}//这个Pass最终会输出到颜色缓冲里
            
            cull off

            HLSLPROGRAM //CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #define LINEAR_BLENDING 1

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 normalOS : NORMAL;
                float2 uv : TEXCOORD;
            };
            struct Varings//这就是v2f
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD;
                float3 positionWS : TEXCOORD1;
                float3 viewDirWS : TEXCOORD2;
                float3 normalWS : TEXCOORD3;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(irradianceMap);
            SAMPLER(samplerirradianceMap);
            TEXTURE2D(irradianceMeanMeanSquared);
            Varings vert(Attributes IN)
            {
                Varings OUT;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(IN.normalOS.xyz);
                OUT.positionCS = positionInputs.positionCS;
                  
                OUT.uv=TRANSFORM_TEX(IN.uv,_BaseMap);
                OUT.positionWS = positionInputs.positionWS;
                OUT.viewDirWS = GetCameraPositionWS() - positionInputs.positionWS;
                OUT.normalWS = normalInputs.normalWS;
                return OUT;
            }

            float4 frag(Varings IN):SV_Target
            {
                half4 baseMap = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                float4 SHADOW_COORDS = TransformWorldToShadowCoord(IN.positionWS);
                Light light = GetMainLight(SHADOW_COORDS);
                half3 n = normalize(IN.normalWS);
                half3 v = normalize(IN.viewDirWS);
                half3 h = normalize(light.direction + v);
                
                
                half nl = max(0.0,dot(light.direction ,n));
                half nh = max(0.0,dot(h ,n));
                
                half atten =  light.shadowAttenuation;
                half3 diffuse = atten * nl * baseMap.xyz ;
                half3 specular = atten * light.color * pow(nh ,16);
                
                //half3 color=diffuse*_BaseColor.xyz +specular;
                half3 color=diffuse*_BaseColor.xyz;
                
                
                int2 baseColorMapSize = int2(baseColorMapSize_x,baseColorMapSize_y);
                int2 irradianceMapSize = int2(irradianceMapSize_x,irradianceMapSize_y);
                int3 gridCoord = baseGridCoord(L[0], IN.positionWS);
                float3 baseProbePos = gridCoordToPosition(L[0], gridCoord);
                Irradiance3 sumIrradiance = Irradiance3(0,0,0);
                float sumWeight = 0.0;
                // alpha is how far from the floor(currentVertex) position. on [0, 1] for each axis.
                float3 alpha = clamp((IN.positionWS - baseProbePos) / L[0].probeStep, float3(0,0,0), float3(1,1,1));
            
                // Iterate over adjacent probe cage
                for (int i = 0; i < 8; ++i) {
                    // Compute the offset grid coord and clamp to the probe grid boundary
                    // Offset = 0 or 1 along each axis
                    GridCoord  offset = int3(i, i >> 1, i >> 2) & int3(1,1,1);
                    GridCoord  probeGridCoord = clamp(gridCoord + offset, GridCoord(0,0,0), GridCoord(L[0].probeCounts - GridCoord(1,1,1)));
                    ProbeIndex p = gridCoordToProbeIndex(L[0], probeGridCoord);
            
                    // Make cosine falloff in tangent plane with respect to the angle from the surface to the probe so that we never
                    // test a probe that is *behind* the surface.
                    // It doesn't have to be cosine, but that is efficient to compute and we must clip to the tangent plane.
                    float3 probePos = gridCoordToPosition(L[0], probeGridCoord);
            
                    // Bias the position at which visibility is computed; this
                    // avoids performing a shadow test *at* a surface, which is a
                    // dangerous location because that is exactly the line between
                    // shadowed and unshadowed. If the normal bias is too small,
                    // there will be light and dark leaks. If it is too large,
                    // then samples can pass through thin occluders to the other
                    // side (this can only happen if there are MULTIPLE occluders
                    // near each other, a wall surface won't pass through itself.)
                    float3 probeToPoint = IN.positionWS - probePos + (n + 3.0 * v) * L[0].normalBias;
                    float3 dir = normalize(-probeToPoint);
            
                    // Compute the trilinear weights based on the grid cell vertex to smoothly
                    // transition between probes. Avoid ever going entirely to zero because that
                    // will cause problems at the border probes. This isn't really a lerp. 
                    // We're using 1-a when offset = 0 and a when offset = 1.
                    float3 trilinear = lerp(1.0 - alpha, alpha, offset);
                    float weight = 1.0;
            
                    // Clamp all of the multiplies. We can't let the weight go to zero because then it would be 
                    // possible for *all* weights to be equally low and get normalized
                    // up to 1/n. We want to distinguish between weights that are 
                    // low because of different factors.
            
                    // Smooth backface test
                    {
                        // Computed without the biasing applied to the "dir" variable. 
                        // This test can cause reflection-map looking errors in the image
                        // (stuff looks shiny) if the transition is poor.
                        float3 trueDirectionToProbe = normalize(probePos - IN.positionWS);
            
                        // The naive soft backface weight would ignore a probe when
                        // it is behind the surface. That's good for walls. But for small details inside of a
                        // room, the normals on the details might rule out all of the probes that have mutual
                        // visibility to the point. So, we instead use a "wrap shading" test below inspired by
                        // NPR work.
                        // weight *= max(0.0001, dot(trueDirectionToProbe, wsN));
            
                        // The small offset at the end reduces the "going to zero" impact
                        // where this is really close to exactly opposite
                        weight *= square(max(0.0001, (dot(trueDirectionToProbe, n) + 1.0) * 0.5)) + 0.2;
                    }
                    
                    // Moment visibility test
                    {
                        float2 texCoord = textureCoordFromDirection(-dir,
                            p,
                            L[0].depthTextureWidth,
                            L[0].depthTextureHeight,
                            L[0].depthProbeSideLength);
            
                        float distToProbe = length(probeToPoint);
                        //int2 itexCoord = int2(irradianceMapSize * texCoord);
                        //float2 temp = irradianceMeanMeanSquared[itexCoord].xy;
                        float2 temp = SAMPLE_TEXTURE2D(irradianceMeanMeanSquared, samplerirradianceMap, texCoord).rg;
                        float mean = temp.x;
                        float variance = abs(square(temp.x) - temp.y);
            
                        // http://www.punkuser.net/vsm/vsm_paper.pdf; equation 5
                        // Need the max in the denominator because biasing can cause a negative displacement
                        float chebyshevWeight = variance / (variance + square(max(distToProbe - mean, 0.0)));
                            
                        // Increase contrast in the weight 
                        chebyshevWeight = max(pow3(chebyshevWeight), 0.0);
            
                        weight *= (distToProbe <= mean) ? 1.0 : chebyshevWeight;
                    }
            
                    // Avoid zero weight
                    weight = max(0.000001, weight);
                             
                    float3 irradianceDir = n;
            
                    float2 texCoord = textureCoordFromDirection(normalize(irradianceDir),
                        p,
                        L[0].irradianceTextureWidth,
                        L[0].irradianceTextureHeight,
                        L[0].irradianceProbeSideLength);
                    //int2 itexCoord = int2(irradianceMapSize * texCoord);
                    //Irradiance3 probeIrradiance = texture(L[0].irradianceProbeGridbuffer, texCoord).rgb;
                    //Irradiance3 probeIrradiance = irradianceMap[itexCoord].rgb;
                    
                    Irradiance3 probeIrradiance = SAMPLE_TEXTURE2D(irradianceMap ,samplerirradianceMap ,texCoord).rgb;
            
                    // A tiny bit of light is really visible due to log perception, so
                    // crush tiny weights but keep the curve continuous. This must be done
                    // before the trilinear weights, because those should be preserved.
                    const float crushThreshold = 0.2;
                    if (weight < crushThreshold) {
                        weight *= weight * weight * (1.0 / square(crushThreshold)); 
                    }
            
                    // Trilinear weights
                    weight *= trilinear.x * trilinear.y * trilinear.z;
            
                    // Weight in a more-perceptual brightness space instead of radiance space.
                    // This softens the transitions between probes with respect to translation.
                    // It makes little difference most of the time, but when there are radical transitions
                    // between probes this helps soften the ramp.
            #       if LINEAR_BLENDING == 0
                        probeIrradiance = sqrt(probeIrradiance);
            #       endif
                    
                    sumIrradiance += weight * probeIrradiance;
                    sumWeight += weight;
                }
            
                Irradiance3 netIrradiance = sumIrradiance / sumWeight;
            
                // Go back to linear irradiance
            #   if LINEAR_BLENDING == 0
                    netIrradiance = square(netIrradiance);
            #   endif
                netIrradiance *= energyPreservation;
                           
                return half4(0.5 * pi * netIrradiance * baseMap.xyz * _BaseColor.xyz + color ,1.0);
                //return half4(0.5 * pi * netIrradiance * baseMap.xyz * _BaseColor.xyz ,1.0);
            }
            ENDHLSL  //ENDCG          
        }
        
        pass {
			Tags{ "LightMode" = "ShadowCaster" }
			cull off
			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			
 
			struct appdata
			{
				float4 vertex : POSITION;
			};
 
			struct v2f
			{
				float4 pos : SV_POSITION;
			};
 
			sampler2D _MainTex;
			float4 _MainTex_ST;
 
			v2f vert(appdata v)
			{
				v2f o;
				o.pos = mul(UNITY_MATRIX_MVP,v.vertex);
				return o;
			}
			float4 frag(v2f i) : SV_Target
			{
				return half4(0.0,0.0,0.0,1.0);
			}
			ENDHLSL
		}
    }
}