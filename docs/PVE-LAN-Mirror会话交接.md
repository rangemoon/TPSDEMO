# PVE 局域网联机（Mirror）会话交接

> 日期：2026-09-06  
> 工程：`F:\UnityProject\TPSShooterMilitaryStyle`  
> Unity：2022.3.62f3c1  
> 相关会话：[PVE Mirror 联机](55213e84-d82f-4c2b-9276-605861b40dee)

下次会话请先读本文，再改代码。用户规则：回复用简体中文；Unity 改动走 `modify-project` Skill（仓库路径 `C:\Users\m1720\.cursor\skills\modify-project\SKILL.md`）。

---

## 0. 给下一会话 Agent 的启动提示

```text
先读 docs/PVE-LAN-Mirror会话交接.md。
这是 Mirror PVE 局域网联机（方案 A：Host 权威）。
玩家预制体是 FullPlayer，不是 Player；PlayerBehaviour 在子物体上。
用户要求从 Menu 启动，三个关卡都能联机。不要改回「只在 Advanced 就地 Host」。
不要重做已完成的 Registry / GameNetwork / PlayerBehaviour 单例拆解，除非测试证明坏了。
场景 FullPlayer 带 NetworkIdentity 后，Mirror 进 Play 会预禁用它：单机必须 Restore，联机必须关掉并清 sceneId。
```

---

## 1. 目标与已选方案

用户要用 **Mirror** 做 **PVE 局域网联机**。已明确选择：

- **方案 A：Listen Server + Host 权威 + 实体同步**
- 一人当 Host（服务器 + 本地客户端），其他人当 Client
- Host 权威：敌人 AI、伤害、刷怪、胜负、暂停由 Host 裁决
- 玩家位移用 `NetworkTransformUnreliable`（`ClientToServer`）
- 动画用 `NetworkAnimator`（`clientAuthority = true`）
- 发现房间用 `NetworkDiscovery`，传输用 `KcpTransport`（默认 UDP 7777）
- 最多 4 人（`pveMaxConnections`）

**当前入口（2026-09-06 14:18 起）：从 Menu 启动，三个关卡都能开房。**

- Host：Menu 左上角 `创建房间: 关卡名` → `GameNetworkManager.StartHostAtScene(buildIndex)` → 设 `onlineScene` 再 `StartHost`，客户端跟进同一关
- Client：也从 Menu 进，搜索或填 IP 加入
- 单机：仍走原来的 Play → 选武器 → 选关（`LocationChoose.OnPlay` → `Downloading`）
- 关卡内直接 Play 仍可点「创建房间 (Host)」就地开房（兼容旧测法）

未选方案（不要中途改拓扑，除非用户要求）：

- 方案 B：独立 Dedicated Server
- 方案 C：纯客户端预测 + 和解（工作量大）

---

## 2. 关键约束（必须记住）

1. **没有单独的 Player 预制体，只有 `FullPlayer`。**
   - 路径：`Assets/TPS Shooter (Military style)/Prefabs/Entities/FullPlayer.prefab`
   - `PlayerBehaviour` 在**子物体**上，不在根节点
   - 根节点带相机、HUD、`AudioListener`、`TPSCamera`、`DesktopInput`
   - `PlayerNetwork` 必须挂在 **FullPlayer 根节点**
   - `NetworkTransform.target` 指向子物体 `Player` 的 Transform
   - `NetworkAnimator.animator` 指向子物体上的 `Animator`

2. **场景里本来就摆了一个 FullPlayer（单机用）。**
   - 给预制体加 `NetworkIdentity` 后，场景实例也会带 NI，并有 `sceneId`
   - Mirror `NetworkScenePostProcess` 进 Play **会先把带 sceneId 的场景 FullPlayer SetActive(false)**
   - 单机必须 `PlayerRegistry.RestoreScenePlayersForOffline()` 再打开，否则没角色、没 AudioListener
   - Host 再 `Spawn` 一个前必须关掉场景实例，否则两个玩家、两套相机、两个 AudioListener
   - 关掉时必须把该实例的 `sceneId` 清成 0，否则随后 `SpawnObjects` 会再把它激活并同步
   - 出生点用场景 FullPlayer 的位置，再按人数 `+right * extraPlayerSpawnOffset`（默认 2.5）

3. **原单机是 `PlayerBehaviour.GetInstance()` 单例。**
   - 已改为 `PlayerRegistry.GetLocalPlayer()`
   - 远端玩家不能当本地单例
   - 本地玩家未就绪时，`GetInstance()` 会返回 `null`，旧脚本会 NRE
   - 全局输入事件（开火、切枪、暂停藏 HUD）必须只处理本机、且物体 `isActiveAndEnabled`，否则会打到已关掉的场景玩家

