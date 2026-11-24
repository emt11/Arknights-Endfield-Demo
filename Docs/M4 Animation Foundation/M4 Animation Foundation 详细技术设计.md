# M4 Animation Foundation 详细技术设计

**已在 CombatDemo 实现 · 2026-11-23**

M4 在现有 `Assets/Scenes/CombatDemo.unity` 中接入并验收第三人称操控和动画。进入该场景后，玩家用鼠标环视菲比，用 WASD 相对镜头行走、按住 Shift 奔跑，左键按下立即普攻，持续按住 0.40 秒再请求重击，右键向输入方向闪避。祀使用自己的移动、闪避和攻击片段，保留独立的动作时钟，供双角色对照。首版地面 FSM 仅含 `Locomotion` 和 `Dodge`。跳跃、空中状态、音频和特效均在本版范围之外。

## 1. 工程依据与模块分工

| M4 接入前的工程基线 | 菲比 | 祀 |
| --- | --- | --- |
| CombatDemo 逻辑根 → 模型 Animator | `Characters/PlayerRoot` → `Phoebe` | `Characters/SiRoot` → `Si` |
| 模型配置 | 有效 Humanoid `PhoebeAvatar`。角色 Prefab 的 Controller 为空，CombatDemo 实例引用 `Phoebe_Controller` | 有效 Humanoid `SiAvatar`。角色 Prefab 和 CombatDemo 实例的 Controller 均为空 |
| 待机、走、跑 | `GS_Idle / GS_Walk / GS_Run` | `DK_Idle / DK_Walk / DK_Run` |
| 走、跑的原生水平速度 | 1.43936、4.91111 m/s | 1.54665、6.82405 m/s |
| 单次闪避 | `GS_Evade`，1.7000 s | `DK_Evade`，约 1.4667 s |
| 验收用攻击片段 | `GS_Attack1 / GS_Attack2`，约 3.1333 / 2.8333 s | `DK_Attack1 / DK_Attack2`，约 1.5333 / 1.9000 s |

上表来自 Unity 6000.3.28f1 中导入的片段。十二个片段均为 30 fps 的 Humanoid，待机、走、跑循环，两套闪避和攻击均单次播放。速度取 1 倍速下 `AnimationClip.averageSpeed` 在 XZ 平面的模长。运行时的片尾以 `clip.length` 为准。接入前菲比控制器仅有默认状态 `Locomotion`，其一维树使用阈值 0、1、2，状态速度参数 `PlaybackRate` 默认 1。[基础控制器](../../Assets/Game/Animations/Characters/Controllers/Phoebe_Controller.controller) · [大剑片段](../../Assets/Game/Animations/Characters/Clips/GreatSword/Normal/) · [双刀片段](../../Assets/Game/Animations/Characters/Clips/DualKatana/Normal/)

**CombatDemo 接入点。** `Combat Session` 已有 BattleTimeDriver 和 SceneActors，开场名单依次引用 `Characters/PlayerRoot`、`Characters/SiRoot`。玩家根已有 CharacterController。场地使用 `Environment/Plane` 和 `Environment/Wall`，两者均在 Environment 层并带碰撞体。场景根已有 `Directional Light` 和 `Main Camera`。主相机的 CinemachineBrain 已禁用，原先显示为 Missing Script 的历史相机组件已在此次接入中替换为 `ThirdPersonCamera`。装配位置见第 7 节。[现有战斗场景](../../Assets/Scenes/CombatDemo.unity)

**输入和相机依据。** 工程的 `Combat.inputactions` 已有本原型所需键鼠绑定，项目已定义 `Environment`、`Player` 层。相机参考 Git 中 `Assets/Scripts/Runtime/Camera/ThirdPersonCamera.cs` 的历史文件对象，可用 `git show a77596382e9474095eb3dbbe9ce3ed0fd7299516` 查看。该版本将水平朝向提供给移动控制器，在 LateUpdate 检测遮挡、立即收近并逐渐恢复距离。提交 `6e5bdf2` 中的 `Assets/Scripts/Camera/CustomThirdPersonCamera.cs` 也保留了同一套近裁面保护算法。[战斗输入资产](../../Assets/Input/Combat.inputactions) · [工程图层](../../ProjectSettings/TagManager.asset)

