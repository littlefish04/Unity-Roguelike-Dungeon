# Unity 项目 — 程序化地牢生成 & 2D 角色控制 & 小地图系统

> 一个用于熟悉 Unity 2D 技术栈的练习项目，涵盖程序化生成、物理移动、UI 系统、设计模式等主题。

---

## 关于本项目

这是一个Unity 练习项目。项目围绕一个 2D 俯视角地牢探险的游戏框架，实现了地牢程序化生成、玩家角色控制、小地图UI三个功能模块。每一个模块都对应一组明确的技术学习目标。

**已完成的 3 个模块对应 3 轮技术探索：**

| 轮次 | 模块 | 主要学习目标 |
|------|------|-------------|
| 1 | 地牢程序化生成 | ScriptableObject、Tilemap、图算法、Editor 扩展 |
| 2 | 玩家角色控制 | New Input System、Rigidbody2D、对象池、协程 |
| 3 | 小地图 UI | 程序化纹理、Canvas 布局 |

---

## 各模块详解

### 模块一：地牢程序化生成

**学习目标：** ScriptableObject 数据驱动、Tilemap 批量渲染、图论算法、自定义 Editor 工具

- 基于 **ScriptableObject** 的配置系统，房间模板和地牢参数可在 Inspector 中可视化编辑
- 自定义 **Editor 脚本**，在 Inspector 中点击网格即可绘制房间形状模板
- 使用 **并查集（Union-Find）+ Kruskal** 构建最小生成树，确保所有房间连通
- 在 MST 基础上添加额外边产生环路，增加地图复杂度
- 实现 **4-bit 位掩码**算法，根据 4 方向邻接地板状态自动匹配墙壁 Tile（覆盖全部 16 种组合）
- 生成后自动检测 T 字口、薄墙、孤立柱等不良结构，换种子重试

**涉及文件：** `DungeonGenerator.cs` | `CorridorGenerator.cs` | `RoomTemplate.cs` | `DungeonData.cs` | `RoomTemplateEditor.cs`

### 模块二：玩家角色控制

**学习目标：** New Input System、2D 物理、对象池模式、协程时序控制、摄像机算法

- **New Input System** 代码式绑定：`PlayerInput` + `InputActionMap` + `Composite` 组合键
- **Rigidbody2D** 物理移动：`velocity` 控制 + `Interpolation` 平滑 + `CapsuleCollider2D` 碰撞
- **协程驱动的冲刺系统**：空格触发 → 短暂加速 → 冷却 → 可中断的时序状态机
- **对象池（Object Pool）** 实现的残影拖尾：`Queue<AfterimageGhost>` 预创建 + 定时产出 + 回收复用
- **摄像机平滑跟随**：`LateUpdate` + `SmoothDamp` + 房间边界 Clamp + 小房间锁定中心，不依赖 Cinemachine

**涉及文件：** `PlayerController.cs` | `PlayerData.cs` | `AfterimageEffect.cs` | `AfterimageGhost.cs` | `CameraFollow.cs` | `GameManager.cs`

### 模块三：小地图系统

**学习目标：** 程序化纹理生成、UGUI 层级布局、多分辨率适配

- **Texture2D 程序化生成**：`SetPixels32` / `Apply` 更新地图贴图
- **迷雾探索算法**：根据角色探索逐步更新地图
- **多分辨率适配**：世界坐标 → 像素坐标 → UI 坐标，`CanvasScaler.referenceResolution` 多分辨率适配
- **UGUI 层级设计**：`Screen Space Overlay` → `RectMask2D` 裁剪 → `RawImage` 贴图 → 程序化 `Sprite.Create` 玩家标记
- **输入隔离**：地图打开时禁用 `PlayerController`，防止 UI 输入穿透到游戏逻辑

**涉及文件：** `MinimapController.cs` | `MinimapConfig.cs`

---

## 代码组织

```
Assets/Scripts/
├── Core/           # GameManager（入口单例）、CameraFollow
├── Dungeon/        # 地牢生成（生成器、走廊、房间模板、Editor工具）
├── Player/         # 玩家控制、残影效果、对象池
└── UI/             # 小地图控制器
```

可配置参数提取为 **ScriptableObject** 资产（`Assets/DungeonData/`），运行时数据与配置数据分离。

---

## 技术栈总览

| 类别 | 具体技术 |
|------|---------|
| **输入** | New Input System（代码式绑定 + Composite） |
| **物理** | Rigidbody2D + CapsuleCollider2D |
| **UI** | UGUI · Canvas · RawImage · RectMask2D  |
| **设计模式** | 单例 · 对象池 · ScriptableObject 数据驱动 · 回调/依赖反转 |
| **算法** | 并查集（Union-Find）· Kruskal MST · 4-bit 位掩码  |
| **编辑器** | 自定义 Inspector · Editor 脚本 |
| **动画** | Animator状态机 |

---

## 如何运行

```bash
git clone https://github.com/littlefish04/Unity-Roguelike-Dungeon.git
```

克隆后有两种方式运行：

- **不需要 Unity**：进入 `Build/` 文件夹，双击 `New Roguelike Dungeon Shooting.exe`
- **需要 Unity 2022.3.x**：用 Unity Hub 打开项目，打开 `Assets/Scenes/` 下的场景，点击 Play

| 操作 | 按键 |
|------|------|
| 移动 | WASD |
| 瞄准 | 鼠标 |
| 冲刺 | 空格 |
| 小地图 | M（Tab 缩放 / WASD 平移 / Esc 关闭） |

---

## 许可

本项目仅用于技术学习与展示。美术素材版权归原作者所有。
