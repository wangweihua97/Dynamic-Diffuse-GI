#ifndef BVH_INCLUCDE
#define BVH_INCLUCDE
         
#define BVH_STACK_SIZE 32
#define BVH_FLT_MAX 3.402823466e+38f

struct BvhData 
{
    float3 min;
    float3 max;

    int leftIdx;
    int rightIdx;

    int triangleIdx; // -1 if data is not leaf
    int triangleCount;
};

struct BvhTriangle 
{
    float3 pos0;
    float3 pos1;
    float3 pos2;
    float3 normal;
    float2 uv0;
    float2 uv1;
    float2 uv2;
    int MaterialIndex;
};

struct GoInfo
{
    float3 Min;
    float3 Max;
    float3 Pos;
    int BvhDatasStart;
    int BvhDatasCount;
    int TrianglesStart;
    int TrianglesCount;
};



StructuredBuffer<BvhData> bvhBuffer;
StructuredBuffer<BvhTriangle> triangleBuffer;
StructuredBuffer<GoInfo> goInfoBuffer;

inline float determinant(float3 v0, float3 v1, float3 v2)
{
    return determinant(float3x3(
        v0.x, v1.x, v2.x,
        v0.y, v1.y, v2.y,
        v0.z, v1.z, v2.z
    ));
}

void GetHitUV(BvhTriangle tri,float3 hitPos, out float2 uv)
{
    float3 p0 = tri.pos0 - hitPos;
    float3 p1 = tri.pos1 - hitPos;
    float3 p2 = tri.pos2 - hitPos;
    
    float total = length(cross(tri.pos1 - tri.pos0, tri.pos2 - tri.pos0));
    
    float w0 = length(cross(p1 ,p2)) / total;
    float w1 = length(cross(p0 ,p2)) / total;
    float w2 = length(cross(p0 ,p1)) / total;
    
    uv = w0 * tri.uv0 + w1 * tri.uv1 + w2 * tri.uv2;
}


// Line triangle
// https://shikousakugo.wordpress.com/2012/06/27/ray-intersection-2/
inline bool LineTriangleIntersection(BvhTriangle tri, float3 origin, float3 rayStep, out float rayScale)
{
    rayScale = BVH_FLT_MAX;

    float3 normal = tri.normal;
    float dirDot = dot(normal, rayStep);
    if ( dirDot > 0 ) return false;

    float3 origin_from_pos0 = origin - tri.pos0;
    if(dot(origin_from_pos0, normal) < 0 ) return false;

    float3 rayStep_end_from_pos0 = origin_from_pos0 + rayStep;
    if(dot(rayStep_end_from_pos0, normal) > 0 ) return false;

    float3 edge0 = tri.pos1 - tri.pos0;
    float3 edge1 = tri.pos2 - tri.pos0;

    const float float_epsilon = 0.001;

    float d = determinant(edge0, edge1, -rayStep);
    if ( d> float_epsilon)
    {
        float dInv = 1.0 / d;
        float u = determinant(origin_from_pos0, edge1, -rayStep) * dInv;
        float v = determinant(edge0, origin_from_pos0, -rayStep) * dInv;

        if ( 0<=u && u<=1 && 0<=v && (u+v)<=1)
        {
            float t = determinant(edge0, edge1, origin_from_pos0) * dInv;
            if ( t > 0 )
            {
                rayScale = t;
                return true;
            }
        }
    }

    return false;
}

bool LineTriangleIntersectionAll(float3 origin, float3 rayStep, out float rayScale, out float3 normal)
{
    uint num, stride;
    triangleBuffer.GetDimensions(num, stride);

    rayScale = BVH_FLT_MAX;
    for(uint i=0; i<num; ++i)
    {
        BvhTriangle tri = triangleBuffer[i];

        float tmpRayScale;
        if (LineTriangleIntersection(tri, origin, rayStep, tmpRayScale))
        {
            if ( tmpRayScale < rayScale)
            {
                rayScale = tmpRayScale;
                normal = tri.normal;
            }
        }
    }

    return rayScale != BVH_FLT_MAX;
}

