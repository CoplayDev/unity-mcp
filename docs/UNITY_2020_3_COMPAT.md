# Unity-shikiMCP — Unity 2020.3 兼容补丁清单

基线：`CoplayDev/unity-mcp` **v10.1.2**（tag `v10.1.2`，commit `4ce7dd3`）
本仓库：`RoyougiShiki/Unity-shikiMCP`（GitHub fork，保留上游关系，可 PR 回上游）
适配目标：**Unity 2020.3 LTS 编译零错误、零警告，功能等价保留**

验证方法：本机 `Unity 2020.3.24f1`（C:\SoftWare\Unity\2020.3.24f1）batchmode 真实编译
- 编译：`Unity.exe -batchmode -nographics -quit` → **0 error / 0 warning**
- UXML：11/11 全部可加载，CompatDropdownField 的 choices/index 属性解析正确
- 运行时：主窗口打开、下拉控件回调、核心工具 ping 全部通过

---

## 一、C# 9 语法 → C# 8 改写（Unity 2020.3 编译器为 C# 8）

| 类型 | 数量 | 改写方式 | 说明 |
|---|---|---|---|
| target-typed `new()` / `new(...)` | 51+14 | 显式类型 | 全部为 `= new Type()` 形态，机械改写 |
| `is not T x` 模式 | 20 | `!(x is T x)` | 语义完全等价 |
| `is A or B` 模式 | 8 | `x == A \|\| x == B` | Tommy.cs 字符/字符串判断 |
| 属性模式 `{ P: v }` 组合 or | 3 | 拆分为两个 is 表达式 | Tommy.cs |
| switch 表达式内 `"a" or "b" =>` | 2 | 拆分为两个 case arm | 值重复 |
| 三元表达式 target-typed | 4 | `(IMcpResponse)` 显式转型 | Success/ErrorResponse 三目 |

## 二、.NET API 差异（netstandard2.0 vs 2.1）

| API | 替换 | 文件数 |
|---|---|---|
| `string.Contains(char)` | `.Contains("x")` | 6 |
| `string.Contains(str, StringComparison)` | `.IndexOf(str, cmp) >= 0` | 3 |
| `string.Join(char, ...)` | `.ToString()` | 3 |
| `Index/Range` 切片 `s[..^n]` / `s[n..]` | `Substring` | 5 |
| `Math.Clamp` | `Mathf.Clamp` | 4 |
| `Task.IsCompletedSuccessfully` | `TaskStatus.RanToCompletion` | 1 |
| `Dictionary.Remove(k, out v)` | `TryGetValue` + `Remove` | 1 |
| `Path.GetRelativePath` | 自写 `MakeRelativePath` | 1 |
| `string.Replace(s1,s2,cmp)` | 无比较重载 | 1 |
| `ProcessStartInfo.ArgumentList.Add` | 扩展方法 `AddArg`（引号处理） | 3 |
| `Rfc2898DeriveBytes(...,SHA256)` 4 参 | 3 参构造（SHA1） | 1 |
| `Enum.TryParse(Type,str,bool,out)` | `Enum.Parse(Type,str,true)` + catch | 2 |
| `ArraySegment.ToArray`（缺 Linq） | 补 using | 1 |

## 三、Unity 2021.2+ 专用 API → 条件编译 + 等价实现

以下全部用 `#if UNITY_2021_2_OR_NEWER` 双分支，**2020.3 分支提供等价功能**：

