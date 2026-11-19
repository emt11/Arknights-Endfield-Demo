# 当前项目设计优化计划

> 更新日期：2026-11-17
>
> 状态：OPT-01～OPT-09 已实施并完成针对性验证；结果见第 6 节
>
> 范围：当前 P1 工程的设计优化，不扩展 P2～P5 功能

## 1. 审查结论与执行边界

本计划最初依据 `Assets/Scripts/Runtime/`、`Assets/Scripts/Editor/` 的源码、调用位置、相关资产和已有测试整理。初始静态审查覆盖 45 个自有运行时脚本及 7 个编辑器脚本，不包含自动生成的输入代码；以下保留设计理由和实施要求，本轮已执行的编译、编辑器与 Play Mode 验证见第 6 节，未执行 Player 构建。

现有架构总体合理：动作定义与单次执行状态分离，命中查询与伤害结算分离，角色位移统一由 Motor 提交，对象池和编辑器预览各自管理资源生命周期。本轮优先处理参数一致性、烘焙依赖与停用契约，再进行局部结构和代码规范整理。

执行时遵循以下约束：

- 按任务逐项修改，保留输入消费、零时刻通知、动画求值、位移提交、命中结算和结果发布的既有顺序
- 接口、抽象和方法提取必须解决当前问题，不建立通用动作框架、查询接口、全局事件总线或依赖注入框架
- 继续信任开发者维护的配置；检查运行中会变化的启用状态，不恢复全量配置扫描或保存前校验流程
- 结构整理保持现有行为；涉及根运动开关等行为变化时，按本计划单独标明并验证
- 不修改 `Docs/TODO.md`，不覆盖现有未提交改动，不修改无关 UI、素材、第三方源码和生成文件
- 需要改名或移动脚本时维护引用并保留必要的 `.meta`；不要只为格式统一迁移序列化字段

本文只记录本轮任务与验收条件。整体路线以 [仿终末地完整实现方案](仿终末地完整实现方案.md) 为准，实际完成与验证记录写入 [战斗系统完整实现文档](战斗系统完整实现文档.md)，源码入口变化同步 [项目阅读说明](项目阅读说明.md)。本计划中的预期行为不能提前写成已实现结果。

## 2. 任务顺序

| 顺序 | 编号 | 任务 | 性质 | 状态 |
|---|---|---|---|---|
| 1 | OPT-01 | 补齐根轨迹烘焙签名 | 已确认的依赖遗漏 | 已完成 |
| 2 | OPT-02 | 统一组件停用与中央调度契约 | 已确认的生命周期边界不一致 | 已完成 |
| 3 | OPT-03 | 明确根运动开关含义 | 接口语义调整，采用下文建议方案时会改变部分行为 | 已完成 |
| 4 | OPT-04 | 统一预览与运行时的运动参数 | 已确认的参数差异及职责整理，结合 OPT-03 实施 | 已完成 |
| 5 | OPT-05 | 简化移动状态机 | 可选结构整理，保持行为 | 已完成 |
| 6 | OPT-06 | 收窄内部操作入口 | 可选 API 整理，保持行为 | 已完成 |
| 7 | OPT-07 | 局部代码规范整理 | 可选整理，可随相关任务一起完成 | 已完成 |
| 8 | OPT-08 | 复用已有 Phoebe 模型，移除专用烘焙 Prefab | 资源简化，保持烘焙轨迹与运行时行为 | 已完成 |
| 9 | OPT-09 | 按通知类型拆分 ActionPoint 数据 | 数据结构优化，保持通知行为并迁移已有资产 | 已完成 |

OPT-01 可独立完成。OPT-02、OPT-03 都涉及模拟器，分开修改和验证；OPT-04 在移动来源语义明确后收口。OPT-05～OPT-07 不作为前四项完成的前置条件。

OPT-08 可与 OPT-01 同批处理根轨迹资产，但需分别验收：模型替换前后使用相同的动画、尺度和采样率，避免将采样参数变化混入模型替换的轨迹比较。

OPT-09 可独立实施；与 OPT-06 同时涉及 ActionController 时协调修改范围，并单独验收通知时序与资产迁移，不将两项的验证结果相互替代。

