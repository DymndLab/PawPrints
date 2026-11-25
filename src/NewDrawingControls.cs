using Blueprint.Rendering;
using Blueprint.Storage;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace Blueprint;

public sealed partial class BlueprintMod
{
    enum NewDrawingRoute { Blank, Arrange, Template }

    sealed class NewDrawingDraft(VehicleEditor editor, TitlePreferences preferences)
    {
        readonly NewDrawingDefaults defaults = preferences.Drawing ?? NewDrawingDefaults.Initial;
        public VehicleEditor Editor { get; } = editor;
        public string Craft { get; } = editor.EditingSpace.Id;
        public readonly ImInputString Title = Input(editor.EditingSpace.Id, 256);
        public readonly ImInputString Author = Input(preferences.Data.Creator, 256);
        public readonly ImInputString Initials = Input(preferences.Data.CreatorInitials, 48);
        public readonly ImInputString Company = Input(preferences.Data.Company, 256);
        public readonly OutputSelection Output = DraftOutput(preferences);
        public readonly uint WearSeed = WearPlacement.NewSeed();
        public int Angle = preferences.ProjectionAngle;
        public bool ShowTitle = preferences.TitleBlockVisible;
        public int Theme = (preferences.Drawing ?? NewDrawingDefaults.Initial).Theme;
        public int Wear = (preferences.Drawing ?? NewDrawingDefaults.Initial).Wear;
        public bool Graph = (preferences.Drawing ?? NewDrawingDefaults.Initial).Grid;
        public bool Borders = (preferences.Drawing ?? NewDrawingDefaults.Initial).Zones;
        public int Handwriting = Array.IndexOf(InitialsFont.Names, InitialsFont.Normalize(preferences.Data.InitialsStyle));
        public int InitialsSize = preferences.Data.CreatorInitialsSize;
        public string? SampleText;
        public (int Width, int Height) SampleSize;
        public byte[][] InitialsSamples = [];
        public LogoImage? Logo;
        public LogoImage? Thumbnail;
        public string LogoName = preferences.LogoFile;

        public string DrawingTitle => string.IsNullOrWhiteSpace(Title.ToString()) ? Craft : Title.ToString();

        public TitleBlockData TitleData => preferences.Data with
        {
            Title = DrawingTitle,
            Creator = Author.ToString(),
            CreatorInitials = Initials.ToString(),
            Company = Company.ToString(),
            InitialsStyle = InitialsFont.Names[Handwriting],
            CreatorInitialsSize = InitialsSize,
            Logo = Logo,
            DrawingDate = CompanyTitleBlock.LocalDate(),
            ApprovalDate = CompanyTitleBlock.LocalDate()
        };

        public NewDrawingDefaults Paper => defaults with
        {
            Output = Output.Output, Width = Output.Size.Width, Height = Output.Size.Height,
            Theme = Theme, Wear = Wear, Grid = Graph, Zones = Borders
        };

        public PrintOptions Options => new(View: 0, Theme: Theme, Grid: Graph, Archive: Wear > 0, Emphasis: 10,
            Layout: 2, VisibleViews: 15, DrawingSize: 100, Orientation: 3, Brand: 2, TitleBlock: TitleData, Zones: Borders,
            CustomWidth: Output.Size.Width, CustomHeight: Output.Size.Height, WearLevel: Wear,
            OutputTarget: Output.Output, FreeLayout: true, TitleBlockVisible: ShowTitle, ProjectionAngle: Angle, Boxes: new(), WearSeed: WearSeed);

        static ImInputString Input(string value, int capacity)
        {
            var input = new ImInputString(capacity);
            input.SetValue(value.AsSpan());
            return input;
        }

        static OutputSelection DraftOutput(TitlePreferences settings)
        {
            var paper = settings.Drawing ?? NewDrawingDefaults.Initial;
            return new(paper.Output, new(paper.Width, paper.Height));
        }

