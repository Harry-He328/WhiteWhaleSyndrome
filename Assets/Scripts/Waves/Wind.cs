using UnityEngine;

/// <summary>
/// 风力系统（单例）
/// 影响全局船只和炮弹的速度
/// </summary>
public class Wind : MonoBehaviour
{
    public static Wind instance;
    
    [Header("风力设置")]
    [SerializeField] private WindLevel windLevel = WindLevel.None;
    [SerializeField] private WindDirection windDirection = WindDirection.North;
    
    [Header("船只速度修正系数（百分比，正值加速，负值减速）")]
    [SerializeField] private float shipLevel1SpeedModifier = 0.1f;  // 1级风力对船只：10%加速
    [SerializeField] private float shipLevel2SpeedModifier = 0.2f;  // 2级风力对船只：20%加速
    [SerializeField] private float shipLevel3SpeedModifier = 0.3f;  // 3级风力对船只：30%加速
    
    [Header("炮弹速度修正系数（百分比，正值加速，负值减速）")]
    [SerializeField] private float shellLevel1SpeedModifier = 0.1f;  // 1级风力对炮弹：10%加速
    [SerializeField] private float shellLevel2SpeedModifier = 0.2f;  // 2级风力对炮弹：20%加速
    [SerializeField] private float shellLevel3SpeedModifier = 0.3f;  // 3级风力对炮弹：30%加速
    
    // 公共属性
    public WindLevel CurrentWindLevel => windLevel;
    public WindDirection CurrentWindDirection => windDirection;
    
    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            instance = this;
        }

        Debug.Log($"[Wind] 游戏开始时风力: {windLevel}, 风向: {windDirection}");
    }
    
    /// <summary>
    /// 获取当前风向的向量（归一化）
    /// </summary>
    public Vector3 GetWindDirectionVector()
    {
        float angle = (int)windDirection * 45f; // 每个方向45度
        float angleRad = angle * Mathf.Deg2Rad;
        
        // Unity坐标系：X右，Z前
        // 0度（北）= Z轴正方向
        Vector3 direction = new Vector3(
            Mathf.Sin(angleRad),
            0,
            Mathf.Cos(angleRad)
        ).normalized;
        
        return direction;
    }
    
    /// <summary>
    /// 获取当前风力等级对船只的速度修正系数
    /// </summary>
    private float GetShipWindSpeedModifier()
    {
        switch (windLevel)
        {
            case WindLevel.Level1:
                return shipLevel1SpeedModifier;
            case WindLevel.Level2:
                return shipLevel2SpeedModifier;
            case WindLevel.Level3:
                return shipLevel3SpeedModifier;
            default:
                return 0f;
        }
    }
    
    /// <summary>
    /// 获取当前风力等级对炮弹的速度修正系数
    /// </summary>
    private float GetShellWindSpeedModifier()
    {
        switch (windLevel)
        {
            case WindLevel.Level1:
                return shellLevel1SpeedModifier;
            case WindLevel.Level2:
                return shellLevel2SpeedModifier;
            case WindLevel.Level3:
                return shellLevel3SpeedModifier;
            default:
                return 0f;
        }
    }
    
    /// <summary>
    /// 计算速度修正系数（基于移动方向与风向的夹角）
    /// </summary>
    /// <param name="moveDirection">移动方向（归一化向量）</param>
    /// <param name="isShip">true=船只，false=炮弹</param>
    /// <returns>速度修正系数（1.0 = 无影响，1.1 = 10%加速，0.9 = 10%减速）</returns>
    private float GetSpeedModifier(Vector3 moveDirection, bool isShip)
    {
        if (windLevel == WindLevel.None) return 1f;
        
        Vector3 windDir = GetWindDirectionVector();
        Vector3 moveDir2D = new Vector3(moveDirection.x, 0, moveDirection.z).normalized;
        
        // 使用点积计算夹角（cos值）
        // 1.0 = 完全重合（0度），0.0 = 完全垂直（90度），-1.0 = 完全相反（180度）
        float dotProduct = Vector3.Dot(windDir, moveDir2D);
        
        // 获取对应的风力修正系数
        float windModifier = isShip ? GetShipWindSpeedModifier() : GetShellWindSpeedModifier();
        
        // 计算最终速度修正：1.0 + (dotProduct * modifier)
        // 完全重合：1.0 + (1.0 * 0.2) = 1.2（20%加速）
        // 完全垂直：1.0 + (0.0 * 0.2) = 1.0（无影响）
        // 完全相反：1.0 + (-1.0 * 0.2) = 0.8（20%减速）
        float speedModifier = 1f + (dotProduct * windModifier);
        
        return speedModifier;
    }
    
    /// <summary>
    /// 应用风力影响后的船只速度
    /// </summary>
    /// <param name="originalSpeed">原始速度</param>
    /// <param name="moveDirection">移动方向</param>
    /// <returns>修正后的速度</returns>
    public float ApplyWindEffectToShip(float originalSpeed, Vector3 moveDirection)
    {
        float modifier = GetSpeedModifier(moveDirection, true);
        return originalSpeed * modifier;
    }
    
    /// <summary>
    /// 应用风力影响后的炮弹速度
    /// </summary>
    /// <param name="originalSpeed">原始速度</param>
    /// <param name="moveDirection">移动方向</param>
    /// <returns>修正后的速度</returns>
    public float ApplyWindEffectToShell(float originalSpeed, Vector3 moveDirection)
    {
        float modifier = GetSpeedModifier(moveDirection, false);
        return originalSpeed * modifier;
    }
}

/// <summary>
/// 风力等级
/// </summary>
public enum WindLevel
{
    None = 0,   // 无风
    Level1 = 1, // 1级风力
    Level2 = 2, // 2级风力
    Level3 = 3  // 3级风力
}

/// <summary>
/// 风向（8个指南针方向）
/// </summary>
public enum WindDirection
{
    North = 0,      // 北（0°）
    Northeast = 1,  // 东北（45°）
    East = 2,       // 东（90°）
    Southeast = 3,  // 东南（135°）
    South = 4,      // 南（180°）
    Southwest = 5,  // 西南（225°）
    West = 6,       // 西（270°）
    Northwest = 7   // 西北（315°）
}
