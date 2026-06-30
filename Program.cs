using System;
using System.Threading;
using System.Windows.Forms;
using AimpSmtc;



AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    AppIdentity.Log($"CRASH: {e.ExceptionObject}");
};


const string MutexName = "AimpSmtc_SingleInstance_Mutex";
using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);

if (!createdNew)
{
    AppIdentity.SetProcessAumid();
    AppIdentity.ShowToast("AIMP SMTC", "Already running - check the system tray.");
    return;
}

try
{
    // Order matters here:
    // 1. The process AUMID must be set before any window or WinRT API touches the process.
    AppIdentity.SetProcessAumid();

    // 2. The shortcut (also carrying the AUMID) must exist before the SMTC
    //    is used for the first time, which happens inside TrayApp.
    AppIdentity.EnsureStartMenuShortcut();

    ApplicationConfiguration.Initialize();
    PlaybackLog.Log("AIMP SMTC started");
    Application.Run(new TrayApp());
    PlaybackLog.Log("AIMP SMTC exited");
}
catch (Exception ex)
{
    AppIdentity.Log($"Startup crash: {ex}");
    MessageBox.Show(
        $"AIMP SMTC failed to start:\n\n{ex.Message}\n\nDetails in %LOCALAPPDATA%\\AimpSmtc\\error.log",
        "AIMP SMTC - Error",
        MessageBoxButtons.OK,
        MessageBoxIcon.Error);
}
