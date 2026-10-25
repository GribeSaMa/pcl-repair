# PCL 一键修复工具

独立的 Windows 桌面工具，为已验证的 **PCL 2.99.0.0（内部版本 413）** 生成下载功能修复副本。单文件 EXE，无需安装，修复过程不联网，原启动器完整保留。

**仅支持下方 SHA-256 对应的构建。** 相同版本号但文件内容不同的启动器也会被拒绝。

## 下载与使用

前往 [Releases](https://github.com/GribeSaMa/pcl-repair/releases/latest)，下载 `PCL-Repair.exe`，也可以下载包含使用说明及校验文件的 ZIP 包。

1. 关闭正在运行的 PCL，打开 `PCL-Repair.exe`。
2. 等待自动查找，选中目标启动器；也可使用“深度查找”“手动选择 PCL”或拖入 EXE。
3. 点击“一键修复”，完成后点击“打开修复版”。

修复副本位于原启动器同目录，文件名为 `Plain Craft Launcher 2 (Download Fixed).exe`。已有相同修复副本时可直接打开；遇到同名但内容不同的文件时，工具会停止，避免覆盖。

运行环境：Windows 10/11，.NET Framework 4.8。修复时需要对启动器所在目录有写入权限。

## 支持范围

- 恢复此构建中原版 Minecraft 的自动下载安装入口。
- 不恢复整合包导入，也未验证 Mod、光影等其他下载流程。
- 不修改账号、游戏存档或系统设置。
- 不兼容其他 PCL 版本或未验证的构建。

| 文件 | SHA-256 |
| --- | --- |
| 支持的原启动器 | `6B6E1260CB5A1D6A303855F1C60CAB3D0BC1AEBBA7E40A964180188B271E7386` |
| 生成的修复副本 | `BA7585206D97D63AD6F48CDF5FE3F79757B405990D85D1009E1EE19EDF4272A9` |

## 从源码构建

使用 Windows PowerShell，在项目目录执行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

输出为项目根目录的 `PCL-Repair.exe`。构建使用系统的 .NET Framework C# 编译器，不需要 NuGet。`src/app.ico` 由 `tools/IconBuilder.cs` 自动生成。

## 测试

仓库及发布包均不包含 PCL 启动器或 Minecraft 游戏文件。自行准备上方哈希匹配的原启动器，执行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test.ps1 -LauncherPath "D:\Games\Plain Craft Launcher 2.exe"
```

20 项核心测试覆盖精确版本识别、补丁范围、原文件保留、重复运行、文件冲突、未知文件拒绝、并发、锁定文件、扫描与取消。测试在临时目录操作副本并在结束后清理。

2026-10-25 的本地验证还包含：通过修复副本下载安装原版 26.3，并校验 5,223 个游戏文件，缺失及校验失败为 0；未验证游戏进入主菜单。详情见 [修复说明](修复说明.md) 和 [工具使用说明](一键修复工具说明.md)。

## 目录

- `src/`：WinForms 界面与修复逻辑。
- `tests/CoreTests.cs`：核心测试。
- `tools/Repair-Pcl299.ps1`：命令行修复脚本。
- `tools/Verify-Download.ps1`：本地游戏文件校验脚本。
- `tools/LauncherInspector/`：用于检查 .NET IL 的辅助工具，单独构建需要 .NET 10 SDK。

## 项目说明

本项目是独立的第三方工具，与 PCL 官方无隶属关系。PCL 原作者为龙腾猫跃，原项目为 [Meloong-Git/PCL](https://github.com/Meloong-Git/PCL)，其许可及合理使用指南见 [LICENCE](https://github.com/Meloong-Git/PCL/blob/main/LICENCE)。本仓库不分发原启动器或修改后的启动器，相关权利属于各自权利人。
