using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace PclRepair
{
    public enum LauncherState { Ready, Repaired, Unsupported, Conflict }

    public sealed class LauncherInfo
    {
        public string Path { get; set; }
        public string Version { get; set; }
        public string OutputPath { get; set; }
        public LauncherState State { get; set; }
        public bool IsOriginalBuild { get; set; }
        public string Status
        {
            get
            {
                switch (State)
                {
                    case LauncherState.Ready: return "可以修复";
                    case LauncherState.Repaired: return "已有修复版";
                    case LauncherState.Conflict: return "输出文件重名";
                    default: return "暂不支持此版本";
                }
            }
        }
    }

    public static class RepairEngine
    {
        public const string OriginalHash = "6B6E1260CB5A1D6A303855F1C60CAB3D0BC1AEBBA7E40A964180188B271E7386";
        public const string FixedHash = "BA7585206D97D63AD6F48CDF5FE3F79757B405990D85D1009E1EE19EDF4272A9";
        public const string OutputName = "Plain Craft Launcher 2 (Download Fixed).exe";
        public const int FileLength = 5980160;
        private const int PatchOffset = 0x2C0DA;
        private static readonly byte[] OriginalInstructions = new byte[] {
            0x72,0x33,0x47,0x01,0x70,0x7E,0xFB,0x09,0x00,0x04,0x2C,0x07,
            0x7E,0xFB,0x09,0x00,0x04,0x2B,0x16,0x7E,0xF9,0x09,0x00,0x04,
            0xFE,0x06,0x67,0x14,0x00,0x06,0x73,0x8D,0x05,0x00,0x0A,0x25,
            0x80,0xFB,0x09,0x00,0x04,0x14,0x18,0x73,0x8E,0x05,0x00,0x0A };

        public static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
        }

        public static string HashFile(string path)
        {
            using (var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "");
        }

        public static LauncherInfo Inspect(string path)
        {
            path = System.IO.Path.GetFullPath(path);
            var version = FileVersionInfo.GetVersionInfo(path);
            var info = new LauncherInfo {
                Path = path, Version = version.FileVersion ?? "未知版本",
                State = LauncherState.Unsupported,
                OutputPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path), OutputName)
            };
            if (new FileInfo(path).Length != FileLength) return info;
            string hash = HashFile(path);
            if (hash == FixedHash)
            {
                info.State = LauncherState.Repaired;
                info.OutputPath = path;
            }
            else if (hash == OriginalHash)
            {
                info.IsOriginalBuild = true;
                info.Version = "2.99.0.0 (413)";
                info.State = LauncherState.Ready;
                if (File.Exists(info.OutputPath))
                    info.State = HashFile(info.OutputPath) == FixedHash ? LauncherState.Repaired : LauncherState.Conflict;
            }
            return info;
        }

        public static string Repair(string path)
        {
            path = System.IO.Path.GetFullPath(path);
            string output = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path), OutputName);
            string temporary = null;
            // Deny writes and renames to the input while validating and publishing the copy.
            using (var source = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (source.Length != FileLength) throw new InvalidOperationException("此文件不是已验证的 PCL 2.99.0.0（413）构建，未作修改。");
                byte[] bytes = new byte[FileLength];
                int position = 0;
                while (position < bytes.Length)
                {
                    int count = source.Read(bytes, position, bytes.Length - position);
                    if (count == 0) throw new IOException("启动器文件读取不完整，未作修改。");
                    position += count;
                }
                string hash = Hash(bytes);
                if (hash == FixedHash) return path;
                if (hash != OriginalHash) throw new InvalidOperationException("启动器版本或文件内容不匹配，未作修改。当前仅支持已验证的 2.99.0.0（413）原版。");
                if (String.Equals(path, output, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("原版使用了修复版文件名。请先为原版换一个名称后重试。");
                if (File.Exists(output))
                {
                    if (HashFile(output) == FixedHash) return output;
                    throw new IOException("同目录已有不同内容的修复版同名文件。请先手动改名，再重试；本工具不会覆盖它。");
                }
                for (int index = 0; index < OriginalInstructions.Length; index++)
                    if (bytes[PatchOffset + index] != OriginalInstructions[index])
                        throw new InvalidOperationException("目标指令不匹配，未作修改。");
                Array.Clear(bytes, PatchOffset, OriginalInstructions.Length);
                bytes[PatchOffset] = 0x7E;
                bytes[PatchOffset + 1] = 0x0F;
                bytes[PatchOffset + 2] = 0x04;
                bytes[PatchOffset + 4] = 0x04;
                if (Hash(bytes) != FixedHash) throw new InvalidOperationException("修复结果校验失败，未写入文件。");
                try
                {
                    temporary = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path), ".pcl-repair-" + Guid.NewGuid().ToString("N") + ".tmp");
                    using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        file.Write(bytes, 0, bytes.Length);
                        file.Flush(true);
                    }
                    if (HashFile(temporary) != FixedHash) throw new IOException("写入后的文件校验失败，没有生成修复版。");
                    try { File.Move(temporary, output); }
                    catch (IOException)
                    {
                        if (!File.Exists(output) || HashFile(output) != FixedHash) throw;
                    }
                    return output;
                }
                finally
                {
                    if (temporary != null && File.Exists(temporary))
                    {
                        try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    }
                }
            }
        }
    }

    public static class LauncherScanner
    {
        private static readonly HashSet<string> SkipFolders = new HashSet<string>(new[] {
            "Windows", "ProgramData", "$Recycle.Bin", "System Volume Information", "WindowsApps",
            ".git", ".svn", "node_modules", ".nuget", ".cache", "Cache", "Caches", "Code Cache",
            "GPUCache", "assets", "libraries", "runtime", "logs", "screenshots", "saves", "mods",
            "target", "obj", "__pycache__", "venv", ".venv", "SteamLibrary", "steamapps"
        }, StringComparer.OrdinalIgnoreCase);

        public static IEnumerable<string> QuickRoots(string appDirectory)
        {
            var roots = new List<string> {
                appDirectory,
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };
            string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (string name in new[] { "Games", "Minecraft", ".minecraft", "PCL", "PCL2" }) roots.Add(System.IO.Path.Combine(user, name));
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            foreach (string name in new[] { "PCL", "PCL2", ".minecraft" }) roots.Add(System.IO.Path.Combine(roaming, name));
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;
                    roots.Add(drive.RootDirectory.FullName);
                    foreach (string name in new[] { "Games", "Game", "Minecraft", "我的世界", "PCL", "PCL2", "软件", "游戏", "下载", "工具", "Tools", "Coding" })
                        roots.Add(System.IO.Path.Combine(drive.RootDirectory.FullName, name));
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
        }

        public static void Scan(IEnumerable<string> roots, int maxDepth, CancellationToken cancellation,
            Action<LauncherInfo> found, Action<string, int> progress, bool deep)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pending = new Queue<Tuple<string, int>>();
            foreach (string root in roots)
            {
                try { pending.Enqueue(Tuple.Create(System.IO.Path.GetFullPath(root), 0)); }
                catch (ArgumentException) { }
            }
            var timer = Stopwatch.StartNew();
            int folders = 0;
            while (pending.Count > 0)
            {
                cancellation.ThrowIfCancellationRequested();
                var entry = pending.Dequeue();
                if (!visited.Add(entry.Item1)) continue;
                try
                {
                    if ((File.GetAttributes(entry.Item1) & FileAttributes.ReparsePoint) != 0) continue;
                    folders++;
                    if (timer.ElapsedMilliseconds >= 180)
                    {
                        progress(entry.Item1, folders);
                        timer.Restart();
                    }
                    foreach (string file in Directory.EnumerateFiles(entry.Item1, "*.exe", SearchOption.TopDirectoryOnly))
                    {
                        cancellation.ThrowIfCancellationRequested();
                        try
                        {
                            long length = new FileInfo(file).Length;
                            if (length < 1024 * 1024 || length > 200L * 1024 * 1024) continue;
                            var version = FileVersionInfo.GetVersionInfo(file);
                            string description = (version.FileDescription ?? "") + " " + (version.ProductName ?? "") + " " + (version.OriginalFilename ?? "");
                            if (description.IndexOf("Plain Craft Launcher", StringComparison.OrdinalIgnoreCase) >= 0)
                                found(RepairEngine.Inspect(file));
                        }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                        catch (System.ComponentModel.Win32Exception) { }
                        catch (ArgumentException) { }
                    }
                    // Drive roots participate in the quick scan without recursively walking the whole disk.
                    bool driveRoot = String.Equals(System.IO.Path.GetPathRoot(entry.Item1).TrimEnd('\\'), entry.Item1.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
                    if (entry.Item2 >= maxDepth || (!deep && driveRoot)) continue;
                    foreach (string directory in Directory.EnumerateDirectories(entry.Item1))
                    {
                        string name = System.IO.Path.GetFileName(directory);
                        if (SkipFolders.Contains(name)) continue;
                        if (!deep && String.Equals(name, "AppData", StringComparison.OrdinalIgnoreCase)) continue;
                        pending.Enqueue(Tuple.Create(directory, entry.Item2 + 1));
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (System.Security.SecurityException) { }
            }
            progress("", folders);
        }
    }
}
