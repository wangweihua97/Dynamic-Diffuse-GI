using System;
using ComputeShaderBvhMeshHit;
using Unity.Mathematics;
using UnityEngine;

namespace MyDDGI
{
    public class BVH_GO : MonoBehaviour
    {
        public BvhAsset bvhAsset;
        public int gizmoDepth;
        public bool gizmoOnlyLeafNode;
        public float3 Box_Min_Offset;
        public float3 Box_Max_Offset;

        [HideInInspector]
        public float3 Center_Offset;
        [HideInInspector]
        public float3 Size;

        private void Awake()
        {
            Center_Offset = 0.5f * (Box_Max_Offset + Box_Min_Offset);
            Size = (Box_Max_Offset - Box_Min_Offset);
        }

        private void Update()
        {
            Center_Offset = 0.5f * (Box_Max_Offset + Box_Min_Offset);
            Size = (Box_Max_Offset - Box_Min_Offset);
        }

        private void OnDrawGizmos()
        {
            Center_Offset = 0.5f * (Box_Max_Offset + Box_Min_Offset);
            Size = (Box_Max_Offset - Box_Min_Offset);
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(transform.position + (Vector3)Center_Offset, Size);
            
            if (bvhAsset != null) bvhAsset.DrwaGizmo(gizmoDepth, gizmoOnlyLeafNode);
        }
    }
}