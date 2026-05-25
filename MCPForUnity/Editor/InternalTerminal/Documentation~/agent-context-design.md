# Add to Agent 上下文设计

本文档说明 WTL Internal Terminal 如何把 Unity 上下文送入内置终端，供终端中的 AI agent 使用。

核心目标不是设计一个只有插件自己能理解的私有协议，而是粘贴一段普通文本。这段文本应当同时满足：

- 人能读懂。
- AI agent 容易理解。
- 未来工具容易解析。
- Unity 内部有机会重新定位到对应对象。

## 目标

- 支持两种自然入口：可拖入的内容直接拖入终端，不方便拖入的内容使用右键菜单 `Add to Agent`。
- 上下文通过终端现有粘贴通道进入终端。
- Unity 只负责收集和格式化上下文，终端和 agent 接收普通文本。
- 优先使用可读路径，再附加稳定 Unity 标识作为辅助锚点。
- 不在粘贴内容里暴露裸 `instanceID`，它是会话内临时数字。
- 文件、场景对象、组件、Console 信息使用一致的表达方式。

## 非目标

- 不要求 Node 后端理解 Unity。
- 不要求 Node 后端或终端内 agent 理解专用协议；Unity 侧可以提供轻量 Editor API，方便自定义按钮复用相同上下文格式和粘贴通道。
- 不依赖 WebView、浏览器 DOM 或 Unity 内的 xterm.js。
- 不把 `GlobalObjectId` 当成唯一可见标识。

## 交互模型

上下文插入应支持两种入口：

- 直接拖入终端。
- 右键菜单 `Add to Agent`。

二者关系：

- 拖入是快捷入口，适合 Unity 本身可以拖拽的对象。
- 右键菜单是完整入口，所有可以加入 agent 的上下文都应提供 `Add to Agent`。
- 同一种上下文如果同时支持拖入和右键，两者输出必须一致。
- 不因为支持拖入而移除右键菜单。

如果某种 Unity 上下文天然支持拖拽，例如 Project 资源、Hierarchy 对象、场景对象引用，应允许用户直接拖入 Internal Terminal。拖入后不再弹出或要求选择 `Add to Agent`，而是直接把对应上下文粘贴进终端。

右键菜单用于补充这些场景：

- 不方便拖拽的上下文。
- Console 信息。
- Inspector 中的 Component。
- 多选后想明确执行“加入 agent 上下文”的场景。

右键菜单项名称为：

```text
Add to Agent
```

`Add to Agent` 应放在右键菜单最上面，至少应位于本插件添加的菜单项第一位。它是高频动作，不应被隐藏在二级菜单或菜单底部。

推荐支持的位置：

- Project Browser 中的资源和文件夹。
- Hierarchy 中的 GameObject。
- Inspector 中的 Component 右键菜单，如果 Unity 版本允许稳定扩展。
- Internal Terminal 内部右键菜单，用于终端自身相关操作。
- Unity Console，如果目标 Unity 版本存在可用扩展点。

如果 Unity Console 没有稳定公开的右键扩展点，可以先在终端窗口右键菜单顶部提供：

```text
Add Console to Agent
```

这样仍然保持右键菜单交互，不再增加工具栏按钮。

推荐支持矩阵：

| 上下文 | 直接拖入 | 右键 `Add to Agent` |
| --- | --- | --- |
| Project 文件和文件夹 | 支持 | 支持 |
| Scene 资源 `.unity` | 支持 | 支持 |
| Prefab、Material、Script 等资源 | 支持 | 支持 |
| Hierarchy GameObject | 支持 | 支持 |
| 多选资源或对象 | 支持 | 支持 |
| Inspector Component | 暂不作为主要入口 | 支持 |
| Console 单条或多条信息 | 通常不支持 | 支持 |
| Console 全量信息 | 不支持 | 支持 |
| 当前 Selection 摘要 | 不需要 | 支持 |
| 活跃 Scene 摘要 | 不需要 | 支持 |

## 拖入行为

能拖入的上下文应该允许直接拖入终端。

拖入终端时的行为：

- 使用和 `Add to Agent` 相同的格式化规则。
- 使用和 `Add to Agent` 相同的粘贴通道。
- 不插入本机绝对路径，仍然使用 `Assets/...` 或 `@unity(...)`。
- 多个拖入对象时，一行一个上下文引用。
- 拖入文本类内容时，保留原始文本边界，必要时使用块格式包裹。

拖入不是 `Add to Agent` 的别名菜单，而是更直接的输入方式。用户能拖就直接拖；不能拖、或需要从 Console/Inspector 等位置加入时，再使用右键菜单。右键菜单始终保留，因为它是完整能力入口。

## 粘贴通道

所有上下文插入都应走 `SendPaste`，不要模拟键盘逐字输入。

原因：

- 多行文本更安全。
- 支持 bracketed paste 的 shell 可以把它当作一次粘贴处理。
- 避免长上下文走按键事件造成卡顿或字符丢失。
- 保持上下文插入和 IME 输入逻辑分离。

