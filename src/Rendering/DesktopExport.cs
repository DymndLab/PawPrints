namespace Blueprint.Rendering;

static class DesktopExport
{
    public static byte[] Resize(byte[] rgb, PageSize source, PageSize target)
        => ImageResampling.ResizeRgb(rgb, source.Width, source.Height, target.Width, target.Height);
}
