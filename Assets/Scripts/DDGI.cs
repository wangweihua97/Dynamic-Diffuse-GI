using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ComputeShaderBvhMeshHit;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;

namespace MyDDGI
{
    public struct IrradianceField {
        public int3                    probeCounts;
        public float3                  probeStartPosition;
        public float3                  probeStep;
        public int                     lowResolutionDownsampleFactor;

        public int                     irradianceTextureWidth;
        public int                     irradianceTextureHeight;
        public int                     depthTextureWidth;
        public int                     depthTextureHeight;
        public int                     irradianceProbeSideLength;
        public int                     depthProbeSideLength;

        public float                   irradianceDistanceBias;
        public float                   irradianceVarianceBias;
        public float                   irradianceChebyshevBias;
        public float                   normalBias;
    };
    public class DDGI : MonoBehaviour
    {
        float3          probeDimensions         = new float3(1,1,1);

        int3            probeCounts             = new int3(40, 7, 30);

        /** Side length of one face */
        int             irradianceOctResolution = 8;
        int             depthOctResolution      = 16;
        
        /** Subtract a little distance = bias (pull sample point) to avoid
            texel artifacts (self-shadowing grids).  */
        float           irradianceDistanceBias = 0.0f;

        /** Add a little variance = smooth out bias / self-shadow. 
            Larger values create smoother indirect shadows but als light leaks. */
        float           irradianceVarianceBias = 0.02f; 

        /** Bias the to avoid light leaks with thin walls.
            Usually [0, 0.5]. 0.05 is a typical value at 32^2 resolution cube-map probes.
            AO will often cover these as well. Setting the value too LARGE can create
            light leaks in corners as well. */
        float           irradianceChebyshevBias = 0.07f;

        /** Slightly bump the location of the shadow test point away from the shadow casting surface. 
            The shadow casting surface is the boundary for shadow, so the nearer an imprecise value is
            to it the more the light leaks.
        */
        float           normalBias = 0.25f;

		/** Control the weight of new rays when updating each irradiance probe. A value close to 1 will
			very slowly change the probe textures, improving stability but reducing accuracy when objects
			move in the scene, while values closer to 0.9 or lower will rapidly react to scene changes
			but exhibit flickering.
		*/
		float           hysteresis = 0.98f;

		/** Exponent for depth testing. A high value will rapidly react to depth discontinuities, but risks
			exhibiting banding.
		*/
		float           depthSharpness = 50.0f;

		/** Number of rays emitted each frame for each probe in the scene */
		int             irradianceRaysPerProbe = 64;

        /** If true, add the glossy coefficient in to matte term for a single albedo. Eliminates low-probability,
            temporally insensitive caustic effects. */
        bool            glossyToMatte = true;

        bool            singleBounce = false;

        int             irradianceFormatIndex = 4;
        int             depthFormatIndex = 1;

        bool            showLights = false;
        bool            encloseBounds = false;
        
        float3 StartPosition = new float3(-20,-0.5f,-10);
        
        RenderTexture                m_irradianceProbes;
        RenderTexture                m_meanDistProbes;
        
        RenderTexture                m_irradianceRayOrigins;
        RenderTexture                m_irradianceRayDirections;

        RenderTexture                m_rayHitPoses;
        RenderTexture                m_rayHitNormals;
        RenderTexture                m_rayHitUvs;
        RenderTexture                m_rayHitIndexs;
        
        RenderTexture                m_rayHitGBuffer;

        RenderTexture                m_rayHitColors;

        GraphicsBuffer m_randomDirBuffer;
        GraphicsBuffer m_irradianceFieldBuffer;
        public ComputeShader GenerateRays;
        public ComputeShader RayQuery;
        public ComputeShader DirectRenderRayHitGBuffer;
        public ComputeShader UpdateIrradianceProbeDepth;
        public ComputeShader UpdateIrradianceProbeIrradiance;
        public ComputeShader CopyProbeEdges;
        public List<Texture> Textures;

        public IrradianceField L;

        int ProbeAmount;
        private RenderTexture _texture2DArray;
        public Cubemap SkyCubeMap;
        