## 统一引用格式

Unity 对象引用统一使用这一格式：

```text
@unity(type:"...", path:"...", gid:"...")
```

规则：

- `type` 必填。
- 只要存在可读路径，`path` 必填。
- `gid` 可选，但建议场景对象和子资源尽量带上。
- 字段值使用双引号。
- 字段值中的双引号和反斜杠必须转义。
- 每条引用是一行普通文本，末尾加换行。

这个格式刻意保持紧凑。它可以被粘进 shell、AI agent 提示词、Markdown 笔记，也方便未来写解析器。

## 资源引用

Project 中的资源使用 Unity 项目路径，不使用本机绝对路径：

```text
@unity(type:"script", path:"Assets/Scripts/PlayerController.cs")
@unity(type:"prefab", path:"Assets/Prefabs/Enemy.prefab")
@unity(type:"scene", path:"Assets/Scenes/Battle.unity")
@unity(type:"material", path:"Assets/Materials/Enemy.mat")
@unity(type:"folder", path:"Assets/Scripts")
```

使用 `Assets/...` 路径的原因：

- 可读。
- 项目迁移到其他机器后仍然成立。
- 符合 Unity 工具链的常用表达。
- 不泄露用户本机目录结构。

推荐类型映射：

| 文件类型 | type |
| --- | --- |
| `.cs` | `script` |
| `.unity` | `scene` |
| `.prefab` | `prefab` |
| `.mat` | `material` |
| `.asset` | `asset` |
| 文件夹 | `folder` |
| 未识别文件 | `asset` |

## 场景对象引用

场景对象需要同时包含可读的场景/层级路径，以及一个稳定辅助锚点。

推荐格式：

```text
@unity(type:"gameObject", path:"Assets/Scenes/Main.unity#Player/Camera", gid:"GlobalObjectId_V1-...")
```

其中 `path` 的结构是：

```text
<场景资源路径>#<Hierarchy 路径>
```

示例：

```text
@unity(type:"gameObject", path:"Assets/Scenes/Main.unity#Environment/LightProbeGroup", gid:"GlobalObjectId_V1-...")
```

Hierarchy 路径可读，但不一定唯一。Unity 允许同级 GameObject 重名，因此 `gid` 可以作为更强的定位锚点。

## 组件引用

组件引用应包含所属 GameObject 路径和组件类型。

推荐格式：

```text
@unity(type:"component", path:"Assets/Scenes/Main.unity#Player:UnityEngine.Rigidbody", gid:"GlobalObjectId_V1-...")
```

如果同一个 GameObject 上有多个同类型组件，增加索引：

```text
@unity(type:"component", path:"Assets/Scenes/Main.unity#Player:UnityEngine.BoxCollider[1]", gid:"GlobalObjectId_V1-...")
```

索引可以帮助人理解重复组件，但不应该作为唯一锚点。Unity 能提供 `GlobalObjectId` 时，应同时附加 `gid`。

## Prefab 实例引用

场景中的 Prefab 实例应按场景对象处理，而不是只引用 Prefab 资源：

```text
@unity(type:"gameObject", path:"Assets/Scenes/Main.unity#Enemies/Enemy_01", gid:"GlobalObjectId_V1-...")
```

如果后续需要，也可以附加来源 Prefab：

```text
@unity(type:"gameObject", path:"Assets/Scenes/Main.unity#Enemies/Enemy_01", prefab:"Assets/Prefabs/Enemy.prefab", gid:"GlobalObjectId_V1-...")
```

## GlobalObjectId 是魔法数字吗

`GlobalObjectId` 不够可读，所以不应该作为主要上下文。但它可以作为辅助定位信息。

推荐规则：

- 主标识：可读的 `Assets/...` 路径和 Hierarchy 路径。
- 辅助标识：`gid`。
- 避免粘贴裸 `instanceID`。

不使用 `instanceID` 的原因：

- 它会随 Editor 会话变化。
- 对人不可读。
- AI agent 很容易错误地把它当成稳定事实。

保留 `GlobalObjectId` 的原因：

- 比 `instanceID` 稳定得多。
- 可以消除重名对象造成的歧义。
- 未来 Unity 侧工具可以用它重新解析精确对象。

## Console 上下文

Console 信息应该粘贴完整文本，而不是粘贴引用。

原因：

- Console 是诊断证据。
- 真正有用的是 message、stack trace、file、line、level。
- 对 AI agent 来说，完整错误文本比“某条 Console 引用”更有价值。
- Console 条目是临时信息，没有干净稳定的项目身份。

推荐块格式：

```text
<unity-console generated:"2026-05-22T15:30:00+08:00" count:"2">
--- entry 1/2 ---
level: error
file: Assets/Scripts/PlayerController.cs
line: 42
message:
NullReferenceException: Object reference not set to an instance of an object

stack:
PlayerController.Update() (at Assets/Scripts/PlayerController.cs:42)

--- entry 2/2 ---
level: warning
file:
line:
message:
Some warning text

stack:

</unity-console>
```

