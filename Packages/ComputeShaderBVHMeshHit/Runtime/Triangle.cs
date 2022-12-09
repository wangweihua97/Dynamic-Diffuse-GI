using UnityEngine;
namespace ComputeShaderBvhMeshHit
{
    
    [System.Serializable]
    public struct Triangle
    {
        public Vector3 pos0;
        public Vector3 pos1;
        public Vector3 pos2;
        public Vector3 normal;
        
        public Vector2 uv0;
        public Vector2 uv1;
        public Vector2 uv2;
        
        public int MaterialIndex;
    }
}
