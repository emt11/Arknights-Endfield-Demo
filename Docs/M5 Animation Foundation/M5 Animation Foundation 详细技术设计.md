# M5 Animation Foundation 详细技术设计

**M5 设计基线 · 2026-11-25**　M4 已收尾，两个角色的动作步、实际水平速度与基础步态图可直接复用。第 5 节记录根运动闪避的资源条件，第 7 节保留旧动画原型的实测。

M5 负责把玩家移动和动作请求变成菲比、祀的最终骨骼姿态。每人使用独立的手动 PlayableGraph，以本角色动作时间驱动走跑混合、闪避和单槽攻击。M4 给出实际走跑速度与一次性 Dodge 命令。M5 实现队员闪避状态、根轨迹位移及同步姿态，M6 接管攻击许可、取消窗口与命中，M7 由 Combos 选择当前普攻连段的招式，末段为重击，并随连招接入伤害结算和跨帧输入缓存，并按连招需要扩展攻击之间的姿态过渡。[M4 操控详设](../M4%20Third%20Person%20Control/M4%20第三人称控制器、相机与基础移动%20FSM%20详细技术设计.md) · [总体路线](../仿终末地战斗系统设计方案.md)

## 1. 角色与资源基线

| 资源与装配 | 菲比 | 祀 |
| --- | --- | --- |
| CombatDemo 逻辑根 → 模型 Animator | `Characters/PlayerRoot` → `Phoebe` | `Characters/SiRoot` → `Si` |
| M3 模型与 M5 配置 | Humanoid `PhoebeAvatar`，沿用 Phoebe_Controller 并加入闪避姿态 | Humanoid `SiAvatar`，沿用 Si_Locomotion.overrideController |
| 待机、走、跑 | `GS_Idle / GS_Walk / GS_Run` | `DK_Idle / DK_Walk / DK_Run` |
| 走、跑的原生水平速度 | 1.43936、4.91111 m/s | 1.54665、6.82405 m/s |
| 单次闪避 | `GS_Evade`，1.7000 s | `DK_Evade`，约 1.4667 s |
| 验收用攻击片段 | `GS_Attack1 / GS_Attack2`，约 3.1333 / 2.8333 s | `DK_Attack1 / DK_Attack2`，约 1.5333 / 1.9000 s |

表中闪避片长已在第 5 节重新采样。其余片段时长、原生速度和循环标记取自用户提供的旧版设计，其数值曾在 Unity 6000.3.28f1 中读取。十二个片段均为 30 fps 的 Humanoid，待机、走、跑循环，两套闪避和攻击均单次播放。速度取 1 倍速下 `AnimationClip.averageSpeed` 在 XZ 平面的模长。运行时的片尾以 `clip.length` 为准。现有基础控制器以 `Locomotion` 为默认状态，其一维树使用阈值 0、1、2，状态速度参数 `PlaybackRate` 默认 1。[基础控制器](../../Assets/Game/Animations/Characters/Controllers/Phoebe_Controller.controller) · [大剑片段](../../Assets/Game/Animations/Characters/Clips/GreatSword/Normal/) · [双刀片段](../../Assets/Game/Animations/Characters/Clips/DualKatana/Normal/)