// Line AABB
// http://marupeke296.com/COL_3D_No18_LineAndAABB.html
bool LineAABBIntersection(float3 origin, float3 rayStep, BvhData data)
{
    float3 aabbMin = data.min;
    float3 aabbMax = data.max;

    float tNear = -BVH_FLT_MAX;
    float tFar  =  BVH_FLT_MAX;

    for(int axis = 0; axis<3; ++axis)
    {
        float rayOnAxis = rayStep[axis];
        float originOnAxis = origin[axis];
        float minOnAxis = aabbMin[axis];
        float maxOnAxis = aabbMax[axis];
        if(rayOnAxis == 0)
        {
            if ( originOnAxis < minOnAxis || maxOnAxis < originOnAxis ) return false;
        }
        else
        {
            float rayOnAxisInv = 1.0 / rayOnAxis;
            float t0 = (minOnAxis - originOnAxis) * rayOnAxisInv;
            float t1 = (maxOnAxis - originOnAxis) * rayOnAxisInv;

            float tMin = min(t0, t1);
            float tMax = max(t0, t1);

            tNear = max(tNear, tMin);
            tFar  = min(tFar, tMax);

            if (tFar < 0.0 || tFar < tNear || 1.0 < tNear) return false;
        }
    }
    return true;
}

bool LineAABBIntersection_WithOut_tNear(float3 origin, float3 rayStep, GoInfo data ,out float tNear)
{
    float3 aabbMin = data.Min;
    float3 aabbMax = data.Max;

    tNear = -BVH_FLT_MAX;
    float tFar  =  BVH_FLT_MAX;

    for(int axis = 0; axis<3; ++axis)
    {
        float rayOnAxis = rayStep[axis];
        float originOnAxis = origin[axis];
        float minOnAxis = aabbMin[axis];
        float maxOnAxis = aabbMax[axis];
        if(rayOnAxis == 0)
        {
            if ( originOnAxis < minOnAxis || maxOnAxis < originOnAxis ) return false;
        }
        else
        {
            float rayOnAxisInv = 1.0 / rayOnAxis;
            float t0 = (minOnAxis - originOnAxis) * rayOnAxisInv;
            float t1 = (maxOnAxis - originOnAxis) * rayOnAxisInv;

            float tMin = min(t0, t1);
            float tMax = max(t0, t1);

            tNear = max(tNear, tMin);
            tFar  = min(tFar, tMax);

            if (tFar < 0.0 || tFar < tNear || 1.0 < tNear) return false;
        }
    }
    return true;
}

// Line Bvh
// http://raytracey.blogspot.com/2016/01/gpu-path-tracing-tutorial-3-take-your.html
bool TraverseBvh(float3 origin, float3 rayStep, out float rayScale, out float3 normal)
{
    int stack[BVH_STACK_SIZE];

    int stackIdx = 0;
    stack[stackIdx++] = 0;

    rayScale = BVH_FLT_MAX;

    while(stackIdx)
    {
        stackIdx--;
        int BvhIdx = stack[stackIdx];
        BvhData data = bvhBuffer[BvhIdx];

        if ( LineAABBIntersection(origin, rayStep, data) )
         {
            // Branch node
            if (data.triangleIdx < 0)
            {
                if ( stackIdx+1 >= BVH_STACK_SIZE) return false;

                stack[stackIdx++] = data.leftIdx;
                stack[stackIdx++] = data.rightIdx;
            }
            // Leaf node
            else
            {
                for(int i=0; i<data.triangleCount; ++i)
                {
                    BvhTriangle tri = triangleBuffer[i + data.triangleIdx];

                    float tmpRayScale;
                    if (LineTriangleIntersection(tri, origin, rayStep, tmpRayScale))
                    {
                        if (tmpRayScale < rayScale)
                        {
                            rayScale = tmpRayScale;
                            normal = tri.normal;
                        }
                    }
                }
            }
        }
    }

    return rayScale != BVH_FLT_MAX;
}

