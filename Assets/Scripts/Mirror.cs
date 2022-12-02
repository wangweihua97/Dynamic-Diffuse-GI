using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace MyDDGI
{
    public class Mirror : MonoBehaviour
    {
        public ComputeShader MirrorComputeShader;
        public ComputeShader RayQueryComputeShader;

        private RenderTexture _rayOrigin;
        private RenderTexture _rayDirection;

        private RenderTexture _posMap;
        private RenderTexture _normalMap;
        private RenderTexture _uvMap;
        private RenderTexture _indexMap;

        private Material _material;
        private void Awake()
        {
            _rayOrigin = CreatRenderTexture(GraphicsFormat.R16G16B16A16_SFloat);
            _rayDirection = CreatRenderTexture(GraphicsFormat.R16G16B16A16_SFloat);
            
            _posMap = CreatRenderTexture(GraphicsFormat.R16G16B16A16_SFloat);
            _normalMap = CreatRenderTexture(GraphicsFormat.R16G16B16A16_SFloat);
            _uvMap = CreatRenderTexture(GraphicsFormat.R16G16_SFloat);
            _indexMap = CreatRenderTexture(GraphicsFormat.R16_SInt);

            _material = GetComponent<MeshRenderer>().sharedMaterial;
            _material.SetTexture("_UvMap" ,_uvMap);
        }

        RenderTexture CreatRenderTexture(GraphicsFormat graphicsFormat)
        {
            var renderTexture = new RenderTexture(256,256,graphicsFormat,GraphicsFormat.None);
            renderTexture.enableRandomWrite = true;
            renderTexture.Create();
            return renderTexture;
        }

        private void Update()
        {
            MirrorComputeShaderUpdate();
            RayQueryComputeShaderUpdate();
        }

        void MirrorComputeShaderUpdate()
        {
            int kernelHandle = MirrorComputeShader.FindKernel("CSMain");

            MirrorComputeShader.SetTexture(kernelHandle, "rayOrigin", _rayOrigin);
            MirrorComputeShader.SetTexture(kernelHandle, "rayDirection", _rayDirection);
            MirrorComputeShader.SetMatrix("worldMat" ,transform.localToWorldMatrix);

            Vector3 v = (Camera.main.transform.position - transform.position).normalized;
            Vector3 n = -transform.forward;
            MirrorComputeShader.SetVector("viewDir" ,V3_2_V4(v));
            MirrorComputeShader.SetVector("normal" ,V3_2_V4(n));
            MirrorComputeShader.SetVector("cameraPos" ,V3_2_V4(Camera.main.transform.position));
            MirrorComputeShader.Dispatch(kernelHandle, 256 / 8, 256 / 8, 1);
        }

        Vector4 V3_2_V4(Vector3 v)
        {
            return new Vector4(v.x, v.y, v.z, 1);
        }

        void RayQueryComputeShaderUpdate()
        {
            int kernelHandle = RayQueryComputeShader.FindKernel("CSMain");

            RayQueryComputeShader.SetTexture(kernelHandle, "rayOrigin", _rayOrigin);
            RayQueryComputeShader.SetTexture(kernelHandle, "rayDirection", _rayDirection);

            RayQueryComputeShader.SetTexture(kernelHandle, "posMap", _posMap);
            RayQueryComputeShader.SetTexture(kernelHandle, "normalMap", _normalMap);
            RayQueryComputeShader.SetTexture(kernelHandle, "uvMap", _uvMap);
            RayQueryComputeShader.SetTexture(kernelHandle, "indexMap", _indexMap);
            RayQueryComputeShader.Dispatch(kernelHandle, 256 / 8, 256 / 8, 1);
        }

        public void SetBuffer(GraphicsBuffer bvhBuffer,
            GraphicsBuffer triangleBuffer,
            GraphicsBuffer goInfoBuffer)
        {
            int kernelHandle = RayQueryComputeShader.FindKernel("CSMain");
            RayQueryComputeShader.SetBuffer(kernelHandle ,"bvhBuffer" ,bvhBuffer);
            RayQueryComputeShader.SetBuffer(kernelHandle ,"triangleBuffer" ,triangleBuffer);
            RayQueryComputeShader.SetBuffer(kernelHandle ,"goInfoBuffer" ,goInfoBuffer);
        }
    }
}