M3 的 CombatDemo 已登记这两名角色。M4 已在两个角色根接入 ActorAnimationBinding。菲比使用 Phoebe_Controller，祀使用 Si_Locomotion.overrideController，两张手动图均可播放待机、走和跑。M4 中用于混合的 `walkReferenceSpeed` 和 `runReferenceSpeed` 由 Inspector 保存，数值取对应片段的水平原速。自动从片段提取参考速度仍是提案。基础控制器已有 Dodge 状态，其触发与攻击混合列入 M5。M1.4 已用原型图验证角色动作时钟，M3 提供模型 Animator 和已登记身份。[CombatDemo](../../Assets/Scenes/CombatDemo.unity) · [M1.4 时钟记录](../M1%20Core%20Data%20Foundation/M1.4%20CombatContracts%20与%20Timing%20详细技术设计.md) · [M3 角色交接](../M3%20Actor%20Foundation/M3%20Actor%20Foundation%20详细技术设计.md#7-交给-m4-与-m5-的接口)

## 2. 实施文件

| 变更 | 相对项目根目录的完整路径 | 职责 |
| --- | --- | --- |
| 新增 | `Assets/Scripts/Runtime/Animation/CharacterAnimationGraph.cs` | 定义 `ICharacterAnimationDriver` 和图，管理基础控制器、实速标定、单槽攻击、手动求值和释放 |
| 扩展 M4 文件 | `Assets/Scripts/Runtime/CombatSession/ActorAnimationBinding.cs` | 持有每个模型的图与标定，接收本步命令和 M4 移动结果。在 M5 原型中持有攻击片段引用并选择播放 |
| 接入 M4 文件 | `Assets/Scripts/Runtime/CombatSession/ActorStepBinding.cs` | 作为场景组合入口，在同一回调依次调度原型攻击、M4 移动和 M5 采样，每个角色仅持有一份战斗步订阅 |
| 扩展 | `Assets/Scripts/Runtime/CharacterControl/CharacterControl.cs` | Step 支持攻击占用，返回本步有效的闪避区间与边界标志 |
| 扩展 | `Assets/Scripts/Runtime/CharacterControl/PartyMemberControl.cs`、`Assets/Scripts/Runtime/CharacterControl/FSM/States/PartyMember/PartyMemberGroundedState.cs` | 按角色配置轨迹、创建本人的 Dodge 状态，在正步仲裁优先级 |
| M5 新增 | `Assets/Scripts/Runtime/CharacterControl/FSM/States/PartyMember/PartyMemberDodgeState.cs` | 锁定方向、推进游标并将轨迹差量交给同根 Motor |
| M5 新增 | `Assets/Scripts/Runtime/Animation/DodgeRootTrajectory.cs`、`Assets/Scripts/Editor/Animation/DodgeRootTrajectoryEditor.cs` | 只读轨迹与插值；编辑器用真实 Avatar 从 Evade 片段采样并校验 |
| M5 生成 | `Assets/ScriptObjects/Animation/PhoebeDodgeTrajectory.asset`、`Assets/ScriptObjects/Animation/SiDodgeTrajectory.asset` | 保存各自实测的水平根轨迹，供角色根引用 |
| M5 新增 | `Assets/Scripts/Test/Editor/Animation/DodgeRootTrajectoryTests.cs` | 对照实时采样，验证插值、末帧、长步与分步、零轨迹拒收 |
| 修改 | `Assets/Scenes/CombatDemo.unity` | 沿用两根的动画绑定和 ActorStepBinding，分别引用本角色轨迹与示例普攻片段，核对覆盖的 Dodge 片段与轨迹来源一致 |
| 修改 | `Assets/Game/Animations/Characters/Controllers/Phoebe_Controller.controller` | 保留 Locomotion 和走跑树，配置 Dodge 片段，交由图在有效区间结束时显式切回 Locomotion |
| 复用 | `Assets/Game/Animations/Characters/Controllers/Si_Locomotion.overrideController` | 以 Phoebe_Controller 为基础，覆盖祀的待机、走、跑、闪避四个片段 |
| 复用角色资源 | `Assets/Prefabs/Characters/Phoebe.prefab`、`Assets/Prefabs/Characters/Si.prefab` | 通过 Combatant 取得各自模型 Animator，片段资源见第 1 节 |
| 新增 | `Assets/Scripts/Test/Editor/Animation/CharacterAnimationGraphTests.cs` | 双角色步态、参考骨骼姿态、片尾、取消与节点回收 |
| 新增 | `Assets/Scripts/Test/Editor/Animation/ActorAnimationSceneTests.cs` | 在 CombatDemo 验证按角色推进时间、临时禁用、恢复及重载 |
| 扩展 M4 用例 | `Assets/Scripts/Test/Editor/CharacterMotion/ThirdPersonControlTests.cs` | 保留 M4 独立移动断言，增加启用动画后的移动转攻击和闪避取消验收 |

本节覆盖原动画系统迁入 M5 的文件。M4 的 CharacterMotor、GameInputManager、第三人称相机，以及 M3 的角色登记由各自模块维护。原 M5 运动扩展的接入位置与验收边界见第 6 节。

## 3. 角色动画图与步态

每名已绑定角色持有一张 `DirectorUpdateMode.Manual` 图，Mixer 的两个端口共同输出到该角色的模型 Animator。

```text
端口 0  AnimatorControllerPlayable  [Locomotion ⇄ Dodge] ─┐
                                                       ├─ AnimationMixerPlayable → AnimationPlayableOutput → 模型 Animator
端口 1  AnimationClipPlayable       [当前攻击，按需创建] ─┘
```

| 所属类型 | 关键成员、初值与用途 |
| --- | --- |
| ActorAnimationBinding | Inspector 配置 `RuntimeAnimatorController locomotionController`，`float walkReferenceSpeed, runReferenceSpeed` 按第 1 节填入 m/s，满足 `0 < walk < run`。M5 输入联调使用单个 `AnimationClip attackClip`，菲比取 GS_Attack1，祀取 DK_Attack1。第 1 节的 Attack2 用于夹具验证换片。正式连段的末段片段由对应角色的连招动作配置指定。绑定创建、持有和销毁该模型的图，对外公开只读 `ICharacterAnimationDriver Driver` |
| CharacterAnimationGraph | 创建一份 `PlayableGraph`、`AnimatorControllerPlayable`、双输入 `AnimationMixerPlayable` 和输出。初始 `MoveAmount = 0`、`PlaybackRate = 1`、攻击权重 `w = 0`。建图后 Play 并 Evaluate(0) 采样待机首姿 |
| 攻击输入 | `AnimationClip clip` 初始为空，`AnimationClipPlayable attack` 初始无有效节点。TryPlay 创建节点并设置播放倍率，GetTime 返回片段秒数。自然退出时长、当前过渡的起止权重、已用及总时长随当前播放请求保存，淡化使用角色动作秒 |

**基础控制器的姿态状态。** Locomotion 使用原走跑树。菲比的 Dodge 播放 GS_Evade，祀由 Override 提供 DK_Evade，均为 1 倍速。M5 首次进入闪避时先 Play(Dodge, 0, 0f) 并 Evaluate(0) 建立首姿，再消费本步有效区间。图在片尾显式回到 Locomotion，移除会提前切走的自动过渡。动画的 IsDodging 用于诊断，游戏状态、方向与结束时刻由第 5 节的队员 FSM 决定。[基础控制器](../../Assets/Game/Animations/Characters/Controllers/Phoebe_Controller.controller)

**步态换算。** 外部传入本次常规移动的实测水平速度 `v`（m/s）。设本角色的走、跑标定为 `W`、`R`，图将下式结果写入控制器的 `MoveAmount`。该值无单位，控制器中的三个阈值对两人分别表示待机、纯走、纯跑。更换走跑片段时同时更新该角色的两项标定。[Unity 一维 Blend Tree](https://docs.unity3d.com/6000.3/Documentation/Manual/BlendTree-1DBlending.html)

| 输入 | MoveAmount |
| --- | --- |
| `v ≤ 0` | `0` |
| `0 < v ≤ W` | `v / W` |
| `W < v < R` | `1 + (v − W) / (R − W)` |
| `v ≥ R` | `2` |

例如，同样传入 4.91111 m/s，菲比得到 2，祀得到 `1 + (4.91111 − 1.54665) / (6.82405 − 1.54665) ≈ 1.638`。走跑标定描述动画的步态。玩家控制器单独配置玩法速度，撞墙后以 Motor 实际走出的距离计算 `v`。闪避位移取第 5 节的片段轨迹，期间 GroundSpeed 取 0。

## 4. 攻击、取消与手动求值

| ICharacterAnimationDriver 入口 | 参数、返回值与行为 |
| --- | --- |
| `void SetGroundSpeed(float metersPerSecond)` | 按第 3 节换算并写控制器参数。场景绑定传入 M4 本步的 GroundSpeed，独立测试直接给定速度 |
| `bool TryPlay(AnimationClip clip, float speed, float fadeIn, float fadeOut)` | 接受非循环 Humanoid Clip、有限正倍率和有限非负淡化秒数，0 表示硬切。无效请求返回 false 并保留原动作。有效请求从片段时间 0 播放，成功返回 true |
| `void Stop(float fadeOut)` | 从当前攻击权重淡回基础 FSM。正在退出时重复 Stop 保留原进度。传入非法时长抛出参数异常 |
| `void PlayDodge(float attackFadeOut)` | 将基础控制器切入 Dodge，同时令攻击从此刻权重向 0 淡出，已有淡出时采用本次指定的非负有限时长，0 表示立即交还全身姿态。没有攻击时直接播放闪避 |
| `AnimationClip CurrentClip`、`double CurrentClipTime` | 返回当前目标攻击和片段时间。Stop、自然到尾或 PlayDodge 开始返回基础时即读作 null、0。淡出期间仍可看到上一击的姿态贡献 |
| `bool IsDodging` | 读取最近一次求值后的 Dodge 姿态状态，供动画测试观察 |

**单槽换片。** 任一时刻最多连接一个攻击 Clip。移动与攻击之间可交叉淡化。已获准的新攻击替换旧攻击时，立即断开并销毁旧节点，接入新片段，从原有攻击权重向 1 淡入。原权重为 1 时两击直接硬切，重复播放同一 Clip 也从头开始。原型左键触发见第 5 节，M6 Actions 接管攻击许可。

**每个动作步。** `Advance(double actionDelta)` 仅由绑定组件调用。零增量保持姿态、游标和混合进度。正增量在片尾或淡化终点拆成子步，先确定子步末的攻击权重，再 `graph.Evaluate((float)dt)` 一次。攻击权重为 `w = start + (target − start) × min(1, elapsed / duration)`，基础权重始终为 `1 − w`。零时长过渡立即就位。节点按自身 speed 自行前进。若到片尾，将节点固定在 length、速度置 0，从此时权重按该请求的 fadeOut 淡回基础。主动退出中的节点到尾也固定末姿，继续原淡出。退出达到零权重后回收节点，继续处理该步剩余时间。[Unity Mixer](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Animations.AnimationMixerPlayable.html) · [手动求值](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Playables.PlayableGraph.Evaluate.html)

例如菲比跑步中提交 GS_Attack1，倍率 1、fadeIn = 0.100 s。推进 0.025 s 后，基础/攻击权重为 0.75/0.25，攻击游标为 0.025 s。再进 0.075 s 后攻击权重为 1。此时接受 `PlayDodge(0.080)`，攻击即失去当前动作身份，推进 0.040 s 后闪避/旧攻击各占 0.5，再进 0.040 s 后由闪避独占输出。

## 5. 与 M4 共用一个动作步

**装配与初始化。** 两名角色各沿用 M4 的 ActorStepBinding，Time Driver 均指向 Combat Session。两根沿用 M4 已装配的 ActorAnimationBinding、步态标定与控制器引用。M5 将现有图扩展为支持动作混合的 CharacterAnimationGraph。两模型各保留自己的 Avatar，Animator 的 Controller 槽留空，Apply Root Motion 关闭，Culling Mode 取 Always Animate。将 GS_Attack1 和 DK_Attack1 分别指定给菲比与祀的 `attackClip`。M3 登记后由场景绑定显式初始化动画绑定，以 `Combatant.ModelAnimator`、控制器与标定创建图，采样待机首姿。

### 闪避根运动

**资源实测与交付前提。** 2026-11-24，Unity 6000.3.28f1。在临时预览场景中分别实例化 Phoebe、Si 的 Prefab，以本角色 Avatar、单位播放倍率、开启根运动的模型 Animator 和手动 AnimationClipPlayable 图单独播放对应片段。从首姿开始按 30 Hz 读每步 `Animator.deltaPosition`，包含末帧。下表的路程为各步 XZ 位移长度之和，可以区分原地动作和先移出再移回的动作。

| 角色与 Clip | 长度 | 0.10 s / 0.20 s 的累计 XZ 位移 | 全片水平路程 | 同方法播放 Walk 的水平路程 |
| --- | ---: | --- | ---: | ---: |
| 菲比 · GS_Evade | 1.7000 s | 均为 (0, 0) m | 0.0000 m | GS_Walk 约 1.4789 m |
| 祀 · DK_Evade | 1.4667 s | 均为 (0, 0) m | 0.0000 m | DK_Walk 约 1.7295 m |

两个 FBX 当前指定 `root` 为运动节点。身体姿态曲线有变化，但按现有导入配置导出的水平根位移为零。M5 实施时先检查源动画的移动根、导入器的 Root Motion Node 与 XZ Bake Into Pose；若源片段本身原地播放，由动画制作者提供带水平根轨迹的闪避版本。将修正后的 Clip 和角色 Avatar 重采样，在预览中核对突进方向、身体与胶囊的相对位置。只有通过此项检查的轨迹才启用角色的 Dodge 转换。两人的实际距离、起动和收势时间届时逐人记录。[菲比导入配置](../../Assets/Game/Animations/Characters/Clips/GreatSword/Normal/GS_Evade.fbx.meta) · [祀的导入配置](../../Assets/Game/Animations/Characters/Clips/DualKatana/Normal/DK_Evade.fbx.meta) · [Unity 根位移读数](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Animator-deltaPosition.html)

**轨迹资产与制作。** 每名角色使用一份 `DodgeRootTrajectory`。在资产 Inspector 为 `modelPrefab: GameObject` 指定角色 Prefab，为 `clip: AnimationClip` 指定实际覆盖的 Evade 片段。点击「从片段采样」。编辑器在预览场景中复用上述采样方式，清空模型控制器，以角色根起始坐标系为基准，将世界水平增量旋回该坐标系并累加。角色运行时保持模型 Animator 的自动根运动关闭。预览结束销毁临时图并关闭预览场景，场景与源 FBX 保持原状。

| 轨迹数据 | 采样与读取约定 |
| --- | --- |
| `sampleRate: float`、`duration: float` | 烘焙时记录 Clip 的帧率和 `clip.length`，秒。样本时刻为 0、1 / sampleRate、…、duration，最后不足一帧也写入准确片尾。更换 Clip、Avatar、比例或根导入设置后重烘焙 |
| `positionsXZ: Vector2[]` | 角色起始局部空间的累计 XZ 坐标，单位米，首样本为零。对每一段记录根增量，包括横移和回撤。`EvaluatePosition(t)` 钳位到首尾，在相邻**实际样本时刻**之间线性插值；同一只读资产可供多个实体使用 |
| `travelAxisXZ: Vector2` | 美术在 Inspector 指定片段的主突进方向，例如前扑为 (0, 1)，后撤为 (0, −1)。以预览箭头和实际轨迹核对后保存单位向量。烘焙拒收累计水平路程不足 0.001 m 的轨迹，提示 Clip 名和路程；配置或烘焙失效时在验收前修正 |
| 每个队员实例的 `dodgeTrajectory`、Dodge 状态中的 `elapsed` 与 `basisYaw` | 角色根在 Inspector 引用本人的资产。进入状态时将 elapsed 归零；从角色的 MoveWorld 取得锁定的世界水平方向，无输入时取角色朝向。basisYaw 将片段主突进轴旋到锁定方向，至退出保持不变。玩法只消费 XZ，Y 留给模型姿态，角色根朝向由 Motor 处理 |

**同一角色动作步的位移与姿态。** M5 在地面状态选态时给有效的 Dodge 请求最高优先级。进入状态的第一步就消费动作时间，进行中重复按键保持当前进度。每步从同一个游标得到 `t0 = elapsed`、`t1 = min(t0 + actionDelta, duration)`。每跨一个轨迹采样点就切分该段，将每个小区间的 `P(b) − P(a)` 变换为世界 XZ 位移，通过 `Motor.MovePlanar` 各提交一次；首段由 Motor 将角色朝向对齐片段基准。Walk/Run 在该步让出 Motor，模型只播放姿态。碰撞返回实际位移，动作游标仍走到 t1，下一步从 P(t1) 继续。全局暂停或该角色完全顿帧时本步不移动、不推进姿态。后续 M8 接入队伍切换时，已进入的闪避仍由原角色继续完成。

M5 在 `CharacterControl.cs` 中定义只读值 `DodgeStep`，由 `Step(double actionDelta, bool movementLocked)` 返回。它包含 `bool Active, Entered, Finished` 以及以片段秒为单位的 `double From, To`。默认值表示本步无闪避，Dodge.Tick 按当步实际区间填写。返回值在该步内保留，即使 Tick 已经切回 Idle。动画绑定按进入标志从首姿启动，只让闪避片段消费 `t1 − t0`；跨尾步先完成末段位移与姿态，再切回 Locomotion，余时只推进基础图，按住方向的走跑在下一正步恢复。攻击淡出的混合权重只影响姿态。闪避期间不重复消费 Animator 的 deltaPosition，也不叠加旧的定速位移。

### 场景调度

每个角色保留一份 ActorStepBinding 订阅，控制、轨迹和图读取同一个动作步。

1. 绑定取得本角色动作时间。冻结或死亡时丢弃待执行按下，保持移动状态、攻击游标和权重。M5 将 M4 当前位于 Step 开头的命令读取提前到本步动作仲裁前，移动与攻击读取同一份 `StepCommands`，每步只取出一次。
2. 正动作步先判断闪避占用。正在闪避或本步接受了具有有效轨迹的 Dodge 请求时忽略本步 Attack。其余情况下，收到 Attack 且攻击槽空闲时，M5 原型播放该角色的示例 `attackClip`，倍率 1、淡入 0.100 s、自然淡出 0.080 s。持续按住左键不会追加播放。`CurrentClip` 是否有效决定本步 `movementLocked`。M6 接管动作许可，M7 接入连段选段。
3. M5 扩展 Step 接收攻击占用并返回本步闪避区间。地面状态接受闪避时优先于攻击，常规移动被攻击占用时置 Idle、GroundSpeed 为 0。闪避状态先消费轨迹差量，进入标志触发 Driver.PlayDodge(0.080f)，向图传递该区间。M4 单独工作时，右键仅在 StepCommands 中产生一次 Dodge 标记。
4. 动画绑定按本角色的动作秒推进。本步含闪避时按上述区间及片尾分段；其余步传 GroundSpeed 给图，再 Advance(actionDelta)。关掉模型 Animator 只跳过姿态求值，已配置轨迹仍可推动碰撞胶囊。
5. 临时禁用角色根时绑定退订，保留图与动作游标。恢复后从原游标续步。若只停用模型 Animator 或动画绑定，闪避轨迹仍按角色钟消费，重新启用时按 FSM 当前阶段和游标定位姿态，以 Evaluate(0) 刷新，不补发位移。离场或重载时释放图和订阅，新的角色从 Idle 开始。

**正式普攻连段。** 首段、中间段和末段都接收同一种 `Attack` 请求。M7 的 Combos 根据当前连段及已获准的衔接选择动作，末段动作是重击。M6 的 Actions 提交该动作后，动画驱动以 `TryPlay` 播放对应片段。M5 的单槽换片测试可以直接提交 Attack2 验证新游标与姿态，正式玩法中的重击身份取决于它在角色连段中的位置。[M4 输入约定](../M4%20Third%20Person%20Control/M4%20第三人称控制器、相机与基础移动%20FSM%20详细技术设计.md#3-输入与两个时间尺度)

## 6. 实施与验收

先让两个 Evade 通过根轨迹采样检查，执行 DodgeRootTrajectoryTests；随后建立动画图，用真实 Avatar 对照步态和片尾，再把图接到 M4 的角色动作步。运行 **Window → General → Test Runner → EditMode** 中的 CharacterAnimationGraphTests、ActorAnimationSceneTests，以及 ThirdPersonControlTests 中的攻击联调用例。场景用例进入 Play Mode 加载 CombatDemo，在 Start 后关闭 BattleTimeDriver 的自动更新，对同一 Clock 提交固定步长。历史通过数见第 7 节，以下各项是对拆分后实现的验收要求。

| 验收项 | 初始条件与操作 | 可观察结果 |
| --- | --- | --- |
| 双角色移动标定 | 独立动画夹具依次输入 0、本角色 W、本角色 R。另给两人相同的 4.91111 m/s | 三端点分别呈现各自的 Idle、Walk、Run，相同速度时菲比为纯跑、祀为约 0.638 跑权重的走跑混合。参考控制器直接设置对应 MoveAmount，从相同归一化相位比较大腿或上臂局部旋转，建议误差 ≤ 0.5° |
| 单槽攻击与闪避取消 | 在菲比跑步姿态执行第 4 节 0.100 s 淡入、0.080 s 闪避取消算例，用独立参考图在相同片段和 FSM 时刻设定预期权重 | 先看到移动/攻击 0.75/0.25，中途取消时看到闪避/旧攻击 0.5/0.5，再进 0.040 s 只有闪避。CurrentClip 自接受闪避起为空。两名角色分别播放 Dodge，在自己的片尾后再推进一个短步均回 Locomotion |
| 单槽替换、自然结束及回收 | 从满权重 Attack1 直接向 Driver 提交 Attack2，比较新片段首段姿态。另让 DK_Attack1 以 2 倍速播放、fadeOut = 0.080 s，距片尾尚余 0.020 动作秒时输入 0.060 s，并与 0.020 + 0.040 s 对照 | 替换后只采样 Attack2，新游标从 0 起。两种跨尾步长均得到基础/攻击末姿 0.5/0.5，CurrentClip 为空，再推进 0.040 s 完全恢复基础。重复十次播放、退出，回到纯基础时图节点数均恢复为建图后的基线 |
| 输入与动作仲裁 | 空槽按下左键一次，保持到本片段结束并完成淡出，再松开。另测左右键同帧、冻结步按左键及重新捕获的点击 | 首次按下立即播放该角色的示例普攻，持续按住到片尾也只播放一次。松开后重新按下可再播放。左右键同帧只进入闪避，冻结步及捕获点击均不补播攻击 |
| 移动与闪避 | 直行、跑步、顶墙，再持 W 点击右键 | 走跑由碰撞后速度决定步态。闪避只启动一次，胶囊按实测轨迹位移；到片尾后的下一正步恢复走路 |
| 插值与分步算例 | 夹具的 XZ 累计样本为 0.00 s 的 (0,0)、0.10 s 的 (0,0.60)、0.20 s 的 (0,1.00)，主轴为 +Z，锁定世界 +X | 0.05 → 0.15 s 的 Z 读数从 0.30 变为 0.80 m，本步空地实际向 +X 移动 0.50 m。该数值只用于验证算法 |
| 两名角色的轨迹 | 在空地分别用自身 Clip 和 Avatar 逐原始帧独立播放，记录真实根位置。再以预采样轨迹执行整段，对照起点、0.10 s、0.20 s、末帧，另将锁定方向旋转 90° | 空地根位置与旋转后的参考轨迹误差 ≤ 0.01 m。片尾总时长取各自 clip.length。同一角色用一个跨尾长步和多个小步结果一致，最后一段只计一次 |
| 撞墙与独立角色顿帧 | 在实际突进途中以墙阻挡，收势再移开；另向两名队员分别提交闪避请求，仅给第一名 30 ms 顿帧，再进两步 20 ms。单独关闭并恢复 Animator | 不穿墙、不补回丢失位移。前者只增加 10 ms、后者增加 40 ms，各自对照轨迹读数；恢复模型后姿态与原角色游标吻合，胶囊位置不跳变 |
| 资产失效 | 用当前零位移 Evade 采样，或把菲比轨迹配给播放祀片段的角色 | 烘焙或装配校验显示角色和片段名；该角色可正常走跑，右键不提交错配或替代位移 |
| 角色时钟与相机独立 | 让两人各播 Attack1，推进到上臂运动明显处。给菲比 0.030 s 顿帧，再连续两步 Advance(0.020)。随后全局暂停一正步，恢复、设置 battleRate = 0.5 后推进 0.040 s。顿帧中用鼠标环绕 | 前两步菲比累计 0.010 动作秒、祀累计 0.040 s，菲比首步姿态保持。全局暂停步两人游标及权重保持，半速步各增 0.020 s，顿帧时镜头仍能环绕。单独跨一个未提交动作步的渲染帧，图游标保持 |
| 禁用与场景重开 | 祀播放中禁用其逻辑根，推进时钟再启用，观察恢复前后的姿势并推进 0.020 s。在 Play Mode 重新加载 CombatDemo | 祀保留 ID、从原姿态与游标续播，恢复步只增 0.020 s，菲比持续响应。重开后两人从待机开始，相机首次定位到安全距离，一次按键产生一次动作，无失效 Playable 报错 |

**后续运动扩展。** 本阶段验收平面闪避轨迹及碰撞。接地、攻击根运动和对目标的 Motion Warping 在后续运动扩展中接入，复用同一 Motor。攻击仍需在空地及墙后目标下分别记录起点、修正位移和实际终点。

## 7. 历史验证记录

**2026-11-23，旧提交 `21d307c` 的操控与动画联合原型。** 按用户提供的原 M4 实测记录，Unity 6000.3.28f1 Test Runner 首轮通过 14 项，接入长按输入后复跑共 **17 项，全部通过，0 项失败或跳过**。其中动画、操控和输入相关用例 10 项，既有场景回归 7 项。场景用例均在 Play Mode 加载 CombatDemo。其中按住左键触发另一击的用例验证的是已废弃的输入方案。现行重击规则的验收在 M7 执行。

| 测试组 | 通过数 | 实际覆盖 |
| --- | ---: | --- |
| CharacterAnimationGraphTests | 4 | 双刀走跑的端点和中间混合、Override 闪避片尾返回、跑步淡入攻击与取消到闪避的上臂姿态。自然退出、主动退出、零时长硬切与参考姿态误差在 0.5° 内，十轮替换及退出后节点数均回到基线 |
| ActorAnimationSceneTests | 1 | 给菲比 30 ms 顿帧，两个 20 ms 步内菲比推进 10 ms、祀推进 40 ms。全局暂停、半速、临时禁用后恢复游标和姿态、重载场景后清理旧 ID 均通过 |
| ThirdPersonControlTests | 3 | 键鼠事件驱动相机相对移动、斜向奔跑、闪避及贴墙停止。空地闪避的两个 0.10 s 步分别累计约 1 m、2 m，主相机通过遮挡及俯仰检查。旧原型中角色动作速率设为 0.25 后，左键按下即播放 GS_Attack1，持续按住按真实时间切到 GS_Attack2，继续按住不重启片段。验证了暂停时的请求丢弃及闪避优先 |
| CombatInputReaderTests | 2 | 旧原型使用真实 Combat 输入资产和虚拟键鼠，分别验证短按、长按只追加一次重击、松开与重复消费均不重复出招、同次采样保留多种请求。Esc 同帧优先，重新捕获的点击持续超过阈值也不会出招，释放捕获、禁用及闪避均能清理手势 |
| ActorSceneTests / BattleClockSceneTests / ResourceSceneTests | 3 / 2 / 2 | 回归同场角色登记与重载、独立动作时钟、资源和 HUD 行为 |

手动图进入 Dodge 后先以 0 秒采样首姿，再消费本次正动作步。验证中发现 Unity 首次求值会用于切换控制器状态，这一处理使首个动作步也计入闪避片段。脚部接地期间的世界坐标漂移和玩家对操控手感的评价未计入上述数值验收。

**本轮状态。** 第 7 节的通过数属于旧原型。现行 M4 的走跑与右键请求已定向复验。M5 已完成第 5 节的源资源测量，正式闪避、轨迹资产、播放同步与场景验收均待实现。
