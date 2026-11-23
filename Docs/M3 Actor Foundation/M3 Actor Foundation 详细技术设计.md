# M3 Actor Foundation 战斗单位详细设计

**阶段已收尾（Verified） · 2026-11-23**　菲比与祀已在 CombatDemo 以统一战斗单位接入。收尾复验 11 项全部通过。[实际验证记录](#实际验证记录--2026-11-23)

两名角色开场时各自取得本场唯一的 `ActorId`。从角色逻辑根的 `Combatant.Runtime` 可访问该实例的构筑、属性、资源、效果和状态，持有 `ActorId` 的调用方可通过 `SceneActors.TryGet` 反查场景组件。[战斗系统总方案](../仿终末地战斗系统设计方案.md)

## 1. 从当前工程接入

| 已核实的工程事实 | 对 M3 的影响 |
| --- | --- |
| 两份角色 Prefab 的逻辑根都挂有 `CharacterStats`、`CharacterBuildComponent`、`CharacterResources`、`CharacterEffects`、`CharacterStatus`。各自引用本角色的属性、构筑和资源配置，共用战斗状态规则 | 两份 Prefab 的逻辑根各有一个 `Combatant`，组合这五个组件已创建的运行时对象。[菲比 Prefab](../../Assets/Prefabs/Characters/Phoebe.prefab) · [祀 Prefab](../../Assets/Prefabs/Characters/Si.prefab) |
| 当前编辑器中，`Characters/PlayerRoot` 是菲比 Prefab 实例，根上还有 `CharacterController`。`Characters/PlayerRoot/Phoebe` 挂有 Animator。场景中禁用的 `Characters/Si` 只有 Transform 和 Animator | 菲比沿用原实例及 HUD 引用，第二个战斗单位是完整的祀 Prefab 实例 `Characters/SiRoot`。[CombatDemo 场景](../../Assets/Scenes/CombatDemo.unity) |
| `Combat Session` 已挂 `BattleTimeDriver` 和 `TeamSkillPoints`。现有时钟能按 `ActorId` 注册、读时、局部顿帧和注销。场景时间用例读取菲比的正式 ID | M3 在同一节点放置本场单位登记组件，由它分配 ID 并向同一时钟登记。队伍 SP 仍由场景持有。[战斗时钟](../../Assets/Scripts/Runtime/Timing/BattleClock.cs) · [场景时间用例](../../Assets/Scripts/Test/Editor/Timing/BattleClockSceneTests.cs) |
| M2 收尾记录已验收效果、状态及两者与战斗时间的联动。`CharacterEffects.Start` 订阅场景时钟的战斗步，订阅持续到销毁 | Actors 登记的是每个角色的**动作时钟**。效果计时沿用现有订阅，避免一次战斗步被推进两次。[M2 验收记录](../M2%20Runtime%20Foundation/M2%20Runtime%20Foundation%20开发设计.md) · [CharacterEffects](../../Assets/Scripts/Runtime/Effects/CharacterEffects.cs) |

总方案已确定角色身份由 Actors 分配，当前 HP 由 Resources 持有，动画、最终位移、攻击命中、伤害、操控权分别在 M4、M5、M6、M7、M8 接入。角色定义由各自的角色 Prefab 承担。根组件 `Combatant` 提供 Unity 接线和 Inspector 配置；每次入场创建一份独立的普通 C# 对象 `ActorRuntime`，向战斗调用方提供身份与模块引用。M3 的目标查询只回答两名已知单位能否互为攻击双方，距离、视野、锁定优先级和友方技能选人留给各自的调用方。

## 2. 实施文件

以下均为相对工程根目录的路径。运行时类型使用命名空间 `Endfield.Actors` 和 Unity 默认程序集，Inspector 使用原生序列化字段。

| 变更 | 路径 | 职责 |
| --- | --- | --- |
| 新增 | `Assets/Scripts/Runtime/Actors/Combatant.cs` | 定义 Faction 枚举和角色逻辑根组件 Combatant，配置显示名、阵营和模型，向调用方提供本实例的 Runtime |
| 新增 | `Assets/Scripts/Runtime/Actors/ActorRuntime.cs` | 普通 C# 对象，组合一次入场的身份、基础模块引用和生命周期 |
| 新增 | `Assets/Scripts/Runtime/Actors/ActorLifecycle.cs` | 定义 `ActorLifeState`，处理生命变化、普通治疗准入、死亡通知和离场清理 |
| 新增 | `Assets/Scripts/Runtime/Actors/SceneActors.cs` | 当前战斗的单位表、ID 分配、时钟登记和敌对目标查询 |
| 修改 | `Assets/Prefabs/Characters/Phoebe.prefab` | 根挂 Combatant，填写菲比名称、阵营，绑定模型 Animator |
| 修改 | `Assets/Prefabs/Characters/Si.prefab` | 根挂 Combatant，填写祀的名称、阵营，绑定模型 Animator |
| 修改 | `Assets/Scenes/CombatDemo.unity` | 装配 SceneActors、第二个完整角色和开场引用 |
| 新增 | `Assets/Scripts/Test/Editor/Actors/ActorLifecycleTests.cs` | 用真实资源容器验证存活、治疗、死亡和解除订阅 |
| 新增 | `Assets/Scripts/Test/Editor/Actors/ActorSceneTests.cs` | 进入 Play Mode 验证两个 Prefab 的接线、身份、阵营、动作时钟和销毁 |
| 修改 | `Assets/Scripts/Test/Editor/Timing/BattleClockSceneTests.cs` | 真实场景用例改读正式角色的 ID；独立时钟的双动画图原型继续自建测试 ID |
| 修改 | `Assets/Scripts/Runtime/Stats/Definitions/CharacterStatProfile.cs`、`Assets/Scripts/Runtime/Stats/Runtime/CharacterStats.cs` | 角色名迁入 Actors，Stats 继续持有数值配置与面板 |
| 修改 | `Assets/Scripts/Editor/Stats/CharacterStatProfileEditor.cs`、`Assets/ScriptObjects/Stats/Phoebe.asset`、`Assets/ScriptObjects/Stats/Si.asset` | 清理旧角色名输入框及两份属性配置的旧字段 |
| 修改 | `Assets/Scripts/Runtime/UI/CharacterStatsPanel.cs`、`Assets/Scripts/Test/Editor/Stats/CharacterStatsIntegrationTests.cs` | 属性页按已绑定的角色根读名，验证菲比与祀的标题、数值以及切换 |

**直接复用的文件**

| 入口文件 | M3 读取或调用的内容 |
| --- | --- |
| [CombatIds.cs](../../Assets/Scripts/Runtime/CombatContracts/CombatIds.cs) · [BattleTimeDriver.cs](../../Assets/Scripts/Runtime/Timing/BattleTimeDriver.cs) · [BattleClock.cs](../../Assets/Scripts/Runtime/Timing/BattleClock.cs) | `ActorId(uint)`、当前场景的 `Clock`、`RegisterActor`、`UnregisterActor` 与 `TryGetActionTime` |
| [CharacterBuildComponent.cs](../../Assets/Scripts/Runtime/CharacterBuild/CharacterBuildComponent.cs) | 从角色根取得 `State`，沿用构筑模块对装备和等级的管理 |
| [CharacterResources.cs](../../Assets/Scripts/Runtime/Resources/Character/CharacterResources.cs) · [CharacterResourceContainer.cs](../../Assets/Scripts/Runtime/Resources/Character/CharacterResourceContainer.cs) · [ResourceReadModels.cs](../../Assets/Scripts/Runtime/Resources/Shared/ResourceReadModels.cs) | 取得 `Container`，读取 Snapshot，订阅前后快照，用 `RestoreHP` 提交获准的治疗 |
| [CharacterEffects.cs](../../Assets/Scripts/Runtime/Effects/CharacterEffects.cs) · [CharacterStatus.cs](../../Assets/Scripts/Runtime/Status/CharacterStatus.cs) · [ChangeNotifier.cs](../../Assets/Scripts/Runtime/Common/ChangeNotifier.cs) | 借用两份 `Controller`，通过既有通知工具发布本单位的死亡事实 |

## 3. 从角色定义到运行时实例

菲比和祀各有一份角色 Prefab，开场各一名。角色显示名、阵营和模型引用配置在各自逻辑根的 Combatant 上。名称供 UI 展示，事件与时钟使用本场的 ActorId，角色重新入场时取得新 ID。[M1.4 的身份约定](../M1%20Core%20Data%20Foundation/M1.4%20CombatContracts%20与%20Timing%20详细技术设计.md)

| 所属类型 | 关键成员和来源 | 读取或变更时机 |
| --- | --- | --- |
| `Combatant : MonoBehaviour` 配置 | 序列化 `string displayName`（必填）、`Faction faction`（Player = 0、Enemy = 1）、`Animator modelAnimator`（必填）。分别公开只读 DisplayName、Faction、ModelAnimator | 两份 Prefab 均选 Player。属性页在 OnEnable、打开和切换时读取同根 Combatant.DisplayName，无需等待入场。属性行名（如攻击力）仍读 StatDefinition.DisplayName |
| `Combatant` 场景绑定 | `SceneActors owner` 与 `ActorRuntime Runtime` 入场前均为空。Bind 接收本场单位表和 ID，获取同根五份模块，并构造 Runtime | 销毁时通过 owner 注销，Unbind 释放 Life 并清空两个引用。模型和 Transform 留在 Unity 组件上 |
| `ActorRuntime` 身份 | 对外只读 `ActorId Id`，构造时取 SceneActors 分配的正整数，现场先后为菲比 1、祀 2 | 登记时创建，临时禁用期间保留。后续 Actions 以 Id 构造 AttackId，HitDetection 用目标 Id 填 HitContext.Target |
| `ActorRuntime` 角色数据 | 只读属性 `CharacterBuildState Build`、`IReadOnlyStatSet Stats`、`IReadOnlyCharacterResources Resources`，依次取同根组件的 State、Stats、Container | 普通调用方直接读最终面板、生命和能量。装备操作走 Build 的公开方法。只读接口背后仍是原组件持有的那一份实例 |
| `ActorRuntime` 战斗状态 | 只读属性 `EffectController Effects`、`StatusController Status`，取同根两份 Controller。每次入场新建一份 `ActorLifecycle Life` | Effects 与 Status 继续各自更新。Actors 只拥有自己创建的 Life，销毁时各基础组件自行清理所持模块 |
| `ActorLifecycle` | `ActorLifeState State` 取 Alive、Dead、Removed。构造时保存本次 ActorId、可写的 CharacterResourceContainer，读取 Snapshot.CurrentHP，并持有一份 `IDisposable` 资源订阅和一份 `ChangeNotifier<ActorId>` | 初始 HP 大于 0 为 Alive，等于 0 为 Dead。资源回调更新存活状态，离场更新为 Removed。HP 始终从 Resources 读取，单位为生命点 |
| `SceneActors : MonoBehaviour` | 序列化 `Combatant[] initialActors`，默认空数组。运行时持有同根驱动的 `BattleClock`、初始空的 `Dictionary<ActorId, Combatant>`、初值为 1 的 `uint nextId` | Start 按 Inspector 顺序登记。之后每成功登记一个新实例使用下一个正整数；离场只移除对应项。重开场景创建新表与新时钟 |

这些引用足以让后续系统按需取数。例如 M7 捕获出招面板可取 `actor.Runtime.Stats` 和 `actor.Runtime.Effects.OutgoingDamageBonus`。目标扣血仍由 Damage 向 Resources 提交，本阶段用资源接口输入已结算的数值来验收生命周期。[M7 伤害设计草案](../M2%20Runtime%20Foundation/M2.4%20Damage%20详细技术设计.md)

## 4. 入场、查询与离场

### 开场与动态入场

1. `BattleTimeDriver.Awake` 以顺序 −200 建立本场时钟。两个角色根上依次执行 Stats（−100）、Build（−75）、Resources（−50）、Effects（−40）、Status（−30）的 Awake。起始装备在资源容器读取 MaxHP 前生效。
2. `SceneActors` 与驱动同挂，以 `[DefaultExecutionOrder(-10)]` 在 Awake 缓存同根 `BattleTimeDriver.Clock`，在 Start 遍历 `initialActors`，调用 `ActorRuntime Register(Combatant unit)`。默认顺序的场景消费者在 Start 就能获取开场单位。ID 分配、查询和生命通知均在 Unity 主线程执行。
3. Register 对本表已登记的 unit 直接返回原 Runtime。首次登记要求 unit 属于同一场景并已启用。若已有其他单位表持有该角色，调用方会收到错误。
4. 以 `new ActorId(nextId)` 登记角色动作时钟。成功后 Combatant.Bind 校验显示名、模型以及已初始化的同根模块，构造 Runtime 和 Life。绑定失败就撤销此次时钟登记；时钟自身报告冲突时保留原记录。绑定成功才加入字典并递增序号。
5. 开场名单登记完毕后，生成者可 `Instantiate` 一个已启用的完整 Prefab，显式调用同一 Register。若在禁用父节点下预装配，则先激活再登记。普通 Prefab 可独立用于既有属性、资源或动画测试，只有登记入场的实例才取得战斗身份。

### 查找单位与判定敌对

| API | 返回值与调用方约定 |
| --- | --- |
| `bool SceneActors.TryGet(ActorId id, out Combatant unit)` | 已登记返回 true 与对应角色根，包含仍在场的倒下或临时禁用单位。0、未知或已离场的 ID 返回 false，unit 为空。事实订阅者可以用它定位刚倒下的角色 |
| `bool SceneActors.CanTargetEnemy(ActorId source, ActorId target)` | 先查本表中的双方，再检查两者的 Combatant 均为 `isActiveAndEnabled`、Life.State 均为 Alive、阵营相异。全部满足才返回 true。Player 对 Enemy、Enemy 对 Player 均可，同阵营及自己均不可 |
| `double ActorLifecycle.RestoreHP(double finalHealing)` | 普通治疗的角色入口，输入是已结算的非负有限生命点。Alive 时直接返回资源容器实际恢复量，Dead 或 Removed 时返回 0。治疗量与剩余容量的裁剪由 Resources 完成 |
| `IDisposable ActorLifecycle.SubscribeDied(Action<ActorId> listener)` | 订阅该实例入场后发生的死亡。回调同步收到死者 ID，可读取已提交的 HP 和 Dead 状态。订阅方释放返回值可解除监听 |

M6 可从碰撞体向上取得 `Combatant`，取 Runtime.Id，再用本场的 CanTargetEnemy 筛选。冻结和失衡仍由 Status 表达；被冻结的活单位仍可成为敌方目标，之后能否发起动作由 Actions 判断。相同数值的 ID 可以出现在两次不同战斗中，命中与查询应使用产生该 ID 的那场单位表。

### 临时禁用、生命归零和销毁

`ActorLifecycle(ActorId id, CharacterResourceContainer resources)` 构造时读取快照并订阅该容器。SceneActors 在角色离场时调用 `ActorLifecycle.Dispose()`，将状态置为 Removed 并释放这份订阅。这两个公开入口也供纯 C# 生命周期用例创建和释放夹具。角色根和场景节点统一经 `SceneActors` 的内部方法 `void Unregister(Combatant unit)` 完成注销。

| 触发 | 本场可观察到的结果 |
| --- | --- |
| 角色受到非致命伤害、治疗、回能或失衡变化 | Life 读取资源通知中的前后快照。HP 仍大于 0 时维持 Alive。普通治疗由上述入口直接返回实际恢复点数 |
| Alive 且 HP 从正数变为 0 | 同步先写 Dead，再发布一次死者 ID。MaxHP 降为 0 导致的归零也遵循此规则。重复打击、回能或增加 MaxHP 都不再发布死亡。入场时已经为 0 HP 的单位直接建立 Dead 状态，无历史事件补发 |
| 单位已倒下 | 保留登记、动作时钟和最后的模块引用，供死亡表现或致命一击反馈使用。敌对目标筛选排除该单位，普通治疗返回 0。M3 的 Dead 持续至离场；复活玩法需另定规则。Effects 继续按现有战斗时间到期 |
| 临时 `SetActive(false)`，随后重新启用 | OnDisable 保留登记、ID、生命订阅和动作累计。禁用期间敌对筛选返回 false。重新启用后，同一个仍存活的实例再次可选，期间收到的致死资源变化仍有效 |
| 销毁已入场角色 | Combatant.OnDestroy 通知 owner 按本实例注销。SceneActors 从表移除它，调用 Clock.UnregisterActor，Life 置 Removed 并释放资源订阅，最后解绑 Combatant。主动销毁活单位只表示离场。已持有的旧 Life 可观察到 Removed |
| 卸载场景 | SceneActors.OnDestroy 对表内剩余单位走相同注销流程。若角色先销毁，其记录已移除；若登记组件先销毁，它先解绑角色，随后角色销毁无需再次注销。重新加载 CombatDemo 时从 1 编号 |

致命扣血的可观察顺序是 Resources 写入 HP → Life 在资源通知中标记 Dead 并通知监听者 → `ApplyDamage` 返回实际扣血和溢出 → M7 Damage 完成本次结算并发布扣血事实。死亡回调只表示已经归零；最终致命一击的扣血事实仍能通过 ID 定位单位。

## 5. Unity 装配与开发顺序

1. **配置两份角色 Prefab。** 在 Prefab Mode 选中最外层逻辑根，添加 Combatant。菲比的 Display Name 填「菲比」，祀填「祀」，Faction 都选 Player。在 Model Animator 中拖入各自模型子节点的 Animator，保存 Prefab。
2. **接入属性页。** GameUI 的 CharacterStatsPanel 保留场景中已有的 CharacterStats 引用。BindCharacter 从同根 Combatant 读取名字，再从 CharacterStats 读取属性及其变化。即使面板在开场时已展开，也可在 SceneActors.Start 之前显示正确的名字。
3. **绑定场景。** 实现 SceneActors。在 `Combat Session` 上挂一份，在 `Characters` 下拖入完整的 `Si.prefab`，改名 `SiRoot` 并启用，放在菲比旁可辨认的位置。保留原有禁用模型 `Characters/Si`。将 Initial Actors 的第 0 项设为 `Characters/PlayerRoot`，第 1 项设为 `Characters/SiRoot`，均拖逻辑根。现有 HealthHUD 与属性页继续绑定菲比。
4. **交接场景测试。** 现有时间场景用例加载场景后先等一帧，再取 PlayerRoot 的 Runtime.Id，替换手写的 `new ActorId(1)` 和手工注册。周期回能用例直接使用已登记的 SiRoot，测试内关闭、重开该角色，退出 Play Mode 时随场景清理。角色动画图原型使用自建的 BattleClock，与场景登记独立。

## 6. 验收步骤与预期值

在当前工程打开 `Window → General → Test Runner → EditMode`。先运行 `Endfield.Actors.Tests.ActorLifecycleTests`，再运行 `ActorSceneTests`；后者通过 `EnterPlayMode` 加载 CombatDemo，等待开场 Start 完成。下表各行使用独立夹具或重新加载的场景。

| 用例 | 输入与操作 | 调用者应观察到 |
| --- | --- | --- |
| 生命、治疗与死亡通知 | 取 150 HP 的菲比容器，创建 Life 并订阅。依次扣 40，普通治疗 10，再扣 150，随后再扣 1、普通治疗 20 | HP 为 110 → 120 → 0 → 0 → 0。治疗分别返回 10 和 0，致命一击 HPLost = 120、Overkill = 30。只收到一次菲比 ID，回调内 HP 已是 0 且 State 为 Dead |
| 初始零生命与离场 | 在绑定 Life 前先将 HP 扣至 0，登记并订阅后回能。另取一个满血实例，保留容器和 Life 引用，先执行 Life 的离场清理再对容器扣至 0 | 前者初始 Dead，回能后死亡通知数为 0。后者保持 Removed，旧监听者收到 0 条死亡通知 |
| 双角色装配与隔离 | 加载正式场景。按两个根的 ID 反查，核对 Runtime 五份引用与各自根组件的公开对象；取 `ModelAnimator` 与对应模型组件比对。给菲比扣 40，并对祀再次调用 Register | ID.Value 分别为 1、2，均属 Player。满血初值分别为 150/150、180/180，终结技能量为 90/100、30/80。随后 HUD 显示 `110 / 150`，祀仍为 180/180。重复登记返回同一 Runtime。两人之间与对自己调用 CanTargetEnemy 均为 false |
| 重开战斗 | 给菲比扣 40，保留她的旧 Runtime 和战斗时钟引用，重新以 Single 模式加载 CombatDemo | 旧 Life 为 Removed，旧时钟查不到菲比。新菲比以 ID 1、150 HP 开场 |
| 敌我筛选与禁用 | 仅在该用例临时将场景中的祀设为 Enemy。双向查询，禁用、启用后再查询，最后在禁用期间给祀提交 180 点伤害并启用 | 启用且存活时双方互选为 true。禁用时 ID 和时钟仍可查，双向互选为 false。致死后祀保持 Dead，菲比保持 Alive |
| 局部顿帧与重建 | 停用驱动的自动 Update，给菲比 0.030 秒顿帧，手动两次 Clock.Advance(0.020)。销毁祀并等一帧，再生成一名祀并登记 | 菲比累计推进 0.010 秒，祀推进 0.040 秒。旧 ID 2 从单位表与时钟注销，向旧 ID 请求顿帧返回 false。新祀 ID 3、HP 180、动作累计为 0，菲比保留原进度 |
| 配置错误恢复 | 新祀首次登记前清空 Display Name，检查候选 ID 3，填回「祀」后重试 | 首次报告角色缺失显示名，Runtime 为空，单位表和时钟均无 ID 3。修正后登记成功，仍获得 ID 3 |

对已有行为做两项定向回归。运行 `BattleClockSceneTests.CombatDemoDrivesCharacterEffectsAcrossPauseAndCharacterSwitch`，确认祀的终结技能量仍按 30 → 35 → 40 → 45 推进且菲比冻结到期。运行 `CharacterBuildSceneTests.CombatDemoEquipmentUpdatesHealthBarAndStatPanel`，确认木剑使菲比受伤后的 HUD 从 110/150 更新至 110/165。两项均从 EditMode 入口自动进入 Play Mode。[时间与效果用例](../../Assets/Scripts/Test/Editor/Timing/BattleClockSceneTests.cs) · [构筑与 HUD 用例](../../Assets/Scripts/Test/Editor/CharacterBuild/CharacterBuildSceneTests.cs)

### 实际验证记录 · 2026-11-23

Unity 6000.3.28f1 在当前工程完成脚本编译。通过 EditMode Test Runner 运行 11 项，最终 11 项通过、0 项失败、0 项跳过。场景用例由测试自动进入 Play Mode。覆盖范围如下。

| 用例组 | 通过 | 实际核对的结果 |
| --- | ---: | --- |
| ActorLifecycleTests | 3 | 致命一击实际扣 120、溢出 30，只发布一次死者 ID。MaxHP 降至 0 也触发倒下，离场后停止治疗与死亡通知 |
| ActorSceneTests | 3 | 两个真实 Prefab 取得 ID 1、2，各自引用本角色的五个模块。禁用期间仍能接收致死变化。菲比与祀的动作累计分别增加 0.010、0.040 秒；祀销毁重建后取得 ID 3，错误配置修正后可重新登记 |
| CharacterStatsIntegrationTests | 3 | 调整属性配置仍只影响随后创建的角色。属性页显示「菲比」，切换后显示「祀」，装备、属性刷新和重新打开面板均通过 |
| CharacterBuildSceneTests、BattleClockSceneTests 各一项 | 2 | 木剑使受伤菲比的 HUD 从 110/150 更新至 110/165。祀在暂停、半速和临时禁用期间按既有规则回能，观测到 35、40、45，菲比冻结到期 |

开发期间首轮有一项用例在测试夹具加载阶段失败，改用协程场景用例后全组通过。将 `Faction` 归入 `Combatant.cs` 后，阶段收尾再次在当前工程执行上表全部 11 项，结果为 **11 通过、0 失败、0 跳过**。Windows Player 构建列入 M10 集成验收。

## 7. 交给 M4 的接口

M4 Animation 在角色登记后，从 `Combatant.ModelAnimator` 取得模型的 Animator，从 `Combatant.Runtime.Id` 取得本次入场的身份。在本场 `BattleTimeDriver.Clock` 上调用 `TryGetActionTime(id, out var step)`，成功时使用 `step.Delta` 推进该角色的动画图。角色临时禁用时保留同一 ID；销毁后旧 ID 查询返回 false。两人的动作时间独立，菲比的局部顿帧不会扣减祀的动作时间。[动作时钟](../../Assets/Scripts/Runtime/Timing/BattleClock.cs) · [双角色场景验收](../../Assets/Scripts/Test/Editor/Actors/ActorSceneTests.cs)

M3 的交付接口以当前实现为准。生命周期通过 `ActorRuntime.Life` 提供，Resources 持有 HP，`ActorLifecycle` 负责死亡通知、普通治疗准入和离场状态。后续若调整这条接口，按总方案的冻结规则核对调用方并回归相应用例。
