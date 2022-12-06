using System;
using UnityEngine;

public class DebugRenderTexture : MonoBehaviour
{
    public static DebugRenderTexture Instance;

    private Material _material;
    private void Awake()
    {
        Instance = this;
        _material = GetComponent<MeshRenderer>().sharedMaterial;
    }

    public void SetRenderTexture(RenderTexture rt)
    {
        _material.mainTexture = rt;
    }
    
}