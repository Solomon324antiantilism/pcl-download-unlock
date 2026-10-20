using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using dnlib.DotNet;
using FileAttributes = System.IO.FileAttributes;

internal sealed class PclInfo {
    public string Path { get; set; }
    public string Version { get; set; }
    public string Hash { get; set; }
    public string State { get; set; }
    public string Detail { get; set; }
    public bool CanUnlock { get; set; }
    public bool IsPatched { get; set; }
    public bool HasBackup { get; set; }
    public string LastRecord { get; set; }
}
internal sealed class ScanResult {
    public List<PclInfo> Candidates = new List<PclInfo>();
    public int Directories;
    public bool Truncated;
}
internal sealed class OperationResult {
    public string Message;
    public string Backup;
    public string Record;
    public string Warning;
}
internal sealed class Transaction {
    public int Schema = 1;
    public string Scope = "minecraft-version-download";
    public string CreatedAt, Exe, Backup, OriginalSha256, PatchedSha256, State, Error;
}
internal sealed class PclService {
    internal const string OriginalHash = "6b6e1260cb5a1d6a303855f1c60cab3d0bc1aebba7e40a964180188b271e7386";
    internal const string PatchedHash = "d8f095acbd7adde4e1e26c2a0fee0fb58e359e9630a842dac16d43df73cd62b8";
    private readonly string transactionRoot;
    private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
    private static readonly Regex CandidateName = new Regex(@"^(Plain Craft Launcher 2|PCL(?:2)?(?:[ ._-].*)?)\.exe$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> Ignored = new HashSet<string>(new[] { ".git", ".codex", "node_modules", ".minecraft", "libraries", "assets", "saves", "logs", "$recycle.bin", "windows", "appdata", "__pycache__" }, StringComparer.OrdinalIgnoreCase);
    internal PclService(string records) {
        transactionRoot = records ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCLDownloadUnlock", "Transactions");
    }
    internal static string HashFile(string path) {
        using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) return HashStream(file);
    }
    private static string HashStream(Stream file) {
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
    }
    private static string HashString(string value) {
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Utf8.GetBytes(value))).Replace("-", "");
    }
    internal static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static string Normalize(string path) {
        string full = System.IO.Path.GetFullPath(path);
        Require(!full.StartsWith(@"\\", StringComparison.Ordinal), "请先将 PCL 放到本机磁盘；本工具不修改网络共享中的程序。");
        string ancestor = full;
        while (!string.IsNullOrEmpty(ancestor)) {
            Require((File.GetAttributes(ancestor) & FileAttributes.ReparsePoint) == 0, "请选择真实文件或目录，本工具不跟随符号链接或目录联接。");
            ancestor = System.IO.Path.GetDirectoryName(ancestor);
        }
        return full;
    }
    internal PclInfo Inspect(string input) {
        var info = new PclInfo { Path = input, Version = "未识别", State = "无法读取" };
        try {
            info.Path = Normalize(input);
            info.Hash = HashFile(info.Path);
            using (var module = ModuleDefMD.Load(info.Path)) {
                if (module.Assembly != null) info.Version = module.Assembly.Version.ToString();
                Require(module.Assembly != null && module.Assembly.Name == "Plain Craft Launcher 2", "不是可识别的 PCL 启动器。");
            }
            if (info.Hash == OriginalHash || info.Hash == PatchedHash) {
                var json = new JavaScriptSerializer();
                var details = json.Deserialize<Dictionary<string, object>>(json.Serialize(PatchEngine.Inspect(info.Path)));
                bool restricted = (bool)details["restrictMcDownload"], real = (bool)details["realVersionLoader"], card = (bool)details["minecraftCardVisible"];
                if (info.Hash == OriginalHash) {
                    Require(restricted && !real && !card, "文件哈希与下载页结构不一致。");
                    info.CanUnlock = true; info.State = "可恢复下载"; info.Detail = "已识别支持的原始构建。恢复前会自动备份。";
                } else {
                    Require(!restricted && real && card, "修改后文件结构校验失败。");
                    info.IsPatched = true; info.State = "已恢复下载"; info.Detail = "已识别本工具对应的修改后构建。";
                    info.HasBackup = BackupPaths(info.Path).Any();
                }
            } else { info.State = "不支持此构建"; info.Detail = "文件 SHA256 不在已验证范围内，不会修改。版本号相同也可能是不同构建。"; }
        } catch (Exception ex) { info.Detail = FriendlyError(ex); }
        info.LastRecord = LastRecord(info.Path);
        return info;
    }
    private string LastRecord(string exe) {
        if (!Directory.Exists(transactionRoot)) return null;
        try {
            foreach (string dir in Directory.GetDirectories(transactionRoot).OrderByDescending(x => x, StringComparer.Ordinal).Take(500)) {
                string file = System.IO.Path.Combine(dir, "transaction.json");
                if (!File.Exists(file) || (File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0 || (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0 || new FileInfo(file).Length > 65536) continue;
                try {
                    var record = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(file, Utf8)) as Dictionary<string, object>;
                    object schema, recordedExe;
                    if (record != null && record.TryGetValue("Schema", out schema) && schema is int && (int)schema == 1 && record.TryGetValue("Exe", out recordedExe) && string.Equals(recordedExe as string, exe, StringComparison.OrdinalIgnoreCase)) return file;
                } catch (IOException) { } catch (UnauthorizedAccessException) { } catch (ArgumentException) { } catch (InvalidOperationException) { } catch (FormatException) { } catch (OverflowException) { } catch (System.Security.SecurityException) { }
            }
        } catch (IOException) { } catch (UnauthorizedAccessException) { } catch (System.Security.SecurityException) { }
        return null;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid folderId, uint flags, IntPtr token, out IntPtr path);
    internal static string DownloadsRoot() {
        IntPtr value = IntPtr.Zero;
        try {
            if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out value) == 0 && value != IntPtr.Zero) return Marshal.PtrToStringUni(value);
        } finally { if (value != IntPtr.Zero) Marshal.FreeCoTaskMem(value); }
        return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }
    internal static List<string> DefaultRoots() {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots = new List<string> { AppDomain.CurrentDomain.BaseDirectory, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), DownloadsRoot(), System.IO.Path.Combine(home, "Games") };
        foreach (string name in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" }) {
            string value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(value)) { roots.Add(System.IO.Path.Combine(value, "Desktop")); roots.Add(System.IO.Path.Combine(value, "Documents")); }
        }
        return roots.Where(x => !string.IsNullOrWhiteSpace(x) && Directory.Exists(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
    internal ScanResult Discover(IEnumerable<string> roots, CancellationToken cancel, Action<string> progress) {
        var result = new ScanResult(); var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<Tuple<string, int>>(roots.Reverse().Select(x => Tuple.Create(x, 6)));
        while (pending.Count > 0) {
            cancel.ThrowIfCancellationRequested(); var item = pending.Pop(); string dir = item.Item1; int remaining = item.Item2, previous;
            try {
                dir = Normalize(dir);
                if (seen.TryGetValue(dir, out previous) && previous >= remaining) continue;
                seen[dir] = remaining;
                if (result.Directories >= 8000) { result.Truncated = true; break; }
                result.Directories++;
                if (result.Directories % 50 == 1) progress("正在搜索，已检查 " + result.Directories + " 个目录…");
                foreach (string file in Directory.GetFiles(dir, "*.exe")) {
                    cancel.ThrowIfCancellationRequested();
                    if (CandidateName.IsMatch(System.IO.Path.GetFileName(file)) && (File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0) files.Add(file);
                }
                if (remaining > 0) foreach (string sub in Directory.GetDirectories(dir)) if (!Ignored.Contains(System.IO.Path.GetFileName(sub))) pending.Push(Tuple.Create(sub, remaining - 1));
            } catch (OperationCanceledException) { throw; } catch (IOException) { } catch (UnauthorizedAccessException) { } catch (System.Security.SecurityException) { } catch (InvalidOperationException) { }
        }
        foreach (string file in files.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) {
            cancel.ThrowIfCancellationRequested(); progress("正在检查：" + System.IO.Path.GetFileName(file)); result.Candidates.Add(Inspect(file));
        }
        return result;
    }
    internal static string FriendlyError(Exception ex) {
        if (ex is UnauthorizedAccessException) return "没有写入或读取此目录的权限。请选择有权限的目录；如确有需要，可手动以管理员身份重开工具。";
        if (ex is IOException) return "文件操作未完成：" + ex.Message + " 请检查文件占用、磁盘空间和目录权限。";
        return ex.Message;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(int access, bool inherit, int processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder path, ref int size);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    internal static void AssertStopped(string exe) {
        foreach (var process in Process.GetProcesses()) using (process) {
            string name;
            try { name = process.ProcessName; } catch (InvalidOperationException) { continue; } catch (System.ComponentModel.Win32Exception) { continue; }
            bool possibleTarget = name.StartsWith("PCL", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "Plain Craft Launcher 2", StringComparison.OrdinalIgnoreCase) || string.Equals(name, System.IO.Path.GetFileNameWithoutExtension(exe), StringComparison.OrdinalIgnoreCase);
            IntPtr handle = OpenProcess(0x1000, false, process.Id);
            if (handle == IntPtr.Zero) {
                if (!possibleTarget) continue;
                try { if (process.HasExited) continue; } catch (InvalidOperationException) { continue; } catch (System.ComponentModel.Win32Exception) { }
                throw new InvalidOperationException("有一个 PCL 进程无法检查。请关闭对应启动器后重试；工具不会强制结束进程。");
            }
            try {
                int size = 32768; var path = new StringBuilder(size);
                if (!QueryFullProcessImageName(handle, 0, path, ref size)) {
                    if (!possibleTarget) continue;
                    try { if (process.HasExited) continue; } catch (InvalidOperationException) { continue; } catch (System.ComponentModel.Win32Exception) { }
                    throw new InvalidOperationException("无法读取 PCL 进程路径，请关闭启动器后重试。");
                }
                Require(!string.Equals(System.IO.Path.GetFullPath(path.ToString()), System.IO.Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase), "目标 PCL 正在运行，请先关闭它再重试；不需要结束正在运行的 Minecraft 游戏。");
            } finally { CloseHandle(handle); }
        }
    }
    private static List<string> BackupPaths(string exe) {
        return Directory.GetFiles(System.IO.Path.GetDirectoryName(exe), System.IO.Path.GetFileName(exe) + ".before-download-unlock-*.bak")
            .Where(x => (File.GetAttributes(x) & FileAttributes.ReparsePoint) == 0).OrderByDescending(File.GetLastWriteTimeUtc).Take(1000).ToList();
    }
    private static string FindVerifiedBackup(string exe) {
        foreach (string file in BackupPaths(exe)) try { if (HashFile(file) == OriginalHash) return file; } catch (IOException) { } catch (UnauthorizedAccessException) { }
        throw new InvalidOperationException("未找到校验通过的原始备份。请保留同目录下的 .before-download-unlock-*.bak 文件；工具不会使用损坏或不匹配的备份。");
    }
    private static void WriteRecord(string file, Transaction record) { File.WriteAllText(file, new JavaScriptSerializer().Serialize(record), Utf8); }
    private static Mutex Acquire(string exe) {
        var mutex = new Mutex(false, @"Local\PCLDownloadUnlock-" + HashString(exe.ToUpperInvariant())); bool acquired;
        try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) { mutex.Dispose(); throw new InvalidOperationException("另一个工具窗口正在操作此 PCL，请等待它完成。"); }
        return mutex;
    }
    private static void Replace(string source, string target, string sourceHash, string expectedCurrent, string verifiedBackup) {
        using (var backupGuard = new FileStream(verifiedBackup, FileMode.Open, FileAccess.Read, FileShare.Read)) {
        Require(HashStream(backupGuard) == OriginalHash, "原始备份在替换前发生变化，已停止；目标文件未改动。");
        Require(HashFile(source) == sourceHash, "待写入文件校验失败，已停止。");
        string temp = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(target), ".pcl-download-unlock-" + Guid.NewGuid().ToString("N") + ".tmp");
        try {
            File.Copy(source, temp, false);
            Require(HashFile(temp) == sourceHash, "临时文件校验失败，已停止。");
            AssertStopped(target);
            Require(HashFile(target) == expectedCurrent, "目标文件在操作期间被更新或修改，已停止替换。");
            File.Replace(temp, target, null);
            Require(HashFile(target) == sourceHash, "替换后文件校验失败，请保留备份与操作记录。");
        } finally { if (File.Exists(temp)) try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
    }
    internal OperationResult Unlock(string input, Action<string> progress) {
        string exe = Normalize(input); using (var mutex = Acquire(exe)) try {
            AssertStopped(exe); var info = Inspect(exe);
            if (info.IsPatched) return new OperationResult { Message = "此 PCL 已恢复版本下载，无需重复修改。" };
            Require(info.CanUnlock && info.Hash == OriginalHash, "不支持此构建，未修改目标文件。" + Environment.NewLine + info.Detail);
            string id = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string dir = System.IO.Path.Combine(transactionRoot, id); Directory.CreateDirectory(dir);
            string original = System.IO.Path.Combine(dir, "original.exe"), patched = System.IO.Path.Combine(dir, "patched.exe"), manifest = System.IO.Path.Combine(dir, "transaction.json");
            var record = new Transaction { CreatedAt = DateTimeOffset.Now.ToString("o"), Exe = exe, Backup = exe + ".before-download-unlock-" + id + ".bak", OriginalSha256 = OriginalHash, PatchedSha256 = PatchedHash, State = "preparing" };
            WriteRecord(manifest, record);
            try {
                progress("正在生成并验证修改副本…"); File.Copy(exe, original, false); Require(HashFile(original) == OriginalHash, "目标文件在准备期间发生变化，已停止。");
                PatchEngine.Patch(original, patched); Require(HashFile(patched) == PatchedHash, "生成文件与已验证产物不一致，已停止。");
                record.State = "prepared"; WriteRecord(manifest, record);
                progress("正在核对目标并创建原始备份…"); AssertStopped(exe); Require(HashFile(exe) == OriginalHash, "目标文件在准备期间被修改，已停止。");
                File.Copy(exe, record.Backup, false); Require(HashFile(record.Backup) == OriginalHash, "备份校验失败，已停止。");
                record.State = "backed-up"; WriteRecord(manifest, record);
                progress("正在替换并校验文件，请稍候…"); Replace(patched, exe, PatchedHash, OriginalHash, record.Backup);
                record.State = "applied";
                var result = new OperationResult { Message = "Minecraft 版本下载入口已恢复。文件与备份均已校验。", Backup = record.Backup, Record = manifest };
                try { WriteRecord(manifest, record); } catch (Exception ex) { result.Warning = "文件已修改，但操作记录未能更新：" + ex.Message; }
                return result;
            } catch (Exception ex) { record.State = "failed"; record.Error = ex.Message; try { WriteRecord(manifest, record); } catch { } throw; }
        } finally { mutex.ReleaseMutex(); }
    }
    internal OperationResult Restore(string input, Action<string> progress) {
        string exe = Normalize(input); using (var mutex = Acquire(exe)) try {
            AssertStopped(exe); string current = HashFile(exe);
            if (current == OriginalHash) return new OperationResult { Message = "此 PCL 已是原始文件，无需重复还原。" };
            Require(current == PatchedHash, "当前程序已被更新或另行修改，不能用旧备份覆盖。目标文件未改动。");
            progress("正在寻找并校验原始备份…"); string backup = FindVerifiedBackup(exe);
            string dir = System.IO.Path.Combine(transactionRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-restore-" + Guid.NewGuid().ToString("N").Substring(0,8)); Directory.CreateDirectory(dir);
            string manifest = System.IO.Path.Combine(dir, "transaction.json");
            var record = new Transaction { CreatedAt = DateTimeOffset.Now.ToString("o"), Exe = exe, Backup = backup, OriginalSha256 = OriginalHash, PatchedSha256 = PatchedHash, State = "restoring" }; WriteRecord(manifest, record);
            try {
                progress("正在还原并校验文件，请稍候…"); Replace(backup, exe, OriginalHash, PatchedHash, backup); record.State = "restored";
                var result = new OperationResult { Message = "已还原原始 PCL，备份文件保留。", Backup = backup, Record = manifest };
                try { WriteRecord(manifest, record); } catch (Exception ex) { result.Warning = "已还原，但操作记录未能更新：" + ex.Message; }
                return result;
            } catch (Exception ex) { record.State = "failed"; record.Error = ex.Message; try { WriteRecord(manifest, record); } catch { } throw; }
        } finally { mutex.ReleaseMutex(); }
    }
}
