using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;

namespace TrafficLimiter.App;

static class Platform
{
    public static bool IsElevated =>
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    const string TaskName = "TrafficLimiter";

    /// <summary>
    /// Autostart als Aufgabe mit höchsten Rechten (startet beim Anmelden ohne UAC-Abfrage).
    /// Das Anlegen braucht einmalig eine Administrator-Bestätigung. Liefert false, wenn sie abgelehnt wurde/fehlschlug.
    /// shortcut: Pfad der aktuellen EXE wird fest eingetragen, nach Verschieben der EXE Option aus-/einschalten.
    /// </summary>
    public static bool SetAutostart(bool enable)
    {
        string exe = Environment.ProcessPath!.Replace("'", "''");
        string dir = System.IO.Path.GetDirectoryName(Environment.ProcessPath)!.Replace("'", "''");
        string script = enable
            ? $@"$ErrorActionPreference='Stop'
$a=New-ScheduledTaskAction -Execute '{exe}' -Argument '--autostart' -WorkingDirectory '{dir}'
$u=[Security.Principal.WindowsIdentity]::GetCurrent().Name
$t=New-ScheduledTaskTrigger -AtLogOn -User $u
$s=New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero)
$p=New-ScheduledTaskPrincipal -UserId $u -LogonType Interactive -RunLevel Highest
Register-ScheduledTask -TaskName {TaskName} -Action $a -Trigger $t -Settings $s -Principal $p -Force | Out-Null"
            : $@"$ErrorActionPreference='Stop'
if (Get-ScheduledTask -TaskName {TaskName} -ErrorAction SilentlyContinue) {{ Unregister-ScheduledTask -TaskName {TaskName} -Confirm:$false }}";

        var psi = new ProcessStartInfo("powershell.exe",
            "-NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)))
        { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
        try
        {
            using var p = Process.Start(psi)!;
            p.WaitForExit();
            return p.ExitCode == 0;
        }
        catch (Win32Exception) { return false; } // UAC abgelehnt
    }
}
