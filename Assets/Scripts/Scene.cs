using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using ComputeShaderBvhMeshHit;
using Unity.Mathematics;
using UnityEngine;

namespace MyDDGI
{
    public class Scene : MonoBehaviour
    {
        public struct GoInfo
        {
            public float3 Min;
            public float3 Max;
            public float3 Pos;
            public int BvhDatasStart;
            public int BvhDatasCount;
            public int TrianglesStart;
            public int TrianglesCount;
        }
        
        
        private List<BVH_GO> _gos;
        private List<BvhAsset> _bvhs;
        
        private List<BvhData> totalBvhDatas;
        private List<Triangle> totalTriangles;

        private List<GoInfo> _goInfos;
        private List<Texture> _renderTextures;
        
        GraphicsBuffer bvhBuffer;
        GraphicsBuffer triangleBuffer;
        GraphicsBuffer goInfosBuffer;

        public Mirror Mirror;

        void Awake()
        {
            totalBvhDatas = new List<BvhData>();
            totalTriangles = new List<Triangle>();
            
            _renderTextures = new List<Texture>();
            
            _gos = new List<BVH_GO>();
            _bvhs = new List<BvhAsset>();
            
            _goInfos = new List<GoInfo>();
        }

        private void Start()
        {
            foreach (var go in gameObject.GetComponentsInChildren<BVH_GO>())
            {
                _gos.Add(go);
                _bvhs.Add(go.bvhAsset);
                GoInfo goInfo;
                goInfo.Pos = (float3) go.transform.position;
                goInfo.Min = (float3)go.transform.position + go.Center_Offset - 0.5f * go.Size;
                goInfo.Max = (float3)go.transform.position + go.Center_Offset + 0.5f * go.Size;
                goInfo.BvhDatasStart = totalBvhDatas.Count;
                goInfo.BvhDatasCount = go.bvhAsset.bvhDatas.Count;
                goInfo.TrianglesStart = totalTriangles.Count;
                goInfo.TrianglesCount = go.bvhAsset.triangles.Count;
                _goInfos.Add(goInfo);
                totalBvhDatas.AddRange(go.bvhAsset.bvhDatas);
                totalTriangles.AddRange(go.bvhAsset.triangles);
                
                _renderTextures.Add(go.GetComponent<MeshRenderer>().sharedMaterial.mainTexture);
            }

            CreateBuffers();
            Mirror.SetBuffer(bvhBuffer ,triangleBuffer ,goInfosBuffer);
        }
        
        
        void CreateBuffers()
        {
            bvhBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalBvhDatas.Count, Marshal.SizeOf<BvhData>());
            bvhBuffer.SetData(totalBvhDatas);

            triangleBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalTriangles.Count, Marshal.SizeOf<Triangle>());
            triangleBuffer.SetData(totalTriangles);
            
            goInfosBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _goInfos.Count, Marshal.SizeOf<GoInfo>());
            goInfosBuffer.SetData(_goInfos);
        }

        private void Update()
        {
            for (int i = 0; i < _gos.Count; i++)
            {
                GoInfo goInfo = _goInfos[i];
                goInfo.Pos = (float3)_gos[i].transform.position;
                goInfo.Min = (float3)_gos[i].transform.position + _gos[i].Center_Offset - 0.5f * _gos[i].Size;
                goInfo.Max = (float3)_gos[i].transform.position + _gos[i].Center_Offset + 0.5f * _gos[i].Size;
                _goInfos[i] = goInfo;
            }
            goInfosBuffer.SetData(_goInfos);
            Mirror.SetBuffer(bvhBuffer ,triangleBuffer ,goInfosBuffer);
        }
    }
}