        private void Awake()
        {
            ProbeAmount = probeCounts.x * probeCounts.y * probeCounts.z;
            m_rayHitGBuffer = CreatAboutProbesRenderTexture(GraphicsFormat.R8G8B8A8_SRGB);
            m_irradianceProbes = CreatAboutProbesRenderTexture(GraphicsFormat.R8G8B8A8_SRGB);
            m_meanDistProbes = CreatAboutProbesRenderTexture(GraphicsFormat.R16G16B16A16_SFloat);

            m_irradianceRayOrigins = CreatAboutRayRenderTexture(GraphicsFormat.R16G16B16A16_SFloat);
            m_irradianceRayDirections = CreatAboutRayRenderTexture(GraphicsFormat.R16G16B16A16_SFloat);
            
            m_rayHitPoses = CreatAboutRayRenderTexture(GraphicsFormat.R16G16B16A16_SFloat);
            m_rayHitNormals = CreatAboutRayRenderTexture(GraphicsFormat.R16G16B16A16_SFloat);
            m_rayHitUvs = CreatAboutRayRenderTexture(GraphicsFormat.R16G16_UNorm);
            m_rayHitIndexs = CreatAboutRayRenderTexture(GraphicsFormat.R16_SInt);
            
            m_rayHitColors = CreatAboutRayRenderTexture(GraphicsFormat.R8G8B8A8_SRGB);

            CreatGraphicsBuffer();
            CreatIrradianceField();
            CreatTexture2DArray();
        }
        
        RenderTexture CreatAboutRayRenderTexture(GraphicsFormat graphicsFormat)
        {
            var renderTexture = new RenderTexture(irradianceRaysPerProbe ,ProbeAmount,graphicsFormat,GraphicsFormat.None);
            renderTexture.enableRandomWrite = true;
            renderTexture.Create();
            return renderTexture;
        }
        
        RenderTexture CreatAboutProbesRenderTexture(GraphicsFormat graphicsFormat)
        {
            int irradianceWidth = (irradianceOctResolution + 2) * probeCounts.x * probeCounts.y + 2;
            int irradianceHeight = (irradianceOctResolution + 2) * probeCounts.z + 2;
            var renderTexture = new RenderTexture(irradianceWidth,irradianceHeight,graphicsFormat,GraphicsFormat.None);
            renderTexture.enableRandomWrite = true;
            renderTexture.Create();
            return renderTexture;
        }

        void CreatTexture2DArray()
        {
            RenderTextureDescriptor d = new RenderTextureDescriptor(1024, 1024, RenderTextureFormat.ARGB32);
            d.dimension = TextureDimension.Tex2DArray;
            d.volumeDepth = Textures.Count; // We will have 2 slices (I.E. 2 textures) in our texture array.

            _texture2DArray = new RenderTexture(d);
            _texture2DArray.Create();
            _texture2DArray.name = "texture2DArray";

            for (int i = 0; i <Textures.Count; i++)
            {
                var pauseRenderTexture = RenderTexture.GetTemporary(Textures[i].width, Textures[i].height);
                Graphics.Blit(Textures[i], pauseRenderTexture);
                Graphics.CopyTexture(pauseRenderTexture, 0,
                    _texture2DArray, i);
            }
            
        }

