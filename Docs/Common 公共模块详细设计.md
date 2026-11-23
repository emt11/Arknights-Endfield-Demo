# Common 公共模块详细设计

**已接入 Stats 与 Resources · 25 项相关 EditMode 测试通过**

Common 收纳已有业务模块中语义一致、可以共用一份实现的逻辑，首批从 Stats 和 Resources 提取数值校验以及事件的订阅、退订和发布 · [战斗系统总方案](./仿终末地战斗系统设计方案.md)

## 1. 模块位置与依赖

实现位于 `Assets/Scripts/Runtime/Common/`，命名空间为 `Endfield.Common`。Common、Stats 和 Resources 一同编入 Unity 默认运行时程序集 `Assembly-CSharp.dll`

Common 的接口使用数值、泛型和委托，业务类型由调用方提供。Stats 和 Resources 均调用 Common，角色资源通过 Stats 的只读接口取得 MaxHP · [StatSet](../Assets/Scripts/Runtime/Stats/Runtime/StatSet.cs) · [角色资源容器](../Assets/Scripts/Runtime/Resources/Character/CharacterResourceContainer.cs)

## 2. 数值校验

调用方提供闭区间上下限。`NumericValidation.IsFiniteInRange` 返回数值是否有限且位于区间内，`RequireFiniteInRange` 复用同一判断，对非法参数抛出带参数名和原值的 `ArgumentOutOfRangeException` · [数值校验](../Assets/Scripts/Runtime/Common/NumericValidation.cs)

| 当前调用方 | 可共用的判断 | 业务模块保留的行为 |
| --- | --- | --- |
| Stats 的 `StatModifier.Validate` | 乘法倍率须有限且大于等于 0 | Stats 根据运算类型选择区间，固定值修正仍允许负数，非法词条仍走原有的失败路径 |
| Resources 的角色容器与队伍 SP 池 | 直接调用 `RequireFiniteInRange` 校验非负点数，进场值还须小于等于配置上限 | Resources 指定上限并传入原参数名，在写入前拒绝非法输入 |

只需判断有限性的地方直接使用 `double.IsFinite`

资源恢复和失衡累积直接使用 `Math.Clamp` 将相加后的数值限制在 0 与资源上限之间，Stats 的最终值则按各属性定义的上下限截取 · [角色资源容器](../Assets/Scripts/Runtime/Resources/Character/CharacterResourceContainer.cs) · [队伍 SP 池](../Assets/Scripts/Runtime/Resources/Team/SkillPointPool.cs) · [属性计算](../Assets/Scripts/Runtime/Stats/Calculation/StatCalculator.cs)

## 3. 事件订阅、退订与发布

每个 `StatSet`、角色资源容器和队伍 SP 池分别持有自己的 `ChangeNotifier<T>`，其中 `T` 表示一次提交携带的数据，可传入属性变化列表，也可传入一对资源快照

| 公开成员 | 行为 |
| --- | --- |
| `Subscribe(Action<T> listener) → IDisposable` | 拒绝空回调，登记监听器并返回本次订阅的句柄，首次 `Dispose()` 解除该订阅，重复调用安全 |
| `Subscribe(listener, invoke) → IDisposable` | 当已有回调接收多个参数时，由调用方说明如何传参，通知器在登记时取得各个回调，发布时逐个执行并隔离异常 |
| `Publish(T change)` | 在调用线程同步、按登记顺序通知本轮监听器，单个回调抛异常时记录异常，继续通知其余监听器 |
| `HasSubscribers` | 供调用方在准备通知数据前查询，例如 Stats 可在无人订阅时跳过属性差异收集 |

**Stats** 在数值提交成功后收集实际变化，整批发布 `IReadOnlyList<StatChange>`，通知期间对同一面板的写入限制仍由 `StatSet` 管理 · [现有 StatSet 通知实现](../Assets/Scripts/Runtime/Stats/Runtime/StatSet.cs)

**Resources** 在值确实改变后发布旧、新快照组成的元组，角色容器和队伍池在订阅入口将元组展开为两个回调参数，HUD 沿用原签名 · [公共通知器](../Assets/Scripts/Runtime/Common/ChangeNotifier.cs) · [Resources 只读接口](../Assets/Scripts/Runtime/Resources/Shared/ResourceReadModels.cs)

## 4. 迁移与验收

Stats、角色资源和队伍 SP 均通过原有公开接口订阅，以下结果用于复验

- **数值行为**　负的固定值词条仍然有效，负倍率仍被拒绝<br>资源进场值超过容量、扣血量为负数或非有限值时，仍在提交前抛出 `ArgumentOutOfRangeException`
- **通知行为**　一次装备变更引发多项属性变化时，Stats 仍只发布一批<br>MaxHP 下调同时裁减当前 HP 时，Resources 仍只发布一对快照，回调内可读到已提交的结果
- **订阅生命周期**　退订后不再收到后续通知，重复退订安全，一个监听器抛错时，同一次发布的后续监听器仍能收到数据

2026-11-23 迁移到默认程序集后，在项目副本中使用 Unity 6000.3.28f1 运行 EditMode 测试。Test Runner 从 `Assembly-CSharp-Editor.dll` 发现并执行了以下 25 项，全部通过

| 验证范围 | 通过数 | 本次迁移关注的行为 |
| --- | ---: | --- |
| [Stats 数值测试](../Assets/Scripts/Test/Editor/Stats/StatSystemTests.cs) | 16 | 词条校验、按批通知、回调期间拒绝写入同一个 `StatSet` |
| [Stats 配置与战斗面板集成测试](../Assets/Scripts/Test/Editor/Stats/CharacterStatsIntegrationTests.cs) | 2 | 角色配置使用对应的 Inspector，CombatDemo 面板随角色最终属性刷新 |
| [Resources 数值测试](../Assets/Scripts/Test/Editor/Resources/ResourceTests.cs) | 5 | 数值范围、快照发布顺序、独立退订及监听器异常隔离 |
| [Resources 场景测试](../Assets/Scripts/Test/Editor/Resources/ResourceSceneTests.cs) | 2 | 角色资源、队伍 SP 和血量 HUD 的场景联动 |