## 3. 优先修正

### OPT-01：补齐根轨迹烘焙签名

**问题与位置**

[RootMotionBaker.Signature](../Assets/Scripts/Editor/Combat/RootMotionBaker.cs) 的签名包含动画依赖、模型依赖和模型尺度，却没有包含 `sampleRate`。[RootMotionDefinition.Sample](../Assets/Scripts/Runtime/Combat/Animation/RootMotionDefinition.cs) 使用当前采样率计算样本索引，因此修改采样率后，旧样本会被按新参数解释，而现有过期提示无法识别这次变化。

**具体修改**

1. 将采样率加入现有依赖签名，使用稳定的数值格式
2. 保留 [RootMotionDefinitionEditor](../Assets/Scripts/Editor/Combat/RootMotionDefinitionEditor.cs) 的过期提示和显式烘焙入口；参数变更后重新烘焙再使用
3. 不增加运行时签名校验、自动烘焙或全量资产扫描
4. 签名格式变化后，在实际实施时重新烘焙受影响的现有根轨迹资产，保留其 GUID、动画引用及原有参数

**最小验收**

- 在临时副本上仅修改采样率，签名应变化，Inspector 应提示重新烘焙
- 使用新采样率烘焙后提示消失，样本覆盖片段起点、内部时间和准确终点
- 复用 `CombatWarpEditorValidation` 增补该场景，检查预览资源释放；无需运行全部战斗测试

### OPT-02：统一组件停用与中央调度契约

**问题与位置**

[CombatSimulation](../Assets/Scripts/Runtime/Combat/CombatSimulation.cs) 主要按 `Combatant.isActiveAndEnabled` 筛选单位，然后直接调用 Motor、Actions 和 Animation。各组件又分别拥有自己的 `OnDisable` 清理逻辑，导致单独停用组件后，仍可能被中央流程调用。

例如，[ActionController](../Assets/Scripts/Runtime/Combat/Actions/ActionController.cs) 停用时取消当前动作，但起手检查没有拒绝停用状态；[AnimationPlayer](../Assets/Scripts/Runtime/Combat/Animation/AnimationPlayer.cs) 停用时销毁图，模拟器仍可能调用其求值方法。

**建议采用的行为契约**

| 停用对象 | 预期行为 |
|---|---|
| PlayerController | 停止玩家意图提交并清理待处理玩家输入，不自动取消已经正常起手的动作 |
| ActionController | 取消当前动作、归还其效果句柄，拒绝新动作；普通移动可继续 |
| CharacterMotor 或 AnimationPlayer | 停止依赖该组件的主动作执行并清理当前动作；不调用停用的 Motor 或已释放的动画图 |
| Combatant 或角色 GameObject | 清理当前动作，停止该角色的模拟与命中参与 |
| 重新启用上述角色组件 | 从有效的空闲状态恢复，不恢复已经结束的执行 ID 和旧效果句柄 |

**具体修改**

1. 在角色生命周期协调、中央调度和动作提交边界落实上述约定，不在每个私有函数里重复检查
2. 统一动作推进、事件处理、命中收集与自然结束阶段的参与条件，避免只停止动画却继续推进玩法
3. 协调动作退出和动画图释放的顺序；动作清理不应依赖已经销毁的图，停用期间也不能重新起手
4. 保留静止木桩没有 Motor、Animation、Actions 的现有组成，不要求所有 Combatant 拥有完整玩家组件
5. 保留现有执行 ID 检查和结果发布期间延后取消的机制；核对回调中停用对象时，已排队的清理仍能正确完成
6. 区分自动 Update 与显式步进：现有验证会先禁用 `CombatSimulation` 再手动调用 `AdvanceFrame`，不能因此封死手动验证入口

**最小验收**

- 攻击期间分别停用 ActionController、AnimationPlayer、CharacterMotor 和角色对象，旧动作结束、效果句柄释放，不再产生旧动作命中
- 停用 ActionController 后继续输入不能启动新动作；重新启用后可正常起手
- 停用 PlayerController 后不消费残留攻击请求，已启动动作仍可按上述契约完成
- 复用 `CombatActionProbe`、必要的 `CombatHitProbe` 场景补充生命周期检查；确认木桩受击和手动步进入口仍正常

