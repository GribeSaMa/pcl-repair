using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PclRepair;

static class CoreTests
{
    static readonly List<string> Passed = new List<string>();
    static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        Passed.Add(name);
        Console.WriteLine("PASS: " + name);
    }
    static void MustReject(Action action, string name)
    {
        bool rejected = false;
        try { action(); } catch (InvalidOperationException) { rejected = true; } catch (IOException) { rejected = true; }
        Check(rejected, name);
    }
    static int Main(string[] args)
    {
        try
        {
            string original = Path.GetFullPath(args[0]);
            string root = Path.GetFullPath(args[1]);
            Directory.CreateDirectory(root);
            string run = Path.Combine(root, "run-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(run);
            Func<string, string> makeCase = name => { var p = Path.Combine(run, name); Directory.CreateDirectory(p); return p; };
            string source = Path.Combine(makeCase("repair"), "renamed-launcher.exe");
            File.Copy(original, source);
            Check(RepairEngine.Inspect(source).State == LauncherState.Ready, "supported and renamed launcher recognized");
            string result = RepairEngine.Repair(source);
            Check(RepairEngine.HashFile(source) == RepairEngine.OriginalHash, "original remains byte-identical");
            Check(RepairEngine.HashFile(result) == RepairEngine.FixedHash, "repaired executable matches validated output");
            byte[] before = File.ReadAllBytes(source), after = File.ReadAllBytes(result);
            var differences = Enumerable.Range(0, before.Length).Where(i => before[i] != after[i]).ToArray();
            Check(differences.Length == 41 && differences.All(i => i >= 0x2C0DA && i < 0x2C0DA + 48), "only 41 expected bytes change");
            DateTime written = File.GetLastWriteTimeUtc(result);
            Check(RepairEngine.Repair(source) == result && File.GetLastWriteTimeUtc(result) == written, "repeated repair preserves existing fixed file");
            Check(RepairEngine.Repair(result) == result, "already repaired input is idempotent");
            Check(RepairEngine.Inspect(source).State == LauncherState.Repaired, "inspection recognizes repaired sibling");
            string conflictSource = Path.Combine(makeCase("conflict"), "PCL.exe");
            File.Copy(original, conflictSource);
            string collision = Path.Combine(Path.GetDirectoryName(conflictSource), RepairEngine.OutputName);
            File.WriteAllText(collision, "user file - preserve");
            MustReject(() => RepairEngine.Repair(conflictSource), "different output file is rejected");
            Check(File.ReadAllText(collision) == "user file - preserve", "colliding user file remains untouched");
            Check(RepairEngine.Inspect(conflictSource).State == LauncherState.Conflict, "collision status is visible");
            string altered = Path.Combine(makeCase("unsupported"), "PCL.exe");
            byte[] alteredBytes = File.ReadAllBytes(original); alteredBytes[alteredBytes.Length - 1] ^= 1;
            File.WriteAllBytes(altered, alteredBytes);
            MustReject(() => RepairEngine.Repair(altered), "same-size modified executable is rejected");
            Check(!File.Exists(Path.Combine(Path.GetDirectoryName(altered), RepairEngine.OutputName)), "unsupported input creates no output");
            string wrongName = Path.Combine(makeCase("source-name"), RepairEngine.OutputName);
            File.Copy(original, wrongName);
            MustReject(() => RepairEngine.Repair(wrongName), "source cannot be overwritten by output path");
            Check(RepairEngine.HashFile(wrongName) == RepairEngine.OriginalHash, "wrong-name source is preserved");
            string concurrent = Path.Combine(makeCase("concurrent"), "PCL.exe");
            File.Copy(original, concurrent);
            Task.WaitAll(Task.Run(() => RepairEngine.Repair(concurrent)), Task.Run(() => RepairEngine.Repair(concurrent)));
            Check(RepairEngine.HashFile(Path.Combine(Path.GetDirectoryName(concurrent), RepairEngine.OutputName)) == RepairEngine.FixedHash, "concurrent repairs publish one verified file");
            Check(Directory.GetFiles(Path.GetDirectoryName(concurrent), "*.tmp").Length == 0, "concurrent repair leaves no temporary file");
            string locked = Path.Combine(makeCase("locked"), "PCL.exe"); File.Copy(original, locked);
            using (var handle = File.Open(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                MustReject(() => RepairEngine.Repair(locked), "locked input is rejected");
            Check(!File.Exists(Path.Combine(Path.GetDirectoryName(locked), RepairEngine.OutputName)), "locked input creates no output");
            string scanRoot = makeCase("scan");
            string nested = Path.Combine(scanRoot, "folder", "another"); Directory.CreateDirectory(nested);
            string renamed = Path.Combine(nested, "my-game.exe"); File.Copy(original, renamed);
            var found = new List<LauncherInfo>();
            LauncherScanner.Scan(new[] { scanRoot }, 6, CancellationToken.None, i => found.Add(i), (p, n) => { }, true);
            Check(found.Count == 1 && found[0].Path == renamed, "scanner finds renamed launcher by executable metadata");
            var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            bool cancellationObserved = false;
            try { LauncherScanner.Scan(new[] { scanRoot }, 6, cancelled.Token, i => { }, (p, n) => { }, true); }
            catch (OperationCanceledException) { cancellationObserved = true; }
            Check(cancellationObserved, "scan cancellation is honored");
            File.WriteAllLines(Path.Combine(root, "latest-results.txt"), Passed.Concat(new[] { "Total: " + Passed.Count + " passed", "Evidence: " + run }));
            Console.WriteLine("Total: " + Passed.Count + " passed");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e.ToString()); return 1; }
    }
}
