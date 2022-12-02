using System;
using System.Runtime.InteropServices;
using ComputeShaderBvhMeshHit;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
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

        int3            probeCounts             = new int3(10, 6, 10);

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
        
        
        
        RenderTexture                m_irradianceProbes;
        RenderTexture                m_meanDistProbes;
        
        RenderTexture                m_irradianceRayOrigins;
        RenderTexture                m_irradianceRayDirections;

        RenderTexture                m_rayHitPoses;
        RenderTexture                m_rayHitNormals;
        RenderTexture                m_rayHitUvs;
        RenderTexture                m_rayHitIndexs;

        RenderTexture                m_rayHitColors;

        GraphicsBuffer m_randomDirBuffer;
        GraphicsBuffer m_irradianceFieldBuffer;
        public ComputeShader GenerateRays;

        public IrradianceField L;

        int ProbeAmount;
        

        private void Awake()
        {
            ProbeAmount = probeCounts.x * probeCounts.y * probeCounts.z;
            m_irradianceProbes = CreatAboutProbesRenderTexture(GraphicsFormat.R8G8B8A8_SRGB);
            m_meanDistProbes = CreatAboutProbesRenderTexture(GraphicsFormat.R16G16_SFloat);

            m_irradianceRayOrigins = CreatAboutRayRenderTexture(GraphicsFormat.R16G16B16A16_SFloat);
            m_irradianceRayDirections = CreatAboutRayRenderTexture(GraphicsFormat.R16G16B16A16_SFloat);
            
            m_rayHitPoses = CreatAboutRayRenderTexture(GraphicsFormat.R16G16B16A16_SFloat);
            m_rayHitNormals = CreatAboutRayRenderTexture(GraphicsFormat.R16G16B16A16_SFloat);
            m_rayHitUvs = CreatAboutRayRenderTexture(GraphicsFormat.R16G16_UNorm);
            m_rayHitIndexs = CreatAboutRayRenderTexture(GraphicsFormat.R16_SInt);
            
            m_rayHitColors = CreatAboutRayRenderTexture(GraphicsFormat.R8G8B8A8_SRGB);

            CreatGraphicsBuffer();
            CreatIrradianceField();
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
            var renderTexture = new RenderTexture(irradianceOctResolution * irradianceOctResolution * 4 ,probeCounts.x * probeCounts.y * probeCounts.z,graphicsFormat,GraphicsFormat.None);
            renderTexture.enableRandomWrite = true;
            renderTexture.Create();
            return renderTexture;
        }

        void CreatGraphicsBuffer()
        {
            m_randomDirBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, Marshal.SizeOf<float3x3>());
            m_irradianceFieldBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, Marshal.SizeOf<IrradianceField>());
        }

        void CreatIrradianceField()
        {
            L.probeStep = probeDimensions;
            L.probeCounts = probeCounts;
            L.probeStartPosition = new float3(-5,-1,-5);
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

        float3x3 GetRandomRayMat()
        {
            float3 dir = new float3(
                2.0f * Random.Range(0.0f,1.0f) - 1.0f,
                2.0f * Random.Range(0.0f,1.0f) - 1.0f,
                2.0f * Random.Range(0.0f,1.0f) - 1.0f
            );

            float epsilon = Random.Range(0.0f, 360.0f);
            float3x3 result = float3x3.AxisAngle(dir ,epsilon);
            return result;
        }

        private void Update()
        {
            GenerateRandomRays();
        }
    }
}