        void CreatGraphicsBuffer()
        {
            m_randomDirBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, Marshal.SizeOf<float3x3>());
            m_irradianceFieldBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, Marshal.SizeOf<IrradianceField>());
        }

        void CreatIrradianceField()
        {
            int x = (irradianceOctResolution + 2) * probeCounts.x * probeCounts.y + 2;
            int y = (irradianceOctResolution + 2) * probeCounts.z + 2;
            L.probeStep = probeDimensions;
            L.probeCounts = probeCounts;
            L.probeStartPosition = StartPosition;
            L.irradianceTextureWidth = x;
            L.irradianceTextureHeight = y;
            L.irradianceProbeSideLength = irradianceOctResolution;
            L.normalBias = 0.25f;
            m_irradianceFieldBuffer.SetData(new []{L});
        }

        void GenerateRandomRays()
        {
            int kernelHandle = GenerateRays.FindKernel("CSMain");
            GenerateRays.SetTexture(kernelHandle ,"rayOrigin" ,m_irradianceRayOrigins);
            GenerateRays.SetTexture(kernelHandle ,"rayDirection" ,m_irradianceRayDirections);
            m_randomDirBuffer.SetData(new []{
                GetRandomRayMat()
            });
            
            GenerateRays.SetBuffer(kernelHandle ,"randomOrientation" ,m_randomDirBuffer);
            GenerateRays.SetBuffer(kernelHandle ,"L" ,m_irradianceFieldBuffer);
            GenerateRays.Dispatch(kernelHandle, irradianceRaysPerProbe / 8, ProbeAmount / 8, 1);
        }

        void StartRayQuery()
        {
            int kernelHandle = RayQuery.FindKernel("CSMain");
            RayQuery.SetTexture(kernelHandle, "rayOrigin", m_irradianceRayOrigins);
            RayQuery.SetTexture(kernelHandle, "rayDirection", m_irradianceRayDirections);

            RayQuery.SetTexture(kernelHandle, "posMap", m_rayHitPoses);
            RayQuery.SetTexture(kernelHandle, "normalMap", m_rayHitNormals);
            RayQuery.SetTexture(kernelHandle, "uvMap", m_rayHitUvs);
            RayQuery.SetTexture(kernelHandle, "indexMap", m_rayHitIndexs);
            RayQuery.Dispatch(kernelHandle, irradianceRaysPerProbe / 8, ProbeAmount / 8, 1);
        }

        void StartDirectRenderRayHitGBuffer()
        {
            int kernelHandle = DirectRenderRayHitGBuffer.FindKernel("CSMain");
            DirectRenderRayHitGBuffer.SetTexture(kernelHandle, "rayOrigin", m_irradianceRayOrigins);
            DirectRenderRayHitGBuffer.SetTexture(kernelHandle, "posMap", m_rayHitPoses);
            DirectRenderRayHitGBuffer.SetTexture(kernelHandle, "normalMap", m_rayHitNormals);
            DirectRenderRayHitGBuffer.SetTexture(kernelHandle, "uvMap", m_rayHitUvs);
            DirectRenderRayHitGBuffer.SetTexture(kernelHandle, "indexMap", m_rayHitIndexs);
            DirectRenderRayHitGBuffer.SetBuffer(kernelHandle ,"L" ,m_irradianceFieldBuffer);
            
            DirectRenderRayHitGBuffer.SetTexture(kernelHandle, "rayHitColors", m_rayHitColors);
            DirectRenderRayHitGBuffer.SetTexture(kernelHandle, "irradianceMeanMeanSquared", m_meanDistProbes);
            DirectRenderRayHitGBuffer.SetTexture(kernelHandle, "irradianceMap", m_irradianceProbes);
            DirectRenderRayHitGBuffer.SetTexture(kernelHandle ,"baseColorMaps" ,_texture2DArray);
            DirectRenderRayHitGBuffer.SetTexture(kernelHandle ,"skyCubeMap" ,SkyCubeMap);
            
            DirectRenderRayHitGBuffer.SetInt("probeSideLength" ,irradianceOctResolution);
            DirectRenderRayHitGBuffer.SetFloat("energyPreservation" ,1.0f);
            int x = (irradianceOctResolution + 2) * probeCounts.x * probeCounts.y + 2;
            int y = (irradianceOctResolution + 2) * probeCounts.z + 2;
            DirectRenderRayHitGBuffer.SetInts("baseColorMapSize" ,new int[]{1024,1024});
            DirectRenderRayHitGBuffer.SetInts("irradianceMapSize" ,new int[]{x
                ,y});
            DirectRenderRayHitGBuffer.Dispatch(kernelHandle, irradianceRaysPerProbe / 8 , ProbeAmount / 8 , 1);
        }

        void StartUpdateIrradianceProbeDepth()
        {
            int kernelHandle = UpdateIrradianceProbeDepth.FindKernel("CSMain");
            UpdateIrradianceProbeDepth.SetTexture(kernelHandle, "rayOrigins", m_irradianceRayOrigins);
            UpdateIrradianceProbeDepth.SetTexture(kernelHandle, "rayDirections", m_irradianceRayDirections);
            UpdateIrradianceProbeDepth.SetTexture(kernelHandle, "rayHitLocations", m_rayHitPoses);
            UpdateIrradianceProbeDepth.SetTexture(kernelHandle, "rayHitNormals", m_rayHitNormals);
            UpdateIrradianceProbeDepth.SetTexture(kernelHandle, "rayHitRadiances", m_rayHitColors);
            UpdateIrradianceProbeDepth.SetBuffer(kernelHandle ,"L" ,m_irradianceFieldBuffer);
            
            
            int x = (irradianceOctResolution + 2) * probeCounts.x * probeCounts.y + 2;
            int y = (irradianceOctResolution + 2) * probeCounts.z + 2;
            
            UpdateIrradianceProbeDepth.SetInt("fullTextureWidth" ,x);
            UpdateIrradianceProbeDepth.SetInt("fullTextureHeight" ,y);
            UpdateIrradianceProbeDepth.SetInt("probeSideLength" ,irradianceOctResolution);
            
            UpdateIrradianceProbeDepth.SetFloat("maxDistance" ,10.0f);
            UpdateIrradianceProbeDepth.SetFloat("hysteresis" ,0.98f);
            UpdateIrradianceProbeDepth.SetFloat("depthSharpness" ,50.0f);
            
            UpdateIrradianceProbeDepth.SetTexture(kernelHandle ,"results" ,m_meanDistProbes);
            
            
            
            UpdateIrradianceProbeDepth.Dispatch(kernelHandle, x / 8 , y / 8 , 1);
            
        }
        
        void StartUpdateIrradianceProbeIrradiance()
        {
            int kernelHandle = UpdateIrradianceProbeIrradiance.FindKernel("CSMain");
            UpdateIrradianceProbeIrradiance.SetTexture(kernelHandle, "rayOrigins", m_irradianceRayOrigins);
            UpdateIrradianceProbeIrradiance.SetTexture(kernelHandle, "rayDirections", m_irradianceRayDirections);
            UpdateIrradianceProbeIrradiance.SetTexture(kernelHandle, "rayHitLocations", m_rayHitPoses);
            UpdateIrradianceProbeIrradiance.SetTexture(kernelHandle, "rayHitNormals", m_rayHitNormals);
            UpdateIrradianceProbeIrradiance.SetTexture(kernelHandle, "rayHitRadiances", m_rayHitColors);
            UpdateIrradianceProbeIrradiance.SetBuffer(kernelHandle ,"L" ,m_irradianceFieldBuffer);
            
            
            int x = (irradianceOctResolution + 2) * probeCounts.x * probeCounts.y + 2;
            int y = (irradianceOctResolution + 2) * probeCounts.z + 2;
            
            UpdateIrradianceProbeIrradiance.SetInt("fullTextureWidth" ,x);
            UpdateIrradianceProbeIrradiance.SetInt("fullTextureHeight" ,y);
            UpdateIrradianceProbeIrradiance.SetInt("probeSideLength" ,irradianceOctResolution);
            
            UpdateIrradianceProbeIrradiance.SetFloat("maxDistance" ,100.0f);
            UpdateIrradianceProbeIrradiance.SetFloat("hysteresis" ,0.98f);
            UpdateIrradianceProbeIrradiance.SetFloat("depthSharpness" ,50.0f);
            UpdateIrradianceProbeIrradiance.SetTexture(kernelHandle ,"results" ,m_irradianceProbes);
            
            UpdateIrradianceProbeIrradiance.Dispatch(kernelHandle, x / 8 , y / 8 , 1);
        }
        
        void StartCopyProbeEdges()
        {
            int kernelHandle = CopyProbeEdges.FindKernel("CSMain");
            int x = (irradianceOctResolution + 2) * probeCounts.x * probeCounts.y + 2;
            int y = (irradianceOctResolution + 2) * probeCounts.z + 2;
            CopyProbeEdges.SetInt("fullTextureWidth" ,x);
            CopyProbeEdges.SetInt("fullTextureHeight" ,y);
            CopyProbeEdges.SetInt("probeSideLength" ,irradianceOctResolution);
            CopyProbeEdges.SetTexture(kernelHandle ,"results" ,m_irradianceProbes);
            CopyProbeEdges.Dispatch(kernelHandle, x / 8 , y / 8 , 1);
            
            CopyProbeEdges.SetTexture(kernelHandle ,"results" ,m_meanDistProbes);
            CopyProbeEdges.Dispatch(kernelHandle, x / 8 , y / 8 , 1);
            
        }

        void SetGlocalShaderVariables()
        {
            Shader.SetGlobalInt("probeSideLength" ,irradianceOctResolution);
            Shader.SetGlobalFloat("energyPreservation" ,1.0f);
            int x = (irradianceOctResolution + 2) * probeCounts.x * probeCounts.y + 2;
            int y = (irradianceOctResolution + 2) * probeCounts.z + 2;
            Shader.SetGlobalInt("baseColorMapSize_x" ,1024);
            Shader.SetGlobalInt("baseColorMapSize_y" ,1024);
            Shader.SetGlobalInt("irradianceMapSize_x" ,x);
            Shader.SetGlobalInt("irradianceMapSize_y" ,y);
            
            Shader.SetGlobalTexture("irradianceMap" ,m_irradianceProbes);
            Shader.SetGlobalTexture("irradianceMeanMeanSquared" ,m_meanDistProbes);
            Shader.SetGlobalBuffer("L" ,m_irradianceFieldBuffer);
        }

        float3x3 GetRandomRayMat()
        {
            float3 dir = new float3(
                2.0f * Random.Range(0.0f,1.0f) - 1.0f,
                2.0f * Random.Range(0.0f,1.0f) - 1.0f,
                2.0f * Random.Range(0.0f,1.0f) - 1.0f
            );
            float epsilon = Random.Range(0.0f, 360.0f);
            float3x3 result = float3x3.AxisAngle(math.normalize(dir) ,epsilon);
            return result;
        }

        private void Update()
        {
            GenerateRandomRays();
            StartRayQuery();
            StartDirectRenderRayHitGBuffer();
            StartUpdateIrradianceProbeDepth();
            StartUpdateIrradianceProbeIrradiance();
            StartCopyProbeEdges();
            SetGlocalShaderVariables();
            DebugRenderTexture.Instance.SetRayHitColorRenderTexture(m_rayHitColors);
            DebugRenderTexture.Instance.SetProbesIrradianceRenderTexture(m_irradianceProbes);
        }
    }
}