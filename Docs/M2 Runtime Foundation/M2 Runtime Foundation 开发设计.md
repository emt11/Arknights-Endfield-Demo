# M2 Runtime Foundation 战斗运行时基础开发设计 V1.0

**阶段进度 · M2.1–M2.3 已实现，M2.4 待开发**　M2 的交付目标是让一次已确认命中的攻击，能根据出招时的增益和命中时的目标状态完成结算，并让临时表现对象安全地重复使用 · [战斗系统总方案第 6.1 节](../仿终末地战斗系统设计方案.md#61-总体路线)

以祀攻击菲比为例，祀的增伤在出招后到期，这一击仍保留出招时的加成，菲比在命中前新获得的脆弱也会参与结算

## 1. 开发顺序与交付入口

| 阶段 | 完成后调用方能做什么 | 首个可观察结果 |
| --- | --- | --- |
| [M2.1 Pooling 对象池](./M2.1%20Pooling%20详细技术设计.md) | 每个场景池直接引用一个 Prefab，按固定容量租用与回收 | 两个槽位均在播放时，第三份火花请求返回 false，已完成的扣血仍然有效 |
| [M2.2 Effects 持续效果](./M2.2%20Effects%20详细技术设计.md) | 把效果施加到指定角色，独立刷新、到期、移除，按周期触发一次玩法操作 | 同模板两个增益先后到期，只撤销各自的词条，装备贡献仍然有效 |
| [M2.3 Status 战斗状态](./M2.3%20Status%20详细技术设计.md) | 查询和消费脆弱、施加元素附着、处理首个反应，以及从满槽进入和退出失衡 | 失衡积累从 80 加 35 到 100，只进入一次失衡，到期后槽归零 |
| [M2.4 Damage 伤害结算](./M2.4%20Damage%20详细技术设计.md) | 锁定攻击侧面板，按目标当前防御、抗性和状态计算一次命中，提交并取得明细 | 同一攻击快照在施加者增益到期后仍有效，资源扣血结果与 HUD 一致 |

Pooling 可独立验收

Effects 按每个实例向 Stats 写入来源，Status 使用 Effects 的计时和 Resources 的失衡槽，Damage 读取 Stats、Status 并调用 Resources

表现端收到扣血事实后才调用 `ScenePrefabPool.TryRent`，成功就播放，池满就跳过这次火花，随后由本轮播放捕获的 `PoolLease` 归还对象

## 2. 已有数据与 M2 的接入边界

| 当前工程的入口 | M2 用法 |
| --- | --- |
| `CharacterStats.Stats` 与 `IReadOnlyStatSet.GetValue` | Effects 持有目标可写的 `StatSet`，Damage 只读面板<br>菲比一级空手 ATK 704、HP 150，祀一级空手 ATK 201.6、HP 180 · [属性计算器](../../Assets/Scripts/Runtime/Stats/Calculation/StatCalculator.cs) · [菲比](../../Assets/ScriptObjects/Stats/Phoebe.asset) · [祀](../../Assets/ScriptObjects/Stats/Si.asset) |
| `StatSet.ReplaceSource / RemoveSource` | CharacterBuild 已使用 `CharacterBuild/0`，Effects 使用本目标独立的 `Effect/实例序号`，两者可同时影响同一个属性 · [现有构筑提交](../../Assets/Scripts/Runtime/CharacterBuild/CharacterBuildState.cs) · [来源提交规则](../../Assets/Scripts/Runtime/Stats/Runtime/StatSet.cs) |
| `CharacterResources.Container` | HP、终结技能量、失衡积累继续由现有容器维护<br>Damage 采用 `ApplyDamage` 返回的实际扣血与溢出量，Status 采用 `AddStagger` 的跨阈值结果 · [资源容器](../../Assets/Scripts/Runtime/Resources/Character/CharacterResourceContainer.cs) |
| Common 的通知与数值规则 | 各实例复用 `ChangeNotifier<T>`，按业务含义调用 `NumericValidation`<br>M2 操作均在 Unity 主线程执行 · [Common 设计](../Common%20公共模块详细设计.md) |
| M1.4 的战斗契约和时间接口 | Effects 接收本次推进的战斗秒数，结算接收已经确定的攻击方和目标<br>定向用例可直接传值，`CombatDemo` 由 `BattleTimeDriver` 每帧发布一次战斗步，角色效果订阅 `Battle.Delta`，攻击编号与窗口去重随调用方接入 · [M1.4 设计](../M1/M1.4%20CombatContracts%20与%20Timing%20详细技术设计.md) |

模块放入总方案规定的 Runtime 目录，各自使用默认程序集

角色逻辑在 Prefab 根取得已有组件，模型子节点留给动画，两个角色 Prefab 已接入 Effects 和 Status，沿用现有 CombatDemo 和 HUD · [已有场景用例](../../Assets/Scripts/Test/Editor/CharacterBuild/CharacterBuildSceneTests.cs)

实施时，建议在 `Assets/ScriptObjects/Effects/` 配置 3 秒增伤和周期回能模板，在 `Assets/ScriptObjects/Status/` 配置首套状态规则，在 `Assets/ScriptObjects/Damage/` 配置倍率 1.0 的练习攻击

池组件的 Inspector 填写 Prefab、预热量和总容量，首个可重复播放的火花 Prefab 放在 `Assets/Prefabs/Effects/`

## 3. 一次贯穿四个模块的验收

下列倍率、层数和持续时间是本版建议的 Demo 验收配置

实施时从原始精度比较浮点结果，表中的小数是便于复算的显示值

| 步骤 | 操作 | 预期 |
| --- | --- | --- |
| 1 · 准备 | 加载 CombatDemo，目标取 `Characters/PlayerRoot` 的默认菲比，另建默认祀实例，火花池容量 1、预热 1 | 菲比 HUD 为 `150 / 150`，物抗 0、DEF 100，祀的暴击率为 0.20 |
| 2 · 出招 | 给祀施加 3 秒、伤害加成 +25% 的效果，锁定一次倍率 1.0 的物理攻击 | 快照保存 ATK 201.6 和加成 0.25，祀的面板 ATK 仍为 201.6 |
| 3 · 命中前 | 推进 3 秒使增益到期，再给菲比施加一层脆弱 | 祀新攻击的伤害加成为 0，旧快照保留 0.25，菲比当前物理承伤倍率为 1.10 |
| 4 · 首击 | 用旧快照结算，暴击抽样取 0.80 | `201.6 × 1.25 × 0.5 × 1.10 = 138.6`，实际扣血 138.6，溢出 0，HUD 为 `11.4 / 150` |
| 5 · 反馈与再击 | 首击订阅者在目标位置租用火花 A，使其保持播放<br>用增益到期后的新快照和相同抽样提交第二击，订阅者再次尝试租用 | 第二击结算值 110.88，实际扣血 11.4，溢出 99.48，HUD 为 `0 / 150`，两条扣血事实均已收到<br>第二次租用因池满返回 false，A 仍在播放，A 播完归还后才可再次租用 |

本例由场景夹具直接调用一次命中结算入口

M6 的 Actions 和 HitDetection 将在同一入口提供真实的出招与命中，招式去重归 HitDetection

护盾的吸收顺序待护盾玩法设计时接入

## 4. 验收与交接

每个子模块详设列出独立用例

联合验收放在 `Assets/Scripts/Test/Editor/Damage/`，从当前工程的 `Window → General → Test Runner → EditMode → Assembly-CSharp-Editor.dll` 运行各详设列明的命名空间，最后只需一条进入 Play Mode 的联合用例核对第 3 节

本次交付为开发设计，表中读数供实现后验收

各模块收尾时在对应详设补充 Unity 编译结果、实际执行的用例和待验证范围，四个模块联调完成后再记录 M2 阶段结果
