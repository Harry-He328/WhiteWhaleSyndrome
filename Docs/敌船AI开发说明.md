# 敌船 AI

直接运行现有 SampleScene，沿用既有敌船预制体 PrototypeEnemyShip。没有创建新场景、房间物件或 UI 面板；右上角已有战斗说明更新为当前行为。

## 写法与分工

参考 Dicy 的 Boss / BossAI 分工，使用中文 Header 分组、camelCase 字段、明确状态枚举、region 分区与手写 IEnumerator 顺序组合。没有引入行为树或 AI 编辑器。

- EnemyActor：敌人实体和战斗引用，统一在 FixedUpdate 推进刚体与波高跟随。
- EnemyShipActions：基础动作 MoveToPlayer、TurnBroadsideToPlayer、FireBroadside、Wait。动作不启动下一招，不负责挑选组合。
- EnemyShipAI：唯一主协程 CombatLoop，按条件执行 CombatSequence1。组合直接按顺序 yield return 动作，不单独启动动作子协程。

## 当前行为

准备等待 → 接近玩家 → 转向使一侧炮口对准玩家 → 开炮 → 等待 → 重新判断。

准备时间沿用 BattleDefinition.firstShotDelay（默认 4 秒），轮次等待沿用 shotInterval（默认 3 秒）。选取需要转向最少的已安装侧舷炮，调整整艘船朝向；没有可独立旋转的瞄准炮塔。只击发对准玩家的一舷，仍使用现有 Cannon 和真实炮弹。

玩家距离超过追赶阈值时，等待提前结束，重新接近。每一轮重新判断距离和朝向；射击前再次检查距离、角度、战斗状态与目标是否存活。F6 只控制开炮，不停止敌人移动。

## 可调参数

既有敌船预制体上的 EnemyShipActions：

- moveSpeed：4 海洋单位/秒。
- turnSpeed：90 度/秒。
- attackDistance：8 海洋单位，接近动作的停止距离。
- followDistance：11 海洋单位，超过后重新追赶，也作为开炮距离上限。两个距离留出缓冲，减少反复启停。
- approachTimeout：8 秒，单次追赶的重新判断上限。
- turnTimeout：3 秒。
- fireAngle：6 度，炮口允许的水平误差。

移动实时读取 MovementSpeedModifier，网子期间由 4 降为 2 单位/秒，到期恢复。海面高度跟随和船身转向不乘此倍率。移动使用刚体 MovePosition / MoveRotation，并用 SweepTest 限制本步前进，避免直接穿过前方碰撞体；当前没有绕礁寻路、撤退或群体协作。

## 中断与清理

EnemyShipAI 只维护一个主协程。敌人死亡、玩家死亡、战斗停止、禁用和重试时统一 StopAI，停止主序列、清理移动请求、取消待执行的刚体移动并解除事件订阅。新一轮战斗创建新敌人并重新开始准备阶段，旧序列不会继续控制新敌人。

EnemyShipAI.CurrentState 与 SequenceCount、EnemyShipActions.CurrentSpeed 与 FiredCount 供调试观察。

## 验证

现有场景进入 Play 后执行 EnemyShipAIVerification.Begin()。验证临时调整运行时船位置以隔离岛礁，不保存场景；检查追赶、网子速度倍率、停止与侧舷对准、实际炮弹伤害、玩家驶远后重新追赶、失败后的动作停止、重试唯一性及敌人死亡结算。

结果：Captures/EnemyShipAIVerification.txt。验证结束后退出 Play 恢复场景原来的状态。
