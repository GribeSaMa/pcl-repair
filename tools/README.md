# 辅助工具

- `IconBuilder.cs`：构建时生成工具图标，由根目录 `build.ps1` 自动调用。
- `Repair-Pcl299.ps1`：命令行修复，使用 `-LauncherPath` 指定已验证的原启动器。
- `Verify-Download.ps1`：校验根目录 `.minecraft` 中已下载的游戏文件，不执行下载。默认版本为 26.3，可通过 `-Version` 指定版本。
- `LauncherInspector/`：检查启动器 .NET IL 的分析程序，单独构建需要 .NET 10 SDK；运行时传入启动器路径。

桌面 EXE 独立运行，不需要携带本目录。
