using System.Numerics;
namespace Blueprint.Rendering;

static class PreviewNavigation
{

    public static float Zoom(float current, float wheel) => Math.Clamp(current * MathF.Pow(1.18f, Math.Clamp(wheel, -8, 8)), .5f, 6);

    public static Vector2 ZoomScroll(Vector2 scroll, Vector2 pointInImage, float ratio) => Vector2.Max(Vector2.Zero, scroll + pointInImage * (ratio - 1));

    public static Vector2 PanScroll(Vector2 startScroll, Vector2 startMouse, Vector2 mouse) => Vector2.Max(Vector2.Zero, startScroll - (mouse - startMouse));
}
