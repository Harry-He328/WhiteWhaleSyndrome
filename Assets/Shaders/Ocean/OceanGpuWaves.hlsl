#ifndef WHITEWHALE_OCEAN_GPU_WAVES
#define WHITEWHALE_OCEAN_GPU_WAVES

float _OceanWaveCount;
float4 _OceanWaveX[32]; // direction XZ, wave number, phase
float4 _OceanWaveZ[32]; // vertical and horizontal amplitudes
float4 _OceanGroup;     // wave vector XZ, phase, contrast
float4 _OceanPatchOffset;

float3 OceanDisplace(float2 oceanXZ, out float3 tangentX, out float3 tangentZ)
{
    float envelopePhase = dot(_OceanGroup.xy, oceanXZ) + _OceanGroup.z;
    float envelope = 1.0 - _OceanGroup.w + _OceanGroup.w * cos(envelopePhase);
    float2 envelopeGradient = -_OceanGroup.w * sin(envelopePhase) * _OceanGroup.xy;
    float3 displacement = 0.0;
    float3 derivativeX = 0.0;
    float3 derivativeZ = 0.0;
    [loop]
    for (int wave = 0; wave < (int)_OceanWaveCount; wave++)
    {
        float4 shape = _OceanWaveX[wave];
        float2 amplitude = _OceanWaveZ[wave].xy;
        float2 waveVector = shape.xy * shape.z;
        float phase = dot(waveVector, oceanXZ) + shape.w;
        float waveSin, waveCos;
        sincos(phase, waveSin, waveCos);
        float horizontal = amplitude.y * waveCos;
        displacement += float3(shape.x * horizontal, amplitude.x * waveSin, shape.y * horizontal);
        float horizontalDerivative = -amplitude.y * waveSin;
        float3 derivative = float3(shape.x * horizontalDerivative, amplitude.x * waveCos, shape.y * horizontalDerivative);
        derivativeX += derivative * waveVector.x;
        derivativeZ += derivative * waveVector.y;
    }
    tangentX = float3(1, 0, 0) + derivativeX * envelope + displacement * envelopeGradient.x;
    tangentZ = float3(0, 0, 1) + derivativeZ * envelope + displacement * envelopeGradient.y;
    return displacement * envelope;
}

#endif
