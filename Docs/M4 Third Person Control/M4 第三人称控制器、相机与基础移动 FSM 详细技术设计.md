# M4 第三人称控制器、相机与基础移动 FSM 详细技术设计

**已收尾（Verified） · 2026-11-25**　基于 M3 Actor Foundation（`f55bf44`）交付。Unity 编辑器收尾复验 18 项全部通过。[M4 收尾验收记录](#8-实现与验收记录)

**2026-11-26 设计修订**　下文区分 M4 收尾实现和 M5 的闪避实现。第 8 节保留原验收日期与结果

在 `Assets/Scenes/CombatDemo.unity` 中，玩家用 WASD 相对镜头移动，左 Shift 奔跑，右键产生一次 Dodge 请求，正式闪避在 M5 按角色片段的根轨迹执行。菲比和祀各自持有角色行为状态机。开场只有菲比接收键鼠，祀从 Idle 开始。走跑由 Motor 驱动，前闪取可见 Animator 的自然根运动，镜头在角色移动后跟随。[总体路线](../仿终末地战斗系统设计方案.md) · [M5 动画详设](../M5%20Animation%20Foundation/M5%20Animation%20Foundation%20详细技术设计.md)

## 1. 工程依据与交付范围

| 场景位置 | M3 基线 | M4 场景装配 |
| --- | --- | --- |
| `Combat Session` | `SceneActors.initialActors` 依次指向 PlayerRoot、SiRoot。`BattleTimeDriver` 持有本场 BattleClock | 在此处挂共享的 GameInputManager 和本地 PlayerController，操控角色固定为菲比。每个角色根各订阅一次同一时钟的战斗步通知 |
| `Characters/PlayerRoot` → `Phoebe` | 菲比逻辑根有 Combatant 和 CharacterController。模型 Animator 在场景中引用 Phoebe_Controller | 在根上装队员控制、Motor 与动作步绑定，沿用现有胶囊 |
| `Characters/SiRoot` → `Si` | 祀是独立登记的角色。M3 的 Si Prefab 和场景实例均未配置 CharacterController | 在场景实例上补胶囊，装与菲比相同的队员控制、Motor 和绑定，创建祀自己的状态对象 |
| `Main Camera`、`Environment/Plane`、`Environment/Wall` | 主相机已有透视 Camera、AudioListener，CinemachineBrain 已禁用。旧 CustomThirdPersonCamera 引用的脚本 GUID 在当前 Assets 中缺失。地面和墙分别带 MeshCollider、BoxCollider，均在 Environment 层 | 用 ThirdPersonCamera 替换失效组件。使用现有墙验证人物碰撞和镜头遮挡 |

M3 登记时为每个 Combatant 创建一份 ActorRuntime、分配 ActorId 并登记动作时钟。角色是否存活以 `Runtime.Life.State` 为准。M4 在角色根新增控制组件，复用这套身份和生命周期。键鼠绑定已经存在，当前 `Gameplay/Attack` 的 Interactions 为空。工作区中的菲比动画控制器已有 Locomotion、Dodge 姿态状态，基础步态由各角色的动画绑定驱动，验收覆盖角色根的实际位移和模型腿部姿态。[SceneActors](../../Assets/Scripts/Runtime/Actors/SceneActors.cs) · [ActorLifecycle](../../Assets/Scripts/Runtime/Actors/ActorLifecycle.cs) · [输入资产](../../Assets/InputSystem_Actions.inputactions) · [CombatDemo](../../Assets/Scenes/CombatDemo.unity)

**交付范围。** `CombatDemo` 开场固定操控菲比。菲比和祀各有独立状态机、Motor、动作时钟和基础步态图。M4 交付镜头相对走跑、平滑转向与停步、碰撞、相机遮挡和角色面板输入。左键和右键分别提交一次 `Attack`、`Dodge`。Attack 在本阶段只作为输入预留。M5 扩展队员 FSM 和动画图以执行根轨迹闪避。未来 M6 Actions 管理攻击生命周期、许可与取消，M7 Combos 选段，M8 接入队伍切换与 AI

## 2. 实施文件与类型归属

下表列出本次实现的脚本、场景和测试。运行时代码使用 Unity 默认程序集。`CharacterControl/FSM/` 集中存放角色状态机、抽象基类 `CharacterState` 和具体状态。每个角色持有自己的 `StateMachine`，由其控制组件创建该实体的状态实例。

| 变更 | 相对项目根目录的完整路径 | 类型与职责 |
| --- | --- | --- |
| 新增 | `Assets/Scripts/Runtime/Input/GameInputManager.cs` | 持有独立的 EndfieldInputActions 实例，处理角色面板快捷键、玩家操控采样、一次性命令和光标捕获 |
| 修改 | `Assets/Scripts/Runtime/UI/CharacterStatsPanel.cs` | 订阅面板快捷键、处理关闭按钮并刷新属性，开关时将实际可见状态告知共享输入组件 |
| 新增 | `Assets/Scripts/Runtime/CharacterControl/PlayerController.cs` | 读取共享输入帧，更新相机，按镜头朝向向场景指定的角色提交输入 |
| 新增 | `Assets/Scripts/Runtime/Input/InputDataFrame.cs` | 本地键鼠的一次只读快照，含 Move、LookDelta、SprintHeld、Pressed |
| 新增 | `Assets/Scripts/Runtime/CharacterControl/CharacterCommand.cs` | 声明共享的 `[Flags] CharacterCommand`，Attack 和 Dodge 可同时按下 |
| 新增 | `Assets/Scripts/Runtime/CharacterControl/FSM/StateMachine.cs` | 普通 C# 对象，持有当前状态，执行初始化、切换和 Tick |
| 新增 | `Assets/Scripts/Runtime/CharacterControl/FSM/CharacterState.cs` | 角色状态基类，持有本角色上下文，定义 Enter、Tick、Exit 生命周期及步首选态入口 |
| 新增 | `Assets/Scripts/Runtime/CharacterControl/CharacterControl.cs` | 角色根的抽象组件，持有本实体唯一的 FSM、当前步意图、Motor 和可观察的移动结果，公开只读 CurrentState |
| 新增 | `Assets/Scripts/Runtime/CharacterControl/PartyMemberControl.cs` | 实际挂载的队员组件，构造本人的三个状态实例，持有走跑与转向配置 |
| 新增 | `Assets/Scripts/Runtime/CharacterControl/FSM/States/PartyMember/PartyMemberGroundedState.cs` | 队员普通地面状态共用的转换规则，统一判断方向和步态 |
| 新增 | `Assets/Scripts/Runtime/CharacterControl/FSM/States/PartyMember/PartyMemberIdleState.cs` | 提供零目标速度，保持根朝向并沿当前方向减速至停稳 |
| 新增 | `Assets/Scripts/Runtime/CharacterControl/FSM/States/PartyMember/PartyMemberWalkState.cs` | 每个动作步使用本角色的 WalkSpeed 调用 StepGroundMovement |
| 新增 | `Assets/Scripts/Runtime/CharacterControl/FSM/States/PartyMember/PartyMemberRunState.cs` | 每个动作步使用本角色的 RunSpeed 调用 StepGroundMovement |
| 新增 | `Assets/Scripts/Runtime/CharacterMotion/CharacterMotor.cs` | 本角色最终位移出口，应用水平转向和胶囊碰撞，返回实际位移 |
| 新增 | `Assets/Scripts/Runtime/CombatSession/ActorStepBinding.cs` | 每角色一份。按 ActorId 取动作时间，将本次动作秒交给角色控制，由当前状态推进移动和动画 |
| 新增 | `Assets/Scripts/Runtime/Animation/AnimationSource.cs` | 每个可见模型持有基础步态控制器与标定，手动驱动同一角色模型的 Locomotion 混合树 |
| 接入 | `Assets/Game/Animations/Characters/Controllers/Si_Locomotion.overrideController` | 在共用步态树上使用祀的双刀片段 |
| 新增 | `Assets/Scripts/Runtime/Camera/ThirdPersonCamera.cs` | 环绕与跟随、输出水平朝向、近裁面遮挡检测和距离恢复 |
| 修改 | `Assets/Scenes/CombatDemo.unity` | 装配场景输入、两名角色和现有主相机，配置引用与场景实例参数 |
| 新增 | `Assets/Scripts/Test/Editor/Input/GameInputManagerTests.cs` | 验证一次按下只产生一次 Attack、持续按住和松开的行为、同次采样、消费及重新捕获 |
| 新增 | `Assets/Scripts/Test/Editor/Input/GameViewInputScope.cs` | 两组虚拟键鼠测试共用的临时输入设置，使 Game 视图在后台也能接收事件，结束时恢复编辑器原有设置 |
| 新增 | `Assets/Scripts/Test/Editor/CharacterControl/StateMachineTests.cs` | 用普通 C# 状态夹具验证切换生命周期、重入和独立实例，可使用简化敌人状态 |
| 新增 | `Assets/Scripts/Test/Editor/CharacterMotion/ThirdPersonControlTests.cs` | 在 CombatDemo 中验证固定主控、面板输入、实际位移与步态、镜头遮挡和双角色时钟隔离 |

| 直接复用 | 相对项目根目录的完整路径 |
| --- | --- |
| 时钟与角色时间读数 | `Assets/Scripts/Runtime/Timing/BattleTimeDriver.cs`、`Assets/Scripts/Runtime/Timing/BattleClock.cs`、`Assets/Scripts/Runtime/CombatContracts/CombatIds.cs` |
| 角色登记、身份与生命周期 | `Assets/Scripts/Runtime/Actors/SceneActors.cs`、`Assets/Scripts/Runtime/Actors/Combatant.cs`、`Assets/Scripts/Runtime/Actors/ActorRuntime.cs`、`Assets/Scripts/Runtime/Actors/ActorLifecycle.cs` |
| 普攻与移动输入绑定 | `Assets/InputSystem_Actions.inputactions`（沿用 Gameplay/Attack 的 Button 和左键绑定） |
| 输入动作访问 | `Assets/Scripts/Runtime/Input/Generated/EndfieldInputActions.cs`，由上述输入配置生成，提供 Gameplay、Control 和 System 动作的强类型属性 |
| 角色实例来源与图层 | `Assets/Prefabs/Characters/Phoebe.prefab`、`Assets/Prefabs/Characters/Si.prefab`、`ProjectSettings/TagManager.asset` |
| 现有回归依据 | `Assets/Scripts/Test/Editor/Actors/ActorSceneTests.cs`、`Assets/Scripts/Test/Editor/Timing/BattleClockTests.cs`、`Assets/Scripts/Test/Editor/Timing/BattleClockSceneTests.cs` |

两个角色的配置在 CombatDemo 实例上使用默认 Inspector 编辑。本阶段无需新配置资产或自定义编辑器。 M5 在队员状态目录新增 `Assets/Scripts/Runtime/CharacterControl/FSM/States/PartyMember/PartyMemberDodgeState.cs`。该文件、轨迹资产与动画图均列在 M5 实施清单中

## 3. 输入与两个时间尺度

**M4.1 玩家输入。** 场景中的 `GameInputManager` 每个渲染帧写入一份只读 `Frame`。`PlayerController` 读取这份值，将操作交给固定角色。其他角色可以通过自己的 `SetControlInput` 接收世界方向。

| 类型或成员 | 数据与消费规则 |
| --- | --- |
| `GameInputManager.Awake / OnEnable / OnDisable / OnDestroy` | Awake 创建一份 EndfieldInputActions 并订阅动作。启用时启动 System/Pause 和 Control/ToggleCharacterPanel，根据面板状态启停 Gameplay 并设置光标。禁用时清空输入、释放光标，销毁时解绑并释放动作实例 |
| `InputDataFrame Frame { get; private set; }` | 默认值为空帧。Update 写入长度上限为 1 的移动方向 Move、左 Shift 状态 SprintHeld、本帧鼠标像素增量 LookDelta 和一次性命令 Pressed。锁定光标时记录 captureFrame，捕获当帧及接下来的两个渲染帧将 LookDelta 置零 |
| `InputDataFrame.Pressed` | `CharacterCommand` 位标记，默认 None。Attack 和 Dodge 回调先写入输入管理器的 `pending`。Update 将按下标记写入 `Frame` 并清空 `pending`，采样之间完成的点击也能送达。连续采样只在第一份输入帧上看到该次按下 |
| `ReleaseCapture()`、`ClearPendingPresses()` | Esc 或失焦时释放光标。自由光标的首次左键用于恢复操控。角色绑定启停时清掉输入管理器中待采样和已发布的按下，保留持续方向和奔跑状态 |
| `PanelToggleRequested`、`PanelCloseRequested`、`SetPanelOpen(bool open)` | C 请求切换面板，面板内 Esc 请求关闭。面板调用 `SetPanelOpen` 同步可见状态，输入管理器切换 Gameplay 与光标并发出 `GameplayInputCleared`。PlayerController 收到通知立即清空角色已提交的输入 |
| `CharacterControl.SetControlInput(Vector3 moveWorld, bool sprintHeld, CharacterCommand pressed)` | 角色保存世界 XZ 方向和奔跑标记。方向去掉 Y 分量、长度限制到 1。每次提交覆盖上次尚未执行的按下，持续输入保留至再次提交或清理 |
| `CharacterControl.StepCommands` | 每个正动作步开始时从本角色取出待执行的按下并清空待执行值。FSM 和后续动作模块可在同一步读取相同标记，连续推进两个动作步时仅第一个步获得命令 |

**角色属性面板。** CombatDemo 的 `GameUI/CharacterPanelRoot` 常驻启用，子节点 `PanelView` 默认关闭。`PanelView` 包含遮罩 `DimBackground` 和窗口 `Panel`，开场捕获鼠标，可直接操控菲比。`EventSystem` 上启用的 `InputSystemUIInputModule` 处理按钮点击与滚动。`CharacterStatsPanel` 启用时订阅输入管理器的切换与关闭请求，并同步 `IsOpen`。按 C 打开面板。再次按 C、按 Esc 或点击面板内的关闭按钮均可返回操控。面板打开时禁用本地 Gameplay、清空当步移动与命令、释放光标。此时 WASD、Shift、镜头移动与鼠标按键只供界面使用。关闭面板时重新捕获鼠标；下一次采样可读取持续按住的移动键，攻击和闪避需要新的按下。面板关闭时 Esc 仍可单独释放鼠标。战斗时钟继续运行，其他角色和已开始的动作照常推进。

**普通攻击输入。** `Gameplay/Attack` 沿用现有 Button 和空 Interactions。达到按下阈值时只处理一次 `performed`，写入一个 `Attack` 请求。保持按住和松开均不追加请求，下一次按下才能再请求攻击。右键仍产生 `Dodge`。每套普通攻击连段的最后一击是重击。M4 传递 Attack 与 Dodge 请求，地面 FSM 只执行走跑，M7 的 Combos 根据角色的连段配置、当前节点和衔接窗口选出下一段。按键时长不参与选段。

`GameInputManager.Update`（−300）写入 `Frame`。`PlayerController.Update`（−250）读取 `inputManager.Frame`，调用 `view.ApplyLook(frame.LookDelta)`。使用更新后的 `view.PlanarRotation × (frame.Move.x, 0, frame.Move.y)` 得出世界方向，在同一渲染帧调用 `controlledMember.SetControlInput`。`BattleTimeDriver.Update`（−200）随后调用 `Clock.Advance(Time.unscaledDeltaTime)`。正常运行一渲染帧提交一次可变时长的战斗步，菲比和祀的 FSM 各按自己的动作时间推进。[BattleTimeDriver](../../Assets/Scripts/Runtime/Timing/BattleTimeDriver.cs)

## 4. 每个角色的行为状态机

### 状态归属

每个 `CharacterControl` 实例持有一台 `StateMachine`，在初始化入口调用本角色的 `CreateInitialState()`。实际挂载的 `PartyMemberControl` 在此创建并持有自己的 `Idle / Walk / Run` 引用（类型均为 `CharacterState`），将 `this` 传给各状态。菲比和祀复用状态机机制和队员状态类，两人的状态对象、上下文及运行数据各自独立。M5 按同一方式给每人创建一个 Dodge 实例

具体 State 决定本状态的行为、可转往的目标及条件优先级。共用具体类须逐项核对 Enter、Tick、Exit、转换条件以及计时、目标等运行数据的含义与生命周期。未来 `EnemyControl` 可以创建 `EnemyIdleState`、`EnemyPatrolState`、`EnemyChaseState`。例如敌人 Idle 等待巡逻计时到期，队员 Idle 则读取本步移动输入。即使两个敌人使用同一个类，也分别持有各自的计时和目标。M8 的行为树可向敌人提交巡逻或追击意图

`CharacterControl` 提供本角色的输入、Motor、只读 CurrentState 和更新入口。`PartyMemberControl` 另提供每人的步态参数及 `StepGroundMovement` 等队员行为接口。两个 Controller 均不使用 switch / if 集中仲裁具体状态。共享 StateMachine 只执行生命周期，业务模块不能直接写 CurrentState

### 角色状态生命周期与动作步

| 接口 | 约定 |
| --- | --- |
| `CharacterState.Enter() / Tick(double delta) / Exit()` | 普通 C# 对象。每个 State 通过构造函数取得本角色上下文。Enter 初始化本次进入的数据，Tick 使用调用者传入的增量，Exit 完成本状态的清理 |
| `StateMachine.Initialize(CharacterState initial)`、`CharacterState CurrentState { get; }` | `CharacterControl.Initialize()` 调用具体组件的 `CreateInitialState()` 建立其全部状态实例，并用返回的首态初始化一次。队员的首态是 Idle。CurrentState 的唯一写入者是这台状态机 |
| `StateMachine.ChangeState(CharacterState next)` | 在 Enter/Exit 执行期间再次调用 ChangeState，抛出 InvalidOperationException，嵌套请求不执行。其余情况下，相同对象立即返回，有效切换同步执行旧状态 Exit、更新 CurrentState、新状态 Enter。初始化 Enter 同样受保护，切换标记通过 try/finally 复位 |
| `StateMachine.Tick(double delta)`、`CharacterState.ChangeState(CharacterState next)` | 状态机调用当前对象的 Tick 一次。状态在 Tick 中达到自身退出条件时，调用受保护的 ChangeState，将本角色的目标实例交给 Machine，并立即 return。新状态同步 Enter，从下一次正常更新才可获得 Tick |
| `CharacterState.SelectNext()` | 读取所属角色本步数据的步首选态入口，返回本实体的目标 CharacterState 实例，默认返回自身。只判条件，切换由 StateMachine 执行。状态机只调用状态的生命周期方法 |
| `void CharacterControl.Step(double actionDelta)` | 绑定只以正动作秒调用。将待执行按下移入 StepCommands，由当前 State.SelectNext() 选态，经 ChangeState 切换后 Tick 一次。GroundSpeed 提供碰撞后的走跑速度 |

**M4 步首转换示例**　Run 中松开 Shift，保持 W 并点击右键

1. `CharacterControl.Step` 取出按下标记放入 StepCommands，清空待执行值，调用当前 Run 的 `SelectNext()`
2. Run 继承的 `PartyMemberGroundedState.SelectNext()` 看到方向非零、SprintHeld 为 false，返回本人的 Walk。M4 此时只记录 Dodge 输入
3. Control 将 Walk 传给 `Machine.ChangeState`。Machine 调用 Run.Exit（此时 CurrentState 仍为 Run），写入 Walk，再调用 Walk.Enter（此时已能读到 Walk）。若返回的就是当前实例则跳过 Exit 和 Enter。Enter、Exit 中均禁止递归切换
4. Control 调用一次 `Machine.Tick(actionDelta)`。Walk 在本步就以走速为目标调用 Motor，实际速度从跑速向走速下降。下一步持续按住右键不会再产生 Dodge 标记

**M5 前闪转换**　队员有可用的 Controller Dodge 状态时，地面状态在步首优先响应 Dodge。入场后若读到合法片段，当步由 Dodge.Tick 消费动作秒。匹配 SO 且开启根运动时送自然位移和偏航给 Motor，关闭根运动或缺少匹配 SO 时原地播放。匹配 SO 的点通知“允许移动取消”到达时，若已有移动输入则在该帧打断；若尚无移动输入，则在之后首次有移动输入的正动作步切到 Walk 或 Run，当步恢复地面移动。到片尾或通知打断时按当前方向和 SprintHeld 选出地面态，本步余时推进步态图，下一正步恢复地面位移。若入场后 Controller 无法提供合法片段，立即切回地面态并把整步交给它

### 队员状态与移动参数

| 当前状态 | 本步行为和转换 |
| --- | --- |
| Idle / Walk / Run | 三者继承 PartyMemberGroundedState 的步首选态。M5 优先检查本步 Dodge 标记和可用的 Controller Dodge 状态，其余情况按 MoveWorld 和 SprintHeld 选 Idle、Run 或 Walk。三个子类各自实现下述移动 Tick |
| Idle | 松键当步切入 Idle。目标速度为零，Motor 清除转向角速度，沿最后朝向减速。停稳后位移和 GroundSpeed 都为零 |
| Walk / Run | `PartyMemberWalkState.Tick` 和 `PartyMemberRunState.Tick` 分别传入本角色的 WalkSpeed 和 RunSpeed。`PartyMemberControl.StepGroundMovement` 调用 Motor，并将碰撞后的实际水平速度写入 GroundSpeed。Motor 根据该档速度乘输入力度得到目标速度。持续 W 顶墙时仍可处于 Walk，实测速度会趋近 0 |

| 所属类型与关键成员 | 来源、单位与读写时机 |
| --- | --- |
| CharacterControl 的 `MoveWorld`、`SprintHeld`、`StepCommands` 和 `GroundSpeed` | 每个实体有一台 StateMachine。方向、奔跑标志、按下标记初值均为默认值，由控制输入与动作步分别更新。GroundSpeed 初始 0 m/s，由活动地面状态写入。冻结步清除按下标记并保留上一次步态速度 |
| PartyMemberControl：`float walkSpeed, runSpeed, turnSpeed` | Inspector 以 m/s、°/动作秒配置，`0 < walkSpeed < runSpeed`。菲比初值 1.43936、4.91111、720。祀本阶段以 1.54665、6.82405、720 为场景初值，两份配置各自可调 |
| CharacterMotor：`float acceleration, deceleration, turnSmoothTime` | 每个角色根的 Inspector 初值为 12、18 m/s² 和 0.12 动作秒，均为正数。运行时由该 Motor 独立保存当前走跑速度和转向角速度，禁用时清零 |
| CharacterMotor：`float StepLocomotion(Vector3 direction, float gaitSpeed, float turnSpeed, float deltaTime)` | 输入水平意图、步态满速、转速参数和正动作秒。先用 MoveTowards 将速度推向目标，有方向时用 SmoothDampAngle 绕 Y 轴转向，再按更新后的根朝向和速度提交本步位移。返回碰撞后的实际水平距离除以本步时长，供 Idle、Walk、Run 共用 |
| CharacterMotor：`void MovePlanar(Vector3 worldDelta, Quaternion worldYaw)` | 输入世界水平位移（米）和最终水平旋转，先对齐角色根的偏航，再用同根 CharacterController.Move 约束平移。零位移时也可落实朝向 |

**走跑算例。** 菲比从静止朝 +Z 起步，满幅按 W，首个 0.10 s 动作步达到 1.20 m/s 并前进 0.120 m，下一步 0.10 s 达到走速 1.43936 m/s，再前进约 0.144 m。行进中切到 D，位移沿正在转向 +X 的根朝向。满速奔跑时松键，首个 0.02 s 动作步速度降到 4.55111 m/s，继续前进约 0.091 m。动画按该步的实际速度混合。

**右键验收。** 在空地从 Idle 单击右键，推进 0.10 动作秒。该步可读到 Dodge，根位置不变。菲比已达稳定走速时，按住 W 并单击右键，0.10 动作秒内沿原走路方向移动约 0.144 m。M5 才将这个请求转换成闪避位移。

## 5. 从渲染帧到角色动作步

`ActorStepBinding` 挂在每个受控实体的根上。Inspector 的 `BattleTimeDriver timeDriver` 指向 Combat Session。绑定持有 `ActorId`、同根的控制组件及战斗步订阅。每个实体用自己的动作时间推进 FSM。开场玩家只向菲比提交输入，祀按默认的零方向保持 Idle。

1. **初始化。** 控制和 Motor 在 Awake 缓存本根依赖。`SceneActors.Start`（−10）完成开场登记后，绑定在默认顺序 Start 读取 `Combatant.Runtime.Id`，调用 `control.Initialize()`。由具体的 `PartyMemberControl` 建立本人的状态对象并进入 Idle，再向本场 `Clock.SubscribeBattleStep` 订阅一次。动态角色若在 Start 之后才 Register，绑定会在登记后的第一个战斗步初始化该角色并使用本步动作时间。
2. **战斗步到达。** BattleClock 先更新各角色动作时钟，再广播本步的全局战斗时间。每份绑定调用 `TryGetActionTime(actorId, out step)` 取本人的 `step.Delta`。查不到 ID 时结束本次绑定并退订。[时钟实现](../../Assets/Scripts/Runtime/Timing/BattleClock.cs)
3. **冻结或暂停。** 当 `step.Delta <= 0` 或角色已死亡，绑定清除本角色的待执行按下及 StepCommands，随后结束该角色步。State、计时、根位置和朝向保持。恢复时重新读取仍按住的移动方向，冻结步丢弃的 Dodge 和 Attack 请求均过期。其余角色照各自的 Delta 推进。
4. **正动作步**　绑定调用 `control.Step(step.Delta)`，角色消费输入、选态并推进当前状态。地面状态先移动，再用碰撞后的水平速度和本步动作秒推进步态图，相机在 LateUpdate 根据移动后的角色根定位
5. **禁用与销毁。** 临时禁用角色根时退订，保留该入场 ID、FSM 和状态内部计时，离开期间不补步。重新启用且原 ID 有效时恢复一次订阅。离场或场景卸载时解除绑定及输入通知、释放持有的订阅，角色实例销毁时一并释放其状态引用。重新进场的新实例从 Idle 开始。

角色动作时间为 `(可用真实时长 − 本步局部顿帧) × battleRate`，全局暂停时可用时长为 0。倍率为 1 时，只给菲比 30 ms 顿帧，再连续推进两个 20 ms 步，菲比依次得到 0、10 ms，祀依次得到 20、20 ms。FSM 内部的 `Tick(double delta)` 仅使用调用方传入的时长。输入采样与相机仍能按渲染帧运行。

### M5 接入时的调用方变更

角色控制入口为 `void Step(double actionDelta)`。步首先选态，再由选中的状态消费本角色动作钟提供的时长。闪避自然结束或被通知打断时，将本步余下时间交给基础步态图，下一正步恢复地面移动

M4 收尾时测量的两份 Evade 水平根路程均为 0 m。正式 M5 前闪按角色使用 GS_Quickshift_F、DK_Quickshift_F，沿用片段原始导入设置并单独烘焙。M4 的左键仍只产生 Attack 输入，未来 Actions 管理攻击生命周期并委托 Graph 播放。[M5 接入顺序](../M5%20Animation%20Foundation/M5%20Animation%20Foundation%20详细技术设计.md#5-与-m4-共用一个动作步)

## 6. 场景装配、玩家控制与相机

### 两个角色与一个本地输入来源

1. 打开 `Assets/Scenes/CombatDemo.unity`，定位 `Characters/PlayerRoot` 和 `Characters/SiRoot`。两根各挂 PartyMemberControl、CharacterMotor、ActorStepBinding，Time Driver 都指向 Combat Session。沿用 PlayerRoot 的 CharacterController。在 SiRoot 新增 CharacterController，首版以现有玩家胶囊为初值：高 1.8 m、半径 0.30 m、中心 (0, 0.9, 0)、minMoveDistance 0。分别填入第 4 节参数，在 Play Mode 检查两人的胶囊与地面接触。两个角色的可见 Animator 节点各挂 AnimationSource，菲比使用 Phoebe_Controller，祀使用 Si_Locomotion.overrideController。两个角色根上的 PartyMemberControl 各自保存闪避动画 SO，walkReferenceSpeed 和 runReferenceSpeed 采用第 4 节同角色的走跑初值（m/s）。两个模型 Animator 的 Controller 槽留空，由动画源初始化时开启 Apply Root Motion，Culling Mode 设为 Always Animate。
2. 在 `Combat Session` 挂一份 GameInputManager 和一份 PlayerController。将控制器的 `inputManager` 和 `GameUI/CharacterPanelRoot` 上 CharacterStatsPanel 组件的 `inputManager` 都指向这份输入组件。面板脚本的 `panel` 字段指向子节点 `PanelView`，`closeButton` 指向 `PanelView/Panel/CloseBTN`。控制器的 `controlledMember` 指向 `Characters/PlayerRoot` 上的 PartyMemberControl，`view` 指向 Main Camera 上的 ThirdPersonCamera。镜头沿用该节点的 Camera、AudioListener 和已禁用的 CinemachineBrain。键位由 `InputSystem_Actions.inputactions` 配置。
3. PlayerController 启用时让相机跟随 `controlledMember.transform`。移动、环视和一次性按键均作用于场景指定的角色。其 ActorStepBinding 禁用期间暂停提交角色输入。
4. 测试及未来 AI 可直接调用各自角色的 `SetControlInput`。例如给祀提交世界 −X 方向，再推进 Clock，菲比和祀各自执行本人的控制输入。每个角色同一时间由一个控制方写入，后续 Party 模块负责玩家与 AI 的交接。
5. 禁用 PlayerController 时清空该角色已收到的输入并解除相机跟随。独立的 GameInputManager 继续处理面板快捷键。禁用输入管理器时释放光标并立即通知控制器清除角色输入。再次启用输入管理器时按面板可见状态恢复输入。

### 镜头参数与遮挡

`ThirdPersonCamera` 挂在 CombatDemo 现有的透视 `Main Camera` 上。跟随点从玩家逻辑根实时计算，模型在跑步、挥剑时的骨骼摆动不会带动镜头。`Initialize(Transform player)` 保存目标，yaw 取玩家初始水平朝向，pitch 取钳制后的 initialPitch，并标记下一帧直接定位。`ApplyLook(Vector2 deltaPixels)` 将鼠标像素增量应用一次，`PlanarRotation` 返回 `Quaternion.Euler(0, yaw, 0)` 供控制器读取。

| Inspector 字段 | 初值和含义 |
| --- | --- |
| `float height, distance` | 观察点位于根上方 1.5 m，无障碍跟随距离 4 m |
| `float sensitivity, initialPitch, minPitch, maxPitch` | 0.1°/像素，初始俯角 15°，允许 −50°～70°。水平向右增加 yaw，鼠标向上减少 pitch。鼠标增量直接乘灵敏度 |
| `float collisionRadius, collisionPadding, recoverySpeed` | 检测半径下限 0.2 m，命中后保留 0.03 m，离开遮挡后每个真实秒最多拉远 4 m |
| `LayerMask obstructionMask` | 原型勾选 Environment 层。地面和实体墙属于此层，玩家、祀及 Trigger 均排除在相机遮挡检测之外 |

每次 LateUpdate 依次执行下列操作。只读 `float SafeDistance, CurrentDistance` 和 `bool PivotOverlapping` 公开本帧结果，便于定位墙边镜头行为。初始化相机跟随后的第一帧直接取安全距离。

1. 取 `pivot = player.position + up × height`、`rotation = Euler(pitch, yaw, 0)` 和 `back = rotation × Vector3.back`。yaw 环绕 360°，pitch 限制在 Inspector 范围内。
2. 设透视相机近裁面距离为 `n`，半高 `h = n × tan(垂直视野角 / 2)`，半宽 `b = h × aspect`。球半径取 `max(collisionRadius, sqrt(n² + h² + b²))`，使球包住近裁面四角。
3. 先以该半径在 pivot 做 `CheckSphere`。若起点重叠，置 PivotOverlapping 为 true、SafeDistance 为 0，镜头收至观察点。其余情况沿 back 做最远 distance 的 SphereCast。命中时取 `max(0, hit.distance − collisionPadding)`，畅通时取 distance。两次查询使用同一遮挡层和 `QueryTriggerInteraction.Ignore`。观察点本身陷入墙体时，0 距离是该算法的表现边界。
4. 障碍变近时当帧取 SafeDistance。空间变大时用 `MoveTowards(CurrentDistance, SafeDistance, recoverySpeed × Time.unscaledDeltaTime)` 平滑拉远。最后将相机设在 `pivot + back × CurrentDistance`、朝向 rotation。恢复时间取真实帧间隔，让单人顿帧时的观察与环绕仍然连续。

## 7. 验收步骤

在 **Window → General → Test Runner → EditMode** 依次运行 GameInputManagerTests、StateMachineTests、ThirdPersonControlTests。前两组分别使用虚拟键鼠和普通 C# 状态夹具。第三组进入 Play Mode 加载现有 CombatDemo，等 Start 完成后停用 BattleTimeDriver 的自动 Update，使用同一个 `Clock.Advance(realDelta)` 提交动作步。默认倍率为 1、无暂停。调整场景障碍或角色位置后调用 `Physics.SyncTransforms()`。所有临时调整仅在 Play Mode 实例上进行。下表以 M4 收尾的场景配置为基准。M5 中使用缺少可用 Dodge 状态的角色回归右键请求，使用匹配 SO 且启用根运动的角色执行前闪位移用例

| 验收项 | 初始条件与操作 | 可观察结果 |
| --- | --- | --- |
| 角色面板与操控切换 | 进入 CombatDemo 后按 W，再同时按 C 和鼠标键。面板打开期间移动鼠标，按住 W + 左 Shift，按 Esc 关闭。另在操控中按 Esc 释放鼠标，单击游戏窗口恢复操控，再按 C 打开面板并点击关闭按钮 | 开场即能走路。打开面板后光标可见，角色停步，镜头朝向不变。关闭后恢复捕获，持续按住的方向和奔跑键恢复生效，恢复操控的首次点击和关闭按钮的点击均不会触发普攻或闪避 |
| 普攻按下与捕获 | 分别在按下后 0.12 s、0.80 s 松开，在按下、持续按住和松开后分别采样。另在相邻采样之间完成一次点击，再用一次左键点击恢复已释放的光标 | 两种时长都只在按下时读到一次 Attack，持续按住、松开和重复消费均读到 None。采样间的点击保留一次 Attack。重新捕获的点击无攻击命令，再次按下才产生 Attack |
| 角色 FSM | 用两个普通测试状态记录进入、退出和实际获得的 delta。重复切到当前对象，再在 Enter 或 Exit 中请求另一次切换 | 初始化首态只进入一次。有效切换顺序为旧 Exit、更新 CurrentState、新 Enter。同对象保留进度。递归请求得到约定的异常，正常的下一次独立切换可执行 |
| 状态类别与实例隔离 | 在 StateMachineTests 中用同一简化敌人状态类新建两套实例。A 的 Idle 测试计时为 0.10 s，B 为 0.30 s，各推进 0.10 s。另让队员 Idle 收到移动意图 | A 进入自己的巡逻测试状态，B 仍为自己的 Idle，队员则进入 Walk。三者复用同一 StateMachine 实现。M8 有正式敌人 Prefab 后，在 CombatDemo 生成同一 Prefab 的两个实例，复用下方的异向移动与局部顿帧用例 |
| 镜头相对走跑 | 禁用菲比模型 Animator。空地中根朝 +Z，先将镜头水平转向 +X，再按 W，连续推进 0.02 s 步。稳定后依次按 Shift、松 Shift、松 W | 仅环视时根位置、朝向保持。首步朝向介于 +Z 和 +X 之间，实际水平位移与该朝向一致。之后逐步对齐 +X。达满速后直走或跑各 0.20 s 分别约 0.288、0.982 m |
| 减速与步态混合 | 在空地通过各自的 SetControlInput 让两名角色分别满速跑，再提交零方向后推进 0.02 s，再推进 0.04 s，直至停稳 | 首步状态已是 Idle，速度下降约 0.36 m/s，角色仍沿原朝向前进，MoveAmount 介于 1 与 2。随后速度与混合参数继续下降，停稳后播放各自的 Idle |
| 捕获后的开场视角 | 菲比朝 +Z，启用场景输入组件并按住 W。在捕获当帧及随后两帧各注入大幅向下的鼠标增量，第四帧输入向右 40 px、向下 20 px | 前三帧俯角保持 15°，W 可立即起步。随后俯角为 17°、水平转角为 4° |
| 右键请求与移动 | 从 Idle 单击并保持右键。再松开，按 W 行走并重新单击右键，每步 0.10 s | 静止时只有按下步收到 Dodge，保持 Idle 且位移为零。行走中收到 Dodge 时仍为 Walk，从静止满幅直行两步累计约 0.264 m |
| 实际碰撞 | 从墙前 1 m 持续按 W，顶墙稳定后再推进 0.20 s | 仍处于 Walk，实际位移小于 0.015 m，GroundSpeed 小于 0.075 m/s，模型回到待机步态 |
| 双角色、局部顿帧与暂停 | 两人从静止沿各自根朝向行走，各进 0.10 s。只给菲比 0.030 s 顿帧，再进两步 0.020 s。另在暂停步提交一次性命令并恢复 | 后两步菲比累计 0.010 动作秒，多走约 0.0132 m。祀累计 0.040 动作秒，多走约 0.0597 m。暂停时可环视，恢复后没有补发命令 |
| 固定玩家角色 | 在 CombatDemo 按住 W 并单击左键，推进一个正动作步，再只推进一次战斗步 | 菲比进入 Walk，只有第一个动作步的 StepCommands 包含 Attack。祀维持 Idle，镜头跟随菲比 |
| 遮挡与恢复 | 把 Environment/Wall 放在 pivot 和镜头之间，再移开；使 pivot 落入墙体，将鼠标竖直推过两端 | 遮挡当帧 CurrentDistance 等于 SafeDistance，重叠时为 0。移开后每 0.10 真实秒最多恢复 0.40 m，最终回到 4 m。玩家和 Trigger 不引起收近，俯仰限制为 −50° 和 70° |
| 禁用与重开 | 菲比行走后禁用玩家根，松开 W，在禁用期间点击左右键，然后启用并重载场景 | 恢复本角色原入场身份，不补位移或命令。重开后两人各自从 Idle 入场，玩家仍控制菲比，右键只产生输入标记 |

**历史验收。** 旧提交 `21d307c` 的联合原型在 2026-11-23 的 Unity 6000.3.28f1 定向验收中通过 17 项（失败和跳过各 0）。其中 ThirdPersonControlTests 3 项、CombatInputReaderTests 2 项，其余包含动画与既有模块回归。原型的长按用例已随输入规则变更退役，详见 M5 历史记录。[旧原型验证记录](../M5%20Animation%20Foundation/M5%20Animation%20Foundation%20详细技术设计.md#7-历史验证记录)

**设计走查**　M4 的 W 输入经 PlayerController 换算为世界方向，地面 SelectNext 返回 Walk，Machine 完成切换后由 Walk.Tick 调用 Motor，碰撞后速度决定步态参数。M5 增加的步首抢占、Tick 内自然退出与片尾姿态验收见 M5 第 5、6 节。第 8 节所有通过数均属于原日期的实现

## 8. 实现与验收记录

### 收尾复验（2026-11-25）

Unity 6000.3.28f1 在当前工程执行第 7 节的三组 EditMode 用例，完整复跑 **18 项通过，0 项失败，0 项跳过**。第三人称场景用例实际进入 Play Mode 并加载 `Assets/Scenes/CombatDemo.unity`。本轮验证限于 Unity 编辑器。

| 测试组 | 通过 | 验证到的行为 |
| --- | ---: | --- |
| GameInputManagerTests | 2 | 短按和长按都只在按下时提交一次普攻，左右键可在同次采样提交，重新捕获光标的点击用于恢复操控 |
| StateMachineTests | 3 | 状态生命周期顺序、重复切换与重入处理，以及不同实体状态计时和转换的独立性 |
| ThirdPersonControlTests | 13 | 固定操控菲比，面板开关与点击关闭，镜头相对移动、初始俯角与遮挡恢复，双角色的走跑姿态、平滑停步、碰撞、独立顿帧及场景重载 |

收尾时将场景里旧字段 `character` 保存的引用迁移到 `PlayerController.controlledMember`。从磁盘重载场景后，引用指向 `Characters/PlayerRoot` 的 PartyMemberControl，角色面板的根节点启用，子节点 PanelView 关闭。相机用例也已按第 7 节校正，覆盖捕获当帧及随后两帧保持 15°、第四帧恢复环视。首轮组合按键用例曾只读到 Dodge，单独复跑与最终整组复跑均通过。

### 历史验收（2026-11-24）

以下按当时的实现保留。固定距离闪避、HUD 打开入口和运行时切人曾在不同轮次接受验证；现行交付范围见第 1 节，各轮测试数单独统计。

2026-11-24，在当前工程的 Unity 6000.3.28f1 编辑器运行六组 EditMode 测试。场景测试进入 Play Mode 并加载 `Assets/Scenes/CombatDemo.unity`。共通过 **20 项**，失败 0 项，跳过 0 项。

| 测试组 | 通过 | 验证内容 |
| --- | ---: | --- |
| PlayerInputReaderTests | 2 | 虚拟键鼠的短按、持续按住、采样间点击、重新捕获和一次性命令消费 |
| StateMachineTests | 3 | 状态进入、退出与 Tick 的顺序，重入保护，以及同类敌人测试状态的独立计时与转换 |
| ThirdPersonControlTests | 8 | 真实渲染帧下的输入、战斗步与镜头跟随，关闭 Animator 后的走跑，限时闪避与墙面碰撞，局部顿帧与暂停，主控交接，禁用与重载，以及镜头遮挡恢复 |
| ActorSceneTests | 3 | M3 入场登记、生命周期和重新入场 |
| BattleClockSceneTests / BattleClockTests | 2 + 2 | 动作时钟的场景调度、暂停、倍率和单角色顿帧 |

空地用例按 XZ 平面的实际位移验收。菲比满幅输入走、跑 0.20 动作秒，分别落在 0.288 m、0.982 m 的 ±0.01 m 范围内。两人异向闪避时，菲比受 30 ms 局部顿帧后的两个 20 ms 步合计前进约 0.10 m，祀前进约 0.40 m。场景重载后单独用向下射线检查两名角色胶囊底部与地面的距离，容差使用各自的 skinWidth 加 0.001 m。

本次验证使用虚拟键鼠和场景物理，鼠标灵敏度、转向速度和镜头恢复速度的主观手感留待实际试玩。

**2026-11-24 基础步态补验。** 在 CombatDemo 的 Play Mode 中，菲比和祀依次接收 W、W + 左 Shift 和松键。模型输出分别选中各自的 `GS_Walk / GS_Run / GS_Idle` 和 `DK_Walk / DK_Run / DK_Idle`（对应片段权重大于 0.9）。两人各走 0.16 动作秒时，右大腿骨的局部旋转变化均超过 1°。菲比局部顿帧期间骨骼保持，恢复后继续迈步；持续顶墙至水平速度趋零时回到待机姿态。此次定向复验通过 15 项，其中 ThirdPersonControlTests 10 项、ActorSceneTests 3 项、BattleClockSceneTests 2 项，失败及跳过均为 0。

**2026-11-24 角色面板输入复验（含当时的 HUD 打开按钮）。** 当日的 Unity 6000.3.28f1 Test Runner 通过 21 项，失败和跳过均为 0。覆盖玩家输入 2 项、第三人称控制 12 项、属性集成 3 项、资源场景 2 项和构筑场景 2 项。CombatDemo 中使用虚拟键鼠验证了开场直接行走、按 C 打开、按 C 或 Esc 关闭，以及通过 EventSystem 点击实际打开和关闭按钮。界面操作期间没有产生移动、镜头转动、普攻或闪避，关闭时持续按住的移动和奔跑键可在下一次采样恢复。

**2026-11-24 撤下匀速闪避后复验。** 当前 Unity 6000.3.28f1 编辑器运行 17 项，通过 17 项，失败和跳过均为 0。ThirdPersonControlTests 12 项、PlayerInputReaderTests 2 项、StateMachineTests 3 项。场景实测覆盖静止和行走中点击右键、持续按住只产生一次 Dodge、独立动作时钟、墙面碰撞、切人、冻结与场景重开。M5 的根轨迹采样结果见其第 5 节。

**2026-11-24 转向、停步与开场镜头复验。** Unity 6000.3.28f1 通过 ThirdPersonControlTests 13 项、PlayerInputReaderTests 2 项、StateMachineTests 3 项，共 18 项，失败和跳过均为 0。CombatDemo 用例验证转向时的水平位移跟随根朝向，菲比和祀从跑步进入 Idle 时均继续减速，步态混合值随实测速度下降。模拟捕获当帧和下一帧的 −900 px 纵向增量后，俯角维持 15°，同一时间按 W 能起步，随后正常环视可达 17°。另从编辑器直接启动 CombatDemo，读到主相机俯角 15.000002°，目标为 PlayerRoot，面板关闭，光标已锁定。

**2026-11-24 输入帧重构复验。** 当前工程在 Unity 6000.3.28f1 运行 ThirdPersonControlTests 13 项、PlayerInputReaderTests 2 项、StateMachineTests 3 项，共 18 项通过，失败和跳过均为 0。CombatDemo 中，菲比按住 W 并点击左键，采样后切到面朝 +X 的祀，直接推进 0.10 动作秒，祀沿 +X 前进约 0.120 m，两人都没有收到旧的攻击。连续推进动作步只执行一次按下，下一份输入覆盖未执行的按下。采样后从 UI 打开面板会撤回已提交的移动和按键，局部顿帧只丢弃受影响角色本步的按键。
