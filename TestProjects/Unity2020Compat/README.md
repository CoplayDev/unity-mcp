# Unity 2020.3 Compatibility Test Project

用途：验证 MCPForUnity 包在 Unity 2020.3 LTS 下的编译兼容性。

- Unity 版本：2020.3.24f1（`C:\SoftWare\Unity\2020.3.24f1`）
- 包引用：`file:../../MCPForUnity`（相对路径，克隆仓库后开箱即用）
- `Library/` 等生成目录不入库（见 .gitignore）

## 命令行验证

```cmd
cd /d <repo>\TestProjects\Unity2020Compat
"C:\SoftWare\Unity\2020.3.24f1\Editor\Unity.exe" -batchmode -nographics -quit -projectPath "%~dp0" -logFile compile.log
```

通过标准：`compile.log` 中无 `error CS`，结尾出现 `Exiting batchmode successfully now!`。

图形化：用 Unity Hub 打开本目录，Console 无红错即可。
