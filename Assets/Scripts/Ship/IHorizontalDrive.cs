using UnityEngine;

/// <summary>
/// 「这一帧要往水平方向挪多少」的来源。
///
/// 船的浮力脚本（Floater）要把水平位移和高度合成**同一次**位置写入——位置只能由
/// 一个地方写，两句 MovePosition 是互相覆盖的（原因见 Floater 里的注释）。
/// 所以它需要知道水平位移从哪来：玩家的船是 ShipMovement 算的，敌人的船是 EnemyShip 算的。
///
/// 这个接口就是那个交接点。谁算，谁实现，浮力脚本不关心具体是谁。
/// 世界空间，只有水平分量；竖直方向不归它管。
/// </summary>
public interface IHorizontalDrive
{
    Vector3 LastStep { get; }
}
