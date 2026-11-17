namespace Blueprint.Rendering;

static class GenerationAvailability
{

    public static string? Reason(bool hasCraft, bool busy, bool placing, bool hasViews) =>
        !hasCraft ? "Load a craft in the Vehicle Editor to generate a PawPrint." :
        busy ? "Working on the current print. Wait for it to finish, or cancel generation below." :
        placing ? "Finish or cancel the current placement before generating." :
        !hasViews ? "Choose a template or assign a view to a box before generating." : null;
}
