using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Strazio.Infrastructure.Windows;

// Native Windows notification APIs avoid an external Windows App SDK runtime.
// A Start Menu shortcut supplies the application identity for an unpackaged desktop EXE.
internal static class NativeToast
{
    private const string AppId = "Strazio.Archivio10eLotto";
    public static void Show(string message)
    {
        var executable = Environment.ProcessPath!;
        if (!Path.GetFileName(executable).Equals("strazio.exe", StringComparison.OrdinalIgnoreCase)) return;
        RegisterIdentity(executable);
        var document = new XmlDocument();
        document.LoadXml($"<toast><visual><binding template=\"ToastGeneric\"><text>Strazio</text><text>{SecurityElement.Escape(message)}</text></binding></visual></toast>");
        ToastNotificationManager.CreateToastNotifier(AppId).Show(new ToastNotification(document));
    }

    private static void RegisterIdentity(string executable)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\AppUserModelId\" + AppId))
        {
            key.SetValue("DisplayName", "Strazio");
            key.SetValue("IconUri", executable);
        }
        Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(AppId));
        var shortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Strazio.lnk");
        var shell = (IShellLinkW)new ShellLink();
        try
        {
            shell.SetPath(executable);
            shell.SetWorkingDirectory(Path.GetDirectoryName(executable)!);
            shell.SetDescription("Strazio — Archivio 10eLotto");
            var store = (IPropertyStore)shell;
            var key = new PropertyKey { FormatId = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), PropertyId = 5 };
            var value = new PropVariant { Type = 31, Pointer = Marshal.StringToCoTaskMemUni(AppId) };
            try { Marshal.ThrowExceptionForHR(store.SetValue(ref key, ref value)); Marshal.ThrowExceptionForHR(store.Commit()); }
            finally { Marshal.FreeCoTaskMem(value.Pointer); }
            ((IPersistFile)shell).Save(shortcut, true);
        }
        finally { Marshal.FinalReleaseComObject(shell); }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;
    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey { public Guid FormatId; public uint PropertyId; }
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Pointer;
    }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath(IntPtr file, int maxPath, IntPtr data, uint flags);
        void GetIDList(out IntPtr id);
        void SetIDList(IntPtr id);
        void GetDescription(IntPtr text, int length);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetWorkingDirectory(IntPtr text, int length);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments(IntPtr text, int length);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int show);
        void SetShowCmd(int show);
        void GetIconLocation(IntPtr path, int length, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}