        public void SetLogo(LogoImage? image, string name)
        {
            Logo = image;
            LogoName = name;
            Thumbnail = null;
            if (image == null) return;
            float scale = Math.Min(1, 42f / Math.Max(image.Width, image.Height));
            int width = Math.Max(1, (int)MathF.Round(image.Width * scale));
            int height = Math.Max(1, (int)MathF.Round(image.Height * scale));
            Thumbnail = new(width, height, ImageResampling.ResizeRgba(image.Rgba, image.Width, image.Height, width, height));
        }
    }

    static NewDrawingDraft? newDrawing;
    static bool setupGridOpened;

    static void BeginDrawingDraft(VehicleEditor editor)
    {
        DiscardDrawingDraft();
        logoMessage = "";
        var draft = newDrawing = new(editor, profileDefaults);
        if (draft.LogoName.Length > 0)
            LoadLogo(Path.Combine(logoFolder, draft.LogoName), false, draft);
    }

    static void DiscardDrawingDraft()
    {
        newDrawing = null;
        setupGridOpened = false;
        setupLayout = null;
        ClearSetupGesture();
        ResetProfileTransfer();
    }

    static void DrawNewDrawingChoices(VehicleEditor editor)
    {
        var draft = newDrawing!;
        float width = Math.Min(900, ImGui.GetContentRegionAvail().X);
        DrawNewDrawingSettings(draft, width);
        DrawNewDrawingPreferenceActions(draft);
        ImGui.Separator();
        ImGui.Spacing();
        if (DrawNewDrawingRoutes(width) is { } route) StartDrawingRoute(editor, draft, route);
        DrawProfileDialogs(editor);
        DrawSetupGridWarning();
    }

    static void DrawNewDrawingSettings(NewDrawingDraft draft, float width)
    {
        float labelWidth = ImGui.CalcTextSize("Landscape"u8).X;
        bool sideBySide = width >= Math.Max(525, labelWidth * 6f);
        int columns = sideBySide ? 3 : 1;
        var flags = ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoSavedSettings;
        if (sideBySide) flags |= ImGuiTableFlags.BordersInnerV;
        if (!ImGui.BeginTable("New drawing settings"u8, columns, flags, new float2(width, 0)))
            return;
        try
        {
            if (sideBySide)
            {
                ImGui.TableSetupColumn("Sheet Setup", ImGuiTableColumnFlags.WidthStretch, 1);
                ImGui.TableSetupColumn("Paper & Wear", ImGuiTableColumnFlags.WidthStretch, .84f);
                ImGui.TableSetupColumn("Drafter Details", ImGuiTableColumnFlags.WidthStretch, 1.28f);
            }

            ImGui.TableNextColumn();
            ImGui.Text("Sheet Setup");
            DrawOutputChoices(draft.Output, stackedLabels: true);
            DrawSetupProjection(draft, stackedLabels: true);

            ImGui.TableNextColumn();
            ImGui.Text("Paper & Wear");
            DrawPaperSettings(draft, stackedLabels: true);

            ImGui.TableNextColumn();
            ImGui.Text("Drafter Details");
            ImGui.PushItemWidth(SettingsInputWidth("Drawing title"));
            try
            {
                SetupInput("Drawing title", draft.Title);
                SetupInput("Drawn by", draft.Author);
                SetupInput("Company name", draft.Company);
                DrawLogoSelection(draft);
                DrawLogoThumbnail(draft.Thumbnail);
                SetupInput("Initials", draft.Initials);
                DrawInitialsSamples(draft);
            }
            finally { ImGui.PopItemWidth(); }
        }
        finally { ImGui.EndTable(); }
    }

    static string SetupLabel(string label, bool stacked)
    {
        if (!stacked) return label;
        ImGui.Text(label);
        ImGui.SetNextItemWidth(-1);
        return "##" + label;
    }

