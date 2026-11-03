using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace WinDock;

internal sealed record ShortcutDetails(string Target, string Arguments, string WorkingDirectory, bool Editable);

/// <summary>Reads and updates shell links without recreating their other Windows properties.</summary>
internal static class ShellShortcut
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, nint bindContext, out nint itemIdList, uint attributes, out uint actualAttributes);
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")] private class ShellLinkObject { }
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLink
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int size, nint findData, uint flags);
        void GetIDList(out nint list);
        void SetIDList(nint list);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int size);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int size);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string path);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int size);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short key);
        void SetHotkey(short key);
        void GetShowCmd(out int command);
        void SetShowCmd(int command);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int size, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(nint window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }

    internal static ShortcutDetails Read(string path)
    {
        if (!IsLink(path)) return new(path, "", "", Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase));
        object link = new ShellLinkObject();
        try
        {
            ((IPersistFile)link).Load(path, 0);
            var shell = (IShellLink)link;
            var target = new StringBuilder(32768); var args = new StringBuilder(32768); var work = new StringBuilder(32768);
            shell.GetPath(target, target.Capacity, 0, 4);
            shell.GetArguments(args, args.Capacity);
            shell.GetWorkingDirectory(work, work.Capacity);
            var targetPath = Environment.ExpandEnvironmentVariables(target.ToString());
            return new(targetPath, args.ToString(), work.ToString(),
                Path.IsPathFullyQualified(targetPath) && Path.GetExtension(targetPath).Equals(".exe", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException)
        { return new("", "", "", false); }
        finally { Marshal.ReleaseComObject(link); }
    }

    internal static void Write(string destination, string? template, ShortcutDetails details)
    {
        object link = new ShellLinkObject();
        try
        {
            if (template is not null) ((IPersistFile)link).Load(template, 0);
            var shell = (IShellLink)link;
            shell.SetPath(details.Target);
            shell.SetArguments(details.Arguments);
            shell.SetWorkingDirectory(details.WorkingDirectory);
            ((IPersistFile)link).Save(destination, true);
        }
        finally { Marshal.ReleaseComObject(link); }
    }

    internal static bool IsLink(string path) => Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase);

    internal static void WriteShellApplication(string destination, string applicationId)
    {
        nint pidl = 0;
        object link = new ShellLinkObject();
        try
        {
            Marshal.ThrowExceptionForHR(SHParseDisplayName("shell:AppsFolder\\" + applicationId, 0, out pidl, 0, out _));
            ((IShellLink)link).SetIDList(pidl);
            ((IPersistFile)link).Save(destination, true);
        }
        finally
        {
            if (pidl != 0) Marshal.FreeCoTaskMem(pidl);
            Marshal.ReleaseComObject(link);
        }
    }
}
