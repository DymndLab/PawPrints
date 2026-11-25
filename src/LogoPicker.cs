using System.Runtime.InteropServices;

namespace Blueprint;

static class LogoPicker
{


    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct OpenFile
    {
        public int Size;
        public IntPtr Owner, Instance;
        public string? Filter, CustomFilter;
        public int MaxCustomFilter, FilterIndex;
        public IntPtr File;
        public int MaxFile;
        public string? FileTitle;
        public int MaxFileTitle;
        public string? InitialDirectory, Title;
        public int Flags;
        public short FileOffset, FileExtension;
        public string? DefaultExtension;
        public IntPtr CustomData, Hook;
        public string? Template;
        public IntPtr Reserved;
        public int Reserved2, FlagsEx;
    }


    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool GetOpenFileNameW(ref OpenFile data);


    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool GetSaveFileNameW(ref OpenFile data);

    [DllImport("comdlg32.dll")] static extern int CommDlgExtendedError();


    public static Task<string?> Choose(IntPtr owner, string folder) =>
        ChooseFile(owner, folder, "Choose a company logo", "Logo images (*.png)\0*.png\0\0", "png", save: false);

    public static Task<string?> ChooseProfile(IntPtr owner, bool save) =>
        ChooseFile(owner, "", save ? "Export PawPrints profile" : "Import PawPrints profile",
            "PawPrints profiles (*.pawprofile)\0*.pawprofile\0\0", "pawprofile", save);

    static Task<string?> ChooseFile(IntPtr owner, string folder, string title, string filter, string extension, bool save)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException();
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            IntPtr fileBuffer = IntPtr.Zero;
            try
            {
                fileBuffer = Marshal.AllocCoTaskMem(32768 * sizeof(char));
                Marshal.WriteInt16(fileBuffer, 0);
                var data = new OpenFile
                {
                    Size = Marshal.SizeOf<OpenFile>(), Owner = owner, Filter = filter, FilterIndex = 1,
                    File = fileBuffer, MaxFile = 32768, InitialDirectory = folder, Title = title, DefaultExtension = extension,
                    Flags = 0x80000 | (save ? 0 : 0x1000) | 0x800 | 0x8 | 0x2000000
                };
                if (save ? GetSaveFileNameW(ref data) : GetOpenFileNameW(ref data))
                    completion.SetResult(Marshal.PtrToStringUni(fileBuffer));
                else
                {
                    int error = CommDlgExtendedError();
                    if (error != 0)
                        throw new InvalidOperationException($"Logo picker failed ({error:X}).");
                    completion.SetResult(null);
                }
            }
            catch (Exception e) { completion.SetException(e); }
            finally
            {
                if (fileBuffer != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(fileBuffer);
            }
        })
        {
            IsBackground = true,
            Name = "PawPrints file picker"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
