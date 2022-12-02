﻿/*
Ray generation shader for the dynamic irradiance probes.
Uses helpers from the G3D innovation engine (http://g3d.sf.net)
*/
#include "GridHelpers.glsl"
#expect RAYS_PER_PROBE "int"

uniform float3x3        randomOrientation;
uniform IrradianceField irradianceFieldSurface;

out float4              rayOrigin;
out float4              rayDirection;

Vector3int32            probeCounts;
Point3                  probeStartPosition;
Vector3                 probeStep;


/**  Generate a spherical fibonacci point

    http://lgdv.cs.fau.de/publications/publication/Pub.2015.tech.IMMD.IMMD9.spheri/

    To generate a nearly uniform point distribution on the unit sphere of size N, do
    for (float i = 0.0; i < N; i += 1.0) {
        float3 point = sphericalFibonacci(i,N);
    }

    The points go from z = +1 down to z = -1 in a spiral. To generate samples on the +z hemisphere,
    just stop before i > N/2.

*/
Vector3 sphericalFibonacci(float i, float n) {
    const float PHI = sqrt(5) * 0.5 + 0.5;
#   define madfrac(A, B) ((A)*(B)-floor((A)*(B)))
    float phi = 2.0 * pi * madfrac(i, PHI - 1);
    float cosTheta = 1.0 - (2.0 * i + 1.0) * (1.0 / n);
    float sinTheta = sqrt(saturate(1.0 - cosTheta * cosTheta));

    return Vector3(
        cos(phi) * sinTheta,
        sin(phi) * sinTheta,
        cosTheta);

#   undef madfrac
}

/*
    Compute 3D worldspace position from gridcoord based on starting
    location and distance between probes.
*/
Point3 gridCoordToPosition(ivec3 c) {
    return probeStep * Vector3(c) + probeStartPosition;
}

/*
    Compute the grid coordinate of the probe from the index
*/
ivec3 probeIndexToGridCoord(int index) {
    // Slow, but works for any # of probes
    ivec3 iPos;
    iPos.x = index % L.probeCounts.x;
    iPos.y = (index % (L.probeCounts.x * L.probeCounts.y)) / L.probeCounts.x;
    iPos.z = index / (L.probeCounts.x * L.probeCounts.y);

    // Assumes probeCounts are powers of two.
    // Saves ~10ms compared to the divisions above
    // Precomputing the MSB actually slows this code down substantially
    /*
    ivec3 iPos;
    iPos.x = index & (L.probeCounts.x - 1);
    iPos.y = (index & ((L.probeCounts.x * L.probeCounts.y) - 1)) >> findMSB(L.probeCounts.x);
    iPos.z = index >> findMSB(L.probeCounts.x * L.probeCounts.y);*/

    return iPos;
}

/*
    Compute the 3D probe location in world space from the probe index
*/
Point3 probeLocation(int index) {
    return gridCoordToPosition(L, probeIndexToGridCoord(L, index));
}

void main() {
    ivec2 pixelCoord = ivec2(gl_FragCoord.xy);
    
    int probeID = pixelCoord.y;
    int rayID   = pixelCoord.x;
    
    // This value should be on the order of the normal bias.
    const float rayMinDistance = 0.08;

    rayOrigin = float4(probeLocation(probeID), rayMinDistance);
    rayDirection= float4(randomOrientation * sphericalFibonacci(rayID, RAYS_PER_PROBE), inf);
}