**里程碑交接。** M1.4 已用两张单片段手动图验过局部顿帧，M3 已验过正式角色的 ID、模型引用和动作时钟。本次将第三人称操控、相机及 `CharacterMotor` 的平地移动提前到 M4，用同一原型验证输入、受碰撞约束的位移和最终动画。M5 在这份 Motor 上扩展接地、根运动及 Motion Warping。M6 接管攻击许可、取消窗口和命中。M7 有连招需求时再扩展攻击间的姿态过渡。[总方案与 ADR-009](../仿终末地战斗系统设计方案.md) · [M1.4 骨骼与时钟记录](../M1%20Core%20Data%20Foundation/M1.4%20CombatContracts%20与%20Timing%20详细技术设计.md) · [M3 交接接口](../M3%20Actor%20Foundation/M3%20Actor%20Foundation%20详细技术设计.md#7-交给-m4-的接口)

## 2. 实施文件

下表列出本次实现相对项目根目录的文件。运行时代码使用 Unity 默认程序集。场景中的配置均可通过 Inspector 完成。

| 变更 | 完整路径 | 职责 |
| --- | --- | --- |
| 新增 | `Assets/Scripts/Runtime/Animation/CharacterAnimationGraph.cs` | 定义 `Endfield.Animation.ICharacterAnimationDriver` 与 `CharacterAnimationGraph`。维护控制器、单个攻击输入、步态换算和手动求值 |
| 新增 | `Assets/Scripts/Runtime/CombatSession/ActorAnimationBinding.cs` | 绑定模型与角色时钟，组织一个动作步内的操控、位移和动画求值 |
| 新增 | `Assets/Scripts/Runtime/Input/CombatInputReader.cs` | 定义 `Endfield.Input.CombatInputReader` 及标记枚举 `CombatInputCommand`。识别战斗输入、维护光标捕获状态和当前采样的命令 |
| 修改 | `Assets/Input/Combat.inputactions` | 在已有的 Gameplay/Attack 上配置 Hold，复用原左键绑定和 Action ID |
| 新增 | `Assets/Scripts/Runtime/Input/ThirdPersonController.cs` | 消费方向、奔跑和战斗命令，按镜头朝向移动，决定本次动作步接受的攻击或闪避 |
| 新增 | `Assets/Scripts/Runtime/CharacterMotion/CharacterMotor.cs` | 平地移动的唯一出口，通过 Unity CharacterController 处理碰撞，返回实际水平位移 |
| 新增 | `Assets/Scripts/Runtime/Camera/ThirdPersonCamera.cs` | 跟随玩家、处理鼠标环绕、输出水平朝向，保护近裁面并恢复跟随距离 |
| 修改 | `Assets/Game/Animations/Characters/Controllers/Phoebe_Controller.controller` | 保留默认 Locomotion 和原 Blend Tree，加入 Dodge 及返回移动的过渡 |
| 新增 | `Assets/Game/Animations/Characters/Controllers/Si_Locomotion.overrideController` | 引用上述控制器，将 Idle、Walk、Run、Evade 四个大剑片段替换为双刀片段 |
| 修改 | `Assets/Scenes/CombatDemo.unity` | 在现有两名角色和 Main Camera 上装配 M4 组件，复用战斗会话、光照及环境碰撞体 |
| 新增 | `Assets/Scripts/Test/Editor/Animation/CharacterAnimationGraphTests.cs` | 固定动作步验证步态、闪避、单槽混合、回收及片尾 |
| 新增 | `Assets/Scripts/Test/Editor/Animation/ActorAnimationSceneTests.cs` | 在 Play Mode 加载 CombatDemo，验证两角色的姿态、时钟、禁用与卸载 |
| 新增 | `Assets/Scripts/Test/Editor/CharacterMotion/ThirdPersonControlTests.cs` | 在 CombatDemo 中验证相机相对移动、碰撞、闪避、普攻转重击与相机遮挡恢复 |
| 新增 | `Assets/Scripts/Test/Editor/Input/CombatInputReaderTests.cs` | 用已有输入资产及虚拟键鼠验证按下、长按、同帧点击、取消与恢复捕获 |

直接复用两份角色 Prefab 和表中所列 FBX；输入复用 `Assets/Input/Combat.inputactions`。模型与 ID 分别取自 `Combatant.ModelAnimator`、`Combatant.Runtime.Id`，单位登记和战斗步使用现有组件。[菲比 Prefab](../../Assets/Prefabs/Characters/Phoebe.prefab) · [祀 Prefab](../../Assets/Prefabs/Characters/Si.prefab) · [Combatant](../../Assets/Scripts/Runtime/Actors/Combatant.cs) · [SceneActors](../../Assets/Scripts/Runtime/Actors/SceneActors.cs) · [BattleTimeDriver](../../Assets/Scripts/Runtime/Timing/BattleTimeDriver.cs) · [BattleClock](../../Assets/Scripts/Runtime/Timing/BattleClock.cs)

## 3. 角色动画图、地面 FSM 与步态

每名已绑定角色持有一张 `DirectorUpdateMode.Manual` 图，Mixer 的两个端口共同输出到该角色的模型 Animator。

```text
端口 0  AnimatorControllerPlayable  [Locomotion ⇄ Dodge] ─┐
                                                       ├─ AnimationMixerPlayable → AnimationPlayableOutput → 模型 Animator
端口 1  AnimationClipPlayable       [当前攻击，按需创建] ─┘
```

| 所属类型 | 关键成员、初值与用途 |
| --- | --- |
| ActorAnimationBinding | Inspector 引用 `BattleTimeDriver timeDriver`、`RuntimeAnimatorController locomotionController`；`float walkReferenceSpeed, runReferenceSpeed` 按角色填写第 1 节原生速度，单位 m/s，须满足 `0 < walk < run`。本次登记的 `ActorId`、图、战斗步订阅由绑定组件持有。只读 `ICharacterAnimationDriver Driver` 在 Start 绑定后可用 |
| CharacterAnimationGraph | 创建一份 `PlayableGraph`、`AnimatorControllerPlayable`、双输入 `AnimationMixerPlayable` 和输出。初始 `MoveAmount = 0`、`PlaybackRate = 1`、攻击权重 `w = 0`。建图后 Play 并 Evaluate(0) 采样待机首姿 |
| 攻击输入 | `AnimationClip clip`、`AnimationClipPlayable playable` 初始为空，`double speed` 为本次播放倍率。片段秒数取节点 GetTime。自然退出时长、当前过渡的起止权重、已用及总时长随当前播放请求保存，淡化使用角色动作秒 |

**地面 FSM。** 保留 `Base Layer.Locomotion` 为默认状态，其内部用 Blend Tree 连续混合待机、走、跑。在同层添加 `Dodge`，Motion 取 `GS_Evade`，状态速度固定 1。收到闪避命令时，驱动通过控制器节点的 `Play(Dodge完整路径哈希, 0, 0f)` 从首帧进入。从 Dodge 到 Locomotion 配置唯一返回过渡，启用 Has Exit Time、Exit Time = 1、Transition Duration = 0，无条件。Override 沿用归一化退出点，两人随各自的闪避片段结束而恢复。当前闪避状态从控制器读取，C# 只保存位移所需的方向和剩余闪避移动时间。[Unity 控制器播放接口](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Animations.AnimatorControllerPlayable.Play.html) · [Unity Override Controller](https://docs.unity3d.com/6000.3/Documentation/Manual/AnimatorOverrideController.html)

**步态换算。** 外部传入本次常规移动的实测水平速度 `v`（m/s）。设本角色的走、跑标定为 `W`、`R`，图将下式结果写入控制器的 `MoveAmount`。该值无单位，控制器中的三个阈值对两人分别表示待机、纯走、纯跑。更换走跑片段时同时更新该角色的两项标定。[Unity 一维 Blend Tree](https://docs.unity3d.com/6000.3/Documentation/Manual/BlendTree-1DBlending.html)

| 输入 | MoveAmount |
| --- | --- |
| `v ≤ 0` | `0` |
| `0 < v ≤ W` | `v / W` |
| `W < v < R` | `1 + (v − W) / (R − W)` |
| `v ≥ R` | `2` |

例如，同样传入 4.91111 m/s，菲比得到 2，祀得到 `1 + (4.91111 − 1.54665) / (6.82405 − 1.54665) ≈ 1.638`。走跑标定描述动画的步态。玩家控制器单独配置玩法速度，撞墙后以 Motor 实际走出的距离计算 `v`。闪避的突进距离单独处理。

## 4. 攻击、取消与手动求值

| ICharacterAnimationDriver 入口 | 参数、返回值与行为 |
| --- | --- |
| `void SetGroundSpeed(float metersPerSecond)` | 按第 3 节换算并写控制器参数。独立夹具或后续 Motor 可直接提供地面速度 |
| `bool TryPlay(AnimationClip clip, float speed, float fadeIn, float fadeOut)` | 接受非循环 Humanoid Clip、有限正倍率和有限非负淡化秒数，0 表示硬切。无效请求返回 false 并保留原动作。有效请求从片段时间 0 播放，成功返回 true |
| `void Stop(float fadeOut)` | 从当前攻击权重淡回基础 FSM。正在退出时重复 Stop 保留原进度。传入非法时长抛出参数异常 |
| `void PlayDodge(float attackFadeOut)` | 将基础控制器切入 Dodge，同时令攻击从此刻权重向 0 淡出；已有淡出时采用本次指定的非负有限时长，0 表示立即交还全身姿态。没有攻击时直接播放闪避 |
| `AnimationClip CurrentClip`、`double CurrentClipTime` | 返回当前目标攻击和片段时间。Stop、自然到尾或 PlayDodge 开始返回基础时即读作 null、0。淡出期间仍可看到上一击的姿态贡献 |
| `bool IsDodging` | 反映最近一次控制器求值后的 Dodge 状态，供玩家控制器识别闪避恢复阶段。同一步刚接受闪避时，以该命令的结果立即开始位移 |

**单槽换片。** 任一时刻最多连接一个攻击 Clip。移动与攻击之间可交叉淡化。已获准的新攻击替换旧攻击时，立即断开并销毁旧节点，接入新片段，从原有攻击权重向 1 淡入。原权重为 1 时两击直接硬切；重复播放同一 Clip 也从头开始。原型左键的触发规则见第 5 节，后续 Actions 决定何时准许替换。

**每个动作步。** `Advance(double actionDelta)` 仅由绑定组件调用。零增量保持姿态、游标和混合进度。正增量在片尾或淡化终点拆成子步，先确定子步末的攻击权重，再 `graph.Evaluate((float)dt)` 一次。攻击权重为 `w = start + (target − start) × min(1, elapsed / duration)`，基础权重始终为 `1 − w`。零时长过渡立即就位。节点按自身 speed 自行前进。若到片尾，将节点固定在 length、速度置 0，从此时权重按该请求的 fadeOut 淡回基础。主动退出中的节点到尾也固定末姿，继续原淡出。退出达到零权重后回收节点，继续处理该步剩余时间。[Unity Mixer](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Animations.AnimationMixerPlayable.html) · [手动求值](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Playables.PlayableGraph.Evaluate.html)

例如菲比跑步中提交 GS_Attack1，倍率 1、fadeIn = 0.100 s。推进 0.025 s 后，基础/攻击权重为 0.75/0.25，攻击游标为 0.025 s。再进 0.075 s 后攻击权重为 1。此时接受 `PlayDodge(0.080)`，攻击即失去当前动作身份；推进 0.040 s 后闪避/旧攻击各占 0.5，再进 0.040 s 后由闪避独占输出。

## 5. 战斗输入、第三人称控制器与平地位移

### 5.1 战斗输入模块

`CombatInputReader` 是由本地玩家控制器持有的普通 C# 对象。它把设备输入转换成二维方向、鼠标增量、持续按住的奔跑状态，以及一次性战斗命令。场景玩家的引用仍在 `Characters/PlayerRoot` 的 ThirdPersonController Inspector。祀和相机通过既有接口工作。输入层的依赖只有 Unity Input System 和光标 API；动作许可、位移、播放片段均由调用方处理。

| 成员 | 数据来源、更新时机与用途 |
| --- | --- |
| `CombatInputReader(InputActionAsset source)`、`Enable() / Disable() / Dispose()` | Awake 时克隆绑定的 Combat 资产一次，缓存 Gameplay 的 Move、Look、Sprint、Attack、Dodge 和 System/Pause。启用时打开 Gameplay 映射及 Pause，捕获光标。禁用清空输入并归还光标；销毁时解绑回调并释放副本 |
| `Vector2 Move, Look`、`bool SprintHeld, Captured` | 初始为零或 false。Enable 后 Captured 为 true。控制器每帧在 Update 里先调用 `Sample()`，再将 Look 交给镜头。Move 是长度上限为 1 的局部输入，Look 是该输入帧的鼠标像素增量，SprintHeld 反映 Shift 当前是否按住 |
| `CombatInputCommand ConsumeCommands()` | `Attack / HeavyAttack / Dodge` 是可组合的标记。输入回调把请求累积到 pending；Sample 把 pending 转为本次快照并清空 pending。同类请求在一次采样内合并。每次 Consume 取出并清空快照，再取为 None；未消费的旧快照在下一次 Sample 时过期 |
| `ReleaseCapture()`、`CancelAttackHold()` | 前者用于失焦或 Esc，清空方向、按钮请求及长按资格。重新捕获所用的整次左键手势不会出招。后者由控制器在闪避接管时调用，撤销当前按压后续的重击资格，重新按下才能获得新的资格 |

**左键手势。** 在 Input Actions 编辑器选中 `Gameplay/Attack`（Button），其 Action 级 Interactions 配置 `Hold(duration=0.4)`。调整阈值直接改该资产。按下达到按钮阈值时，`started` 提交一次 Attack 并记住本次按压；满 0.40 真实秒时，`performed` 再提交一次 HeavyAttack 并清除资格；`canceled` 收尾。持续按住和松开均不重复出招。比如在 0.12 秒松开只产生一次普攻；按住 0.8 秒产生一次普攻和一次重击。两次回调即使落在同一次采样，也可以同时由 Consume 读出。[Unity HoldInteraction 的真实时间语义](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.20/api/UnityEngine.InputSystem.Interactions.HoldInteraction.html)

**动作步与优先级。** 手势按真实输入时间识别，动作仍按角色时钟执行。控制器每个战斗步先消费快照，动作增量为 0 时丢弃本次请求，恢复时无需补播。同一步内按闪避、重击、普攻的顺序选择分支。重击可从地面空闲进入，也可替换当前普攻；正在闪避或播放重击时忽略新的重击。原型用现有 `GS_Attack2` 显示重击请求，沿用单槽替换；M6 接管动作许可和命中窗口，M7 接入伤害结算与跨帧连招缓存。

### 5.2 控制器与移动

菲比根节点上的 `ThirdPersonController` 在 Inspector 引用 `InputActionAsset actions`、`ThirdPersonCamera view`、`AnimationClip attackClip = GS_Attack1`、`AnimationClip heavyAttackClip = GS_Attack2`。其生命周期驱动同一份 CombatInputReader，Step 消费请求后调用角色动画接口。

| Action 与已有绑定 | 原型行为 |
| --- | --- |
| `Gameplay/Move`（WASD）、`Gameplay/Sprint`（左 Shift） | 默认行走，按住 Shift 奔跑。将二维输入长度限制在 1，平移方向使用本帧镜头 yaw，保留输入力度 |
| `Gameplay/Look`（鼠标 delta） | 每个渲染帧调用一次 ApplyLook，更新水平朝向和俯仰。进入可操作状态时锁定并隐藏光标 |
| `Gameplay/Attack`（左键） | 按下时在地面且攻击槽为空则播放普攻。持续按住满 0.40 真实秒可转入重击。两者均以倍率 1 播放，淡入 0.100 s、自然淡出 0.080 s，攻击期间暂停常规移动 |
| `Gameplay/Dodge`（右键） | 允许从移动或攻击进入闪避，调用 `PlayDodge(0.080f)`。同帧两个按钮均按下时优先闪避，只消费一次。闪避期间忽略再次闪避和攻击，返回 Locomotion 后恢复接收 |
| `System/Pause`（Esc） | 在 CombatDemo 中释放光标、清零移动输入和按钮边沿，暂停采集玩家命令。下一次在 Game 视图左键重新捕获，该次点击仅用于捕获。全局暂停和顿帧测试直接通过 BattleClock 驱动 |

**移动与朝向。** `ThirdPersonController` 的 `float walkSpeed, runSpeed` 是玩法速度，菲比原型初值分别取 1.43936、4.91111 m/s；`turnSpeed` 初值 720°/动作秒。将平面输入 `(x,y)` 旋转为世界方向 `view.PlanarRotation × (x,0,y)`。`CharacterMotor.MovePlanar(Vector3 delta, Vector3 facing, float maxTurnDegrees)` 接收本步世界水平位移（米）、期望朝向和本步最大转角，通过同根 `CharacterController.Move(delta)` 约束平移；有朝向时只绕 Y 轴转向。返回根节点移动前后的实际水平位移（Vector3）。常规移动的 `delta = 世界输入 × 目标速度 × actionDelta`，将返回值的模长除以本步动作秒后传给 SetGroundSpeed。由此，同一时钟控制移动距离和动画时间，持续顶住正面墙时步态回到待机。[Unity CharacterController.Move](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/CharacterController.Move.html)

**闪避。** 接受命令时锁定世界方向，有移动输入取相机相对方向，松开方向键时取角色当前水平前方。Motor 在起始步以最大转角 180° 转向该方向。控制器的 `float dodgeDistance = 2 m`、`float dodgeTravelTime = 0.20 s` 是平地原型调参值。运行时只记录 `Vector3 dodgeDirection` 和 `double travelTimeRemaining`。每步用 `min(actionDelta, travelTimeRemaining)` 计算突进位移，经同一 Motor 处理碰撞，时间用完即停止推进；遇墙消耗本步时间，余程不会在离墙后补走。剩余收势由 Dodge 片段表现，直到 FSM 返回移动。整次闪避传给走跑树的速度为 0。位移时长和片段时长分别决定突进结束与恢复操控的时刻。闪避无敌窗口由后续战斗规则定义。

**谁先执行。** 控制器使用执行顺序 −250，在输入系统的动态更新后、`BattleTimeDriver.Update`（−200）前采样本帧移动、Shift 与按钮边沿，先应用 Look，再保存移动意图。唯一的战斗时钟推进后，`ActorAnimationBinding` 在自己的订阅回调中取本角色的动作步。玩家根先调用 `ThirdPersonController.Step(driver, actionDelta)` 处理动作、Motor 和 SetGroundSpeed，随后调用该角色图的 Advance。祀直接推进自己的图。按钮边沿只在采样帧消费一次，动作秒为 0 时丢弃本帧边沿、保持角色位置及姿态。相机在 LateUpdate 读取已移动的根位置。这样同一帧转动镜头再按 W，会使用新朝向，也避免 Motor 和图的先后依赖两个订阅者的注册顺序。

## 6. 自定义第三人称相机

`ThirdPersonCamera` 挂在 CombatDemo 现有的透视 `Main Camera` 上。跟随点从玩家逻辑根实时计算，模型在跑步、挥剑时的骨骼摆动不会带动镜头。`Initialize(Transform player)` 保存目标，yaw 取玩家初始水平朝向，pitch 取钳制后的 initialPitch，并标记下一帧直接定位。`ApplyLook(Vector2 deltaPixels)` 将鼠标像素增量应用一次；`PlanarRotation` 返回 `Quaternion.Euler(0, yaw, 0)` 供控制器读取。

| Inspector 字段 | 初值和含义 |
| --- | --- |
| `float height, distance` | 观察点位于根上方 1.5 m，无障碍跟随距离 4 m |
| `float sensitivity, initialPitch, minPitch, maxPitch` | 0.1°/像素，初始俯角 15°，允许 −50°～70°。水平向右增加 yaw，鼠标向上减少 pitch。鼠标增量直接乘灵敏度 |
| `float collisionRadius, collisionPadding, recoverySpeed` | 检测半径下限 0.2 m，命中后保留 0.03 m，离开遮挡后每个真实秒最多拉远 4 m |
| `LayerMask obstructionMask` | 原型勾选 Environment 层。地面和实体墙属于此层，玩家、祀及 Trigger 均排除在相机遮挡检测之外 |

每次 LateUpdate 依次执行下列操作。只读 `float SafeDistance, CurrentDistance` 和 `bool PivotOverlapping` 公开本帧结果，便于定位墙边镜头行为。启用或重新绑定目标后的第一帧直接取安全距离。

1. 取 `pivot = player.position + up × height`、`rotation = Euler(pitch, yaw, 0)` 和 `back = rotation × Vector3.back`。yaw 环绕 360°，pitch 限制在 Inspector 范围内。
2. 设透视相机近裁面距离为 `n`，半高 `h = n × tan(垂直视野角 / 2)`，半宽 `b = h × aspect`。球半径取 `max(collisionRadius, sqrt(n² + h² + b²))`，使球包住近裁面四角。
3. 先以该半径在 pivot 做 `CheckSphere`。若起点重叠，置 PivotOverlapping 为 true、SafeDistance 为 0，镜头收至观察点。其余情况沿 back 做最远 distance 的 SphereCast。命中时取 `max(0, hit.distance − collisionPadding)`，畅通时取 distance。两次查询使用同一遮挡层和 `QueryTriggerInteraction.Ignore`。观察点本身陷入墙体时，0 距离是该算法的表现边界。
4. 障碍变近时当帧取 SafeDistance。空间变大时用 `MoveTowards(CurrentDistance, SafeDistance, recoverySpeed × Time.unscaledDeltaTime)` 平滑拉远。最后将相机设在 `pivot + back × CurrentDistance`、朝向 rotation。这里沿用历史实现的遮挡算法，恢复时间取真实帧间隔，让单人顿帧时的观察与环绕仍然连续。

## 7. Unity 装配与生命周期

1. **装配动画。** 编辑 Phoebe_Controller，按第 3 节加入 Dodge。创建 Si_Locomotion.overrideController，原控制器选 Phoebe_Controller，依次替换 GS_Idle、GS_Walk、GS_Run、GS_Evade 为对应的 DK 片段。攻击由调用方持有 FBX 中的同名 AnimationClip。
2. **定位场地和开场单位。** 打开 `Assets/Scenes/CombatDemo.unity`。在 Hierarchy 中定位 `Combat Session` 和 `Characters` 下已登记的两名角色。平地走跑使用 `Environment/Plane`（MeshCollider），碰撞与相机收近使用 `Environment/Wall`（BoxCollider）。沿用已有的 Directional Light。两个模型 Animator 保留各自 Avatar，Apply Root Motion 设为关闭，Culling Mode 设为 Always Animate；将菲比模型上现有的 Controller 引用交给玩家根的 ActorAnimationBinding，再把模型 Animator 的 Controller 留空，确保姿态只由手动图输出。
3. **接玩家、相机与祀。** 两个角色根各加一份 ActorAnimationBinding，Time Driver 指向 `Combat Session`，移动控制器和走跑标定按第 1 节分别赋值。在 PlayerRoot 加 ThirdPersonController、CharacterMotor，复用现有 Player 层和 CharacterController（height 1.8 m、radius 0.30 m、center (0, 0.9, 0)、minMoveDistance 0）。在现有 `Main Camera` 上用 ThirdPersonCamera 替换已失效的旧相机脚本组件，沿用 Camera、AudioListener 和当前已禁用的 CinemachineBrain。将新相机组件拖到玩家 View，指定 Combat 输入资产、普攻 GS_Attack1 和重击 GS_Attack2。控制器 Start 时以 PlayerRoot 为跟随目标；SiRoot 使用自己的动画绑定供对照。
4. **首次绑定。** SceneActors.Start（−10）完成登记后，ActorAnimationBinding 以默认顺序在 Start 读取有效的 Runtime.Id，持有模型、角色图与 `SubscribeBattleStep` 订阅。缺少有效 Avatar、控制器、时钟或角色登记时报告角色及缺项。回调使用 `TryGetActionTime(id, out step)`。战斗步只由 BattleTimeDriver 或测试中控制同一时钟的一处调用者提交。
5. **禁用和离场。** 角色临时禁用时，绑定释放订阅、保留图和游标；控制器清除输入。重新启用且 ID 仍有效时订阅一次，用 Evaluate(0) 恢复当前姿态，跳过隐藏期间的时长。重新捕获输入时沿用镜头朝向。场景卸载、角色时钟查询失败或组件销毁时释放仍持有的订阅和图、清空 Driver；建图中途失败也清理已创建的图。相机失去目标时停止跟随，重新指定目标再直接定位。

## 8. 实施顺序与验收

先用真实 Avatar、基础控制器和 Override 验证步态与 Dodge，再接单槽攻击和角色时钟。接着实现平地 Motor、第三人称输入和相机，在 CombatDemo 跑通“绕视 → 前进 → 攻击 → 右键取消 → 闪避回移动”。开发者可在 **Window → General → Test Runner → EditMode** 运行第 2 节列出的四组测试，场景用例进入 Play Mode 后加载 `Assets/Scenes/CombatDemo.unity`，按第 7 节的层级查找现有对象。需要改变墙或角色位置的用例只调整 Play Mode 实例；相机查询前调用 `Physics.SyncTransforms()`。每次重新加载 CombatDemo 均使用保存的开场布局。时钟用例等场景 Start 完成后停用 BattleTimeDriver 的自动 Update，使用同一个 Clock 输入固定步长，结束时退出 Play Mode 并清理夹具。

| 验收项 | 初始条件与操作 | 可观察结果 |
| --- | --- | --- |
| 双角色移动标定 | 独立动画夹具依次输入 0、本角色 W、本角色 R。另给两人相同的 4.91111 m/s | 三端点分别呈现各自的 Idle、Walk、Run；相同速度时菲比为纯跑、祀为约 0.638 跑权重的走跑混合。参考控制器直接设置对应 MoveAmount，从相同归一化相位比较大腿或上臂局部旋转，建议误差 ≤ 0.5° |
| 单槽攻击与闪避取消 | 在菲比跑步姿态执行第 4 节 0.100 s 淡入、0.080 s 闪避取消算例，用独立参考图在相同片段和 FSM 时刻设定预期权重 | 先看到移动/攻击 0.75/0.25，中途取消时看到闪避/旧攻击 0.5/0.5；再进 0.040 s 只有闪避。CurrentClip 自接受闪避起为空。两名角色分别播放 Dodge，在自己的片尾后再推进一个短步均回 Locomotion |
| 单槽替换、自然结束及回收 | 从满权重 Attack1 直接向 Driver 提交 Attack2，比较新片段首段姿态。另让 DK_Attack1 以 2 倍速播放、fadeOut = 0.080 s，距片尾尚余 0.020 动作秒时输入 0.060 s，并与 0.020 + 0.040 s 对照 | 替换后只采样 Attack2，新游标从 0 起。两种跨尾步长均得到基础/攻击末姿 0.5/0.5，CurrentClip 为空，再推进 0.040 s 完全恢复基础。重复十次播放、退出，回到纯基础时图节点数均恢复为建图后的基线 |
| 相机相对移动、跑速与墙 | 空旷平地，初始 yaw = 0°、角色面向 +Z。输入鼠标水平 +900 px、随后持续 W 共 0.2 动作秒，再在相同位置测 W+Shift、W+D。最后面向平墙持续顶住 | 新 yaw 为 90°，W 沿世界 +X。菲比直行分别约 0.288 m、0.982 m，W+D 的速率与同档直行一致。碰撞阻止前进后，实际速度接近 0、画面回到待机。只转动镜头时角色位置和朝向保持 |
| 输入按压与动作仲裁 | 空地单击左键，再等收势后长按左键。分别在长按期间闪避、释放光标或暂停战斗。重新捕获时按住左键超过阈值 | 单击立即播放 GS_Attack1；长按从 GS_Attack1 切换至 GS_Attack2，各一次。闪避接管后旧手势失效；捕获点击和冻结步收到的命令不会在恢复时出招 |
| 操控与闪避距离 | 开场找到一段空地，W+右键后松开 W 并转动镜头，推进 0.10、0.10 动作秒；再在同一位置改为无方向输入。另在墙前 1 m 右键，及攻击中同时按左右键 | 空地沿起始方向累计约 1 m、2 m，镜头转动不改变突进方向；无方向时沿角色前方。遇墙不能穿过，收势期间不会补足位移。左右键同帧由闪避接管。片尾后 WASD 恢复移动 |
| 遮挡、俯仰与恢复 | 让墙进入观察点与相机之间，再移开。另将测试障碍放到观察点；独立用例将 pitch 推过两端。以 0.10 真实秒观察无障碍恢复 | 命中当帧 CurrentDistance 收至 SafeDistance，重叠时两者为 0。移开后 0.10 s 最多拉远 0.40 m，最终恢复 4 m。pitch 被限制在 −50°、70°，角色自身和 Trigger 不引起收近 |
| 角色时钟与相机独立 | 让两人各播 Attack1，推进到上臂运动明显处。给菲比 0.030 s 顿帧，再连续两步 Advance(0.020)。随后全局暂停一正步，恢复、设置 battleRate = 0.5 后推进 0.040 s。顿帧中用鼠标环绕 | 前两步菲比累计 0.010 动作秒、祀累计 0.040 s，菲比首步姿态保持。全局暂停步两人游标及权重保持，半速步各增 0.020 s；顿帧时镜头仍能环绕。单独跨一个未提交动作步的渲染帧，图游标保持 |
| 禁用与场景重开 | 祀播放中禁用其逻辑根，推进时钟再启用，观察恢复前后的姿势并推进 0.020 s。在 Play Mode 重新加载 CombatDemo | 祀保留 ID、从原姿态与游标续播，恢复步只增 0.020 s；菲比持续响应。重开后两人从待机开始，相机首次定位到安全距离，一次按键产生一次动作，无失效 Playable 报错 |

**设计阶段核查记录 · 2026-11-23。** 已核对工程中的输入绑定、遮挡层、历史相机源码、现有角色与控制器，并在当前 Unity 编辑器复读十二个片段的长度、循环标记和原生速度。此前通过的骨骼与时钟测试属于 M1.4/M3。上表列出 M4 的验收标准，实测记录见第 9 节。

## 9. 实施与验证记录 · 2026-11-23

M4 首次实现在 Unity 6000.3.28f1 的 Test Runner 通过了 14 项定向用例。接入战斗输入模块后新增 3 项，复跑共 **17 项，全部通过，0 项失败或跳过**。其中 M4 覆盖 10 项，既有场景回归 7 项。场景验收均进入 Play Mode 加载现有 CombatDemo。

| 测试组 | 通过数 | 实际覆盖 |
| --- | ---: | --- |
| CharacterAnimationGraphTests | 4 | 双刀走跑的端点和中间混合、Override 闪避片尾返回、跑步淡入攻击与取消到闪避的上臂姿态。自然退出、主动退出、零时长硬切与参考姿态误差在 0.5° 内；十轮替换及退出后节点数均回到基线 |
| ActorAnimationSceneTests | 1 | 给菲比 30 ms 顿帧，两个 20 ms 步内菲比推进 10 ms、祀推进 40 ms。全局暂停、半速、临时禁用后恢复游标和姿态、重载场景后清理旧 ID 均通过 |
| ThirdPersonControlTests | 3 | 键鼠事件驱动相机相对移动、斜向奔跑、闪避及贴墙停止。空地闪避的两个 0.10 s 步分别累计约 1 m、2 m，主相机通过遮挡及俯仰检查。角色动作速率设为 0.25 后，左键按下即播放 GS_Attack1，持续按住按真实时间切到 GS_Attack2，继续按住不重启片段。验证了暂停时的请求丢弃及闪避优先 |
| CombatInputReaderTests | 2 | 使用真实 Combat 输入资产和虚拟键鼠，分别验证短按、长按只追加一次重击、松开与重复消费均不重复出招、同次采样保留多种请求。Esc 同帧优先，重新捕获的点击持续超过阈值也不会出招，释放捕获、禁用及闪避均能清理手势 |
| ActorSceneTests / BattleClockSceneTests / ResourceSceneTests | 3 / 2 / 2 | 回归同场角色登记与重载、独立动作时钟、资源和 HUD 行为 |

手动图进入 Dodge 后先以 0 秒采样首姿，再消费本次正动作步。验证中发现 Unity 首次求值会用于切换控制器状态，这一处理使首个动作步也计入闪避片段。脚部接地期间的世界坐标漂移和玩家对操控手感的评价未计入上述数值验收。
