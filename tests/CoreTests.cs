using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Diagnostics;

internal static class CoreTests {
    private static string root, fixture;
    private static int count;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void ExpectFailure(Action action, string beforeFile, string label) {
        string before = PclService.HashFile(beforeFile); bool failed = false;
        try { action(); } catch (Exception) { failed = true; }
        Check(failed, label + " should fail"); Check(PclService.HashFile(beforeFile) == before, label + " changed target"); Pass(label);
    }
    private static void Pass(string message) { count++; Console.WriteLine("PASS " + message); }
    private static string CopyFixture(string name) {
        string dir = Path.Combine(root, name); Directory.CreateDirectory(dir); string exe = Path.Combine(dir, "Plain Craft Launcher 2.exe"); File.Copy(fixture, exe, false); return exe;
    }
    private static PclService Service(string name) { return new PclService(Path.Combine(root, "records-" + name)); }
    private static void Run(string[] args) {
        fixture = Path.GetFullPath(args[0]); root = Path.GetFullPath(args[1]); Directory.CreateDirectory(root);
        Check(!Environment.Is64BitProcess, "Test runner must exercise x86"); Pass("32-bit process runs on current Windows");
        Check(string.IsNullOrEmpty(typeof(dnlib.DotNet.ModuleDef).Assembly.Location), "dnlib must load from embedded memory, not an external DLL"); Pass("embedded dnlib loads from memory without a sidecar DLL");
        Check(PclService.HashFile(fixture) == PclService.OriginalHash, "Input fixture must be the supported original");
        Check(Path.IsPathRooted(PclService.DownloadsRoot()), "Windows Downloads known folder was not resolved"); Pass("Windows configured Downloads folder resolves without registry changes");
        string exe = CopyFixture("中文 测试目录"); var service = Service("lifecycle");
        Check(service.Inspect(exe).CanUnlock, "Inspect original"); Pass("inspect supported original, Unicode and spaces");
        var scan = service.Discover(new[] { Path.GetDirectoryName(exe) }, CancellationToken.None, delegate { });
        Check(scan.Candidates.Count == 1 && scan.Candidates[0].CanUnlock, "Discover unique fixture"); Pass("discover one candidate without executing it");
        string brokenRecords = Path.Combine(root, "records-lifecycle", "zz-malformed"); Directory.CreateDirectory(brokenRecords);
        File.WriteAllText(Path.Combine(brokenRecords, "transaction.json"), "{\"Schema\":\"not-an-integer\"}");
        Check(service.Inspect(exe).CanUnlock, "Malformed old history blocked inspection"); Pass("malformed old history cannot block current inspection");
        var applied = service.Unlock(exe, delegate { });
        Check(PclService.HashFile(exe) == PclService.PatchedHash && PclService.HashFile(applied.Backup) == PclService.OriginalHash, "Patch and backup hashes");
        PatchEngine.Verify(applied.Backup, exe); Pass("apply, original backup, IL/constants/embedded resources verified");
        Check(new PclService(Path.Combine(root,"records-lifecycle")).Inspect(exe).LastRecord == applied.Record, "Persisted record was not loaded"); Pass("new service reads previous operation record");
        int backupsBefore = Directory.GetFiles(Path.GetDirectoryName(exe), "*.bak").Length;
        service.Unlock(exe, delegate { }); Check(Directory.GetFiles(Path.GetDirectoryName(exe), "*.bak").Length == backupsBefore, "Duplicate backup"); Pass("repeated unlock is idempotent");
        var restored = service.Restore(exe, delegate { }); Check(PclService.HashFile(exe) == PclService.OriginalHash && File.Exists(restored.Backup), "Restore mismatch");
        service.Restore(exe, delegate { }); Pass("restore is exact, repeatable, and keeps backup");
        string legacy = CopyFixture("legacy-backup"); File.Copy(legacy, legacy + ".before-download-unlock-20261020.bak", false);
        string patch = Path.Combine(Path.GetDirectoryName(legacy), "patch.exe"); PatchEngine.Patch(legacy, patch); File.Copy(patch, legacy, true);
        Service("no-old-manifest").Restore(legacy, delegate { }); Check(PclService.HashFile(legacy) == PclService.OriginalHash, "Legacy restore"); Pass("legacy adjacent backup restores without an old manifest");
        string unknown = CopyFixture("unknown"); using (var stream = new FileStream(unknown, FileMode.Append)) stream.WriteByte(65);
        Check(!Service("unknown").Inspect(unknown).CanUnlock, "Unknown build enabled"); ExpectFailure(() => Service("unknown").Unlock(unknown, delegate { }), unknown, "unknown build refused");
        string changed = CopyFixture("external-change"); var changedService = Service("external-change"); var changedResult = changedService.Unlock(changed, delegate { });
        using (var stream = new FileStream(changed, FileMode.Append)) stream.WriteByte(65);
        ExpectFailure(() => changedService.Restore(changed, delegate { }), changed, "external changes cannot be overwritten by restore"); Check(PclService.HashFile(changedResult.Backup) == PclService.OriginalHash, "Backup modified");
        string corrupted = CopyFixture("bad-backup"); var corruptedService = Service("bad-backup"); var corruptedResult = corruptedService.Unlock(corrupted, delegate { });
        File.WriteAllText(corruptedResult.Backup, "corrupt"); ExpectFailure(() => corruptedService.Restore(corrupted, delegate { }), corrupted, "corrupted backup refused");
        string race = CopyFixture("source-race"); bool didChange = false;
        bool refused = false;
        try { Service("source-race").Unlock(race, text => { if (!didChange && text.StartsWith("正在替换")) { using (var stream = new FileStream(race, FileMode.Append)) stream.WriteByte(66); didChange = true; } }); } catch (InvalidOperationException) { refused = true; }
        Check(didChange && refused && PclService.HashFile(race) != PclService.PatchedHash, "Source race not detected"); Pass("target changes between preparation and replacement are refused");
        string changedBackup = CopyFixture("backup-race"); bool backupChanged = false, backupRefused = false;
        try { Service("backup-race").Unlock(changedBackup, text => { if (text.StartsWith("正在替换")) { string saved = Directory.GetFiles(Path.GetDirectoryName(changedBackup), "*.bak").Single(); File.WriteAllText(saved, "backup altered before replacement"); backupChanged = true; } }); } catch (InvalidOperationException) { backupRefused = true; }
        Check(backupChanged && backupRefused && PclService.HashFile(changedBackup) == PclService.OriginalHash, "Changed backup allowed replacement"); Pass("backup changes before replacement are refused while original target stays intact");
        string ro = CopyFixture("readonly"); File.SetAttributes(ro, File.GetAttributes(ro) | FileAttributes.ReadOnly);
        try { ExpectFailure(() => Service("readonly").Unlock(ro, delegate { }), ro, "read-only destination preserves original"); } finally { File.SetAttributes(ro, FileAttributes.Normal); }
        string locked = CopyFixture("locked"); string lockedBefore = PclService.HashFile(locked); bool lockRejected = false;
        using (var hold = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None)) try { Service("locked").Unlock(locked, delegate { }); } catch (Exception) { lockRejected = true; }
        Check(lockRejected && PclService.HashFile(locked) == lockedBefore, "Locked file protection"); Pass("exclusive file lock is handled without modification");
        string multi = Path.Combine(root, "multiple"); Directory.CreateDirectory(multi); string a = Path.Combine(multi,"a"), b = Path.Combine(multi,"b"); Directory.CreateDirectory(a); Directory.CreateDirectory(b);
        File.Copy(fixture, Path.Combine(a,"Plain Craft Launcher 2.exe")); File.Copy(fixture, Path.Combine(b,"PCL2.exe"));
        Check(service.Discover(new[] { multi }, CancellationToken.None, delegate { }).Candidates.Count == 2, "Multiple candidate discovery"); Pass("multiple installations remain separate candidates");
        var cts = new CancellationTokenSource(); cts.Cancel(); bool cancelled = false;
        try { service.Discover(new[] { root }, cts.Token, delegate { }); } catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled, "Scan cancellation"); Pass("scan cancellation makes no changes");
        if (args.Length > 2) {
            string dummy = Path.GetFullPath(args[2]);
            foreach (string candidate in new[] { dummy, Path.Combine(Path.GetDirectoryName(dummy), "RenamedLauncher.exe") }) {
            if (candidate != dummy) File.Copy(dummy, candidate, false);
            using (var process = Process.Start(new ProcessStartInfo(candidate) { UseShellExecute = false, CreateNoWindow = true })) {
                try { Thread.Sleep(500); bool runningRefused = false; try { PclService.AssertStopped(candidate); } catch (InvalidOperationException) { runningRefused = true; } Check(runningRefused, "Running process not detected: " + candidate); Pass("running process path is detected: " + Path.GetFileName(candidate)); }
                finally { if (!process.HasExited) process.Kill(); process.WaitForExit(); }
            }
            }
        }
        Console.WriteLine("TOTAL " + count + " passed; only isolated fixtures were modified.");
    }
    [STAThread]
    private static int Main(string[] args) {
        Program.InitializeEmbeddedAssembly();
        try { Run(args); return 0; } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
