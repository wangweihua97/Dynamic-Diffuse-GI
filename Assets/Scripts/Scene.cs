using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using ComputeShaderBvhMeshHit;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace MyDDGI
{
    public class Scene : MonoBehaviour
    {
        public static Scene Instance;
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

        private List<Material> _materials;
        
        GraphicsBuffer bvhBuffer;
        GraphicsBuffer triangleBuffer;
        GraphicsBuffer goInfosBuffer;

        public RenderTexture BaseColorTextureArray;

        public Mirror Mirror;

        void Awake()
        {
            Instance = this;
            totalBvhDatas = new List<BvhData>();
            totalTriangles = new List<Triangle>();
            
            _renderTextures = new List<Texture>();
            _materials = new List<Material>();
            
            _gos = new List<BVH_GO>();
            _bvhs = new List<BvhAsset>();
            
            _goInfos = new List<GoInfo>();
        }

        private void Start()
        {
            List<int> ids = new List<int>();
            foreach (var go in gameObject.GetComponentsInChildren<BVH_GO>())
            {
                var meshRenderer = go.GetComponent<MeshRenderer>();
                SetMaterialIds(meshRenderer.sharedMaterials ,ids);
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
                totalTriangles.AddRange(SetTrianglesMatId(ids, go.bvhAsset.triangles));
                
            }

            CreateBuffers();
            CreatBaseColorTextureArray();
            //Mirror.SetBuffer(bvhBuffer ,triangleBuffer ,goInfosBuffer);
        }

        List<Triangle>  SetTrianglesMatId(List<int> ids, List<Triangle> triangles)
        {
            List<Triangle> newTriangles = new List<Triangle>();
            for (int i = 0; i < triangles.Count; i++)
            {
                var triangle = triangles[i];
                triangle.MaterialIndex = ids[triangle.MaterialIndex];
                newTriangles.Add(triangle);
            }

            return newTriangles;
        }

        void SetMaterialIds(Material[] materials ,List<int> list)
        {
            list.Clear();
            for (int i = 0; i < materials.Length; i++)
            {
                bool isContain = false;
                for (int j = 0; j < _materials.Count; j++)
                {
                    if (materials[i] == _materials[j])
                    {
                        isContain = true;
                        list.Add(j);
                        break;
                    }
                }

                if (!isContain)
                {
                    
                    _materials.Add(materials[i]);
                    list.Add(_materials.Count -1);
                }
            }
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

        void CreatBaseColorTextureArray()
        {
            RenderTextureDescriptor d = new RenderTextureDescriptor(1024, 1024, RenderTextureFormat.ARGB32);
            d.dimension = TextureDimension.Tex2DArray;
            d.volumeDepth = _materials.Count; // We will have 2 slices (I.E. 2 textures) in our texture array.

            BaseColorTextureArray = new RenderTexture(d);
            BaseColorTextureArray.Create();
            BaseColorTextureArray.name = "BaseColorTextureArray";

            for (int i = 0; i <_materials.Count; i++)
            {
                RenderTexture rt = RenderTexture.GetTemporary(1024, 1024);
                Graphics.Blit(_materials[i].mainTexture ,rt);
                Graphics.CopyTexture(rt, 0,
                    BaseColorTextureArray, i);
            }
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