4. **敌人必须从注册表索敌，不能写死单例。**
   - `PlayerRegistry.GetNearestAlive(position)`
   - 所有玩家死完才失败（`HasAlivePlayer()`）

5. **Player Prefab 必须是 Project 里的预制体，不能是场景实例。**
   - 场景实例有 `sceneId != 0`，客户端 `RegisterPrefab` 会失败，exe 进房看不到 Host 角色

---

## 3. 架构摘要

```
Menu 场景
  GameNetworkManager (dontDestroyOnLoad = true)
    ├─ KcpTransport
    ├─ NetworkDiscovery
    └─ GameLanHud          // 左上角调试 UI，F1 显隐
        未连接且 buildIndex==0：按 Build Settings 列出「创建房间: 关卡名」
        已在关卡内：只显示「创建房间 (Host)」

关卡场景（可另有一份 GameNetworkManager）
  若 Menu 的 Manager 已 DDOL 过来，场景里多的那份会被 Mirror 销毁（会打一条 Warning）

FullPlayer（playerPrefab）
  根：NetworkIdentity + NetworkTransformUnreliable + NetworkAnimator + PlayerNetwork
  子：Player（PlayerBehaviour / CharacterController / Animator）
  子：相机 / HUD / AudioListener / DesktopInput
      └─ 远端实例由 PlayerNetwork.DisableRemotePresentation() 关掉

Enemy / Zombie
  Host 跑 AI，Client-only 不跑 AI
  伤害走 Command 上报 Host
  EnemyNetwork / ZombieNetwork 同步 HP 与死亡
```

查询联机状态用静态类 `GameNetwork`：

- `IsActive`：Server 或 Client 任一在跑
- `IsServer`：`NetworkServer.active`（含 Host）
- `IsClientOnly`：只有客户端

关键 API：

- `GameNetworkManager.StartHostAtScene(int buildIndex)`：从 Menu 开指定关
- `PlayerRegistry.CaptureScenePlayers()` / `DisableUnnetworkedPlayers()` / `RestoreScenePlayersForOffline()`
- `LocationChoose.OnHostPlay()`：选关 UI 创建房间（按钮需用户在 Inspector 绑 OnClick，HUD 不依赖它）

---

## 4. 核心文件

### 新增

| 文件 | 作用 |
|---|---|
| `Assets/TPS Shooter (Military style)/Scripts/Entities/Player/PlayerRegistry.cs` | 全部玩家 + 本机玩家 + 最近存活目标；捕获/关闭/单机恢复场景 FullPlayer；清 sceneId；退订 HUD；保证只有一个 AudioListener |
| `Assets/TPS Shooter (Military style)/Scripts/Network/GameNetwork.cs` | 网络状态查询 |
| `Assets/TPS Shooter (Military style)/Scripts/Network/GameNetworkMessages.cs` | 暂停/恢复/重开/回菜单/会话状态消息 |
| `Assets/TPS Shooter (Military style)/Scripts/Network/GameNetworkManager.cs` | 自定义 NetworkManager；`StartHostAtScene`；切关后重新 Capture |
| `Assets/TPS Shooter (Military style)/Scripts/Network/GameLanHud.cs` | 调试 HUD：按关卡开房 / 加入 IP / 搜索房间 |
| `Assets/TPS Shooter (Military style)/Scripts/Network/PlayerNetwork.cs` | 玩家 NetworkBehaviour，找子物体 PlayerBehaviour |
| `Assets/TPS Shooter (Military style)/Scripts/Network/EnemyNetwork.cs` | 士兵敌人网络同步与伤害 Command |
| `Assets/TPS Shooter (Military style)/Scripts/Network/ZombieNetwork.cs` | 僵尸同上 |
| `Assets/TPS Shooter (Military style)/Editor/Network/FullPlayerNetworkSetup.cs` | 编辑器菜单 |

### 必须改过的现有结构