### OPT-03：明确根运动开关含义

**问题与位置**

[CombatSimulation.MoveUnits](../Assets/Scripts/Runtime/Combat/CombatSimulation.cs) 先根据 `useRootMotion` 决定是否读取动画根位移，随后只要有 `warpWindows` 就用扭曲结果覆盖位移。因此保留扭曲窗口时，关闭该开关仍可能产生位移。现有原地攻击验证同时关闭了开关并清空窗口，没有单独验证开关语义。

现有实现文档把扭曲动作的烘焙轨迹作为独立运动来源，因此这里首先需要明确配置含义，不能直接把当前行为全部视为程序错误。

**采用的方案，已实施**

将 `useRootMotion` 定义为当前动作根位移与根旋转的总开关：

| 动作配置 | 运动来源 |
|---|---|
| 没有当前动作 | 现有普通移动规划 |
| 当前动作关闭根运动 | 不提交该动作的根平移、根旋转或扭曲修正；Motor 仍处理重力和碰撞 |
| 开启根运动，没有扭曲窗口 | 动画图求出的根增量 |
| 开启根运动，有扭曲窗口 | 主动作烘焙轨迹及窗口修正，不叠加动画图根增量 |

1. 在现有位移来源分支中统一选择上述路径，并同步 `ObserveCollision` 的调用条件
2. 同步预览和说明，使保留窗口配置时也能明确切换为原地攻击
3. 保持当前三个攻击资产已开启根运动时的行为与参数，不重置用户调整过的窗口
4. 如果后续明确需要独立启用扭曲而关闭动画图根运动，则改用能表达该含义的命名和提示；此时保持现有行为，不同时实施两套相互冲突的语义

**最小验收**

- 保留有效扭曲窗口，只关闭开关：动作期间没有根运动引起的水平位移和根旋转，骨骼挥击与命中仍工作
- 开启开关后分别检查普通根运动和扭曲路径，每步仍只提交一次移动
- 在 `CombatHitProbe` 的原地攻击场景上补充保留窗口的用例，针对性复核 `CombatWarpProbe`，不扩大到后续动作系统

### OPT-04：统一预览与运行时的运动参数

**问题与位置**

[MotionWarping.Preview](../Assets/Scripts/Runtime/Combat/Animation/MotionWarping.cs) 将动作倍率固定为 1；当前三个攻击资产均为 1.4。求值中的最大移动、转向速度依赖倍率，限速生效时可能产生不同轨迹。此外，[ActionPreview](../Assets/Scripts/Editor/Combat/ActionPreview.cs) 自行维护辅助距离、角度、高度和模拟半径，容易与运行时规则分离。

**具体修改**

1. 将仅由编辑器调用的预览采样循环移入已有 `ActionPreview`；运行时保留共享运动求值、碰撞反馈和必要的计算方法
2. 使用动作的实际 `playbackRate`。按选定的固定战斗步推进局部时间，处理裁剪终点，保留当前默认 60 Hz 预览的明确含义
3. 同步调整轨迹插值和 Hitbox 前后姿态的时间换算；不能在改变样本间隔后继续假定索引恒为 `time * 60`
4. 从预览角色的 Combatant 配置读取身体半径；模拟目标半径作为明确的预览参数，与停止距离的显示使用同一值。当前玩家半径为 0.3、木桩为 0.5，不再分别写死为 0.4
5. 从 [TargetingController](../Assets/Scripts/Runtime/Combat/TargetingController.cs) 提取确实需要两处复用的范围、水平角度和高度判断，保留运行时的阵营、存活、遮挡及地面检查。预览不创建完整战斗模拟器
6. 预览显示遵守 OPT-03 的移动来源契约；Preview Scene、PlayableGraph 的创建与释放继续由预览对象负责

**最小验收**

