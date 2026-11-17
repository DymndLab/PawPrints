using System.Numerics;
namespace Blueprint.Rendering;

// Pointer coordinates must be screen coordinates. Reflow, zoom and scrolling
// change paper coordinates without the user moving their mouse.
sealed class LabelPlacementGesture(Vector2 startScreen, bool held)
{
    public bool Held { get; private set; } = held;

    public bool Update(Vector2 screen, bool down, bool clicked, bool hovered)
    {
        if (Held)
        {
            if (down)
                return false;
            Held = false;
            return Vector2.Distance(screen, startScreen) > 4;
        }
        return hovered && clicked;
    }
}
