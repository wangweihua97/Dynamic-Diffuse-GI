using System;
using UnityEngine;

public class DebugRenderTexture : MonoBehaviour
{
    public static DebugRenderTexture Instance;

    public Material RayHitColorMaterial;
    public Material ProbesIrradianceMaterial;
    public Material DirectRenderMaterial;
    private void Awake()
    {
        Instance = this;
    }

    public void SetRayHitColorRenderTexture(RenderTexture rt)
    {
        RayHitColorMaterial.mainTexture = rt;
    }
    
    public void SetProbesIrradianceRenderTexture(RenderTexture rt)
    {
        ProbesIrradianceMaterial.mainTexture = rt;
    }
    
    public void SetDirectRenderMaterialTexture(RenderTexture rt)
    {
        DirectRenderMaterial.mainTexture = rt;
    }
    
}