- 在相同步长、起点、目标与半径条件下，对比 1 倍及当前 1.4 倍动作的预览与运行时求值
- 至少覆盖一个会触发移动或转向限速的配置，避免只在无限速条件下得到相同结果
- 覆盖近距、角度边界及带高度差的目标，检查两处几何筛选一致
- 复用 `CombatWarpEditorValidation` 检查反复拖动的确定性、目标拖动和资源释放；触及 Timeline 交互时才追加对应 `CombatTimelineValidation` 检查
- 该验收只比较受控的轨迹与参数，不将隔离预览视为实战碰撞或动画混合已通过验收

## 4. 可选结构与规范整理

### OPT-05：简化移动状态机

**依据**：当前 [LocomotionStateMachine](../Assets/Scripts/Runtime/Character/Movement/LocomotionStateMachine.cs) 的三个派生状态只提供不同目标速度，`Enter/Exit` 没有实际实现，没有独立状态或生命周期需要这些对象承载。

**具体修改**

1. 保留 `LocomotionStateId` 和 `CharacterMotor.CurrentState` 的对外含义
2. 在 `CharacterMotor.PlanLocomotion` 中直接根据 `allowInput`、移动意图和跑步输入选择 Idle/Walk/Run 及目标速度
3. 移除不再使用的状态基类、三个派生状态、状态机对象和 `SetTargetSpeed` 转发入口
4. 保留现有加减速、转向平滑、松键减速、动作期间输入限制、重力和单次移动提交顺序；重新启用仍从空闲运动状态开始
5. 实施前搜索全部调用；枚举如需移到独立文件，保持类型名称并正确维护 `.meta`

**最小验收**：复用 `CombatSceneProbe` 的待机、走路、跑步、松键与攻击后恢复检查；针对性确认停用后重新启用不残留速度。不为三分支本身新增一套测试框架。

### OPT-06：收窄内部操作入口

**具体修改**

1. 核对 `ActionController.Initialize/Advance/ProcessEvents/FinishAtEnd` 的调用范围；仅由运行时内部流程调用的方法改为 `internal`
2. `ProcessInitialPoints` 保留“成功起手 → 消费输入 → 零时刻通知”的顺序约束，不为了减少方法数把通知提前到输入消费之前
3. `WarpState` 中仅供求值器维护的可变字段收窄为内部访问；确有外部读取需求时才提供只读入口，不机械地给每个字段增加属性
4. 移除仅作为测试快捷入口的 `ActionController.BasicAttack`；现有验证改为从角色 `Definition.BasicAttacks` 获取对应动作
5. 核对 Runtime、Editor 与验证脚本的程序集边界，保留编辑器所需的最小公开能力，不为本轮访问范围整理新增接口、程序集或兼容转发层

**最小验收**：检查全部调用并完成 Unity 编译；只有行为被触及或编译暴露问题时才追加相关动作验证，不因访问修饰符变化重跑全部场景。

### OPT-07：局部代码规范整理

- 将本轮涉及的多成员初始化器改为每个成员单独一行
- 清理“这个函数非常关键”“处理事件”等无法解释约束的注释，改为说明执行顺序、资源归属或时间边界；修正“跟运动”等笔误
- 编辑器查找属性时，在类型可见且确实指向该成员的位置使用 `nameof`，减少字段改名后的字符串遗漏
- 对本轮触及的、有明确业务含义的重复数值使用具名常量或现有配置；不进行全项目机械替换
- 保持 4 空格缩进、命名规范、控制流大括号和 `Definition` 后缀，不修改自动生成的输入代码

**最小验收**：检查差异与 UTF-8；涉及 C# 修改时完成编译。纯格式、注释和等价 `nameof` 替换不新增运行时测试。

### OPT-08：复用已有 Phoebe 模型，移除专用烘焙 Prefab

**目的与范围**

保留当前 Playable 驱动 Animator 的根运动采样方式，改为直接复用已有 [Phoebe.fbx](../Assets/Game/Art/Characters/Phoebe/Models/Phoebe.fbx)，对比烘焙轨迹一致后移除 `Assets/Game/Combat/RootMotion/PhoebeRootModel.prefab`，减少重复模型资源的维护。此次不改变运动扭曲算法或动画导入设置。

**具体修改**

