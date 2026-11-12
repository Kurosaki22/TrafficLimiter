using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace TrafficLimiter.Core;

/// <summary>
/// Makes the app a true single file: WinDivert.dll and WinDivert64.sys are embedded in this assembly and
/// extracted to %LocalAppData%\TrafficLimiter\native\&lt;version&gt;. WinDivert.dll loads the driver from its own
/// folder, so both files must live side by side.
/// </summary>
static class NativeLoader
{
    const string Version = "2.2.2";   // bump together with the files in native\
    static readonly string[] Files = ["WinDivert.dll", "WinDivert64.sys"];

    [ModuleInitializer]
    internal static void Init() => NativeLibrary.SetDllImportResolver(typeof(NativeLoader).Assembly, Resolve);

    static IntPtr Resolve(string name, Assembly asm, DllImportSearchPath? path)
    {
        if (name != "WinDivert.dll") return IntPtr.Zero;
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TrafficLimiter", "native", Version);
            Directory.CreateDirectory(dir);
            foreach (var f in Files) Extract(asm, f, Path.Combine(dir, f));
            return NativeLibrary.TryLoad(Path.Combine(dir, "WinDivert.dll"), out var h) ? h : IntPtr.Zero;
        }
        catch (IOException) { return IntPtr.Zero; }
        catch (UnauthorizedAccessException) { return IntPtr.Zero; }
    }

    static void Extract(Assembly asm, string resource, string target)
    {
        using var src = asm.GetManifestResourceStream(resource)
            ?? throw new FileNotFoundException("Embedded resource missing: " + resource);
        if (File.Exists(target) && new FileInfo(target).Length == src.Length) return; // already extracted
        using var dst = File.Create(target);
        src.CopyTo(dst);
    }
}
