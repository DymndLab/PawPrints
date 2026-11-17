using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Blueprint;

/// <summary>Windows' folder picker, on its own STA thread like the logo picker.</summary>
static class ExportFolderPicker
{
    public static Task<string?> Choose(IntPtr owner)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException();
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                if (!OperatingSystem.IsWindows())
                    throw new PlatformNotSupportedException();
                completion.SetResult(Show(owner));
            }
            catch (Exception error) { completion.SetException(error); }
        })
        {
            IsBackground = true,
            Name = "Paw Prints export folder picker"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    [SupportedOSPlatform("windows")]
    static string? Show(IntPtr owner)
    {
        var type = Type.GetTypeFromCLSID(new Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7"), true)!;
        var dialog = (IFileDialog)Activator.CreateInstance(type)!;
        IShellItem? item = null;
        try
        {
            dialog.GetOptions(out uint options);
            // PICKFOLDERS, FORCEFILESYSTEM, PATHMUSTEXIST, NOCHANGEDIR,
            // DONTADDTORECENT. Windows provides folder creation explicitly.
            dialog.SetOptions(options | 0x20 | 0x40 | 0x800 | 0x8 | 0x02000000);
            dialog.SetTitle("Choose where PawPrints saves your prints");
            dialog.SetOkButtonLabel("Use this folder");
            int result = dialog.Show(owner);
            if (result == unchecked((int)0x800704C7))
                return null; // User cancelled; retain the previous destination.
            Marshal.ThrowExceptionForHR(result);
            dialog.GetResult(out item);
            item.GetDisplayName(0x80058000, out IntPtr name); // SIGDN_FILESYSPATH
            try
            {
                return Marshal.PtrToStringUni(name);
            }
            finally { Marshal.FreeCoTaskMem(name); }
        }
        finally
        {
            if (item != null)
                Marshal.ReleaseComObject(item);
            Marshal.ReleaseComObject(dialog);
        }
    }

    // Declaration order matches the native IModalWindow/IFileDialog vtable.
    // Unused slots still belong here; removing them shifts the called methods.
    [ComImport, Guid("42F85136-DB7E-439C-85F1-E4075D135FC8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IFileDialog
    {
        [PreserveSig] int Show(IntPtr owner);
        void SetFileTypes(uint count, IntPtr filters);
        void SetFileTypeIndex(uint index);
        void GetFileTypeIndex(out uint index);
        void Advise(IntPtr events, out uint cookie);
        void Unadvise(uint cookie);
        void SetOptions(uint options);
        void GetOptions(out uint options);
        void SetDefaultFolder(IShellItem folder);
        void SetFolder(IShellItem folder);
        void GetFolder(out IShellItem folder);
        void GetCurrentSelection(out IShellItem item);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetFileName(out IntPtr name);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetResult(out IShellItem item);
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItem
    {
        void BindToHandler(IntPtr context, ref Guid handler, ref Guid iid, out IntPtr value);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint kind, out IntPtr name);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IShellItem other, uint hint, out int order);
    }
}