1. 读取三个 `GreatSwordAttack*Root` 资产的当前配置，使用原模型烘焙并保留轨迹作为比较基线
2. 将 `Assets/ScriptObjects/Combat/RootMotion/` 下这三个资产的 `modelPrefab` 改为已有 `Phoebe.fbx`，保留动画引用、模型尺度、采样率及资产 GUID，使用现有 `RootMotionBaker` 重新烘焙并更新依赖签名
3. 对比替换前后的样本数量、采样时间、位置与旋转；只有轨迹在浮点误差范围内一致后，才移除 `PhoebeRootModel.prefab` 及其 `.meta`。若不一致，先定位模型、Avatar 或尺度差异，保留原模型及配置，不直接接受变化
4. 清理旧 Prefab 的活动引用并同步实现文档与阅读说明；不添加自动生成专用模型的流程，不改变动作窗口和运行时位移提交顺序

**最小验收**

- 三个攻击的全部轨迹样本均通过替换前后对比，包含起点和终点，记录最大位置与旋转误差
- 根轨迹资产引用有效、依赖签名匹配，活动资产不再引用旧 Prefab 的 GUID
- 复用 `CombatWarpEditorValidation` 针对性检查烘焙签名、预览和资源释放，不运行全部战斗测试
- 验证通过后再删除旧 Prefab，并记录实际结果；本轮替换及逐样本比较结果见第 6 节

### OPT-09：按通知类型拆分 ActionPoint 数据

**问题与位置**

[ActionPoint](../Assets/Scripts/Runtime/Combat/Actions/ActionDefinition.cs) 当前同时保存 `kind`、音效、特效和空间参数，由 [ActionController.ProcessPoints](../Assets/Scripts/Runtime/Combat/Actions/ActionController.cs) 根据枚举选择使用哪些字段。连段推进点也携带音效、特效和跟随参数，音效点的旋转及跟随字段实际不起作用，数据结构允许填写不会被执行的配置。

本项通过具体类型表达各类点的有效数据，改善配置含义与可读性。采用托管引用会增加相应的序列化成本，不将字段减少直接当作已验证的内存或性能收益。

**目标数据结构**

| 类型 | 继承与字段 | 职责 |
|---|---|---|
| `ActionPoint` | 普通可序列化抽象基类，只保留 `time` | 提供统一的触发时间与集合元素类型 |
| `ComboAdvancePoint` | 继承 ActionPoint，无额外字段 | 标记连段可以推进 |
| `AudioPoint` | 继承 ActionPoint，保存 `audio/bonePath/offset` | 描述当前支持的音效播放位置 |
| `EffectPoint` | 继承 ActionPoint，保存 `effect/bonePath/offset/rotation/follow` | 描述特效播放位置、旋转与跟随方式 |

这些类型均使用 `[Serializable]`，仍内嵌在动作资产中，不为每个通知点创建独立 ScriptableObject。动作定义保留统一数组，字段改为 `[SerializeReference] public ActionPoint[] points = Array.Empty<ActionPoint>();`，以保存各元素的具体派生类型及字段。

**具体修改**

1. 按上表拆分数据，移除点数据中的 `kind`；执行和排序改用具体类型匹配。完成全部调用迁移后清理不再使用的 `ActionPointKind`，不同时维护枚举和类型两份种类信息
2. 保留 ActionController 中的统一调度与执行分支。ActionPoint 只描述配置，不引入 `IActionPoint`、抽象执行方法、通用通知上下文或额外的空间参数继承层次
3. 保留 ActionRuntime 的 `NextPoint`、`ComboAdvanced` 和效果句柄归属；不把执行游标、已触发状态或 PoolHandle 写入共享的点配置
4. 保留按时间处理统一数组的流程及单次执行只触发一次的语义，包括零时刻、完整跨过多个点和准确终点；起手仍先成功提交和消费输入，再处理零时刻通知
5. 调整 [ActionTimelineView](../Assets/Scripts/Editor/Combat/ActionTimelineView.cs) 的添加入口，直接选择并创建音效、特效或连段推进的具体实例，通过 `SerializedProperty.managedReferenceValue` 写入；选中项只显示该类型的数据
6. 复制点时创建字段值相同的独立实例，保留音效、特效资产引用，不让两个可编辑条目共享同一个点对象。类型改变属于替换数据结构，采用删除后重新添加对应类型的明确操作，不沿用旧的 `kind` 下拉切换
7. 保留当前稳定排序：时间优先，同一时刻按连段推进、音效、特效排序，同类型保持原顺序；拖动及排序后选中项仍指向同一条目
8. 同步默认 Inspector 的点添加入口，避免继承结构改造后只能看到空引用而无法创建具体类型；复用同一组简单类型选项，不增加反射注册或通用编辑器框架
9. 同步现有测试、配置生成工具和说明中的构造方式，保留无关窗口、动画、武器和移动参数

