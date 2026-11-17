using System.Numerics;
namespace Blueprint.Rendering;

enum FlagnoteStage
{
    Start, End, Text, Ready
}

// Detached draft: neither endpoint picking nor text editing changes document
// history. The caller commits once, after both points and any custom text exist.
sealed class FlagnotePlacement(PartLabel label, bool custom, int width = Sheet.Width, int height = Sheet.Height)
{
    public PartLabel Label { get; private set; } = label;
    public FlagnoteStage Stage { get; private set; } = FlagnoteStage.Start;
    public bool Custom { get; } = custom;


    public void PickStart(Vector2 point)
    {
        if (Stage != FlagnoteStage.Start)
            throw new InvalidOperationException("Pick the leader start first.");
        Label = Label with
        {
            ManualPlacement = true,
            AttachmentPinned = true,
            Attachment = new(Math.Clamp(point.X, 0, width - 1), Math.Clamp(point.Y, 0, height - 1)),
            Socket = null
        };
        Stage = FlagnoteStage.End;
    }


    public PartLabel PreviewEnd(Vector2 point) => OnPage(Label.MoveTo(point.X, point.Y));


    public void PickEnd(Vector2 point)
    {
        if (Stage != FlagnoteStage.End)
            throw new InvalidOperationException("Pick the leader end after its start.");
        Label = PreviewEnd(point);
        Stage = Custom ? FlagnoteStage.Text : FlagnoteStage.Ready;
    }


    public bool SetText(string text)
    {
        if (!Custom || Stage is not (FlagnoteStage.Text or FlagnoteStage.Ready))
            return false;
        string clean = AnnotationText.Clean(text);
        if (clean.Length == 0)
            return false;
        Label = OnPage(Label with
        {
            Text = clean
        });
        Stage = FlagnoteStage.Ready;
        return true;
    }


    public void RetryEnd()
    {
        if (Stage == FlagnoteStage.Ready && !Custom)
            Stage = FlagnoteStage.End;
    }


    PartLabel OnPage(PartLabel item)
    {
        var bounds = AnnotationText.Bounds(item);
        return item with
        {
            X = Math.Clamp(item.X, 0, width - bounds.Width),
            Y = Math.Clamp(item.Y, 0, height - bounds.Height)
        };
    }
}