| API | 2020.3 等价实现 | 文件 |
|---|---|---|
| `NamedBuildTarget` | `BuildTargetGroup`（旧 API 全有） | Build 系列 3 文件 |
| `PlayerSettings.GetScriptingDefineSymbols(nt)` | `...ForGroup(group)` | 同上 |
| `PrefabStageUtility.OpenPrefab` | `AssetDatabase.OpenAsset` + `GetCurrentPrefabStage` | ManagePrefabs |
| `PrefabStage`/`PrefabStageUtility` 命名空间 | `UnityEditor.Experimental.SceneManagement` | 8 文件 |
| `StandaloneBuildSubtarget`/`subtarget` | 返回 0（仅 Player，2020.3 无 Server 子目标） | Build 系列 |
| `BuildOptions.CleanBuildCache` | 降级为空操作（2020.3 无此选项） | BuildRunner |
| `PackageInfo.GetAllRegisteredPackages` | 新增 `RegisteredPackageInfo`：2020.3 同步解析权威 `Packages/packages-lock.json`（纯文件 IO，无死锁；`Client.List` 轮询会死锁编辑器） | 3 文件 |
| `AddAndRemoveRequest` | `Client.Add`/`Client.Remove` 逐个 | MCPForUnityEditorWindow |
| `ShaderPropertyType.Int` | 2020.3 枚举无 Int（用 Float/Range 分支覆盖） | SkyboxOps |
| `ProfilerCategory.FileIO/VirtualTexturing` | 映射 Loading/Render | CounterOps |
| `LightingSettings.lightmapCompression` | 2020.3 无此属性 → 读字段省略、写字段返回成功但跳过（**真实缺失**） | LightBakingOps |
| `MaterialPropertyBlock.HasColor` | try/catch GetColor | ManagePrefabs |

## 四、UI Toolkit 差异（DropdownField 是 2021.2+ 控件）

**新增 `CompatDropdownField`**（`Editor/Windows/Components/CompatDropdownField.cs`）：
- **2021.2+ 分支**：直接继承 `UnityEngine.UIElements.DropdownField`（行为与上游完全一致）
- **2020.3 分支**：自绘等价下拉（IMGUI Popup 封装），暴露与 DropdownField 相同的公开面：
  `choices / index / value / SetValueWithoutNotify / RegisterValueChangedCallback`
  （2020.3 的 PopupField\<string\>.choices 是 private，无法包装，故自绘）
- 带 `UxmlFactory/UxmlTraits`，UXML 中可用 `<mcpx:CompatDropdownField />`
- 4 个 UXML 文件中的 `<ui:DropdownField>` 已替换为 `<mcpx:CompatDropdownField>`

## 五、2020.3 真实缺失（已确认，无等价 API，降级处理）

| 功能 | 2020.3 状态 | 处理 |
|---|---|---|
| `UIDocument` runtime UI 组件（attach_ui_document / detach_ui_document / get_visual_tree / render_ui / modify_visual_element / create_panel_settings / update_panel_settings） | 2021.2+ 才有 | 返回明确错误信息 "requires Unity 2021.2 or newer"，工具列表仍注册 |
| `LightingSettings.lightmapCompression` | 2021.2+ 才有 | 读取省略该字段，写入静默跳过 |
| `BuildOptions.CleanBuildCache` | 2021.2+ 才有 | clean_build 参数被忽略 |
| Standalone Server 子目标构建 | 2021.2+ 才有 | subtarget 固定为 Player(0) |
| `ProfilerCategory.FileIO` / `VirtualTexturing` | 2021.2+ 才有 | 映射到相近分类 |
| `Rfc2898DeriveBytes` SHA256 变体 | 4 参构造 2021.2+ 才有 | 降级 SHA1（加密文件存储仍在） |

## 六、同步策略备忘

- 上游同步：`git fetch upstream beta` → rebase 重放本补丁（补丁集中在上述四类，冲突点少）
- PR 回上游：大部分改写（C#9→C#8、Contains、条件编译双分支）对上游是**向后兼容增强**，可 PR
- 升级 Unity 后：`UNITY_2021_2_OR_NEWER` 分支自动启用原生 API，CompatDropdownField 自动变回 DropdownField

## 七、验证记录

| 验证项 | 结果 |
|---|---|
| Unity 2020.3.24f1 全量编译（batchmode） | ✅ 0 error / 0 warning |
| 11 个 UXML 全部加载 + 控件属性解析 | ✅ 11/11 |
| 主窗口打开（MCPForUnityEditorWindow） | ✅ |
| CompatDropdownField 值/回调 | ✅ |
| 工具路由 ping | ✅ |
