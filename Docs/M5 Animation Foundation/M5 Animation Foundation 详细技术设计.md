# M5 Animation Foundation 详细技术设计

**开发收尾 · 2026-11-27**　CombatDemo 已装配双角色步态和自然根运动前闪，交付动画通知、Timeline 预览与通用运动扭曲工具。第 1～6 节为当前实现和 M6 交接，第 7～16 节保留各轮验收记录，第 17 节记录收尾核对

队员 FSM 决定何时进入或退出闪避，角色动作钟提供时间，CharacterMotor 执行最终碰撞位移。M5 提供可用于闪避、表演及后续攻击的动画能力。Attack 请求已经由输入传入角色动作步，M6 Actions 接入攻击执行，M7 Combos 选段 · [M4 操控详设](../M4%20Third%20Person%20Control/M4%20第三人称控制器、相机与基础移动%20FSM%20详细技术设计.md) · [总体路线](../仿终末地战斗系统设计方案.md)

## 1. 角色与资源基线

| 资源与装配 | 菲比 | 祀 |
| --- | --- | --- |
| CombatDemo 逻辑根 → 模型 Animator | `Characters/PlayerRoot` → `Phoebe` | `Characters/SiRoot` → `Si` |
| 模型 Avatar 与 M4 步态控制器 | Humanoid `PhoebeAvatar`、Phoebe_Controller | Humanoid `SiAvatar`、Si_Locomotion.overrideController |
| 待机、走、跑 | `GS_Idle / GS_Walk / GS_Run` | `DK_Idle / DK_Walk / DK_Run` |
| 走、跑的原生水平速度 | 1.43936、4.91111 m/s | 1.54665、6.82405 m/s |
| 单次向前闪避 | `GS_Quickshift_F`，1.0000 s | `DK_Quickshift_F`，1.0000 s |
| 前闪允许移动取消 | 第 6 帧，0.20 片段秒 | 第 15 帧，0.50 片段秒 |
| 旧原型片段（历史资源数据） | `GS_Attack1 / GS_Attack2`，约 3.1333 / 2.8333 s | `DK_Attack1 / DK_Attack2`，约 1.5333 / 1.9000 s |

表中闪避片长已在第 5 节重新采样。其余片段时长、原生速度和循环标记取自用户提供的旧版设计，其数值曾在 Unity 6000.3.28f1 中读取。十二个片段均为 30 fps 的 Humanoid，待机、走、跑循环，两套闪避和攻击均单次播放。速度取 1 倍速下 `AnimationClip.averageSpeed` 在 XZ 平面的模长。运行时的片尾以 `clip.length` 为准。现有基础控制器以 `Locomotion` 为默认状态，其一维树使用阈值 0、1、2，状态速度参数 `PlaybackRate` 默认 1。[基础控制器](../../Assets/Game/Animations/Characters/Controllers/Phoebe_Controller.controller) · [大剑片段](../../Assets/Game/Animations/Characters/Clips/GreatSword/Normal/) · [双刀片段](../../Assets/Game/Animations/Characters/Clips/DualKatana/Normal/)