bool TraverseBvh_GoInfo(GoInfo goInfo,float3 origin, float3 rayStep, out float rayScale, out float3 normal ,out float2 uv ,out int matIndex)
{
    int stack[BVH_STACK_SIZE];
    int stackIdx = 0;
    stack[stackIdx++] = 0;
    matIndex = -1;
    rayScale = BVH_FLT_MAX;
    origin = origin - goInfo.Pos;

    while(stackIdx)
    {
        stackIdx--;
        int BvhIdx = stack[stackIdx];
        BvhData data = bvhBuffer[BvhIdx + goInfo.BvhDatasStart];

        if ( LineAABBIntersection(origin, rayStep, data) )
         {
            // Branch node
            if (data.triangleIdx < 0)
            {
                if ( stackIdx+1 >= BVH_STACK_SIZE) return false;

                stack[stackIdx++] = data.leftIdx;
                stack[stackIdx++] = data.rightIdx;
            }
            // Leaf node
            else
            {
                for(int i=0; i<data.triangleCount; ++i)
                {
                    BvhTriangle tri = triangleBuffer[goInfo.TrianglesStart + i + data.triangleIdx];

                    float tmpRayScale;
                    if (LineTriangleIntersection(tri, origin, rayStep, tmpRayScale))
                    {
                        if (tmpRayScale < rayScale)
                        {
                            rayScale = tmpRayScale;
                            normal = tri.normal;
                            normal = dot(normal ,rayStep) > 0 ? normal : -normal;
                            GetHitUV(tri ,origin + rayStep * rayScale ,uv);
                            matIndex = tri.MaterialIndex;
                            
                        }
                    }
                }
            }
        }
    }

    return rayScale != BVH_FLT_MAX;
}

bool TraverseBvh_(float3 origin, float3 rayStep, out float rayScale, out float3 normal ,out float2 uv ,out int matId)
{   
    matId = -1;
    uv = float2(0,0);
    rayScale = BVH_FLT_MAX;
    uint number = 0;
    uint stride = 0;
    goInfoBuffer.GetDimensions(number, stride);
    
    int count = 0;
    int indexs[999];
    int times[999];
    
    for(int i = 0; i < number; i++)
    {
       float tNear;
       if(LineAABBIntersection_WithOut_tNear(origin, rayStep, goInfoBuffer[i] ,tNear))
       {
           indexs[count] = i;
           times[count] = tNear;
           count++;
       }
    }
    
    if(count == 0)
        return false;
    
    for(int i = 0; i < count - 1 ;i++)
    {
        for(int j = 0; j<=i ;j++)
        {
             if(times[j] > times[j + 1])
             {
                 float temp_time = times[j];
                 int temp_index = indexs[j];
                 
                 times[j] = times[j + 1];
                 times[j + 1] = temp_time;
                 
                 indexs[j] = indexs[j + 1];
                 indexs[j + 1] = temp_index;
             }
        }
    }
    for(int i  = 0; i < count;i++)
    {
        if(rayScale < times[i])
            break;
        float temp_rayScale = BVH_FLT_MAX;
        float3 temp_normal;
        float2 temp_uv;
        int temp_matIndex;
        if(TraverseBvh_GoInfo(goInfoBuffer[indexs[i]],origin,rayStep,temp_rayScale, temp_normal,temp_uv,temp_matIndex))
        //if(TraverseBvh(origin,rayStep,temp_rayScale, temp_normal))
        {
            
            if(temp_rayScale < rayScale)
            {
                rayScale = temp_rayScale;
                normal = temp_normal;
                uv = temp_uv;
                matId = temp_matIndex;
            }
        }
    }
    return rayScale != BVH_FLT_MAX;
}

#endif // BVH_INCLUCDE