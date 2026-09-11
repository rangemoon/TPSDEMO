# TPS Shooter (Military Style)

Unity 第三人称射击项目，在原版单机玩法上接入 **Mirror Listen Server**，支持局域网 PVE（最多 4 人）。

| 项 | 版本 / 实现 |
| --- | --- |
| Unity | 2022.3.62f3c1 |
| 网络 | Mirror 96.0.1 + kcp2k（`KcpTransport`） |
| 房间发现 | `NetworkDiscovery` |
| 对象池 | `UnityEngine.Pool.ObjectPool` 封装为 `GamePool` |

本地笔记、诊断输出放在仓库根目录的 `doc/` 或 `docs/`。项目说明以本 README 为准。

---

## 1. Mirror + KCP Listen Server

联机采用 **Listen Server（房主即服务器）**：房主进程同时跑 `NetworkServer` 和 `NetworkClient`，加入端只跑客户端。没有独立 Dedicated Server。

入口是 `GameNetworkManager`（继承 `NetworkManager`）：

- 挂 `KcpTransport`：UDP + KCP 可靠层，适合局域网低延迟射击。
- 挂 `NetworkDiscovery`：大厅搜索房间，或手动填 IP / `localhost` 加入。
- `pveMaxConnections = 4`。
- 创建房间：`StartHostAtScene(buildIndex)`，把目标关卡设为 `onlineScene` 再 `StartHost()`，后加入的客户端跟进同一关。
- 加入房间：`JoinByAddress` / `JoinByUri`。
- `OnStartServer` 时 `AdvertiseServer()`，停服时停止发现。

角色生成走 Host：`OnServerAddPlayer` 用场景出生点实例化 FullPlayer 预制体，并按人数横向错开。场景里原有的占位 FullPlayer 在联机时关掉，避免和网络生成的角色叠两套。

单机不启动 Mirror：场景玩家由 `PlayerRegistry.RestoreScenePlayersForOffline()` 恢复，玩法代码走同一套逻辑，只是没有网络同步。

---

## 2. Host 权威

游戏状态以 Host 为准，客户端上报请求、接收结果。查询统一走 `GameNetwork`（`IsActive` / `IsServer` / `IsClientOnly`），业务层不直接散落 `NetworkServer` 判断。

**Host 裁定、再广播：**

| 内容 | 做法 |
| --- | --- |
| 暂停 / 恢复 / 胜负 | 仅 Host 改 `GameManager`；`GameSessionStateMessage` 同步给所有端。加入端发 `GamePauseRequestMessage` 等请求，由 Host 处理。 |
| 重开 / 回菜单 | 服务器直接忽略加入端请求，只有房主本机可以执行。 |
| 敌人 / 僵尸 AI | 只在 Host 跑。`EnemyGenerator` 纯客户端不刷怪；Host `NetworkServer.Spawn` 后，客户端关 NavMesh / CharacterController，位置交给 `NetworkTransformUnreliable`。 |
| 血量 / 死亡 | `SyncVar` 从 Host 下发；死亡走同一套本地流程。 |
| 玩家受击反馈 | Host `TargetRpc` 只打给受害者本机（方向提示、受击音）。 |

**刻意不做 Host 权威的部分：**

- 本机玩家位移：`NetworkTransformUnreliable.syncDirection = ClientToServer`。
- 本机动画：`NetworkAnimator.clientAuthority = true`。

原因是射击手感依赖本机输入；权威用在伤害、AI、对局结果上，而不是锁死每一帧移动。

联机暂停不用 `Time.timeScale = 0`，避免把网络收发和动画一起冻住。

---

## 3. 子弹 / 弹道 / 伤害

子弹不是物理刚体，而是 **按速度外推的线段检测**：

1. 记录射出时刻的位置与朝向。
2. 每帧 `nextPos = start + forward * speed * t`。
3. `Physics.Linecast` 当前点 → 下一点；命中则贴花、可选结算伤害，再回池。
4. 超时未命中同样回池。

实现见 `AbstractBullet`。`Init()` 会结算伤害；`InitVisualOnly()` 只飞、只贴花，避免远端再打一次。

### 单机

开火端从对象池取出子弹并 `Init()`，碰撞后本地直接扣血：

- 玩家弹 → `EnemyDamagable` / `ZombieDamagable` → 按部位倍率减 HP，可出血液特效。
- 敌人弹 → `PlayerBehaviour.OnBulletHit` 减 HP。
- 手雷爆炸范围内直接 `OnGrenadeHit`（近距离击杀）。

没有网络组件时，上述路径就是最终结算。

### 联机：表现与结算拆开

**玩家开枪**

1. 开枪者本机照常 `Fire()`：扣弹药、枪口特效、生成 **带伤害** 的子弹（本机命中才有效）。
2. `PlayerNetwork.NotifyShotVisual`：Host 直接 `RpcPlayShot`，加入端先 `Cmd` 再 Rpc。
3. 其他端 `PlayReplicatedShot`：枪口特效 + `InitVisualOnly` 子弹，不扣弹药、不结算伤害。

**玩家打中敌人 / 僵尸**

- Host 本机命中：直接减 HP（与单机相同）。
- 加入端命中：本地子弹只用来检测，然后 `CmdApplyBulletDamage(伤害 * 部位倍率)`（`requiresAuthority = false`，因为敌人不归客户端所有）。Host 的 `ApplyServerDamage` 才改血量，再经 `SyncVar` 同步。

**敌人开枪**

- 只有 Host 的 AI 会 `Fire()`，生成带伤害子弹，再 `ServerBroadcastShot`。
- 加入端只播复制弹道（无伤害）。因此玩家掉血只可能来自 Host 的那发弹。
- Host 命中后写玩家 HP `SyncVar`；受害者本机用 `TargetRpc` 播受击提示。远端玩家关掉 `CharacterController` 后会补胶囊碰撞体，否则敌人弹打不中。

**手雷**

爆炸检测仍在投掷端本地做。打到敌人时，加入端走 `CmdKillByGrenade`，Host 执行击杀；Host / 单机本地直接击杀。

---

## 4. 对象池

战斗里子弹、贴花、血液、受击标记创建销毁很频繁，统一走 `GamePool`：

- 按预制体 `GetInstanceID()` 分池，底层是 `UnityEngine.Pool.ObjectPool<GameObject>`。
- `Spawn` / `Despawn`：取出激活、还回隐藏；不在池里的对象回退为 `Destroy`。
- `WarmUp`：开局预创建，避免第一波战斗卡顿。`GameManager` 默认预热玩家弹 / 敌人弹各 30、受击标记 5、血液 200。
- `PooledLifetime`：延时自动还池，并在重新激活时重播粒子。
- 切场景时 `ClearAll()`：`GameManager` 单机加载、以及 `GameNetworkManager` 的 `OnServerChangeScene` / `OnClientChangeScene` 都会清，避免跨场景脏对象。

子弹结束生命不 `Destroy`，而是 `GamePool.Despawn`。联机复制弹道也走同一套池，只是初始化时关掉伤害。
