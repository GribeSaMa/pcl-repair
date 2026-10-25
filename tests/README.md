# 核心测试

在仓库根目录运行 `test.ps1 -LauncherPath "原启动器的完整路径"`。测试需要 README 中 SHA-256 匹配的 PCL 原启动器；仓库不包含该文件。

脚本自动编译 `CoreTests.cs`，在临时目录中创建测试副本并清理。通过的测试列表保存在本地 `tests/evidence/latest-results.txt`，不上传本机路径或日志。
