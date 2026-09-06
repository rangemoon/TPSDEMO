# 游戏核心系统文档

> 本文档记录当前分支 (Lynx) 的核心游戏系统实现细节，便于版本回退后快速恢复或参考。
> 生成日期：2026-08-12

---

## 目录

1. [手榴弹系统](#1-手榴弹系统)
2. [武器选择页面](#2-武器选择页面)
3. [地图选择页面](#3-地图选择页面)
4. [敌人生成系统](#4-敌人生成系统)
5. [胜利判定系统](#5-胜利判定系统)

---

## 1. 手榴弹系统

### 1.1 核心文件清单

| 文件 | 作用 |
|------|------|
| `Scripts/Entities/Grenade/AbstractGrenade.cs` | 手榴弹基类：投掷、定时爆炸、范围伤害 |
| `Scripts/Entities/Grenade/PlayerGrenade.cs` | 玩家手榴弹：爆炸伤害判定 + UI倒计时显示 |
| `Scripts/Entities/Player/Behaviour/PlayerBehaviour.Grenade.cs` | 玩家投掷手榴弹的行为逻辑（partial class） |
| `Scripts/Entities/Player/Behaviour/Settings/PlayerGrenadeSettings.cs` | 手榴弹配置：预制体、挂载点、弹药数 |
| `Scripts/Entities/Player/Abilitites/PlayerGrenadeProjectile.cs` | 手榴弹投掷弹道预测线（LineRenderer） |
| `Scripts/UI/Player/Misc/GrenadeOutOfStockUI.cs` | 手榴弹耗尽时弹出的UI（看广告补充弹药） |
| `Prefabs/Entities/Grenade.prefab` | 手榴弹预制体 |
| `Prefabs/ThirdParties/Particles/Prefabs/GrenadeExplosionEffect.prefab` | 爆炸粒子特效 |

### 1.2 投掷流程

```
玩家按下投掷按钮
  → Events.GrenadeStartThrowRequest
    → PlayerBehaviour.OnGrenadeStartThrowRequest()
      → 校验（存活/非换弹/非载具/弹药数/非正在投掷）
      → GrenadeStartThrow():
          1. 取消瞄准
          2. 播放手榴弹准备动画 (standGrenadeTrigger)
          3. IsThrowingGrenade = true
          4. GrenadeCount--
          5. Events.PlayerGrenadeCountChanged
          6. Instantiate GrenadePrefab 挂到手臂上
          7. AbstractGrenade.OnReady() → 开始计时 + 设置kinematic
          8. Events.PlayerStartGrenadeThrow (触发弹道预测线)

      → 玩家松开按钮
        → Events.GrenadeFinishThrowRequest
          → GrenadeFinishThrow(): 播放投掷动画 (standThrowGrenadeTrigger)

        → Animation Event: GrenadeStartFlying()
          → AbstractGrenade.Throw(velocity)
          → velocity = Camera.forward * 10 + Vector3(0, 7, 0)
          → Events.PlayerFinishGreandeThrow (关闭弹道预测线)

        → Animation Event: GrenadeThrown()
          → IsThrowingGrenade = false
```

### 1.3 爆炸逻辑

`AbstractGrenade.Explode()`:
1. 播放爆炸粒子特效（解绑后播放，销毁粒子物体）
2. 播放爆炸音效
3. `Physics.OverlapSphere(transform.position, explosionRadius)` 获取范围内碰撞体
4. `CollidersImpact()`：对附近 Rigidbody 施加爆炸力
5. `AbstractImpact()`：**由子类实现伤害判定**

`PlayerGrenade.AbstractImpact()` 对范围内对象判定：
- `EnemyBehaviour` → `enemy.OnGrenadeHit(this)` → **直接死亡**
- `ZombieBehaviour` → `zombie.OnGrenadeHit(this)` → **直接死亡**
- `PlayerBehaviour` → `player.OnGrenadeHit(this)` → 自伤
- `VehicleHealthBar` → `vehicle.Explode()`

### 1.4 弹药系统

- `PlayerGrenadeSettings.maxGrenadeCount = 3`：初始/最大弹药数
- `PlayerGrenadeSettings.grenadesPerVideo = 1`：看一次广告补充的弹药数
- 初始化：`InitializeGrenadeCount()` → `GrenadeCount = maxGrenadeCount`
- 弹尽时：`Events.PlayerGrenadeDepleted` → 弹出 `GrenadeOutOfStockUI`
  - 可看广告 (`ShowTrickBoxOrVideo`) 补充弹药
  - 关闭按钮直接关闭
- 无CD限制，只要弹药充足即可连续投掷

### 1.5 关键参数（AbstractGrenade）

| 参数 | 默认值 | 说明 |
|------|--------|------|
| `delay` | 5f | 爆炸延迟（秒） |
| `explosionRadius` | 5f | 爆炸半径 |
| `explosionForce` | 1000f | 对刚体的爆炸力 |

---

## 2. 武器选择页面

### 2.1 两层武器选择系统

本项目有两套武器选择UI：

1. **菜单武器选择**（主菜单 → 选武器）
   - 脚本：`Scripts/UI/Menu/WeaponChoose.cs` (namespace: `TPSShooter.UI.Menu`)

2. **游戏中武器切换**（游戏内 HUD 切换）
   - 脚本：`Scripts/UI/Player/WeaponChoose/WeaponChoose.cs` (namespace: `TPSShooter.UI`)

### 2.2 菜单武器选择（主菜单）

**核心文件**：`Scripts/UI/Menu/WeaponChoose.cs`

**数据结构**：
```csharp
public GameObject[] weapons; // 武器GameObject数组，在Inspector中拖入
```

**武器标识/汉化**：
- 武器名称直接使用 GameObject 的 **`tag`** 属性作为标识
- `infoText.text = weaponTag` → 直接显示 tag 作为武器名称
- **汉化方式：将武器 GameObject 的 tag 设为中文即可**，如 `"自动步枪"`、等
- tag 同时也是解锁系统的 key

**解锁逻辑**：
- 使用 `UnlockManager.IsWeaponUnlocked(weaponTag)` 检查
- 默认解锁武器：`"自动步枪"`（定义在 `UnlockManager.DefaultUnlockedWeapons`）
- 未解锁武器显示 `lockOverlay`，隐藏 `playButton`
- 解锁方式：看激励视频广告 `xh.api.Ad.ShowTrickBoxOrVideo("common_box", ...)`
- 解锁后调用 `UnlockManager.UnlockWeapon(weaponTag)` 写入 PlayerPrefs

**武器切换**：
- `OnNext()` / `OnPrevious()`：循环切换 `weaponIndex`
- 当前武器激活（`SetActive(true)`），其他隐藏
- 选定后 `SaveLoad.WeaponTag = weaponTag` 保存选择
- 点击 Play 进入地图选择：`Events.RequestMenuLocation.Call()`

**页面流程**：
```
主菜单 (Menu.cs)
  → OnPlay() → Events.RequestMenuWeapon
    → WeaponChoose (菜单版) 显示
      → 选择武器 → OnPlay() → Events.RequestMenuLocation
        → LocationChoose 显示
```

### 2.3 游戏内武器切换（HUD）
如果直接在游戏内 setactive false 会不会对游戏的别的流程有影响 如果没影响 直接不显示

### 2.4 UnlockManager（解锁管理）

**文件**：`Scripts/Utils/UnlockManager.cs`

```csharp
// PlayerPrefs key 格式
WeaponUnlock_自动步枪    → int 1(已解锁) / 0(未解锁)
LocationUnlock_2        → int 1(已解锁) / 0(未解锁)

// 默认解锁
DefaultUnlockedWeapons = ["自动步枪"]
DefaultUnlockedLocations = ["2"]
```

### 2.5 SaveLoad（存档）

**文件**：`Scripts/Utils/SaveLoad.cs`

```csharp
PlayerPrefs.SetString("Weapon", weaponTag)      // 当前选中武器tag
PlayerPrefs.SetFloat("TouchS", sensitivity)     // 触摸灵敏度
PlayerPrefs.SetFloat("TouchAS", aimingSens)      // 瞄准灵敏度
PlayerPrefs.SetInt("AutoShot", 0/1)             // 是否自动射击
```

---

## 3. 地图选择页面

### 3.1 核心文件

`Scripts/UI/Menu/LocationChoose.cs` (namespace: `TPSShooter.UI.Menu`)

### 3.2 数据结构

```csharp
[System.Serializable]
public class LocationInfo
{
    public Sprite image;       // 地图缩略图
    public int sceneIndex;     // 对应的场景 Build Index
    public string info;        // 地图描述文本（支持汉化，直接填中文）
}

public LocationInfo[] locations; // 在 Inspector 中配置
```

### 3.3 解锁逻辑

与武器选择完全相同机制：
- `UnlockManager.IsLocationUnlocked(sceneIndex)` 检查
- 默认解锁场景：`"2"`（即 sceneIndex=2）
- 未解锁显示 `lockOverlay`，隐藏 `playButton`
- 解锁方式：看激励视频广告，与武器相同

### 3.4 地图汉化

- `locationInfoText.text = locations[locationIndex].info`
- **直接在 Inspector 的 `LocationInfo.info` 字段填写中文名称即可**

### 3.5 页面流程

```
LocationChoose
  → OnPlay():
      1. 检查解锁
      2. Events.RequestMenuDownloading.Call(sceneIndex)
      3. Hide()
      4. xh.api.Ad.ShowInsert("common")

  → Downloading.cs:
      → OnRequestMenuDownloading(int sceneIndex)
        → Show 下载UI（模拟进度条动画）
        → StartCoroutine(LoadScene(sceneIndex))
          → SceneManager.LoadSceneAsync(sceneIndex)

  → OnBack():
      → Events.RequestMenuWeapon
      → 返回武器选择
```

---

## 4. 敌人生成系统

### 4.1 敌人类型

本项目有 **两种敌人类型**，僵尸 **算入敌人总数**（共享 `_aliveEnemies` 计数器）：

| 类型 | 脚本路径 | 行为特点 |
|------|----------|----------|
| **士兵敌人** (EnemySoldier) | `Scripts/Entities/EnemySoldier/` | 持枪远程攻击、有IK瞄准、可Strafe攻击 |
| **僵尸敌人** (ZombieBehaviour) | `Scripts/Entities/EnemyZombie/` | 近战攻击、HP=100、移动速度较慢 |

### 4.2 敌人生成器 (EnemyGenerator)

**文件**：`Scripts/Entities/EnemySoldier/EnemyGenerator.cs`

```csharp
[System.Serializable]
public class GenerationWave
{
    public GameObject enemyPrefab;      // 该波次的敌人预制体
    public Transform[] spawnPoints;     // 该波次的出生点
}

public GenerationWave[] generationWaves; // 波次数组，Inspector配置
```

**生成逻辑**：
1. `Start()` 时调用 `SpawnEnemies()`，生成第一波
2. 每波遍历 `generationWaves[_currentWaveIndex].spawnPoints`，在每个出生点 `Instantiate` 一个敌人
3. 每生成一个敌人 `_aliveEnemies++`
4. 每一波的敌人可以是士兵或僵尸（由 `enemyPrefab` 决定）

**计数与推进**：
- 监听 `Events.EnemyKilled` 和 `Events.ZobmieKilled`（注意：僵尸事件的key拼写为 `ZobmieKilled`）
- 任一敌人死亡：`_aliveEnemies--`
- 当 `_aliveEnemies == 0`：
  - `_currentWaveIndex++`
  - 若还有下一波：`SpawnEnemies()` 生成下一波
  - 若无下一波：**触发胜利** `Events.GameWon.Call()`

### 4.3 士兵 AI (EnemyBehaviour)

**文件**：`Scripts/Entities/EnemySoldier/EnemyBehaviour.cs`

**状态机**：
```
IdleState（待机）
  ↓ 有巡逻点 → PatrolState（巡逻）
  ↓ 无巡逻点 → IdleState

  ↓ 检测到玩家噪音（无视野） → SearchState（搜索）

  ↓ 进入视野+射线检测命中 → ChaseState（追击）

  ↓ 距离 < InnerAttackRadius → AttackState（攻击）/ StrafeAttackState
  ↓ HP <= 0 → DeathState（死亡）

  ↓ 玩家死亡 → PlayerDiedState
```

**检测条件**：
- `IsPlayerNoticedByRaycast()`：Raycast 视线检测（含缓存优化）
- `IsPlayerInFieldOfView()`：FOV 角度检测（默认80°）
- `IsPlayerNoiseDetected()`：玩家噪音检测
- `InnerAttackRadius = 35`（开始攻击距离）
- `OuterAttackRadius = 40`（停止攻击距离）
- `MaxPlayerDetectionRadius = 150`

**伤害**：
- `OnBulletHit(PlayerBullet, damageMultiplier)`：`hp -= bullet.damage * damageMultiplier`
- `OnGrenadeHit(AbstractGrenade)`：**直接死亡**

**死亡时**：
```
DeathState.OnEnter():
  → 禁用 CharacterController + NavMeshAgent
  → 若 IsRagdolled：启用布娃娃物理
  → 否则：播放死亡动画
  → 卸载 Items（武器等掉落物）
  → host.onDied?.Invoke()
  → Events.EnemyKilled.Call(host)  ← 通知 EnemyGenerator 计数
  → Destroy(gameObject, EnemyDieTime)
```

### 4.4 僵尸 AI (ZombieBehaviour)

**文件**：`Scripts/Entities/EnemyZombie/ZombieBehaviour.cs`

**与士兵类似但有以下区别**：
- HP = 100（士兵也是100）
- 近战攻击（Animation Event `OnAttack()` 使用 OverlapSphere）
- `AttackSphereRadius = 0.3f`，`AttackSphereOffset = (0, 1f, 1f)`
- `MaxWalkSpeed = 2`，`MaxRunSpeed = 3`（比士兵慢）
- `InnerAttackRadius = 1`，`OuterAttackRadius = 2`（必须非常近才攻击）
- `playerDetectionRadius = 75`（比士兵远）
- `fov = 60`（比士兵窄）

**僵尸攻击**：
```csharp
void OnAttack() // Animation Event
{
    // 检测攻击范围内玩家层碰撞体
    Physics.OverlapSphere(transform.position + rotation * AttackSphereOffset, AttackSphereRadius, LayerMask.Player)
    → player.OnZombieHit(this) // damage = 5
}
```

**僵尸死亡**：
```
DeathState.OnEnter():
  → 禁用 CharacterController + NavMeshAgent
  → 播放死亡动画
  → host.onDied?.Invoke()
  → Events.ZobmieKilled.Call(host)  ← 通知 EnemyGenerator 计数
  → Destroy(gameObject, dieTime)
```

### 4.5 僵尸是否算入敌人

**是的，僵尸完全算入敌人系统**：
- `EnemyGenerator` 同时监听 `Events.EnemyKilled` 和 `Events.ZobmieKilled`
- 两者死亡都会减少 `_aliveEnemies` 计数
- 波次配置中的 `enemyPrefab` 可以是士兵或僵尸预制体
- 混合波次：同一波可以包含士兵和僵尸（需为每种类型配置不同的 Wave）

---

## 5. 胜利判定系统

### 5.1 核心文件

| 文件 | 作用 |
|------|------|
| `Scripts/Entities/GameManager/GameManager.cs` | 游戏状态管理：暂停/恢复/胜利/失败 |
| `Scripts/Entities/EnemySoldier/EnemyGenerator.cs` | 敌人计数 → 触发胜利 |
| `Scripts/Events/Events.Game.cs` | 游戏相关事件定义 |

### 5.2 胜利条件

**所有波次的敌人全部被消灭 = 胜利**

```
EnemyGenerator:
  OnEnemyKilled / OnZobmieKilled:
    → _aliveEnemies--
    → if (_aliveEnemies == 0)
      → _currentWaveIndex++
      → if (_currentWaveIndex >= generationWaves.Length)
        → Events.GameWon.Call()  // ← 所有波次清空，游戏胜利！
      → else
        → SpawnEnemies()  // 生成下一波
```

### 5.3 失败条件

**玩家死亡 = 失败**

```
PlayerBehaviour 死亡时:
  → Events.PlayerDied.Call()

GameManager:
  OnPlayerDied():
    → if (!IsGameFinished) FinishGame(false)  // isWin = false
```

### 5.4 游戏结算流程

```
GameManager.FinishGame(bool isWin):
  1. IsGameFinished = true
  2. Events.GameFinished.Call()           // 通知所有UI隐藏/停止
  3. Events.GameFinishedResult.Call(isWin) // 传递胜负结果
  4. xh.api.Ad.ShowInsert("settle")       // 结算页弹出插屏广告
```

### 5.5 结算后操作

```
重玩 (Events.GameReplayRequested):
  → GameManager.Replay()
    → Time.timeScale = 1
    → Events.GameReplay
    → SceneManager.LoadSceneAsync(当前场景) // 重新加载当前关卡

返回主页 (Events.GameLoadHomeSceneRequested):
  → GameManager.LoadHomeScene()
    → Time.timeScale = 1
    → Events.GameLoadHomeScene
    → SceneManager.LoadSceneAsync(0) // 加载场景0（主菜单）
```

### 5.6 游戏事件定义 (Events.Game.cs)

```csharp
public static Event GamePaused;
public static Event GameResumed;
public static Event GameFinished;           // 游戏结束（不论胜负）
public static Event GameWon;                // 胜利
public static Event<bool> GameFinishedResult; // 结算结果 (true=胜, false=负)
public static Event GameReplay;
public static Event GameLoadHomeScene;
public static Event SceneUnload;
```

### 5.7 游戏暂停/恢复

```
Events.GamePauseRequested → PauseGame()
  → Time.timeScale = 0
  → IsGamePaused = true
  → Events.GamePaused.Call()

Events.GameResumeRequested → ResumeGame()
  → Time.timeScale = 1
  → IsGamePaused = false
  → Events.GameResumed.Call()
```

---

## 附录：完整页面流程图

```
启动游戏
  → 场景0 (主菜单)
    → Menu.cs (主菜单UI)
      → OnPlay() → WeaponChoose.cs (武器选择)
        → 选武器(循环切换) → 解锁/锁定
        → OnPlay() → LocationChoose.cs (地图选择)
          → 选地图(循环切换) → 解锁/锁定
          → OnPlay() → Downloading.cs (加载UI)
            → LoadScene(sceneIndex)
              → 进入游戏场景
                → EnemyGenerator 控制波次
                → 消灭所有敌人 → Events.GameWon → 结算
                → 玩家死亡 → Events.PlayerDied → 结算
                  → 重玩 / 返回主页
```