    static void DrawInitialsSamples(NewDrawingDraft draft)
    {
        float gap = ImGui.GetStyle().ItemSpacing.X;
        float width = Math.Min(270, ImGui.GetContentRegionAvail().X);
        var size = new float2(MathF.Floor((width - 2 * gap) / 3), MathF.Ceiling(Math.Max(32, ImGui.GetFontSize() * 2.4f)));
        int inkWidth = (int)size.X - 8;
        int inkHeight = (int)size.Y - 6;
        if (inkWidth < 4) return;

        string text = AnnotationText.Clean(draft.Initials.ToString(), 16);
        if (string.IsNullOrWhiteSpace(text)) text = "XYZ";
        if (draft.SampleText != text || draft.SampleSize != (inkWidth, inkHeight))
        {
            draft.SampleText = text;
            draft.SampleSize = (inkWidth, inkHeight);
            try
            {
                draft.InitialsSamples = InitialsFont.Names
                    .Select(face => InitialsFont.SampleInk(text, face, inkWidth, inkHeight)).ToArray();
            }
            catch (Exception e)
            {
                draft.InitialsSamples = [];
                PrintDiagnostics.Error("setup.initials-preview", e);
            }
        }
        for (int i = 0; i < draft.InitialsSamples.Length; i++)
        {
            if (i % 3 != 0) ImGui.SameLine();
            var at = ImGui.GetCursorScreenPos();
            string face = InitialsFont.Names[i];
            ImGui.PushStyleColor(ImGuiCol.Text, 0u);
            ImGui.PushStyleColor(ImGuiCol.Button, 0u);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, 0u);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, 0u);
            ImGui.PushStyleColor(ImGuiCol.NavCursor, 0u);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0f);
            try
            {
                if (ImGui.Button(face, size))
                    draft.Handwriting = i;
            }
            finally
            {
                ImGui.PopStyleVar();
                ImGui.PopStyleColor(5);
            }

            bool selected = i == draft.Handwriting;
            bool hovered = ImGui.IsItemHovered();
            bool keyboardFocus = ImGui.GetIO().NavVisible && ImGui.IsItemFocused();
            uint outline = selected ? 0xFFF5CAA0u : keyboardFocus ? 0xFFD3BCA7u : 0xFF58514Au;
            var draw = ImGui.GetWindowDrawList();
            draw.AddRectFilled(at, at + size, hovered ? 0xFF322A23u : 0xFF251F1Au, 4);
            draw.AddRect(at, at + size, outline, 4, ImDrawFlags.None, selected ? 1.5f : 1);
            var origin = new float2(MathF.Round(at.X + (size.X - inkWidth) * .5f),
                MathF.Round(at.Y + (size.Y - inkHeight) * .5f));
            DrawSampleInk(draft.InitialsSamples[i], inkWidth, inkHeight, origin, 0x00EFD8C2u);
            if (hovered) PrintUiText.Tooltip(face);
        }
    }

    static void DrawSampleInk(byte[] coverage, int width, int height, float2 origin, uint color)
    {
        var draw = ImGui.GetWindowDrawList();
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width;)
        {
            int first = x;
            uint alpha = coverage[y * width + x++];
            while (x < width && coverage[y * width + x] == alpha) x++;
            if (alpha == 0) continue;
            draw.AddRectFilled(origin + new float2(first, y), origin + new float2(x, y + 1), color | alpha << 24);
        }
    }

    static float SettingsInputWidth(string longestLabel) =>
        Math.Clamp(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(longestLabel).X - ImGui.GetStyle().ItemInnerSpacing.X - 2, 60, 420);

    static void DrawNewDrawingPreferenceActions(NewDrawingDraft draft)
    {
        if (ImGui.Button("Save as Defaults")) SaveDrawingDefaults(draft);
        ContinueSetupRow("Export Preferences");
        if (ImGui.Button("Export Preferences")) OpenProfileDialog(ProfileAction.Export);
        ContinueSetupRow("Import Preferences");
        if (ImGui.Button("Import Preferences")) OpenProfileDialog(ProfileAction.Import);
        if (profileMessage != null) PrintUiText.Wrapped(profileMessage);
    }

    static void ContinueSetupRow(string next)
    {
        ImGui.SameLine();
        if (ImGui.GetContentRegionAvail().X < ImGui.CalcTextSize(next).X + 2 * ImGui.GetStyle().FramePadding.X)
            ImGui.NewLine();
    }

    static NewDrawingRoute? DrawNewDrawingRoutes(float width)
    {
        float gap = ImGui.GetStyle().ItemSpacing.X;
        float label = ImGui.CalcTextSize("From template"u8).X;
        bool sideBySide = (width - 2 * gap) / 3 >= label + 24;
        float cardWidth = sideBySide ? (width - 2 * gap) / 3 : width;
        bool blank = DrawNewDrawingRoute(NewDrawingRoute.Blank, "Blank drawing", cardWidth);
        if (sideBySide) ImGui.SameLine();
        bool arrange = DrawNewDrawingRoute(NewDrawingRoute.Arrange, "Arrange views", cardWidth);
        if (sideBySide) ImGui.SameLine();
        bool template = DrawNewDrawingRoute(NewDrawingRoute.Template, "From template", cardWidth);

        if (blank) return NewDrawingRoute.Blank;
        if (arrange) return NewDrawingRoute.Arrange;
        if (template) return NewDrawingRoute.Template;
        return null;
    }

    static bool DrawNewDrawingRoute(NewDrawingRoute route, string title, float width)
    {
        float font = ImGui.GetFontSize();
        var size = new float2(width, Math.Max(106, font * 6.8f));
        var start = ImGui.GetCursorScreenPos();
        ImGui.PushStyleColor(ImGuiCol.Text, 0u);
        bool click;
        try { click = ImGui.Button(title, size); }
        finally { ImGui.PopStyleColor(); }
        bool hovered = ImGui.IsItemHovered(), pressed = ImGui.IsItemActive();
        var draw = ImGui.GetWindowDrawList();
        uint border = pressed || hovered ? 0xFFF5CAA0u : 0xFF58514Au;
        uint fill = pressed ? 0xFF44301Fu : hovered ? 0xFF302922u : 0xFF221E1Bu;
        draw.AddRectFilled(start, start + size, fill, 3);
        draw.AddRect(start, start + size, border, 3, ImDrawFlags.None, pressed ? 2 : 1);
        if (ImGui.IsItemFocused() && ImGui.GetIO().NavVisible)
            draw.AddRect(start + new float2(4), start + size - new float2(4), 0xFFF5CAA0u, 2);

        var titleSize = ImGui.CalcTextSize(title);
        draw.AddText(start + new float2((width - titleSize.X) / 2, size.Y - titleSize.Y - 9), 0xFFF2E9E0u, title);
        var center = start + new float2(width / 2, (size.Y - titleSize.Y - 12) / 2);
        float iconWidth = Math.Min(width - 28, 117);
        DrawNewDrawingIllustration(route, center, iconWidth, Math.Min(56, size.Y - titleSize.Y - 24));
        if (hovered && !click) PrintUiText.Tooltip("Click to continue");
        return click;
    }

    static void DrawNewDrawingIllustration(NewDrawingRoute route, float2 center, float width, float height)
    {
        var draw = ImGui.GetWindowDrawList();
        const uint paper = 0xFF5B3620, edge = 0xFFAD8870, ink = 0xFFF2D9C0;
        var size = new float2(width * (route == NewDrawingRoute.Template ? .82f : 1), height);
        var page = center - size * .5f;
        if (route == NewDrawingRoute.Template)
        {
            page += new float2(-5, 4);
            var behind = page + new float2(12, -9);
            draw.AddRectFilled(behind, behind + size, 0xFF472C1Eu);
            draw.AddRect(behind, behind + size, edge);
        }
        draw.AddRectFilled(page, page + size, paper);
        draw.AddRect(page, page + size, edge);
        var inner = page + new float2(5);
        var end = page + size - new float2(5);
        draw.AddRect(inner, end, edge);

        if (route == NewDrawingRoute.Arrange)
        {
            var mid = (inner + end) * .5f;
            draw.AddLine(new(mid.X, inner.Y), new(mid.X, end.Y), edge);
            draw.AddLine(new(inner.X, mid.Y), new(end.X, mid.Y), edge);
            DrawTemplateView(page + size * new float2(.26f, .28f), height * .12f, 3, ink);
            DrawTemplateView(page + size * new float2(.26f, .70f), height * .16f, 1, ink);
            DrawTemplateView(page + size * new float2(.73f, .70f), height * .16f, 2, ink);
            draw.AddRect(inner + new float2(2, size.Y / 2), mid + new float2(-2, size.Y / 2 - 7), ink);
        }
        else
        {
            draw.AddRect(end - new float2(size.X * .38f, 8), end, edge);
            if (route == NewDrawingRoute.Template)
            {
                DrawTemplateView(page + size * new float2(.29f, .33f), height * .13f, 3, ink);
                DrawTemplateView(page + size * new float2(.29f, .68f), height * .15f, 1, ink);
                DrawTemplateView(page + size * new float2(.71f, .42f), height * .26f, 4, ink);
            }
        }
    }

    static void StartDrawingRoute(VehicleEditor editor, NewDrawingDraft draft, NewDrawingRoute route)
    {
        switch (route)
        {
            case NewDrawingRoute.Arrange:
                if (!setupGridOpened)
                {
                    Array.Clear(setupViews);
                    setupGrid = 0;
                    setupGridWarning = null;
                    setupPrimary = resolvedSetupPrimary = null;
                    setupWorkingGrid = SetupViewGrid.Empty(draft.Angle);
                    ClearSetupGesture();
                    RefreshSetupViewIssues(0, [], null);
                    setupParts = setupFlags = false;
                    setupGridOpened = true;
                }
                setupStage = SetupStage.SelectViews;
                break;
            case NewDrawingRoute.Template:
                BeginTemplateSetup();
                break;
            default:
                setupLayout = null;
                StartDrawing(editor);
                break;
        }
    }

    static void SaveDrawingDefaults(NewDrawingDraft draft)
    {
        var settings = profileDefaults with
        {
            Data = draft.TitleData,
            LogoFile = draft.LogoName,
            Drawing = draft.Paper,
            TitleBlockVisible = draft.ShowTitle,
            ProjectionAngle = draft.Angle,
            InitialsSizeVersion = 1
        };
        if (SaveProfileSettings(settings)) profileMessage = "Defaults saved";
    }

    static void SetupInput(string label, ImInputString input)
    {
        ImGui.InputText(label, input);
        input.EvaluateLength();
    }

    static void DrawSetupProjection(NewDrawingDraft draft, bool stackedLabels = false)
    {
        int style = draft.Angle == 3 ? 0 : 1;
        if (ImGui.Combo(SetupLabel("Projection", stackedLabels), ref style, setupAngles, setupAngles.Length))
        {
            int angle = style == 0 ? 3 : 1;
            if (!setupGridOpened || TrySetupLayout(setupGrid, setupPrimary, angle)) draft.Angle = angle;
        }
        PrintUiText.Wrapped(draft.Angle == 3 ? "Top above Front · Right view on the right" :
            "Top below Front · Right view on the left");
    }

    static void DrawDraftPaper(NewDrawingDraft draft)
    {
        if (!ImGui.TreeNode("Paper & wear")) return;
        DrawPaperSettings(draft);
        ImGui.TreePop();
    }

    static void DrawPaperSettings(NewDrawingDraft draft, bool stackedLabels = false)
    {
        ImGui.Combo(SetupLabel("Theme", stackedLabels), ref draft.Theme, Themes, Themes.Length);
        ImGui.Combo(SetupLabel("Archive wear", stackedLabels), ref draft.Wear, WearNames, WearNames.Length);
        ImGui.Checkbox("Graph paper", ref draft.Graph);
        ImGui.Checkbox("Zoned borders", ref draft.Borders);
        ImGui.Checkbox("Show title block", ref draft.ShowTitle);
    }

    static void DrawLogoThumbnail(LogoImage? image)
    {
        if (image == null) return;
        var point = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var size = new float2(image.Width + 6, image.Height + 6);
        draw.AddRectFilled(point, point + size, 0xffaaa299);
        point += new float2(3, 3);
        for (int y = 0; y < image.Height; y++)
        for (int x = 0; x < image.Width; x++)
        {
            int i = (y * image.Width + x) * 4;
            if (image.Rgba[i + 3] == 0) continue;
            uint color = (uint)(image.Rgba[i] | image.Rgba[i + 1] << 8 | image.Rgba[i + 2] << 16 | image.Rgba[i + 3] << 24);
            var start = point + new float2(x, y);
            draw.AddRectFilled(start, start + new float2(1, 1), color);
        }
        ImGui.Dummy(size);
    }
}
