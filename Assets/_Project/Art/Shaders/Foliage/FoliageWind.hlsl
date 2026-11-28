// Foliage wind (shared by every foliage pass so shadows move with the plant).
// Vertex colors carry the weights, painted by the Blender plant generator:
//   R = main bend (0 at the base, 1 at the tip),  G = branch sway (and phase),  B = leaf flutter.
// Globals from WindManager: direction, strength, gust noise sweeping across the world, and up to 8 pushers
// (the player and NPCs) that bend plants away from them.
#ifndef ALCAX_FOLIAGE_WIND
#define ALCAX_FOLIAGE_WIND

float4 _WindDirStrength;      // xyz direction (normalized, horizontal), w strength 0..2
float4 _WindGust;             // x gust scale (1/m), y gust speed (m/s), z gust strength, w time
float4 _WindPushers[8];       // xyz position, w radius
float _WindPusherCount;

float WindHash(float2 p) { return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }
float WindNoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    float2 u = f * f * (3 - 2 * f);
    return lerp(lerp(WindHash(i), WindHash(i + float2(1, 0)), u.x), lerp(WindHash(i + float2(0, 1)), WindHash(i + float2(1, 1)), u.x), u.y);
}

// positionWS: the vertex; pivotWS: the plant's origin; color: the weights; stiffness: per material
float3 ApplyFoliageWind(float3 positionWS, float3 pivotWS, float4 color, float stiffness, float flutterAmount)
{
    float t = _WindGust.w;
    float3 dir = _WindDirStrength.xyz;
    float strength = _WindDirStrength.w / max(stiffness, 0.05);
    // Gusts: a noise field blown along the wind, so waves of wind roll across the ground
    float2 gp = (pivotWS.xz - dir.xz * t * _WindGust.y) * _WindGust.x;
    float gust = WindNoise(gp) * 0.7 + WindNoise(gp * 2.7 + 13.1) * 0.3;
    gust = 1 + (gust - 0.4) * 2 * _WindGust.z;
    float phase = WindHash(pivotWS.xz) * 6.2831;

    float bendW = color.r * color.r;
    float height = max(positionWS.y - pivotWS.y, 0.0);
    // Main bend: the whole plant leans with the wind and sways around that lean
    float sway = sin(t * 1.3 + phase) * 0.25 + sin(t * 2.1 + phase * 1.7) * 0.12;
    float lean = strength * gust * (0.6 + sway);
    float3 offset = dir * lean * bendW * 0.18;
    // Branches: a faster sway out of phase, along and across the wind
    float bp = phase + color.g * 9.0;
    float3 across = float3(-dir.z, 0, dir.x);
    offset += (dir * sin(t * 3.1 + bp) + across * cos(t * 2.6 + bp)) * color.g * 0.025 * strength * gust;
    // Leaves: quick flutter, mostly vertical
    float fl = sin(t * 11.0 + bp * 3.0 + positionWS.x * 7.0) * cos(t * 9.0 + positionWS.z * 6.0);
    offset += float3(0.3, 1, 0.3) * fl * color.b * 0.012 * flutterAmount * (0.4 + strength * gust);

    // Pushers: plants bend away from bodies passing through them
    for (int i = 0; i < 8; i++)
    {
        if (i >= (int)_WindPusherCount) break;
        float3 d = pivotWS - _WindPushers[i].xyz;
        d.y = 0;
        float r = _WindPushers[i].w;
        float k = saturate(1 - length(d) / max(r, 0.01));
        offset += normalize(d + float3(1e-4, 0, 0)) * k * k * bendW * 0.45 * (height + 0.2);
    }

    // Keep the length roughly constant: what bends sideways comes down a little
    float side = length(offset.xz);
    offset.y -= side * side * 0.5 / max(height + 0.05, 0.05) * bendW;
    return positionWS + offset;
}

#endif
