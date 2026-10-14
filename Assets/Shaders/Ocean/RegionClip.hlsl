#ifndef WHITEWHALE_OCEAN_REGION_CLIP_INCLUDED
#define WHITEWHALE_OCEAN_REGION_CLIP_INCLUDED

// 海洋显示区域的"一刀切"。
//
// 把显示区域想成一块水平矩形，四个角各立一把无限高的刀。矩形之外的部分被竖直切掉，
// 所以被切开的物体在边界上呈现为一个竖直的切面——没有碰撞体（纯视觉运算），
// 也不受物体高度限制（刀无限高，只有水平位置参与判定）。
//
// 每帧由 OceanRegion.cs 推送下面四个全局量，不放在 UnityPerMaterial 里：
// 它们是"整张地图共用一把刀"，不是每个材质的私有属性。
//
//   _OceanRegionCenter   xyz = 区域中心（世界坐标）
//   _OceanRegionAxisX    xyz = 右半轴向量：方向=区域右方，长度=半宽
//   _OceanRegionAxisZ    xyz = 前向半轴向量：方向=区域前方，长度=半长
//   _OceanRegionClip     0 = 关闭裁剪（在 Scene 视图里看整张地图用）
//
// 代价：全局变量放在 CBUFFER 外面会让这个着色器失去 SRP Batcher 兼容性。
// 这是有意接受的——区域只有一个真值来源，比放进每个材质的 CBUFFER 里各存一份要可靠。
float4 _OceanRegionCenter;
float4 _OceanRegionAxisX;
float4 _OceanRegionAxisZ;
float  _OceanRegionClip;

// 在片元着色器里调用。传世界坐标。
void ApplyOceanRegionClip(float3 positionWS)
{
    if (_OceanRegionClip < 0.5)
        return;

    float2 axisSq = float2(dot(_OceanRegionAxisX.xyz, _OceanRegionAxisX.xyz),
                           dot(_OceanRegionAxisZ.xyz, _OceanRegionAxisZ.xyz));

    // 半轴长度为 0 时无法判断内外，宁可整块不裁也不要全裁掉
    if (axisSq.x < 1e-8 || axisSq.y < 1e-8)
        return;

    float3 d = positionWS - _OceanRegionCenter.xyz;

    // 除以 |轴|² 等于按半轴长度归一化：t 就是"在半轴上的比例"，|t| <= 1 即在区域内
    float2 t = float2(dot(d, _OceanRegionAxisX.xyz) / axisSq.x,
                      dot(d, _OceanRegionAxisZ.xyz) / axisSq.y);

    if (any(abs(t) > 1.0))
        discard;
}

#endif // WHITEWHALE_OCEAN_REGION_CLIP_INCLUDED