**已有资产迁移**

- 在修改序列化结构之前，读取并保存所有活动 ActionDefinition 的旧点数据，包含时间、种类、有效字段、原顺序和资源引用；覆盖三个普攻、TimelineFixture 及实际扫描发现的其他动作资产
- 按旧 `kind` 映射到对应派生类型，仅迁移该类型生效的数据；不依赖简单更换字段声明后自动转换旧的内联序列化内容
- 使用 Unity 编辑器的一次性迁移流程回填并保存资产，保留宿主资产的 GUID 和音效、特效引用；迁移完成后移除临时兼容结构，不在运行时保留新旧两套通知数据
- 保存重导入后逐项核对点数量、时间、实际类型、有效字段和顺序，再移除旧结构；迁移前快照用于核对与恢复，不运行旧配置生成器覆盖用户调整后的动作
- 本轮已完成活动资产迁移，保留宿主 GUID、点时间与顺序；迁移快照和结果见第 6 节，TODO 未修改

**最小验收**

- 在包含三类点的临时动作上确认字段正确：连段点只有时间，音效点没有特效、旋转和跟随字段，特效点没有音效字段
- 复用 `CombatTimelineValidation` 针对性检查三类点的创建、拖动、复制、删除、同时间排序、Undo/Redo 和保存重导入；修改副本不会改变原条目
- 复用 `CombatActionProbe` 或 `CombatSceneProbe` 补充必要通知检查：零时刻、跨多个点、同时间顺序、终点和取消后的停止处理均保持原行为
- 实际确认音效播放位置、特效偏移及跟随行为，取消或结束时仍归还原有规则要求持有的效果句柄；同一动作定义的不同执行不共享运行状态
- 完成 Unity 编译及迁移数据核对，只执行本项受影响路径，不以此声称完整战斗、性能或 Player 构建已通过验收

## 5. 保留的设计与暂缓事项

| 模块 | 本轮处理原则 |
|---|---|
| Combatant / CombatSimulation | 保留角色聚合和明确的中央执行顺序；只落实生命周期边界，不按字段数量或方法行数拆类 |
| ActionDefinition / ActionRuntime / ActionController | 保留共享配置与执行状态分离、执行 ID、通知游标、命中记录和效果句柄归属 |
| ComboController | 保留独立连段选择；当前以定义反查段号，要求各段定义不同。重复使用同一动作定义尚无当前需求，暂不增加段号状态或连招树 |
| WeaponController / Hitbox / HitDetection / CombatResolver | 保留挂载、空间查询、候选筛选、结算分工，不重新引入 Hurtbox 或通用查询接口 |
| Stat / CharacterStats / CharacterHealth | 保留数值、属性集合与生命状态分工，以及内部状态先更新、外部通知延后发布的机制 |
| InputBase 及其派生类 | 保留共同输入生命周期与统一启停管理，当前有真实复用 |
| PooledObject 及其派生类 / PoolHandle | 保留租借身份、版本、激活和回收的统一职责，不为统一外观强制改成接口或抽象类 |
| IDisposable | 保留已有资源释放用途 |
| 音效和粒子 | 继续使用文档允许的表现自身时间，不把暂停战斗时表现仍播放列为本轮缺陷 |
| UI、相机和编辑器工具 | 保持现有职责与布局，仅修改任务明确触及的预览参数与通知点编辑流程 |

本轮不添加没有替换实现或明确边界需求的 `IActionController`、`IMotor`、`IHitDetection` 等接口，也不因未来可能扩展而预建状态层次、通用通知系统或性能缓存。

