# M2 Runtime Foundation 战斗运行时基础开发设计 V1.2

**阶段状态 · 已收尾 · 2026-11-23**　M2.1 Pooling、M2.2 Effects 和 M2.3 Status 均已实现并通过 Unity 编辑器定向验收。下一阶段为 M3 Actor Foundation，伤害结算随 M6 战斗执行实施 · [战斗系统总方案第 6.1 节](../仿终末地战斗系统设计方案.md#61-总体路线)

## 1. 交付内容与验收记录

| 模块与验收记录 | 已交付能力 | 已观察到的结果 |
| --- | --- | --- |
| [M2.1 Pooling 对象池](./M2.1%20Pooling%20详细技术设计.md#阶段验收记录) | 每个场景池管理一种 Prefab，支持预热、固定容量、租约归还与清场 | 8 项用例通过。两个槽位占满时第三次租用返回 false，归还后可复用同一实例，迟到的旧租约无法归还新一轮火花 |
| [M2.2 Effects 持续效果](./M2.2%20Effects%20详细技术设计.md#实际验收记录) | 按实例独立施加、刷新、移除效果，按战斗时间执行周期动作 | 3 项控制器用例通过。菲比装备木剑并叠加两份增益，刷新第二份后，ATK 依次为 710.24 → 750.24 → 730.24 → 710.24 |
| [M2.3 Status 战斗状态](./M2.3%20Status%20详细技术设计.md#实际验收记录) | 管理脆弱层数、元素附着、冻结与失衡，复用 Effects 计时和 Resources 失衡槽 | 3 项控制器用例通过。热遇冰后冻结生效；失衡从 80 加 35 达到 100，只进入一次，到期清槽时仍有效的冻结继续生效 |

## 2. 已有数据与 M2 的接入边界

| 当前工程的入口 | M2 的使用方式与交接数据 |
| --- | --- |
| `CharacterStats.Stats` 与 `IReadOnlyStatSet.GetValue` | Effects 持有目标可写的 `StatSet`，Damage 只读面板<br>菲比一级空手 ATK 704、HP 150，祀一级空手 ATK 201.6、HP 180 · [属性计算器](../../Assets/Scripts/Runtime/Stats/Calculation/StatCalculator.cs) · [菲比](../../Assets/ScriptObjects/Stats/Phoebe.asset) · [祀](../../Assets/ScriptObjects/Stats/Si.asset) |
| `StatSet.ReplaceSource / RemoveSource` | CharacterBuild 已使用 `CharacterBuild/0`，Effects 使用本目标独立的 `Effect/实例序号`，两者可同时影响同一个属性 · [现有构筑提交](../../Assets/Scripts/Runtime/CharacterBuild/CharacterBuildState.cs) · [来源提交规则](../../Assets/Scripts/Runtime/Stats/Runtime/StatSet.cs) |
| `CharacterResources.Container` | HP、终结技能量、失衡积累继续由现有容器维护<br>Damage 采用 `ApplyDamage` 返回的实际扣血与溢出量，Status 采用 `AddStagger` 的跨阈值结果 · [资源容器](../../Assets/Scripts/Runtime/Resources/Character/CharacterResourceContainer.cs) |
| Common 的通知与数值规则 | 各实例复用 `ChangeNotifier<T>`，按业务含义调用 `NumericValidation`<br>M2 操作均在 Unity 主线程执行 · [Common 设计](../Common%20公共模块详细设计.md) |
| M1.4 的战斗契约和时间接口 | `CombatDemo` 由 `BattleTimeDriver` 每帧发布战斗步，Effects 订阅 `Battle.Delta`。M6 按攻击、窗口和目标去重，再使用确认后的命中进行结算 · [M1.4 设计](../M1%20Core%20Data%20Foundation/M1.4%20CombatContracts%20与%20Timing%20详细技术设计.md) |

模块放入总方案规定的 Runtime 目录，各自使用默认程序集

菲比与祀的 Prefab 在逻辑根挂载 `CharacterEffects` 和 `CharacterStatus`。`CombatDemo` 的 `Combat Session` 持有 `BattleTimeDriver`，角色订阅本场的战斗步 · [菲比 Prefab](../../Assets/Prefabs/Characters/Phoebe.prefab) · [祀 Prefab](../../Assets/Prefabs/Characters/Si.prefab)

3 秒增伤和周期回能模板位于 `Assets/ScriptObjects/Effects/`，状态规则位于 `Assets/ScriptObjects/Status/`

场景根节点 `HitSparkPool` 引用 `HitSpark.prefab`，预热 1 份，容量 2 份 · [CombatDemo 场景](../../Assets/Scenes/CombatDemo.unity) · [火花 Prefab](../../Assets/Prefabs/Effects/HitSpark.prefab)

## 3. 验收范围与后续交接

本次收尾沿用 Unity 6000.3.28f1 的既有验收记录。除第 1 节的模块用例外，`CombatDemo` 的 Play Mode 联调用例验证了战斗暂停、半速和角色切换：暂停时祀的终结技能量保持 30，恢复后依次达到 35、40、45，菲比的冻结按战斗时间解除 · [场景联调用例](../../Assets/Scripts/Test/Editor/Timing/BattleClockSceneTests.cs)

M3 从菲比和祀的角色逻辑根接入统一战斗单位，沿用现有的属性面板、资源容器、效果和状态控制器。独立 Windows Player 构建验证排在 M10 集成与发布阶段

M6 输出已去重的命中上下文，在首条连招的每个 Action 攻击段中内嵌 `DamageSpec`，直接填写本段倍率、伤害类型、失衡和附着。出招快照、目标承伤、HUD 与火花反馈的联合验收随 M6 执行。详见[伤害结算设计第 5 节](./M2.4%20Damage%20详细技术设计.md#5-m6-连招验收)