| 文件 | 改了什么 |
|---|---|
| `PlayerBehaviour.cs` | 去掉真正单例；`GetInstance()` 转 Registry；`_localControlEnabled`；`PrepareForNetworkReplacement` 必 Unsubscribe |
| `PlayerBehaviour.Weapon.cs` | 本机且激活才开火/发 HUD 事件 |
| `PlayerWeapon.cs` | 用所属玩家 FirePoint；物体未激活不 Fire |
| `DesktopInput.cs` | 只给本机激活玩家发输入；丢枪空安全 |
| `CanvasElement.cs` | 父节点未激活时不 Show/Hide 协程 |
| `LocationChoose.cs` | 新增 `OnHostPlay()` |
| `PlayerBehaviour.HP.cs` | 血量同步到 Host SyncVar；`ApplyNetworkHp` |
| `GameManager.cs` | 全灭才失败；暂停/结束由 Host 广播；联机不改 `Time.timeScale` |
| `EnemyBehaviour.cs` / `ZombieBehaviour.cs` | 最近玩家索敌；Client-only 不跑 AI；伤害走服务器 |
| `EnemyBehaviourState.Chase/Search/Attack.cs` | `player == null` 防护 |
| `ZombieBehaviourState.Chase/Search.cs` | 同上 |
| `EnemyGenerator.cs` | 仅 Host 刷怪，`NetworkServer.Spawn` |
| `TPSCamera.cs` | 跟随 `GetLocalPlayer()`；本地玩家未就绪则跳过 |
| `Events.Player.cs` | 新增 `AnyPlayerDied` |
| `EnemyHealthBar.cs` / `ZombieHealthBar.cs` / `Radar.cs` | 延迟绑定本机玩家 |
| `HeadBob.cs` / `WeaponSway.cs` / `PlayerVehicleAbility.cs` | 用自身/父级 `PlayerBehaviour`，且仅本机执行 |

---

## 5. 编辑器菜单

- **TPS Shooter / Setup FullPlayer For LAN**  
  给 `FullPlayer.prefab` 加 NI / NetworkTransform / NetworkAnimator / PlayerNetwork，并赋给当前场景 `playerPrefab`。

- **TPS Shooter / Add LAN Manager To Open Scene**  
  在当前打开场景创建 `GameNetworkManager`（Kcp + Discovery + GameLanHud）。

- **TPS Shooter / Setup Menu For LAN**  
  打开名称含 Menu 的场景，放入/配置 `GameNetworkManager`，`dontDestroyOnLoad = true`，`offlineScene = Menu`。

- **TPS Shooter / Resave Game Scenes For LAN**  
  打开并保存 `Scenes` 下全部关卡，生成 FullPlayer 的 sceneId；若 Player Prefab 误指场景实例会改回预制体。

每次改完预制体或场景里的 FullPlayer 实例后，**必须保存场景**，否则 Mirror 报：

```text
Scene ... needs to be opened and resaved, because the scene object FullPlayer has no valid sceneId yet.
```

---

## 6. 用户必须完成的手工步骤（下一会话先确认）

这些不能只靠改脚本，用户可能还没做完：

1. 菜单 **TPS Shooter / Setup Menu For LAN**
2. 菜单 **TPS Shooter / Resave Game Scenes For LAN**
3. **File → Build Settings** 必须是：
   - **0**：Menu（名称含 Menu）
   - **1 / 2 / 3**：三个可联机关卡，全部勾选
4. 打开 **Menu** 再 Play（不要直接开关卡测「从菜单进」）
5. 测联机必须 **重新打 exe**（旧包没有这些脚本）
6. 同一台电脑：编辑器 Host + 一份 exe 加入；**不要**同一编辑器开两个 Play
7. 两台电脑：Client 填 Host 局域网 IPv4；防火墙放行 **UDP 7777**
8. 机器内存很紧（曾到分页 97% / 30GB）。测联机时关掉浏览器、Profiler、不要同时烘灯光。必要时关编辑器、用两份 exe

Inspector：

- **没有**需要新拖的引用字段。`extraPlayerSpawnOffset` 默认 2.5
- `GameNetworkManager.playerPrefab` 必须指向 Project 里的 `FullPlayer` 预制体
- 场景里的 FullPlayer **先留着**（单机 + 出生点），联机运行时会自动关掉
- `LocationChoose.OnHostPlay` 若要做成正式选关按钮，需用户在 Menu 选关 UI 上加按钮并绑 OnClick

---

## 7. 推荐测试步骤

### 单机（必须先通，再测联机）

1. 打开 Menu → Play，不要点创建房间
2. 走原菜单进任一关，应能操控、有声音
3. 或关卡内直接 Play、不点 Host，场景玩家应被 Restore，能玩

### 联机（Menu → 三关）

1. 编辑器打开 Menu → Play
2. 左上角点 `创建房间: xxx`（应列出 Build Settings 里 1 号及之后的关卡）
3. exe 也从 Menu 启动 → 搜索或 `localhost` 加入
4. 两边 HUD「场景:」必须相同
5. 对方在出生点右侧约 2.5m，转一下视角
6. 只有一套相机 / 一个 AudioListener
7. F1 可隐调试 HUD

只按 Play、不点 Host：仍是单机，不会出第二人。

---

## 8. 已修问题（按时间，不要重做）

### 2026-09-06 14:18 — 从 Menu 开三关 + 开火打到未激活玩家

