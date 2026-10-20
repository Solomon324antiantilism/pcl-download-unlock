using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("PCL 下载恢复工具")]
[assembly: AssemblyProduct("PCL 下载恢复工具")]
[assembly: AssemblyDescription("离线检查、备份、恢复 PCL Minecraft 版本下载")]
[assembly: AssemblyVersion("1.0.1.0")]
[assembly: AssemblyFileVersion("1.0.1.0")]
[assembly: TargetFramework(".NETFramework,Version=v4.8")]

internal static class Program {
    internal static void InitializeEmbeddedAssembly() {
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs args) {
            if (new AssemblyName(args.Name).Name != "dnlib") return null;
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PclDownloadUnlock.dnlib.dll")) {
                if (stream == null) throw new FileNotFoundException("内置的检查组件缺失，请重新获取完整工具。");
                using (var buffer = new MemoryStream()) { stream.CopyTo(buffer); return Assembly.Load(buffer.ToArray()); }
            }
        };
    }
    [STAThread]
    private static void Main() {
        InitializeEmbeddedAssembly();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try {
            if (!HasRequiredFramework()) {
                MessageBox.Show("本工具需要 .NET Framework 4.8 或更高版本。\nWindows 10 1903 及之后版本和 Windows 11 已预装。\n请通过微软官方网站安装运行库后重试。", "缺少运行库", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            RunApplication();
        } catch (Exception ex) {
            MessageBox.Show("工具未能启动：\n" + ex.Message, "PCL 下载恢复工具", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
    private static bool HasRequiredFramework() {
        foreach (RegistryView view in new[] { RegistryView.Registry32, RegistryView.Registry64 }) {
            try {
                using (var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                using (var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full")) {
                    if (key != null && Convert.ToInt32(key.GetValue("Release", 0)) >= 528040) return true;
                }
            } catch (System.Security.SecurityException) { } catch (ArgumentException) { }
        }
        return false;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunApplication() { Application.Run(new MainForm(new PclService(null))); }
}
