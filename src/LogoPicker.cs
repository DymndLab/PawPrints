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


    [DllImport("comdlg32.dll")] static extern int CommDlgExtendedError();


    public static Task<string?> Choose(IntPtr owner, string folder)
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
                var data = new OpenFile { Size = Marshal.SizeOf<OpenFile>(), Owner = owner, Filter = "Logo images (*.png)\0*.png\0\0", FilterIndex = 1, File = fileBuffer, MaxFile = 32768, InitialDirectory = folder, Title = "Choose a company logo", Flags = 0x80000 | 0x1000 | 0x800 | 0x8 | 0x2000000 };
                if (GetOpenFileNameW(ref data))
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
            Name = "Paw Prints logo picker"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