- 开火/切枪/暂停会打到已关掉的场景 `Player`、武器、HUD（`StartCoroutine` / `PlayOneShot` 报错）
- 修复：本机 + `isActiveAndEnabled` 才处理；`DesktopInput` 只服务本机激活玩家；`PrepareForNetworkReplacement` 必 Unsubscribe
- 入口改为 Menu：`StartHostAtScene`、HUD 按 Build 关卡列按钮、`Setup Menu For LAN`
- 切关后 `HandleGameplaySceneLoaded` 重新 Capture：单机 Restore，联机 Disable

### 2026-09-06 14:05 — 单机所有关卡不能玩、无 AudioListener

- 给 FullPlayer 写了 sceneId 后，Mirror 进 Play 预禁用场景玩家
- 修复：`RestoreScenePlayersForOffline`；`GameNetworkManager` 上兜底 AudioListener

### 2026-09-06 14:00 — exe 进房看不到编辑器角色

- 场景 FullPlayer 被 `SpawnObjects` 再激活；客户端和 Host 不在同一关；Player Prefab 若是场景实例则客户端无法生成
- 修复：关掉前清 `sceneId`；Host 写入 `networkSceneName`；校验 prefab 不是场景实例

### 2026-09-06 13:19 — 双 AudioListener / GetInstance NRE

- 场景 FullPlayer 已有 NI 时旧逻辑没关掉它，Host 再 Spawn 一个
- 修复：Capture → Disable；出生点错开；血条/雷达/晃动/上车空安全

---

## 9. 可忽略或只需手工处理的日志

| 日志 | 处理 |
|---|---|
| `LightingData is incompatible` | 与联机无关。不要在测联机时 Generate Lighting（很吃内存） |
| `GameObjectInspector` / `TransformInspector` NRE | 选中了已销毁物体，点场景空白处 |
| `Jump -> Falling Idle` 没有 Exit Time | 资源包动画，可忽略 |
| `Multiple NetworkManagers detected` | Menu DDOL 的 Manager + 关卡里又有一份。Mirror 会毁掉多的，留下一个即可 |
| `FullPlayer has no valid sceneId` | 跑 **Resave Game Scenes For LAN**，再 Build |
| `There are no audio listeners` 刷屏 | 若单机仍刷：Restore 没跑或场景玩家没被打开。联机间隙应被兜底 Listener 盖住 |
| 分页内存 96%+ / Discarding profiler frames | 关其它软件和 Profiler；不要编辑器+exe+烘灯光同时开 |
| 两个 AudioListener | 场景 FullPlayer 没被 Capture/Disable。查是否点了 Host、Manager 是否在 |

---

## 10. 下一会话建议（未做完）

按优先级：

1. **确认用户已做第 6 节手工步骤**，再从 Menu 走通：单机一关 + Host/Client 同一关两人可见
2. 检查敌人/僵尸预制体是否挂了 `EnemyNetwork` / `ZombieNetwork` + NI + NetworkTransform + NetworkAnimator；必要时做类似 FullPlayer 的 Editor 菜单
3. 开火、伤害、击杀是否 Host 权威且两端一致
4. 暂停 / 全灭失败 / 重开 / 回菜单是否两端同步（断开应回 Menu）
5. 把 `GameLanHud` 换成正式菜单（`LocationChoose.OnHostPlay` 已留接口，用户之后才需要）
6. 载具、手雷、拾取武器等仍可能 `GetInstance()`，联机下按测试暴露再改

仍可能 `GetInstance()` 为空的位置（上次搜过，部分已加防护）：

- `TPSCamera.StateVehicle.cs` / `TPSCamera.StatePlayerAiming.cs` 部分路径
- `PlayerGrenadeProjectile.cs`
- `PlayerCrosshair.cs` / `PlayerHitMarker.cs` / `PlayerHP.cs` / `WeaponChoose.cs`
- `GrenadeOutOfStockUI.cs`
- 若干 MobileInput

---

## 11. 代码约定（本工程联机部分）

- 命名空间：`TPSShooter`
- 新增方法要有中文 XML 文档注释
- 离线模式必须还能玩：`!GameNetwork.IsActive` 时场景 FullPlayer 自己 `SetLocal` + `EnableLocalControl`
- 联机不要用 `Time.timeScale = 0` 做暂停
- 不要把 `PlayerNetwork` 挂到子物体 Player 上，挂根节点
- 远端 FullPlayer 必须关相机、AudioListener、Canvas、DesktopInput、TPSCamera
- 不要重做 Registry / 单例拆解，除非测试证明坏了
- 不要改回「只在 Advanced 就地 Host、不走 Menu」
