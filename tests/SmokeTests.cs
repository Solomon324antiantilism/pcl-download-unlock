using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Diagnostics;
using System.Windows.Forms;

internal static class SmokeTests {
    private static int passed;
    private static void Assert(bool value, string label) { if (!value) throw new Exception(label); passed++; Console.WriteLine("PASS " + label); }
    [STAThread]
    private static int Main(string[] args) {
        Program.InitializeEmbeddedAssembly();
        try { Run(args); return 0; } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void Run(string[] args) {
        string root = Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
        Assert(!Environment.Is64BitProcess, "x86 smoke test process");
        Assert(string.IsNullOrEmpty(typeof(dnlib.DotNet.ModuleDef).Assembly.Location), "embedded dnlib from memory");
        Assert(Path.IsPathRooted(PclService.DownloadsRoot()), "Windows Downloads known folder");
        string fake = Path.Combine(root, "PCL.exe"); File.WriteAllText(fake, "not a launcher");
        string before = PclService.HashFile(fake), records = Path.Combine(root, "records"), badRecord = Path.Combine(records, "zz-bad"); Directory.CreateDirectory(badRecord);
        File.WriteAllText(Path.Combine(badRecord, "transaction.json"), "{\"Schema\":\"invalid\"}");
        var service = new PclService(records); Assert(!service.Inspect(fake).CanUnlock, "unknown file and malformed history are tolerated");
        bool rejected = false; try { service.Unlock(fake, delegate { }); } catch (InvalidOperationException) { rejected = true; }
        Assert(rejected && PclService.HashFile(fake) == before, "unknown file is never modified");
        Assert(service.Inspect("\0").State == "无法读取", "invalid path does not escape inspection");
        Assert(service.Discover(new[] { root }, CancellationToken.None, delegate { }).Candidates.Count == 1, "bounded discovery does not execute candidates");
        var cts = new CancellationTokenSource(); cts.Cancel(); bool cancelled = false;
        try { service.Discover(new[] { root }, cts.Token, delegate { }); } catch (OperationCanceledException) { cancelled = true; }
        Assert(cancelled, "scan cancellation");
        using (var form = new MainForm(service)) {
            var bind = typeof(MainForm).GetMethod("SetCandidates", BindingFlags.Instance | BindingFlags.NonPublic);
            bind.Invoke(form, new object[] { new List<PclInfo> { new PclInfo { Path = fake, CanUnlock = true }, new PclInfo { Path = fake + "2", CanUnlock = true } }, null });
            var buttons = form.Controls.Find("UnlockButton", true).Concat(form.Controls.Find("RestoreButton", true)).ToArray();
            Assert(buttons.Length == 2 && buttons.All(b => !b.Enabled), "multiple candidates require explicit UI selection");
        }
        if (args.Length > 1) {
            using (var process = Process.Start(new ProcessStartInfo(args[1]) { UseShellExecute = false, CreateNoWindow = true })) {
                try { Thread.Sleep(300); bool runningRejected = false; try { PclService.AssertStopped(args[1]); } catch (InvalidOperationException) { runningRejected = true; } Assert(runningRejected, "renamed running launcher is detected by its image path"); }
                finally { if (!process.HasExited) process.Kill(); process.WaitForExit(); }
            }
        }
        Console.WriteLine("TOTAL " + passed + " fixture-free smoke checks passed.");
    }
}
