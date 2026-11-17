namespace Blueprint.Rendering;

static class AnnotationShortcuts
{


    // Values are editor commands, not native keys: 1 delete, 2 undo, 3 redo, 4 flip reference.
    public static int Match(bool delete, bool z, bool y, int modifiers, bool focused, bool typing, bool f = false)
    {
        if (!focused || typing)
            return 0;
        modifiers &= 15; // Caps/Num Lock do not change an editing shortcut.
        return delete && modifiers == 0 ? 1 : z && modifiers == 2 ? 2 : y && modifiers == 2 ? 3 : f && modifiers == 0 ? 4 : 0;
    }
}
