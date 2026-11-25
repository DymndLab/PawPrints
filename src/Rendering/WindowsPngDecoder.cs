using System.Runtime.InteropServices;

namespace Blueprint.Rendering;

// Narrow WIC bridge: instantiate the Windows PNG decoder directly, never a
// third-party codec selected from the file name. All COM pointers stay local.
static unsafe class WindowsPngDecoder
{
    static readonly Guid DecoderClass = new("389ea17b-5078-4cde-b6ef-25c15175c751");
    static readonly Guid DecoderInterface = new("9edde9e7-8dee-47ea-99df-e6faf2ed44bf");
    static readonly Guid RgbaFormat = new("f5c7ad2d-6a8d-43dd-a7a8-a29935261ae9");

    public static LogoImage Decode(byte[] bytes)
    {
        var (width, height) = LogoPng.Validate(bytes);
        int initialized = CoInitializeEx(0, 0);
        // A caller may already be STA. Use that apartment without changing it.
        if (initialized < 0 && initialized != unchecked((int)0x80010106))
            Marshal.ThrowExceptionForHR(initialized);
        nint stream = 0, decoder = 0, frame = 0, converted = 0;
        try
        {
            stream = SHCreateMemStream(bytes, (uint)bytes.Length);
            if (stream == 0)
                throw new OutOfMemoryException("Cannot open the PNG stream.");
            Check(CoCreateInstance(in DecoderClass, 0, 1, in DecoderInterface, out decoder));
            // IWICBitmapDecoder slots, from wincodec.h (IUnknown occupies 0..2).
            Check(((delegate* unmanaged[Stdcall]<nint, nint, uint, int>)Slot(decoder, 4))(decoder, stream, 0));
            uint count = 0;
            Check(((delegate* unmanaged[Stdcall]<nint, uint*, int>)Slot(decoder, 12))(decoder, &count));
            if (count != 1)
                throw new InvalidDataException("Choose a still PNG logo.");
            Check(((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Slot(decoder, 13))(decoder, 0, &frame));
            uint actualWidth = 0, actualHeight = 0;
            Check(((delegate* unmanaged[Stdcall]<nint, uint*, uint*, int>)Slot(frame, 3))(frame, &actualWidth, &actualHeight));
            if (actualWidth != width || actualHeight != height)
                throw new InvalidDataException("PNG dimensions are inconsistent.");
            // Straight RGBA preserves original RGB codes for theme classification.
            Check(WICConvertBitmapSource(in RgbaFormat, frame, out converted));
            var rgba = new byte[checked(width * height * 4)];
            fixed (byte* pixels = rgba)
                Check(((delegate* unmanaged[Stdcall]<nint, nint, uint, uint, byte*, int>)Slot(converted, 7))
                    (converted, 0, (uint)(width * 4), (uint)rgba.Length, pixels));
            return new(width, height, rgba);
        }
        finally
        {
            Release(converted);
            Release(frame);
            Release(decoder);
            Release(stream);
            if (initialized >= 0)
                CoUninitialize();
        }
    }

    static void Check(int result)
    {
        if (result < 0)
            throw new InvalidDataException("Cannot decode this PNG logo.", Marshal.GetExceptionForHR(result));
    }

    static nint Slot(nint instance, int index) => (*(nint**)instance)[index];
    static void Release(nint instance)
    {
        if (instance != 0)
            ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(instance, 2))(instance);
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("ole32.dll", ExactSpelling = true)]
    static extern int CoInitializeEx(nint reserved, uint flags);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("ole32.dll", ExactSpelling = true)]
    static extern void CoUninitialize();
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("ole32.dll", ExactSpelling = true)]
    static extern int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("shlwapi.dll", ExactSpelling = true)]
    static extern nint SHCreateMemStream(byte[] bytes, uint length);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("windowscodecs.dll", ExactSpelling = true)]
    static extern int WICConvertBitmapSource(in Guid format, nint source, out nint converted);
}