每个队员的模型 Animator 由 `AnimationSource` 持有手动图，角色根保留两项步态速度标定。Controller 选择实际的 Quickshift_F 片段，队员状态消费可见 Animator 本段求值的自然根运动。M1.4 提供经验证的角色动作时钟，M3 提供 Animator 与登记身份 · [CombatDemo](../../Assets/Scenes/CombatDemo.unity) · [M1.4 时钟记录](../M1%20Core%20Data%20Foundation/M1.4%20CombatContracts%20与%20Timing%20详细技术设计.md) · [M3 角色交接](../M3%20Actor%20Foundation/M3%20Actor%20Foundation%20详细技术设计.md#7-交给-m4-与-m5-的接口)

**参考工程与取舍**　参照 ARPG Demo 的 AnimationSource，由模型 Animator 的回调接收增量，角色动作钟推进手动图，独立的 MotionWarpingAsset 保存需要预测的窗口运动

通用 `AnimationDefination` 供单次 Humanoid 片段配置通知和运动开关。需要扭曲的片段另配 `MotionWarpingAsset`，由该资产持有采样模型和窗口摘要。M5 交付求解器和制作工具，运行时动作接入按第 6.2 节推进

## 2. 实施文件与迁移

以下清单列出交付文件和迁移来源，路径均相对项目根目录。第 8 节记录迁移前的前闪基线

| 变更 | 路径 | 职责 |
| --- | --- | --- |
| 替换类型并迁移 | `Assets/Scripts/Runtime/Animation/DodgeRootTrajectory.cs` → `Assets/Scripts/Runtime/Animation/AnimationDefination.cs` | 定义片段、运动开关、扭曲资产引用和通知 |
| 替换编辑器 | `Assets/Scripts/Editor/Animation/DodgeRootTrajectoryEditor.cs` → `Assets/Scripts/Editor/Animation/AnimationDefinationEditor.cs` | 编辑动画 SO，核对配置，提供指定模型采样和通用 Timeline 入口 |
| 新增 | `Assets/Scripts/Editor/Animation/PartyMemberAnimationEditor.cs` | 在队员 Inspector 核对闪避配置与 Controller 片段，同文件的 MotionWarpingAssetEditor 提供通用扭曲资产烘焙入口 |
| 新增 | `Assets/Scripts/Editor/Animation/AnimationTimelineEditor.cs` | EditorWindow 持有当前资产和播放头，编辑轨道、处理 Undo/Redo，将播放头同步到绑定的 Scene 角色 |
| 新增 | `Assets/Scripts/Editor/Animation/AnimationTimelinePreview.cs` | 持有编辑器专用 AnimationPreviewSettings 和预览图，显示 Scene 姿态、轨迹及测试目标，退出时还原角色 |
| 新增 | `Assets/Scripts/Runtime/Animation/AnimationNotify.cs` | 定义独立的点通知与窗口通知类型及其行为回调，供角色播放、Inspector 和 Timeline 共用 |
| 新增 | `Assets/Scripts/Runtime/Animation/AnimationNotifyPlayer.cs` | 单次播放的通知索引、游标和活动窗口，复用 Common 同步分发通知 |
| 新增 | `Assets/Scripts/Runtime/Animation/AnimationSource.cs`、`Assets/Scripts/Runtime/Animation/MotionWarping/MotionWarpingAsset.cs` | 可见模型提供动画段增量，选用扭曲的动作另配窗口资产 |
| 删除 | `Assets/Scripts/Runtime/Animation/ClipRootMotion.cs`、`Assets/Scripts/Runtime/Animation/ClipRootMotion.cs.meta` | 原运行时独立运动骨架及片段图 |
| 新增 | `Assets/Scripts/Runtime/Animation/MotionWarping/MotionWarping.cs` | 提供 BeginWarp、UpdateWarp、EndWarp，以及目标与水平运动结果值类型 |
| 已合并到 AnimationSource | `Assets/Scripts/Runtime/Animation/AnimationGraph.cs` | 创建手动图并将 AnimatorControllerPlayable 直接连接到模型 Animator |
| 修改 | `Assets/Scripts/Runtime/CharacterControl/PartyMemberControl.cs`、`Assets/Scripts/Runtime/CharacterControl/FSM/CharacterState.cs`、`Assets/Scripts/Runtime/CharacterControl/FSM/States/PartyMember/PartyMemberGroundedState.cs` | 初始化队员动画源，提供状态打断入口，地面选态检查 Dodge 动画状态 |
| 新增 | `Assets/Scripts/Runtime/CharacterControl/FSM/States/PartyMember/PartyMemberDodgeState.cs` | 通过 IsDodging 控制切换，消费 Controller 实际播放的进度和增量，处理通知取消 |
| 修改资源 | `Assets/Game/Animations/Characters/Controllers/Phoebe_Controller.controller`、`Assets/Game/Animations/Characters/Controllers/Si_Locomotion.overrideController` | 为 Locomotion 和 Dodge 配置双向参数过渡，为祀覆盖实际的 Quickshift_F 片段 |
| 修改 | `Assets/Scripts/Runtime/CharacterControl/CharacterControl.cs`、`Assets/Scripts/Runtime/CombatSession/ActorStepBinding.cs` | 提供每角色的通知播放器，按角色时钟驱动当前状态 |
| 修改 | `Assets/Scripts/Runtime/CharacterMotion/CharacterMotor.cs` | 接收求值后的世界水平位移与最终水平朝向，由 CharacterController 应用碰撞 |
| 删除 | `Assets/Scripts/Runtime/CombatSession/ActorAnimationBinding.cs`、`Assets/Scripts/Runtime/CombatSession/ActorAnimationBinding.cs.meta` | 可见模型的动画图由 AnimationSource 接管 |
| 迁移资产 | `Assets/ScriptObjects/Animation/PhoebeDodgeTrajectory.asset` → `Assets/ScriptObjects/Animation/PhoebeQuickshiftF.asset`、`Assets/ScriptObjects/Animation/SiDodgeTrajectory.asset` → `Assets/ScriptObjects/Animation/SiQuickshiftF.asset` | 两份 AnimationDefination 分别引用正式前闪 Clip |
| 核对并保存装配 | `Assets/Scenes/CombatDemo.unity` | 两个队员根分别引用新资产，保留原有 Animator、步态控制器与时钟接线 |
| 迁移及扩展测试 | `Assets/Scripts/Test/Editor/Animation/DodgeRootTrajectoryTests.cs` → `Assets/Scripts/Test/Editor/Animation/AnimationDefinationTests.cs`、`Assets/Scripts/Test/Editor/Animation/AnimationGraphTests.cs`、`Assets/Scripts/Test/Editor/Animation/ActorAnimationSceneTests.cs`、`Assets/Scripts/Test/Editor/CharacterMotion/ThirdPersonControlTests.cs` | 保留前闪基线，增加通用采样、曲线、逻辑与显示独立性验证 |
| 新增测试 | `Assets/Scripts/Test/Editor/Animation/AnimationNotifyPlayerTests.cs`、`Assets/Scripts/Test/Editor/MotionWarping/MotionWarpingTests.cs`、`Assets/Scripts/Test/Editor/Animation/AnimationTimelineEditorTests.cs` | 分别验证通知次序与中断、扭曲输出与实际碰撞、Timeline 编辑、场景角色采姿和退出还原 |

两份正式前闪保留各自 GUID 和片段引用，均使用自然位移，动作 SO 开启根运动并关闭扭曲

| 直接复用 | 文件 |
| --- | --- |
| 切换机制 | `Assets/Scripts/Runtime/CharacterControl/FSM/StateMachine.cs` |
| 地面状态的移动行为 | `Assets/Scripts/Runtime/CharacterControl/FSM/States/PartyMember/PartyMemberIdleState.cs`、`Assets/Scripts/Runtime/CharacterControl/FSM/States/PartyMember/PartyMemberWalkState.cs`、`Assets/Scripts/Runtime/CharacterControl/FSM/States/PartyMember/PartyMemberRunState.cs` |
| 输入、角色时间、同步通知 | `Assets/Scripts/Runtime/Input/GameInputManager.cs`、`Assets/Scripts/Runtime/CharacterControl/PlayerController.cs`、`Assets/Scripts/Runtime/Timing/BattleClock.cs`、`Assets/Scripts/Runtime/Common/ChangeNotifier.cs` |
| 角色 Avatar 与采样模型 | `Assets/Prefabs/Characters/Phoebe.prefab`、`Assets/Prefabs/Characters/Si.prefab` |
| 实际闪避片段 | `Assets/Game/Animations/Characters/Clips/GreatSword/Special/GS_Quickshift_F.fbx`、`Assets/Game/Animations/Characters/Clips/DualKatana/Special/DK_Quickshift_F.fbx` |
| M4 生命周期与重入回归 | `Assets/Scripts/Test/Editor/CharacterControl/StateMachineTests.cs` |

## 3. 动画 SO、指定模型采样与姿态

### AnimationDefination 的配置与数据

每份 AnimationDefination 保存片段、根运动开关、通用扭曲配置及通知。运行时的前闪由 Controller 选片，闪避状态按当前 Clip 匹配动画 SO。预览倍率和淡化由 Timeline 会话持有

| 成员 | 类型、初值与使用方式 |
| --- | --- |
| `clip` | 单次 Humanoid 片段，场景中使用各自的可见 Animator，预览模型由编辑器选择 |
| `useRootMotion` | 开启时向 Motor 提交可见 Animator 本段求值的自然位移和朝向，新资产默认关闭，两个正式前闪显式开启 |
| `useMotionWarping`、`warpAsset` | 通用动作的独立开关与可选 MotionWarpingAsset 引用，默认关闭。通用 Timeline 根据它们显示扭曲窗口和预览目标 |
| `pointNotifies`、`windowNotifies` | 按片段帧配置的点与窗口通知，初始为空 |

前闪使用本角色的可见 Animator。PartyMemberDodgeState.Enter 设置 IsDodging，沿用进入前的 MoveAmount 作为淡入的步态参数。动画源的 BeginClip 先执行零秒求值，再按传入的基础层状态完整路径哈希绑定实际 Clip 和有效倍率，重置本次播放的自然累计偏航。若当前状态和目标状态都是 Dodge，优先读取目标状态的片段和进度，以便淡出中再次闪避从第 0 帧开始。EvaluateSegment 用正动作秒推进 Controller，返回本段消耗的动作秒、实际片段端点及自然平面运动。DodgeAnimation 是队员控制组件上的可选 AnimationDefination，匹配本次实际 Clip 时读取根运动开关并按帧范围启用通知。合法片段缺少匹配配置时角色原地播放至结束

**编辑器采样**　动画 Inspector 可临时选择模型查看轨迹。MotionWarpingAsset 的 Inspector 提供「烘焙扭曲窗口」，片段、采样模型、窗口和摘要只存入该资产，窗口端点或源资源变更会使摘要失效

地面状态检查可见动画源、Humanoid 和 Controller 中的 Dodge 状态。闪避进入时读取 Controller 实际选中的单次片段及正播放倍率。原地片段也可进入闪避并推进通知，实际位移取决于本段求值

### Graph 与基础步态

AnimationSource 持有一张手动 PlayableGraph 和一个 AnimatorControllerPlayable，输出连到本角色模型。菲比的 Controller 在 Locomotion 和 Dodge 之间按 IsDodging 双向切换。Locomotion 使用原走跑混合树，Dodge 播放 GS_Quickshift_F，祀的 Override 映射到 DK_Quickshift_F

Controller 关闭两条过渡的 Has Exit Time，由 IsDodging 控制切换。两份正式前闪共用 Fixed Duration，Locomotion 到 Dodge 淡入 0.05 动作秒，Dodge 回 Locomotion 淡出 0.08 动作秒。两条过渡均允许目标状态的反向过渡打断，覆盖淡入时取消和淡出时重按闪避。Dodge 从归一化进度 0 开始，淡入与 Clip 同时推进。片段进度由状态的 normalizedTime 和 Clip 长度换算

Timeline 窗口持有编辑器专用的 AnimationPreviewSettings。speed 默认 1，单位为片段秒／预览秒，窗口更新播放头时读取。fadeInDuration 和 fadeOutDuration 默认 0，单位为片段秒，两条曲线默认线性 0→1、1→0，由当前姿态采样读取。Inspector 或窗口调整通知时只校验片段和通知帧，预览倍率和曲线的校验结果显示在 Timeline

预览在 t 处对进入曲线取 t/fadeInDuration，对退出曲线取 (t−(L−fadeOutDuration))/fadeOutDuration。各自进度钳至 [0,1]，零时长的一侧取权重 1，两条曲线重叠时取较小权重。例如 L=1 s、线性淡入 0.10 s、线性淡出 0.20 s，t=0.025、0.90 s 的权重分别为 0.25、0.50。运行中的淡化使用 Controller 的过渡参数

PartyMemberControl 在 Inspector 保存本角色的走速标定 W、跑速标定 R（0 < W < R，m/s）。它将 Motor 碰撞后的实际水平速度 v 换算成无单位的 MoveAmount，再通过 AnimationSource.SetFloat 写入参数，沿用阈值 0、1、2 的树

| 输入 | MoveAmount |
| --- | --- |
| `v ≤ 0` | `0` |
| `0 < v ≤ W` | `v / W` |
| `W < v < R` | `1 + (v − W) / (R − W)` |
| `v ≥ R` | `2` |

例如，同样传入 4.91111 m/s，菲比得到 2，祀得到 `1 + (4.91111 − 1.54665) / (6.82405 − 1.54665) ≈ 1.638`。走跑标定描述动画的步态。玩家控制器单独配置玩法速度，撞墙后以 Motor 实际走出的距离计算 `v`。闪避位移取第 5 节的片段轨迹，期间 GroundSpeed 取 0。

## 4. 动画通知、运动扭曲与制作工具

### 4.1 动画通知使用每次播放的运行时索引

通知属于动画。一次性通知可表示前闪起步音，持续通知可表示身后拖尾的开启与关闭。同一机制可供 M6 订阅命中窗口。M5 提供派发与生命周期，声音、特效、命中等接收方自行响应。现有 Locomotion 树仍按速度混合，本节通知适用于由 AnimationDefination 驱动的片段

| 配置或运行成员 | 数据及所有权 |
| --- | --- |
| `AnimationPointNotify`、`AnimationWindowNotify` | 点通知在 `pointFrame` 调用 `TriggerNotify`，要求 0 ≤ pointFrame ≤ 末帧。窗口在 `startFrame` 调用 `OnEnterNotify`，在 `endFrame` 调用 `OnExitNotify`，严格位于两者之间的每个片段帧调用一次 `OnTickNotify`。要求 0 ≤ startFrame < endFrame ≤ 末帧。两种类型各自保存 `signal`，资产用独立的 `[SerializeReference]` 列表保存。默认实现发布信号，可派生具体行为，子类将播放状态保存在角色或动作实例上 |
| `AnimationNotifyPlayer` | 每个 CharacterControl 持有一份。`Begin` 将点通知、窗口通知依配置顺序收进同一份条目表，为非空条目分配从 0 开始的连续 `RuntimeIndex`，保留配置引用并快照信号与帧边界。边界使用同一个索引，按帧、阶段、索引排序 |
| `playId`、通知游标、活动窗口 | `playId` 为每个播放器递增的序号。空列表或只有空条目时仅建立播放身份。有有效通知时建立 NotifyTrack，边界游标初始为 0，已扫描帧初始为 −1，活动集合维护已进入且尚未退出的窗口。接收者以 `(ActorId, PlayId, RuntimeIndex)` 区分角色、播放和同名条目 |
| `Subscribe(Action<AnimationNotice> listener) → IDisposable` | 每个播放器持有独立的 `ChangeNotifier<AnimationNotice>`。默认回调同步发布 `AnimationNotice`，载荷含 `ActorId`、角色对象 `User`、`PlayId`、`RuntimeIndex`、`Signal`、`Frame`、阶段和退出原因。派生通知可调用基类回调继续发布信号。订阅者保存句柄，在订阅生命周期结束时调用 `Dispose()`。单个监听器抛异常时记录异常，并继续分发当前通知 |

**推进规则**

1. `Begin(AnimationDefination asset, ActorId actor, GameObject user = null) → playId` 建立播放身份，首个实际条目触发索引和边界表的构建。退出旧播放的回调若先启动了新播放，本次 Begin 返回 0。空播放正常返回有效 ID 并参与替换、IsPlaying 和 End。有通知的首段在移动前派发第 0 帧。帧 k 对应 min(k / clip.frameRate, L)，末帧取覆盖 L 的最后一个采样编号，与烘焙器一致
2. `AdvanceTo(long playId, double clipTime)` 接收绝对片段秒。片尾前由 FrameBefore 换算至最多 LastFrame−1，到达 Duration 才处理 LastFrame。过期 ID、负值、重读和倒退都保持游标。有活动窗口时逐帧处理，其余时间直接跳至下一个边界。正常推进后已扫描帧记到目标帧，包括空白区间。同帧依次发 Point、Enter、Tick、Exit，各组内按 RuntimeIndex 升序
3. `End(playId, reason)` 在自然结束时先消费末帧。打断时跳过尚未触发的条目，给活动窗口补一次 Interrupted Exit。进入或退出边界先更新游标和活动集合，再调用订阅者。回调结束或重播时停止扫描旧播放。清理窗口前先解除旧播放的当前身份，因此窗口退出回调可独立开启新播放
4. 逻辑通知随角色正动作步派发。临时禁用角色根沿用 M4 的暂停和原 ID 恢复。真正离场、死亡终止动作或替换播放时，拥有者结束本次播放并释放持续效果

30 Hz 前闪在第 3 帧设两个同名点通知（索引 0、1），窗口 A（索引 2）覆盖第 3～6 帧，窗口 B（索引 3）覆盖第 4～7 帧。推进到第 7 帧，第 3 帧依次收到 Point(0)、Point(1)、Enter(2)，第 4 帧收到 Enter(3)、Tick(2)，第 5 帧收到 Tick(2)、Tick(3)，第 6 帧收到 Tick(3)、Exit(2)，第 7 帧收到 Exit(3)。若第 4 帧后打断，已打开的两个窗口各收到一次带旧播放号的 Interrupted Exit

### 4.2 M5 交付平面运动扭曲实现，M6 提供目标

MotionWarpingAsset 保存片段、采样模型和 `WarpWindow { int startFrame, endFrame; Vector2 targetOffsetXZ; bool alignYaw; }`。窗口时间满足 0 ≤ startFrame < endFrame ≤ 末帧，按开始帧升序，允许首尾相接，禁止重叠。偏移以目标朝向为基准，单位米，默认零。alignYaw 默认 false。窗口数据与根采样共用片段时间轴。前闪直接消费自然根运动，通用动作可选择扭曲求解器

| M5 公开 API | 参数、结果与调用时机 |
| --- | --- |
| `bool Matches(AnimationClip playingClip, Animator actor)` | 动作用实际片段及可见 Animator 核对扭曲资产。片段、Avatar 和采样模型缩放匹配时返回 true |
| `bool BeginWarp(MotionWarpingAsset asset, IReadOnlyList<WarpTarget> targets)` | 由动作拥有者在开始时调用其独立的 MotionWarping 实例。目标按窗口序号配对，WarpTarget 可取 Transform，或固定世界位置和水平朝向。目标数不符、窗口运动摘要缺失或过期时返回 false，本次按根运动开关决定是否移动。该对象保存当前窗口序号及目标，初始未激活 |
| `PlanarMotion UpdateWarp(double from, double to, Pose actorPose, Quaternion basisYaw, PlanarMotion clipMotion)` | from/to 为本次子区间的片段秒。传入实际角色位姿、片段基准朝向及本段实时求值结果，返回该区间完整的世界 XZ 位移及期望水平朝向。每步先在源片段帧和窗口边界切开，窗口外取原轨迹。未开始扭曲时由调用方直接取原轨迹。调用方把结果交给 Motor.MovePlanar 一次 |
| `void EndWarp()` | 完成、打断或离场时由开始者调用，释放目标引用和本窗口的修正量。重复调用保持空闲 |

**窗口内如何计算。** 对 [s,e]，在进入 s 时记录实际角色位置 x、基准旋转 B，从同一目标快照取世界水平位置 T 和偏航旋转 R。世界终点的水平坐标 g = T + R × targetOffsetXZ，其中偏移的 x、z 分量沿目标的右方、前方，单位为米。天然终点为 x + B × 窗口摘要的净位移，两者的水平差为 C。子区间 [a,b] 的期望位移为 B × clipMotion.Displacement + C × (b−a)/(e−s)。C 固定于该窗口入口，碰墙产生的差额不追补。alignYaw 开启时，以相同归一化进度逐步叠加“目标偏航与原始窗口末偏航的最短角差”。关闭时沿采样朝向。Motor 对齐返回的朝向并裁剪平移，下一窗口从实际位姿重新定基。目标在入窗时取快照。入窗前销毁或缺失则该窗口用原轨迹，入窗后沿已锁定终点执行。M6 可在下个窗口给出新的目标快照

烘焙摘要描述纯片段的自然运动。要求在空地精确到点时，整个扭曲窗口使用全动作权重。窗口内带淡化时，按可见图的实际混合 Delta 加窗口入口算出的纠偏量验收

**M6 调用示例**　选择扭曲的动作在开始时用实际片段核对资产，准备目标并调用 BeginWarp。调用方使用动画求值返回的片段区间，应用该区间的运动，再派发端点通知。M5 使用无攻击语义的片段验证这条接口。设窗口 0.20～0.60 s，原曲线在该窗沿 +Z 匀速前进 1.00 m，目标距入口 1.60 m，则 C=+0.60 m，两半窗各请求 0.50+0.30=0.80 m。若墙将首半窗限制为 0.40 m，后半窗仍只请求 0.80 m。动作拥有者决定目标和取消，Motor 应用运动，伤害结算随 M7 接入

### 4.3 Animation Timeline Editor 与 Scene 预览

从 Window → Endfield → Animation Timeline 打开，或在 AnimationDefination Inspector 点击「编辑时间线」。窗口编辑一份 SO，时间轴显示帧号和片段秒。未绑定角色时可编辑轨道和拖动播放头，绑定后在 Scene 视图查看角色姿态和轨迹

| 编辑区域 | 操作 | 可观察的结果 |
| --- | --- | --- |
| 片段与根运动 | 播放、暂停、逐帧或拖动，编辑倍率与淡化 | 选中的兼容角色按播放头显示姿态和轨迹 |
| 动画通知 | 新增或移动点，拖动持续窗口两端，编辑 signal 和条目顺序 | 时间轴显示当前帧的点和活动窗口，并标注名称。正播、倒拖、循环仅用于查看标记，均不派发玩法通知 |
| 运动扭曲 | 编辑窗口帧、目标局部偏移与 alignYaw，在 Scene 中拖动测试目标，在 Timeline 中调整其偏航 | 同时显示原轨迹、修正轨迹、所选窗口的终点和朝向，模型根跟随当前帧的计划位姿。目标位置和朝向仅在本次预览有效 |

点通知和窗口通知在 Inspector 与 Timeline 中各自添加和排序。存在标记 `[Serializable]` 且有无参构造的具体派生类时，添加按钮提供类型菜单，选中条目后可编辑该类型的序列化字段

**从打开资产到显示一帧**

1. AnimationTimelineEditor 持有当前 AnimationDefination、播放头（片段秒，初始 0）和播放开关（初始暂停）。绑定角色时，AnimationTimelinePreview 为场景实例建立手动图，以角色当前位姿为预览起点。基础步态控制器优先使用窗口指定值，其次使用该角色 AnimationSource 的控制器，再读取 Animator 上的控制器。缺失时以参考姿态参与混合
2. 播放时窗口以编辑器经过秒数乘 speed 推进播放头，钳至片尾或在循环时回到首帧。拖帧按绝对片段秒定位。以同一初始步态相位及从片首经过的动作秒重建基础姿态，按第 3 节曲线采样混合。模型 Animator 只输出骨骼姿态，预览根从起点按编辑器临时轨迹或第 4.2 节扭曲结果定位一次。倒拖或跳帧时从已知起点求值，避免沿上次拖动结果叠加位移。预览展示空地上的计划运动，碰撞由 CombatDemo 中的 CharacterMotor 验收
3. 编辑轨道时用 SerializedObject 分别写动画 SO 的通知和扭曲 SO 的窗口，一次拖拽对应一次 Undo。Inspector 编辑、Undo 和 Redo 后按受影响的数据刷新当前窗口。通知只重绘标记，曲线重采当前姿态，目标或窗口改变则重算受影响的轨迹。仅拖播放头或测试目标时只更新窗口会话。换 Clip 或模型时重建预览，扭曲窗口摘要过期时提示重新烘焙

从动画资产或通用菜单进入 Timeline。窗口根据所选资产的开关显示通用运动预览。预览 Controller 改变时只重建姿态图，Clip 或采样模型替换及重新导入时使原始采样失效
4. 换资产时结束角色预览、清空测试目标并将播放头归零。重开窗口读取 SO 中已保存的制作数据

**在 Scene 中制作**

1. 在 Animation Timeline 选择资产，把 Hierarchy 中的角色拖到「Scene 预览对象」，也可选中角色后点「预览选中角色」。窗口从 `Combatant.ModelAnimator` 获取模型，从角色根获取起始位姿。未挂 Combatant 的模型使用自己的 Animator 和所在节点
2. 播放、循环、单帧步进和拖标尺共用一个播放头。Scene 使用 Unity 的 AnimationMode 对该实例采姿，并记录预览写入的根属性。场景角色带有兼容 Humanoid Animator 时，角色根在起始坐标系中沿预览轨迹移动，Scene 显示轨迹并可拖动测试目标。其他模型在原位采姿
3. 点击「退出预览」、换资产或角色、保存或关闭场景、关闭窗口、脚本重载或进入 Play Mode 时，结束 Scene 预览并恢复采样前的角色姿态和根位姿。测试目标保存在本次预览会话中
4. 在 CombatDemo 用菲比资产绑定 `Characters/PlayerRoot`，祀资产绑定 `Characters/SiRoot`。分别拖到第 3 帧，检查沿各自起始朝向前移 1.2327 m 和 2.2753 m，倒拖再回到第 3 帧时复现姿态和位置。退出后检查角色回到原位，场景及 Prefab 覆盖保持原值

## 5. 与 M4 共用一个动作步

### 5.1 场景装配与指定模型采样基线

CombatDemo 的 PlayerRoot 和 SiRoot 持有 PartyMemberControl、CharacterMotor 与 ActorStepBinding。可见模型的 Animator 节点各挂 AnimationSource，保存 Controller 并管理手动图。步态速度标定与闪避动画配置由角色根上的 PartyMemberControl 保存。Controller 的 Dodge 状态提供本角色前闪片段，匹配 SO 的根运动开关决定是否提交水平增量。队员初始化时取得并初始化自己的模型动画源，然后建立 FSM

下表为现有双角色实测，通用采样器的迁移以此作为回归基准

**历史对照测量（旧 Evade 片段）。** 2026-11-24，Unity 6000.3.28f1。在临时预览场景中分别实例化 Phoebe、Si 的 Prefab，以本角色 Avatar、单位播放倍率、开启根运动的模型 Animator 和手动 AnimationClipPlayable 图单独播放对应片段。从首姿开始按 30 Hz 读每步 `Animator.deltaPosition`，包含末帧。下表的路程为各步 XZ 位移长度之和，可以区分原地动作和先移出再移回的动作。

| 角色与 Clip | 长度 | 0.10 s / 0.20 s 的累计 XZ 位移 | 全片水平路程 | 同方法播放 Walk 的水平路程 |
| --- | ---: | --- | ---: | ---: |
| 菲比 · GS_Evade | 1.7000 s | 均为 (0, 0) m | 0.0000 m | GS_Walk 约 1.4789 m |
| 祀 · DK_Evade | 1.4667 s | 均为 (0, 0) m | 0.0000 m | DK_Walk 约 1.7295 m |

上述 Evade 的水平根路程为零，作为原地播放的对照。正式闪避使用 Special 目录中的 Quickshift_F。2026-11-26 在 Unity 6000.3.28f1 以相同角色 Prefab、Avatar 和 30 Hz 手动图实测，两个片段均为 1.000 s，沿局部 +Z 突进后略有回撤。以下均为片段轨迹的米数，场景碰撞另由 Motor 约束

| 角色与正式片段 | 0.10 s 累计前移 | 0.20 s 累计前移 | 最远前移 | 片尾净前移 | 全片水平路程 |
| --- | ---: | ---: | ---: | ---: | ---: |
| 菲比 · GS_Quickshift_F | 1.2327 | 1.9693 | 2.1841 | 2.0159 | 2.3522 |
| 祀 · DK_Quickshift_F | 2.2753 | 2.7363 | 2.9113 | 2.8072 | 3.0154 |

### 5.2 从状态输入到播放、通知与位移

1. **取时间与输入。** ActorStepBinding 用 ActorId 取本角色 delta。暂停或完全顿帧时清除一次性按下，游标、权重、通知和 Motor 均保持。死亡按角色生命周期中断活动播放并清理窗口。正步沿用 `CharacterControl.Step(double actionDelta)` 消费一次输入并选态
2. **进入。** 地面态优先检查 Dodge 按下和可用动画状态，其余情况按方向和 SprintHeld 选 Idle、Walk 或 Run。Dodge.Enter 锁定 MoveWorld，无输入时取当前朝向。闪避状态写入 IsDodging，由 AnimationSource 在零秒求值后绑定实际 Clip 和有效倍率。匹配 SO 时根据 UseRootMotion 决定是否读取自然增量，按帧范围校验通知并取得播放 ID。存在有效通知的首个正 Tick 先派发第 0 帧，回调可在移动前打断
3. **推进。** AnimationSource.EvaluateSegment 接收已落实端点、剩余动作秒、分段标志、读取根运动标志和段首根朝向。只有纯姿态且没有有效通知时才合并到本步末或片尾，其余按片段帧边界推进可见 Controller 一次。AnimationSource 将收到的根运动逆转段首世界偏航，再用累计自然偏航还原到动作起点坐标系，经 Controller 的 normalizedTime 求得实际到达的片段秒。闪避状态将自然运动转到入场锁定方向，交给 Motor.MovePlanar 处理碰撞，写入已落实端点，再派发通知并检查打断。开关关闭时保持位置和朝向，匹配 SO 的有效通知照常派发。合法片段缺少匹配 SO 时原地播至结束
4. **退出。** 状态扣除各段使用的动作秒，自然结束或通知打断时关闭通知并清除 IsDodging，零秒求值启动回到 Locomotion 的淡出。切回地面态后，本步余时只推进步态图，下一正步才恢复地面移动。淡出期间由步态管理胶囊位移，闪避的根运动在退出时停止消费。Controller 配置无效且无法读出合法 Clip 时，立即退回地面态并将整步交给它一次。死亡事件可在暂停中打断当前状态并启动淡出，暂停时的淡出权重保持在起点

5. **步态与启停。** 普通走跑先经 Motor.StepLocomotion 移动，再将碰撞后的 GroundSpeed 交给动画图。临时禁用整个角色时退订时钟并保留动作进度，启用时零秒刷新姿态，从下一正步续播。销毁或重载释放订阅、通知窗口与图

**移动取消**　在闪避 SO 的时间线上添加信号为 `允许移动取消` 的点通知，正式配置见第 1 节。状态进入时订阅有通知的播放，退出时释放句柄。通知到达前保持闪避。通知到达时若已持有移动输入，在该帧完成碰撞位移后结束闪避，本步余时只推进步态。通知到达时若没有移动输入，闪避仍继续，之后第一个持有移动输入的正动作步切回 Walk 或 Run，并在当步执行地面移动。改动取消点时按角色在 Timeline 单独调整

以 0.20 s 的片段为例，当前 0.15 s，speed=1。保持 W 并松开 Shift，提交 0.10 动作秒。最后 0.05 动作秒完成闪避位移并切回 Walk，剩余 0.05 动作秒推进基础图。若 speed=2，同样末段消耗 0.025 动作秒，剩余 0.075 s 由基础图消费

## 6. 最小验收与下一阶段交接

### 6.1 验收算例

场景用例使用现有 CombatDemo。需要复算帧进度时，停止 BattleTimeDriver 的自动 Update，显式提交动作步。对应的现有 EditMode 用例列于第 2 节

| 验收项 | 初始条件与操作 | 可观察结果 |
| --- | --- | --- |
| 步态与前闪交接 | 从走跑进入前闪。菲比保持移动输入跨第 6 帧，祀从静止开始自然播完。对照一次跨片尾和按片段帧分步 | 步态使用碰撞后的速度，闪避从第 0 帧选取本角色片段。菲比在取消帧交回地面态，本步余时只推进步态。祀自然退出且两个推进方式的闪避终点一致 |
| 碰撞与通知终止 | 菲比前闪夹具第 4 帧碰墙、第 6 帧由通知打断。再分别以第 0 帧打断和关闭根运动播放 | 移墙后只执行未走的片段，打断后无额外闪避位移。第 0 帧可在移动前打断。关闭根运动时位置和朝向保持，第 6 帧通知仍到达 |
| 通知次序与替换 | 比较第 4.1 节算例用大步和逐帧得到的事件，在 Enter 回调中重播 | 两次的帧、阶段和索引一致。旧播放只对已经打开的窗口派发中断退出，新播放可独立推进 |
| 独立动作钟与启停 | 两人同时闪避，菲比顿帧 0.030 s 后推进两次 0.020 s。临时禁用并恢复祀，另在暂停时中断一名角色 | 两人的增量分别为 0.010、0.040 动作秒。祀续用原进度。暂停中的打断释放通知，角色位置保持 |
| 编辑器制作与通用扭曲 | 按第 4.3 节拖两份前闪到第 3 帧，移动点通知再撤销。按第 4.2 节分两段求解并拖动预览目标 | 预览位移约为 1.2327 m 和 2.2753 m。撤销还原帧位，关窗还原角色位姿。扭曲夹具每半窗请求 0.80 m，首段撞墙时次段仍请求 0.80 m |

### 6.2 交给 M6 的接口

| 现有能力 | 接入职责 |
| --- | --- |
| `ActorStepBinding` 与 `CharacterControl.Step(double actionDelta)` | 复用角色提供的动作秒。M6 Actions 管理一次获准攻击的开始、推进、结束与取消 |
| `AnimationSource.BeginClip`、`EvaluateSegment`、`EndClip` | 绑定可见 Controller 实际选择的片段，求值返回消耗的动作秒、片段终点和自然平面运动，由动作拥有者决定何时开始、推进和退出 |
| `AnimationNotifyPlayer.Begin`、`AdvanceTo`、`End`、`Subscribe` | M6 可按片段帧配置命中窗口。动作拥有者负责在求值及运动之后推进通知，结束时关闭活动窗口。武器扫掠、目标筛选和按窗口去重归 M6 |
| `CharacterMotor.MovePlanar`、`MotionWarping` | Motor 应用世界位移与水平朝向。选择扭曲的攻击由 Actions 提供并释放目标，求解器沿用第 4.2 节的分段输入 |

首个 M6 联调可使用一名角色的一段攻击，输出去重后的 `HitContext`。连招和伤害结算随 M7 接入 · [总体路线](../仿终末地战斗系统设计方案.md#61-总体路线)

## 7. 历史验证记录

**2026-11-23，旧提交 `21d307c` 的操控与动画联合原型。** 按用户提供的原 M4 实测记录，Unity 6000.3.28f1 Test Runner 首轮通过 14 项，接入长按输入后复跑共 **17 项，全部通过，0 项失败或跳过**。其中动画、操控和输入相关用例 10 项，既有场景回归 7 项。场景用例均在 Play Mode 加载 CombatDemo。其中按住左键触发另一击的用例验证的是已废弃的输入方案。现行重击规则的验收在 M7 执行。

| 测试组 | 通过数 | 实际覆盖 |
| --- | ---: | --- |
| CharacterAnimationGraphTests | 4 | 双刀走跑的端点和中间混合、Override 闪避片尾返回、跑步淡入攻击与取消到闪避的上臂姿态。自然退出、主动退出、零时长硬切与参考姿态误差在 0.5° 内，十轮替换及退出后节点数均回到基线 |
| ActorAnimationSceneTests | 1 | 给菲比 30 ms 顿帧，两个 20 ms 步内菲比推进 10 ms、祀推进 40 ms。全局暂停、半速、临时禁用后恢复游标和姿态、重载场景后清理旧 ID 均通过 |
| ThirdPersonControlTests | 3 | 键鼠事件驱动相机相对移动、斜向奔跑、闪避及贴墙停止。空地闪避的两个 0.10 s 步分别累计约 1 m、2 m，主相机通过遮挡及俯仰检查。旧原型中角色动作速率设为 0.25 后，左键按下即播放 GS_Attack1，持续按住按真实时间切到 GS_Attack2，继续按住不重启片段。验证了暂停时的请求丢弃及闪避优先 |
| CombatInputReaderTests | 2 | 旧原型使用真实 Combat 输入资产和虚拟键鼠，分别验证短按、长按只追加一次重击、松开与重复消费均不重复出招、同次采样保留多种请求。Esc 同帧优先，重新捕获的点击持续超过阈值也不会出招，释放捕获、禁用及闪避均能清理手势 |
| ActorSceneTests / BattleClockSceneTests / ResourceSceneTests | 3 / 2 / 2 | 回归同场角色登记与重载、独立动作时钟、资源和 HUD 行为 |

旧原型的手动图通过控制器进入 Dodge，先以 0 秒采样首姿，再消费正步；当时的首次求值用于切换控制器状态。脚部接地期间的世界坐标漂移和玩家对操控手感的评价未计入上述数值验收

**旧设计的攻击算例（归档，退出 M5 验收）**　GS_Attack1 以 1 倍速、0.100 s 淡入，0.025 s 时基础/攻击权重为 0.75/0.25，再进 0.075 s 后攻击权重为 1。旧方案用 0.080 s 取消到闪避，0.040 s 时各占 0.5，再进 0.040 s 完成交接。另一算例让 DK_Attack1 以 2 倍速、0.080 s 自然淡出，在距尾 0.020 动作秒处输入 0.060 s，与拆为 0.020 + 0.040 s 均得到基础/末姿 0.5/0.5。这里保留旧参数供后续 Actions 设计参考

**本轮状态**　M4 走跑和一次性右键输入已有收尾记录，M5 已有第 5 节源资源测量。本版的队员 Dodge 状态、两份前闪轨迹与独立 Quickshift_F 播放已接入 CombatDemo

## 8. 本版实现与验收记录（2026-11-26）

以下记录对应通用动画 SO 修订前的前闪实现。第 6 节新增项目另记于第 9 节

在当前工程的 Unity 6000.3.28f1 编辑器运行定向 EditMode 测试，场景用例进入 Play Mode 加载 CombatDemo。六组共 26 项通过，失败 0，跳过 0

| 测试组 | 通过数 | 实际验证 |
| --- | ---: | --- |
| GameInputManagerTests、StateMachineTests | 2 + 3 | 按下标记、状态生命周期、重入约束与角色实例隔离 |
| ThirdPersonControlTests | 15 | 原有输入、面板、相机与步态回归，无轨迹时只报告右键；场景内实际前闪的方向锁定、墙面碰撞、退出步位移及重载 |
| DodgeRootTrajectoryTests | 3 | 两份正式片段重新烘焙，逐帧与开根运动模型的实际位置对照（误差 < 0.01 m），另验证零轨迹拒收和非整帧片尾的插值 |
| CharacterAnimationGraphTests | 2 | 两个 Avatar 的走跑标定、独立前闪片段与参考图的大腿骨姿态（误差 < 0.5°），十次重播后节点数保持 |
| ActorAnimationSceneTests | 1 | 两名队员独立顿帧、暂停、半速、单独停用模型或动画绑定后恢复，以及片尾交还地面状态 |

随后将退出判定按片段长度的浮点精度对齐，复跑受影响的 8 项，全部通过。祀沿 +Z 的前闪分别用一个跨尾长步和十个 0.1 s 步执行，两次均回到 Run，胶囊净前移约 2.8072 m，片尾当步不叠加跑步位移。以上记录来自编辑器，未执行独立 Player 构建


## 9. 通用动画实现与验收记录（2026-11-26）

在 Unity 6000.3.28f1 的 ArknightsEndfield 会话中通过 MCP 重新烘焙并保存两份 AnimationDefination。重新从磁盘打开 CombatDemo 核对模型源、Avatar 和片段引用，PlayerRoot 对应 Phoebe / GS_Quickshift_F，SiRoot 对应 Si / DK_Quickshift_F。两份资产均为 30 Hz、1.000 s，第 3 帧水平前移分别为 1.2327 m、2.2753 m。实际打开菲比的 Timeline 窗口并定位至 0.10 s，预览根读数为 (0, 0, 1.2327) m

定向 EditMode 联合回归覆盖 9 组、35 项，全部通过，失败 0、跳过 0。场景用例在 Play Mode 加载 CombatDemo。首次后台运行时有一项角色面板用例未取得光标锁定，聚焦 Game 视图后单独复测及联合回归均通过。随后接入死亡通知并复跑 ActorAnimationSceneTests，3 项全部通过，其中新增 1 项验证战斗钟暂停时死亡会立即停止闪避、清除片段权重并保持胶囊位置。两轮合计覆盖 36 个不同用例

| 范围 | 本轮直接验证 |
| --- | --- |
| 动画资产与姿态 | 两个模型重新烘焙后与实际片段根位置对照，原地片段可采样但无法用作前闪，非整帧末段可插值，倍率与曲线按片段秒采姿 |
| 动画通知与运动扭曲 | 大步与拆步的同帧顺序、回调中重播的旧窗口清理、目标朝向与局部偏移、目标丢失及目标数不匹配回退；在实际 CharacterMotor 和墙体上验证阻挡损失不追补，通知打断时只消费到触发帧 |
| Timeline 窗口 | 两份前闪倒拖后回到相同根位置，释放预览后场景对象数与脏标记维持原值。移动通知经过第 4 帧到第 5 帧，一次 Undo 返回第 3 帧，Redo 恢复第 5 帧，保存结果写入测试资产 |

此次验证在 Unity Editor 内完成。独立 Player 构建和手工逐项检查视口材质、光照及目标拖动手感未执行

**Scene 预览复验**　定向 EditMode 测试 11 项通过。在当前 CombatDemo 绑定两名真实角色，第 3 帧分别移动 1.2327 m 和 2.2753 m，倒拖后回到同帧可复现，切换和退出会恢复起始姿态及位置。另向实际 Timeline 窗口发送鼠标按下、拖动和释放事件，播放头从第 6 帧移到第 16 帧，菲比随之更新位置，关窗后回到原位。场景脏标记保持原值


## 10. 实时根运动核对（2026-11-26）

在当前 Unity 6000.3.28f1 编辑器的隔离预览场景内，用 ClipRootMotion 对两份正式前闪各连续播放两次，每段按 30 Hz 手动求值。对照组使用原模型 Prefab 和原 Avatar 逐帧播放同一片段。菲比第 3 帧和片尾净前移分别为 1.2327 m、2.0159 m，祀分别为 2.2753 m、2.8072 m。两次播放均重现这些读数，逐帧最大位置差显示到五位小数为 0.00000 m

在菲比动画的内存副本上设置第 3～6 帧的扭曲窗口，目标位于入窗位置前方 1.6000 m。将窗口运动摘要与逐段实时增量交给求解器，空地计算终点和目标 Z 均为 2.8327 m。这项核对只验证求值和扭曲数学结果。本轮未复跑 Play Mode 的胶囊碰撞、通知打断和顿帧回归，第 8、9 节仍为当时实现的记录

## 11. 战斗时钟与动作时间推进验收（2026-11-26）

在当前 ArknightsEndfield 工程的 Unity 6000.3.28f1 编辑器运行 EditMode Test Runner。场景用例进入 Play Mode，载入 CombatDemo 后使用固定动作步。下表记录当时的实现，现行窗口规则见第 4.1 节

| 用例 | 实际结果 |
| --- | --- |
| `AnimationDefinationTests.LiveDeltasMatchTheCharacterAndItsForwardClip` | 菲比与祀的正式前闪各连续播放两次。角色根设为 60° 偏航，可见图的逐帧水平增量换回角色局部空间后，与独立烘焙参考的累计位置误差均小于 0.01 m。菲比第 3 帧与片尾分别约 1.2327、2.0159 m，祀分别约 2.2753、2.8072 m |
| `ActorAnimationSceneTests.MixedDodgeUsesMotorCollisionAndStopsAtTheInterruptingNotice` | 菲比副本以 2 倍速播放，设线性淡入 0.30 片段秒、淡出 0.20 片段秒。空地先验证逐段混合的水平位移，再在第 3～6 帧扭曲窗口内撞墙。移墙后只执行后续段的混合增量和纠偏，第 6 帧在 0.20 片段秒打断，模型没有额外移动。重播时在第 0 帧打断，消耗区间和位移均为零 |
| `ActorAnimationSceneTests` 时钟用例、`ThirdPersonControlTests` | 菲比受 30 ms 顿帧，两步各 20 ms 后只前进 10 ms，祀前进 40 ms。全局暂停保持图位置，半速按比例推进，暂停中死亡清除片段权重，祀离场再启用时从原位置续播。原走跑、方向锁定、撞墙、十次 0.1 s 消费片段至末帧均通过 |
| `AnimationNotifyPlayerTests`、`AnimationTimelineEditorTests.NewWindowDefaultsToBoundariesAndLegacyWindowKeepsItsMode` | 两个第 3～6 帧窗口在相同帧进入和退出，只有逐帧窗口在第 4、5 帧 Tick。确认旧播放中断清理、空白区间游标、末帧派发和重播。模拟去掉旧资产的 tickMode 字段后重新导入，并验证新建默认、复制、重排及 Undo/Redo |

M6～M9 按优化计划第 5 节继续接入 [战斗时钟与动作时间推进优化计划](../战斗时钟与动作时间推进优化计划.md)

## 12. 可见模型动画源与通用动画资产迁移验收

2026-11-26，Unity 6000.3.28f1，在当前工程复跑 8 项已有用例，8 项通过，0 项失败或跳过

- ActorAnimationSceneTests 的 4 项进入 Play Mode，覆盖混合前闪经 Motor 碰撞并在第 6 帧打断、原地片段进入闪避且水平位移为零、暂停中死亡，以及两名角色独立推进和重新入场
- 两份正式前闪的 Scene 预览用例验证第 3 帧位移与退出后的姿态恢复，MotionWarpingTests 的两项用例验证扭曲偏航、碰撞后续段和目标失效
- 在编辑器核对普通 SO 的八个业务字段。临时扭曲资产设置菲比前闪第 3～6 帧，烘焙后保存并卸载重开，摘要有效，窗口净位移约为沿 +Z 的 0.74 m，将结束帧改为 7 后旧摘要失效，临时资产已清理
- 从磁盘重新打开 CombatDemo 的预览场景，确认 Phoebe 和 Si 的 AnimationSource 均位于对应模型 Animator 节点，走／跑标定分别为 1.43936／4.91111 和 1.54665／6.82405 m/s，两名队员的扭曲引用均为空

本轮验证在编辑器完成，未执行独立 Player 构建

## 13. FSM 参数驱动闪避验收（2026-11-26）

在当前 Unity 6000.3.28f1 中检查两份 Controller，再从磁盘载入 CombatDemo 进行固定动作步核对

| 项目 | 实际结果 |
| --- | --- |
| 进入、退出与片段映射 | 两份 Controller 均可在写入 IsDodging 后以零秒求值切换状态，菲比使用 GS_Quickshift_F，祀使用 DK_Quickshift_F |
| 前闪位移 | 首步 0.10 动作秒后分别前移 1.232722 m、2.275283 m，片尾完成位移分别为 2.015932 m、2.807190 m。祀用十次 0.10 s 步推进到相同终点并切回地面态 |
| 碰撞、通知与扭曲 | 菲比在第 6 帧（0.20 片段秒）收到打断通知，通知时胶囊停在墙前 0.33 m，本步结束时位置保持且 IsDodging 清除。使用单独的第 3～6 帧扭曲窗口，可在窗口末端到达 3.000000 m 目标 |
| 独立时钟与启停 | 菲比顿帧 30 ms 后推进两次 20 ms，菲比和祀分别处于 0.11 s、0.14 s。全局暂停保持位置与进度，暂停中死亡回 Locomotion。祀临时停用并启用后从 0.14 s 续播，再推进 20 ms 到 0.16 s |

## 14. 动画 SO 的根运动与扭曲开关

在当前工程复跑 4 项用例，全部通过。Play Mode 中使用带实际位移的菲比前闪，同时关闭两个开关，角色根保持原位和朝向，第 6 帧通知仍到达。只启用扭曲时，第 3 帧入窗前位置保持，至第 6 帧到达目标的水平位置，出窗后停止移动。另一个场景用例覆盖双开关下的胶囊碰撞和通知打断，两份正式前闪的 Timeline 拖帧与复位用例也通过

## 15. 自然根运动与动作时间推进验收（2026-11-26）

在当前工程的 Unity 6000.3.28f1 运行计划指定的三项 EditMode 用例，结果为 3 项通过、0 项失败、0 项跳过。两项角色用例进入 Play Mode，从磁盘载入 CombatDemo

| 用例 | 实际结果 |
| --- | --- |
| `ControllerDodgeUsesMotorCollisionAndStopsAtTheInterruptingNotice` | 前三帧使用原片段位移，第 4 帧被墙截停，移墙后只消费第 5～6 帧的自然位移。第 6 帧打断后回到 Locomotion，第 0 帧打断时位置保持 |
| `DodgeWithOnlyWarpEnabledStaysInPlaceAndStillNotifies` | SO 启用通用扭曲并关闭根运动，闪避全程保持位置和朝向，第 6 帧通知仍到达 |
| `FadeTimesUseClipSecondsAndCurvesOnlyAffectPoseWeight` | 编辑器预览设置在指定片段秒得到 0.25、0.50 和 0.15625 的权重，菲比原始第 3 帧前移约 1.2327 m |

固定步长走查两份正式前闪，均取得有效播放 ID，未建立 NotifyTrack，状态跳过逐段通知，首步位移分别为 1.232722 m 和 2.275284 m。空播放自然结束时事件数为 0。临时清空菲比的闪避 SO 后，Controller 的合法片段原地播放并正常退出

使用临时资产在打开的 Timeline 内改通知名，再由外部 SerializedObject 改名，两次均复用原始采样和修正轨迹，当前帧也未重新求值。改变扭曲偏移后只重算修正轨迹。从闪避入口打开时无扭曲目标或窗口操作，关闭窗口后场景模型恢复原位

**收尾复验**　上述原地闪避用例现名为 `DodgeWithoutRootMotionStaysInPlaceAndStillNotifies`，验证关闭根运动时的位姿与第 6 帧通知。此夹具使用空扭曲资产，闪避对目标和求解器的依赖另由源码与场景引用核对。收尾后复跑表中 3 项用例，3 项通过、0 失败、0 跳过

编辑器中以临时窗口实例核对同一份带扭曲引用的资产，闪避入口隐藏目标，窗口更换资产后恢复通用扭曲引用。另在已打开的 Timeline 上执行通用菜单，窗口恢复通用模式并保留当前所选资产

## 16. 闪避与步态混合验收（2026-11-26）

在当前工程的 Unity 6000.3.28f1 运行 13 项相关 EditMode 用例，全部通过。骨骼姿态用例验证菲比与祀在淡入 0.02 动作秒和淡出 0.04 动作秒时的过渡进度分别为 0.4 和 0.5。CombatDemo 用例验证，自然结束、通知打断和暂停中的死亡照常结束闪避流程，淡出期间没有额外提交闪避位移

在临时预览场景手动推进两名角色。进入闪避时保留原步态权重，淡入中可回到 Locomotion，淡出中重新进入时新 Dodge 从第 0 帧起步。可见 Animator 的淡入混合也会影响首段根增量。从静止开始并以片段帧分段时，菲比和祀在 0.10 动作秒的前移约为 1.1756 m 和 2.0726 m，片尾净前移约为 1.9588 m 和 2.6045 m

## 17. 开发收尾核对（2026-11-27）

在当前 Unity 6000.3.28f1 的 Edit Mode 读取 CombatDemo 装配。菲比的 Controller 选中 GS_Quickshift_F，祀的 Override 选中 DK_Quickshift_F，均与角色引用的 SO 匹配。两片段均为 1 秒、30 fps，开启自然根运动。两名角色各有 Motor，动作步绑定到 Combat Session 的时钟。共同的 Controller 淡入为 0.05 动作秒，淡出为 0.08 动作秒。现行取消帧以角色资产为准，菲比为第 6 帧，祀为第 15 帧

按现行流程核对右键输入、角色正动作步、Controller 求值、自然根运动、碰撞及通知清理。已有移动取消用例从菲比资产读取通知帧，在前 0.01 片段秒检查仍处于闪避，跨通知后检查进入 Walk，随后在下一正步检查恢复地面速度

通知接入 Common 后，已有 5 项通知与场景用例通过。当时还在隔离预览场景核对了菲比与祀按住移动跨越各自取消点，在同一动作步进入 Walk。收尾版通过运行时与编辑器程序集编译，0 个错误，7 条警告。本轮按要求未运行测试套件，Player 构建和蹲起衔接的人工手感仍待验证