规则：

- 如果 Unity 能读取 Console 当前选中项，则优先加入选中项。
- 如果无法读取选中项，`Add Console to Agent` 加入当前 Console 全量信息。
- 保留 stack trace。
- 保留原始 message 文本。
- 使用清晰的开始和结束标签，方便 agent 判断边界。

## 多对象上下文

多选对象时，一行一个引用：

```text
@unity(type:"script", path:"Assets/Scripts/PlayerController.cs")
@unity(type:"prefab", path:"Assets/Prefabs/Player.prefab")
@unity(type:"gameObject", path:"Assets/Scenes/Main.unity#Player", gid:"GlobalObjectId_V1-...")
```

如果后续需要更明确地告诉 agent “这是 Unity 上下文块”，可以增加外层包裹：

```text
<unity-context>
@unity(type:"script", path:"Assets/Scripts/PlayerController.cs")
@unity(type:"prefab", path:"Assets/Prefabs/Player.prefab")
</unity-context>
```

当前阶段建议先保持一行一个 `@unity(...)`，更简单，也更适合直接粘进任意 shell。

## 可以加入哪些上下文

第一阶段建议支持：

- Project 文件和文件夹。
- Scene 文件。
- Prefab 资源。
- 场景 GameObject。
- Component。
- Console 条目。

后续可以增加：

- 当前 Unity 选中项摘要。
- 当前激活场景摘要。
- Build Settings 中的场景列表。
- Inspector 序列化字段快照。
- Unity Test Runner 失败信息。
- Profiler marker 或单帧摘要。

暂时避免：

- 默认粘贴完整场景 dump。
- 默认粘贴完整 Prefab YAML。
- 粘贴二进制资源内容。
- 用户没有明确选择时粘贴超大生成文件。

## 未来工具的解析策略

如果未来 Unity 侧工具收到 `@unity(...)`，推荐按以下顺序解析：

1. 如果存在 `gid`，优先尝试通过 `GlobalObjectId` 解析。
2. 如果 `path` 是 `Assets/...` 资源路径，使用 `AssetDatabase.LoadAssetAtPath`。
3. 如果 `path` 包含 `#`，先定位场景路径，再匹配 Hierarchy 路径。
4. 如果 Hierarchy 路径存在歧义，返回所有候选或让用户选择。

这样这套文本既能在 Unity 内解析，也能在 Unity 外作为可读上下文使用。

## 实现拆分建议

推荐拆成这些模块：

- `InternalTerminalContextMenu`
  - 注册 `Add to Agent` 右键菜单。
  - 收集 `Selection.objects`。
  - 调用公开 API 把生成文本送入当前终端窗口。

- `InternalTerminalAgentContext`
  - 提供给自定义 Editor UI 的公开入口。
  - 支持添加 Selection、Object、多个 Object、资源路径、Console 和纯文本。
  - 复用统一格式化规则和终端粘贴通道。

- `InternalTerminalContextFormatter`
  - 把 Unity 对象转换为 `@unity(...)`。
  - 负责字符串转义。
  - 负责资源路径到 `type` 的映射。

- `UnityConsoleContextReader`
  - 在需要时通过反射读取 Console 条目。
  - 把 Console 条目格式化成 `<unity-console>` 文本块。
  - Unity 内部 Console API 变化时优雅失败。

- `InternalTerminalWindow`
  - 暴露一个小的静态方法，例如 `PasteToActiveTerminal(string text)`。
  - 增加终端内部右键菜单动作。

## 失败兜底

如果没有打开终端窗口：

- 打开 Internal Terminal 窗口。
- 启动或重连后端。
- 连接成功后尽量自动粘贴上下文。

如果后端尚未连接：

- 把上下文复制到系统剪贴板。
- 在 Unity Console 输出清晰提示，说明上下文已复制。

如果 Console 反射读取失败：

- 粘贴或复制一个小的诊断块，说明 Console 提取失败。
- 不要把异常直接抛到 Editor UI。

## 示例流程

用户在 Project Browser 中右键 `Assets/Scripts/PlayerController.cs`，选择 `Add to Agent`。

终端收到：

```text
@unity(type:"script", path:"Assets/Scripts/PlayerController.cs")
```

用户在终端内右键，选择 `Add Console to Agent`。

终端收到：

```text
<unity-console generated:"2026-05-22T15:30:00+08:00" count:"1">
--- entry 1/1 ---
level: error
file: Assets/Scripts/PlayerController.cs
line: 42
message:
NullReferenceException: Object reference not set to an instance of an object

stack:
PlayerController.Update() (at Assets/Scripts/PlayerController.cs:42)
</unity-console>
```

此时 agent 同时拥有稳定文件引用和完整错误文本，可以继续分析或修改代码。
