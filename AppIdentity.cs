using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace AimpSmtc;

internal static class AppIdentity
{
    public const string Aumid    = "NimiGames68.AimpSmtc";
    private const string AppName = "AIMP SMTC";

    public static bool IsPackaged
    {
        get
        {
            try { _ = Windows.ApplicationModel.Package.Current; return true; }
            catch { return false; } // throws when there's no package identity
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string aumid);

    public static void SetProcessAumid()
    {
        if (IsPackaged) return;

        try
        {
            int hr = SetCurrentProcessExplicitAppUserModelID(Aumid);
            Log($"SetProcessAumid hr={hr}");
        }
        catch (Exception ex) { Log($"SetProcessAumid failed: {ex}"); }
    }

    public static void EnsureStartMenuShortcut()
    {
        if (IsPackaged) return; // the MSIX package already registers the app

        try
        {
            string exe = Process.GetCurrentProcess().MainModule!.FileName;
            // SpecialFolder.Programs = .../Start Menu/Programs (not SpecialFolder.StartMenu,
            // which points one level up and is the wrong place for app shortcuts).
            string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            string lnkPath = Path.Combine(programs, $"{AppName}.lnk");

            var link = (IShellLinkW)new CShellLink();
            link.SetPath(exe);
            link.SetWorkingDirectory(Path.GetDirectoryName(exe) ?? "");
            link.SetIconLocation(exe, 0);
            link.SetDescription(AppName);

            var store = (IPropertyStore)link;
            var key = PKEY_AppUserModel_ID;
            var pv = MakeStringPropVariant(Aumid);
            try
            {
                store.SetValue(ref key, ref pv);
                store.Commit();
                Log("AUMID written to shortcut via IPropertyStore.");
            }
            finally
            {
                PropVariantClear(ref pv);
            }

            ((IPersistFile)link).Save(lnkPath, true);
            Log($"Shortcut + AUMID saved to: {lnkPath}");
        }
        catch (Exception ex) { Log($"EnsureStartMenuShortcut failed: {ex}"); }
    }

    public static void ShowToast(string title, string message)
    {
        try
        {
            string xml = $"""
                <toast>
                  <visual>
                    <binding template="ToastGeneric">
                      <text>{System.Security.SecurityElement.Escape(title)}</text>
                      <text>{System.Security.SecurityElement.Escape(message)}</text>
                    </binding>
                  </visual>
                </toast>
                """;
            var doc = new XmlDocument();
            doc.LoadXml(xml);

            // Packaged apps resolve their own identity automatically; an
            // unpackaged app needs the explicit AUMID to match its shortcut.
            var notifier = IsPackaged
                ? ToastNotificationManager.CreateToastNotifier()
                : ToastNotificationManager.CreateToastNotifier(Aumid);

            notifier.Show(new ToastNotification(doc));
        }
        catch (Exception ex) { Log($"ShowToast failed: {ex.Message}"); }
    }

    public static void Log(string msg)
    {
        try
        {
            string logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AimpSmtc", "error.log");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] {msg}\n");
        }
        catch { }
    }

    // COM interop: IShellLinkW + IPersistFile + IPropertyStore

    private static readonly PROPERTYKEY PKEY_AppUserModel_ID = new()
    {
        fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
        pid   = 5,
    };

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PROPVARIANT pvar);

    private const ushort VT_LPWSTR = 31;

    private static PROPVARIANT MakeStringPropVariant(string value)
    {
        return new PROPVARIANT
        {
            vt = VT_LPWSTR,
            p  = Marshal.StringToCoTaskMemUni(value), // freed by PropVariantClear
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY { public Guid fmtid; public uint pid; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPVARIANT
    {
        public ushort vt;
        public ushort wReserved1;
        public ushort wReserved2;
        public ushort wReserved3;
        public IntPtr p;
        public int p2;
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class CShellLink { }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short wHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int iShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport, Guid("0000010b-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string? pszFileName, bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile(out IntPtr ppszFileName);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint cProps);
        void GetAt(uint iProp, out PROPERTYKEY pkey);
        void GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
        void SetValue(ref PROPERTYKEY key, ref PROPVARIANT pv);
        void Commit();
    }
}