## 6. 验证与完成记录

实施各项前读取最新源码和实际配置，避免以本计划中的快照覆盖后续调整。运行 Unity 验证时先确认连接的项目和活动场景，不在当前工作编辑器运行可能退出 Editor 或重建资产的旧批处理入口。

每项完成后记录修改范围、实际运行的验证、结果和未覆盖边界。复用已有针对性探针，只有新增行为或已发现缺口才补充必要断言；通过后不重复执行无关验证。

2026-11-17 已完成 OPT-01～OPT-09。修改前快照位于 `Backups/BeforeCurrentPlan_2026-11-17_212419/`，包含源码、测试、SO、原烘焙模型和文档；场景、UI、动画导入设置和 TODO 未修改。

| 任务 | 实际完成与验证 |
|---|---|
| OPT-01 | 签名加入采样率及稳定格式；临时副本切换到 30 Hz 后签名改变，重新烘焙后匹配，起点、内部采样与终点有效 |
| OPT-02 | 统一起手与中央调度的参与条件，停用清理和结果回调中的延后取消通过 CombatActionProbe 验证；静止目标仍可受击，手动步进仍可使用 |
| OPT-03 | 根运动总开关覆盖扭曲及根旋转；保留扭曲窗口的原地攻击、普通图根运动和扭曲路径均通过验证，每步只提交一次位移 |
| OPT-04 | 预览循环移到编辑器，使用动作倍率、所选步长及真实半径；1 倍和 1.4 倍、限速、近距及高度边界的受控轨迹对比通过；实际 IMGUI 目标拖动及资源释放通过 |
| OPT-05 | Motor 直接选择 Idle/Walk/Run，枚举文件沿用原 `.meta`；移动、跑步、松键、停用后重新启用和连段恢复通过场景验证 |
| OPT-06 | 收窄动作内部入口与 WarpState 字段；移除 BasicAttack 测试快捷属性，预览通过 ActionRuntime.Advance 使用必要的共享推进能力；Unity 编译通过 |
| OPT-07 | 本轮涉及的多成员初始化器、无效注释和类型可见的编辑器字段查找已整理；未进行全项目机械替换 |
| OPT-08 | 三个根轨迹直接引用 Phoebe.fbx；原模型与新模型的 189、172、244 个样本，共 605 个，时间一致，最大位置误差和旋转误差均为 0；一致后移除旧 Prefab |
| OPT-09 | 三类具体通知点、托管引用、类型匹配执行与共同编辑入口已完成；4 个活动动作资产迁移并重导入核对通过，无旧枚举或兼容结构；创建、拖动、稳定排序、独立复制、删除、Undo/Redo、保存重导入和运行时通知通过 |

最终验证结果：Unity 编译通过；CombatActionProbe 127 项、CombatHitProbe 51 项、CombatWarpProbe 48 项、CombatSceneProbe 21 项，共 247 项运行时断言通过；CombatTimelineValidation 25 项通过，CombatWarpEditorValidation 的采样率、预览参数和 IMGUI 验证通过。运行时通知检查覆盖三类零时刻通知、完整跨点、终点、取消、不同执行的状态隔离、音效空间位置与特效偏移/旋转/跟随及句柄归还。跟随验证实测 100 倍骨骼变换下约 0.000016 世界单位的舍入误差，使用 0.0001 容差；未改变跟随实现。

记录见 `Logs/CurrentPlanCombat*Probe.json`、`Logs/CurrentPlanRootMotionValidation.json`、`Logs/CurrentPlanActionPointsBefore.json`、`Logs/CurrentPlanActionPointMigration.json`、`Logs/CombatTimelineValidation.txt` 和 `Logs/CombatWarpEditorValidation.txt`。临时验证对象与临时测试资产已移除，退出 Play Mode，未保存主场景。未执行完整测试集、性能测试或 Player 构建，受控预览验证不代表全部实战动画混合与碰撞组合均已覆盖。

总体方案、实际实现文档与阅读说明已同步；本次文档检查内容、路径、差异和 UTF-8。`Docs/TODO.md` 始终保持只读。
