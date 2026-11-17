using Blueprint.Rendering;
using System.Numerics;
static class FreeTextChecks
{

    public static void Run(Action<bool, string> check)
    {
        var sizedText = new FreeTextBox(77, "Small LARGE tail", new(10, 10, 240, 80), 24);
        sizedText = RichText.Resize(sizedText, 6, 11, 64);
        check(RichText.Sizes(sizedText).Take(6).All(n => n == 24) && RichText.Sizes(sizedText).Skip(6).Take(5).All(n => n == 64)
            && RichText.Sizes(sizedText).Skip(11).All(n => n == 24), "Selection size changes only the selected word");
        var styledMixed = RichText.Toggle(sizedText, 6, 11, FreeTextStyle.Bold);
        check(RichText.Sizes(styledMixed).SequenceEqual(RichText.Sizes(sizedText)), "Bold toggles preserve independent font sizes");
        var insertedMixed = RichText.Edit(styledMixed, "Small BIG tail", 6, 11, 9, FreeTextStyle.Italic, 48);
        check(RichText.Sizes(insertedMixed).Skip(6).Take(3).All(n => n == 48) && RichText.Sizes(insertedMixed).Skip(9).All(n => n == 24),
            "Replacing mixed-size selection preserves surrounding sizes and uses insertion size");
        var sizedLayout = new TextEditorLayout(sizedText);
        check(sizedLayout.Lines.Length > 1 && sizedLayout.Height > sizedLayout.Lines.Length * 24 * 1.3f,
            "Mixed-size wrapping reserves the largest font on each line");
        foreach (var glyph in sizedLayout.Lines.SelectMany(l => l.Glyphs).Where(g => g.Text.Length > 0))
            check(sizedLayout.Hit(sizedLayout.Caret(glyph.Start) + new Vector2(.1f, .1f)) == glyph.Start,
                "Mixed-size hit testing shares rendered glyph geometry " + glyph.Start);
        var sizedCanvas = new Canvas(600, 600, new(255), Vector3.Zero, new PrintOptions());
        FreeTextPainting.Paint(sizedCanvas, [styledMixed]);
        check(sizedCanvas.Runs.Any(r => r.Height == 64 && r.Bold) && sizedCanvas.Runs.Any(r => r.Height == 24 && !r.Bold),
            "One exported text box retains multiple font sizes and styles");
        NativeText.Apply(sizedCanvas.Pixels, 600, 600, sizedCanvas.Runs);
        var options = new PrintOptions(Grid: false, Zones: false, Drawing: new(100, []));
        var text = FreeTextPainting.Fit(new(1, "Flight hardware\n\nHandle with care", new(160, 200, 600, 100), 40, true, true, true, 1), options);
        check(FreeTextPainting.Lines(text).SequenceEqual(new[] { "Flight hardware", "", "Handle with care" }), "Free text retains paragraphs and blank lines without placeholder part names");
        var canvas = new Canvas(1000, 600, new(255), Vector3.Zero, options);
        FreeTextPainting.Paint(canvas, [text]);
        check(canvas.Runs.Count == 3 && canvas.Runs.All(r => r.Bold && r.Italic && r.Underline && r.Height == 40 && r.Face == "Consolas"), "Free-text B/I/U and size reach the native typography renderer");
        foreach (int alignment in new[] { 0, 1, 2 })
        {
            var c = new Canvas(1000, 600, new(255), Vector3.Zero, options);
            FreeTextPainting.Paint(c, [text with { Alignment = alignment }]);
            check(c.Runs.All(r => Math.Abs(r.X - (alignment == 0 ? text.Bounds.X + 8 : alignment == 1 ? text.Bounds.Center.X - r.Text.Length * NativeText.MonoWidth(40) / 2 : text.Bounds.Right - 8 - r.Text.Length * NativeText.MonoWidth(40))) < 1),
                "Free-text line alignment follows the selected box anchor: " + alignment);
        }
        var plain = new Canvas(1000, 600, new(255), Vector3.Zero, options);
        FreeTextPainting.Paint(plain, [text with { Underline = false }]);
        NativeText.Apply(plain.Pixels, 1000, 600, plain.Runs);
        NativeText.Apply(canvas.Pixels, 1000, 600, canvas.Runs);
        check(!plain.Pixels.SequenceEqual(canvas.Pixels), "Underline changes actual exported pixels, not only editor state");
        var narrow = FreeTextPainting.Fit(text with
        {
            Bounds = text.Bounds with
            {
                Width = 180
            }
        }, options);
        check(narrow.Bounds.Height > text.Bounds.Height && narrow.FontSize == text.FontSize, "Text box width reflows lines without shrinking the chosen font");
        var tall = FreeTextPainting.Fit(text with
        {
            Bounds = text.Bounds with
            {
                Height = 900
            }
        }, options);
        check(tall.Bounds.Height == 900 && tall.Bounds.Width == text.Bounds.Width,
            "Text box can grow vertically without changing width or font");
        var shorter = FreeTextPainting.Fit(tall with
        {
            Bounds = tall.Bounds with
            {
                Height = 300
            }
        }, options);
        check(shorter.Bounds.Height == 300, "Text box can shrink unused height independently");
        var minimum = FreeTextPainting.Fit(tall with
        {
            Bounds = tall.Bounds with
            {
                Width = 180,
                Height = 1
            }
        }, options);
        check(minimum.Bounds.Height == MathF.Ceiling(new TextEditorLayout(minimum).Height)
            && minimum.FontSize == tall.FontSize && new TextEditorLayout(minimum).Lines.Length > new TextEditorLayout(tall).Lines.Length,
            "Narrowing wraps text and clamps height only to the complete text layout");
        var expandedMixed = FreeTextPainting.Fit(styledMixed with
        {
            Bounds = new(160, 200, 600, 500)
        }, options);
        var compactMixed = FreeTextPainting.Fit(expandedMixed with
        {
            Bounds = expandedMixed.Bounds with
            {
                Width = 180,
                Height = 1
            }
        }, options);
        check(compactMixed.Bounds.Height >= new TextEditorLayout(compactMixed).Height
            && RichText.Sizes(compactMixed).SequenceEqual(RichText.Sizes(styledMixed)),
            "Two-axis resizing retains mixed font sizes and enough height for every wrapped line");
        var edge = FreeTextPainting.Fit(text with
        {
            Bounds = text.Bounds with
            {
                X = 9999,
                Y = 9999
            }
        }, options);
        check(edge.Bounds.Right <= options.Page.Width && edge.Bounds.Bottom <= options.Page.Height, "Free text remains exportable when moved to the page edge");
        try
        {
            FreeTextPainting.Fit(text with
            {
                Text = " \n "
            }, options);
            throw new Exception("Empty accepted");
        }
        catch (InvalidOperationException) { check(true, "Blank text cannot be accepted"); }
        try
        {
            FreeTextPainting.Fit(text with
            {
                Text = string.Join('\n', Enumerable.Repeat("X", 100)),
                FontSize = 144
            }, options);
            throw new Exception("Overflow accepted");
        }
        catch (InvalidOperationException) { check(true, "Overlong vertical text reports an error instead of dropping lines"); }
        int width = options.Page.Width, height = options.Page.Height;
        var basePaper = new Canvas(width, height, new(255), Vector3.Zero, options);
        var scene = new RenderScene(basePaper.Pixels, Enumerable.Repeat(-1, width * height).ToArray(), [], [], options: options);
        var original = new Sheet("Text fixture", options, basePaper.Pixels, Sheet.MakePreview(basePaper.Pixels), []) { Scene = scene };
        var withText = AnnotationPainting.Compose(original with
        {
            Scene = scene.WithFreeText([text])
        }, [], default);
        check(!withText.Rgb.SequenceEqual(original.Rgb) && withText.Labels.Length == 0, "Free text exports independently of parts and leaders");
        var doc = new AnnotationDocument(scene);
        doc.Rebind(withText.Scene!, [], []);
        var edited = text with
        {
            Text = "Revised note",
            Alignment = 2,
            Bold = false
        };
        doc.Rebind(scene.WithFreeText([edited]), [], []);
        doc.Undo();
        check(doc.Scene.FreeText.Single() == text, "Undo text editing restores content, formatting and placement together");
        doc.Redo();
        check(doc.Scene.FreeText.Single() == edited, "Redo restores free-text formatting");
        doc.Rebind(doc.Scene.WithFreeText([]), [], []);
        doc.Undo();
        check(doc.Scene.FreeText.Single() == edited, "Deleting a text box is undoable");
        var regenerated = CompositionRegeneration.Rebuild(original, withText, default);
        check(regenerated.Scene!.FreeText.Single() == text && regenerated.Rgb.SequenceEqual(withText.Rgb), "Generate retains free text and produces identical text pixels");
        var snapSpace = new TextPlacementSpace(options, [new(700, 400, 400, 200)], []);
        var beside = text with
        {
            Bounds = new(704, 629, 400, 250),
            FontSize = 24,
            Text = "Short"
        };
        var snapped = snapSpace.Snap(beside, false, 12);
        check(snapped.Bounds == new Box(700, 624, 400, 250),
            "Text snaps its full rectangle to matching left edges and stacking gaps");
        check(snapSpace.Snap(beside with
        {
            Outline = true
        }, false, 12).Bounds == snapped.Bounds,
            "Hidden and visible text borders produce exactly the same snap");
        var resizedBox = snapSpace.Snap(beside with
        {
            Bounds = new(700, 624, 394, 195)
        }, true, 12);
        check(resizedBox.Bounds == new Box(700, 624, 400, 200),
            "Text grip snaps width and height to neighboring box dimensions, not glyph edges");
        check(snapSpace.Snap(beside with
        {
            Bounds = new(150, 1000, 400, 250)
        }, false, 12).Bounds == new Box(150, 1000, 400, 250),
            "Distant text boxes do not jump to unrelated alignments");
        check(snapSpace.SnapCorner(new(704, 629), 12) == new Vector2(700, 624),
            "Initial click-and-drag creation snaps the rectangle corners");
        var edgeSpace = new TextPlacementSpace(options, [], []);
        var edgeSnap = edgeSpace.Snap(beside with
        {
            Bounds = new(64, 64, 400, 250)
        }, false, 12);
        check(edgeSnap.Bounds.X == PageLayout.Margin && edgeSnap.Bounds.Y == PageLayout.Margin,
            "Text box rectangle snaps to the printed drawing border");
        check(!snapSpace.Clear(new(750, 450, 180, 100)) && snapSpace.Clear(new(1100, 400, 180, 100)),
            "Text boxes reject occupied rectangles but allow exact edge contact");
        var leaderSpace = new TextPlacementSpace(options, [], [(new(200, 400), new(1000, 1200))]);
        check(!leaderSpace.Clear(new(500, 650, 160, 100)) && leaderSpace.Clear(new(500, 1000, 160, 100)),
            "Diagonal leader checks use actual segments instead of a large diagonal bounding box");
        var viewBounds = new Box(700, 500, 300, 400);
        var viewScene = new RenderScene(basePaper.Pixels, scene.Owners, [], [new(0, "FRONT", viewBounds, null)], options: options)
            .WithCaptions([new(0, "FRONT", viewBounds, new(740, 920, 220, 50), 44)], false)
            .WithFreeText([text]);
        var viewSpace = TextBoxPlacement.Space(viewScene, [], [], text.Id);
        check(!viewSpace.Clear(new(720, 700, 160, 100)) && !viewSpace.Clear(new(750, 930, 160, 100)),
            "Views and their captions both reserve space against text overlap");
        var labelFixture = new PartLabel(9, 0, 0, "LABEL", 1200, 500);
        var labelRoute = new LabelGeometry(labelFixture, AnnotationText.Bounds(labelFixture), ["LABEL"], new(1200, 510), new(1000, 700));
        var labelSpace = TextBoxPlacement.Space(viewScene, [labelFixture], [labelRoute], text.Id);
        check(!labelSpace.Clear(new(1180, 490, 180, 100)) && !labelSpace.Clear(new(1070, 600, 180, 100)),
            "Part/flagnote label text and leaders remain clear of text-box rectangles");
        var tablesScene = scene.WithOptions(options with
        {
            PartsTable = true,
            FlagTable = true
        })
            .WithTables(new(new(1200, 500, 500, 200), new(1200, 750, 500, 150)));
        var tableSpace = TextBoxPlacement.Space(tablesScene, [], []);
        check(!tableSpace.Clear(new(1210, 510, 180, 100)) && !tableSpace.Clear(new(1210, 760, 180, 100)),
            "Both parts and flagnote tables block text placement");
        var specsScene = scene.WithSpecs([new(1, new(1200, 500, 500, 300), Vehicle: true)]);
        check(!TextBoxPlacement.Space(specsScene, [], []).Clear(new(1210, 510, 180, 100)),
            "Performance blocks reserve their full rectangle");
        var otherText = text with
        {
            Id = 8,
            Bounds = new(1000, 500, 600, 200)
        };
        check(!TextBoxPlacement.Space(scene.WithFreeText([text, otherText]), [], [], text.Id).Clear(otherText.Bounds)
            && TextBoxPlacement.Space(scene.WithFreeText([text]), [], [], text.Id).Clear(text.Bounds),
            "Other text boxes obstruct placement while the dragged box excludes itself");
        check(!edgeSpace.Clear(PageLayout.TitleBounds(options)) && !edgeSpace.Clear(new(5, 5, 160, 100)),
            "Text cannot cover the title block or cross the drawing border");
        var obstructed = original with
        {
            Scene = viewScene.WithFreeText([text with { Bounds = viewBounds }])
        };
        try
        {
            AnnotationPainting.Compose(obstructed, [], default);
            throw new Exception("Overlapping text was accepted");
        }
        catch (InvalidOperationException e)
        {
            check(e.Message == TextBoxPlacement.OverlapMessage, "Composition rejects overlap even when no mouse gesture performed validation");
        }
        var borderText = text with
        {
            Outline = true
        };
        var bordered = AnnotationPainting.Compose(original with
        {
            Scene = scene.WithFreeText([borderText])
        }, [], default);
        check(!bordered.Rgb.SequenceEqual(withText.Rgb), "Outline toggle changes exported pixels");
        var borderHistory = new AnnotationDocument(scene);
        borderHistory.Rebind(withText.Scene!, [], []);
        borderHistory.Rebind(bordered.Scene!, [], []);
        borderHistory.Undo();
        check(!borderHistory.Scene.FreeText.Single().Outline, "Undo restores hidden text outline");
        borderHistory.Redo();
        check(borderHistory.Scene.FreeText.Single().Outline, "Redo restores text outline without changing bounds");
        var borderRegen = CompositionRegeneration.Rebuild(original, bordered, default);
        check(borderRegen.Scene!.FreeText.Single() == borderText && borderRegen.Rgb.SequenceEqual(bordered.Rgb),
            "Generate preserves the text rectangle and its outline setting");
        foreach (int theme in Enumerable.Range(0, 4))
        {
            var colors = SheetLayout.Colors(theme);
            var plainPaper = new Canvas(600, 400, colors.Paper, colors.Ink, options with
            {
                Theme = theme
            });
            var underlay = plainPaper.Pixels.ToArray();
            var box = new FreeTextBox(50, "Border", new(80, 80, 400, 220), Outline: true);
            FreeTextPainting.Paint(plainPaper, [box]);
            int corner = (80 * 600 + 80) * 3, interior = (250 * 600 + 300) * 3;
            check(plainPaper.Pixels[corner] == (byte)colors.Ink.X && plainPaper.Pixels[corner + 1] == (byte)colors.Ink.Y
                && plainPaper.Pixels.AsSpan(interior, 3).SequenceEqual(underlay.AsSpan(interior, 3)),
                "Text outline follows theme ink and leaves paper/grid unchanged: " + theme);
        }
        var mixed = FreeTextPainting.Fit(new(2, "one bold word\nsecond line", new(160, 200, 600, 100), 40), options);
        mixed = RichText.Toggle(mixed, 4, 8, FreeTextStyle.Bold);
        mixed = RichText.Toggle(mixed, 14, mixed.Text.Length, FreeTextStyle.Underline);
        var styles = RichText.Styles(mixed);
        check(styles.Take(4).All(s => s == FreeTextStyle.None) && styles.Skip(4).Take(4).All(s => s == FreeTextStyle.Bold)
            && styles.Skip(14).All(s => s == FreeTextStyle.Underline), "Selected word and selected line receive independent formatting");
        var mixedCanvas = new Canvas(1000, 600, new(255), Vector3.Zero, options);
        FreeTextPainting.Paint(mixedCanvas, [mixed]);
        check(mixedCanvas.Runs.Single(r => r.Text == "bold").Bold && mixedCanvas.Runs.Single(r => r.Text == "second line").Underline
            && mixedCanvas.Runs.Where(r => r.Text is not ("bold" or "second line")).All(r => !r.Bold && !r.Underline), "Partial formats reach only their corresponding native text runs");
        var twice = RichText.Toggle(mixed, 4, 8, FreeTextStyle.Bold);
        check(RichText.Styles(twice).Skip(4).Take(4).All(s => s == FreeTextStyle.None) && RichText.Has(twice, 14, twice.Text.Length, FreeTextStyle.Underline), "Toggling one selection off preserves formatting elsewhere");
        var prefix = RichText.Edit(mixed, "New " + mixed.Text, 0, 0, 4, FreeTextStyle.Italic);
        check(RichText.Styles(prefix).Take(4).All(s => s == FreeTextStyle.Italic) && RichText.Has(prefix, 8, 12, FreeTextStyle.Bold), "Typing before formatted words shifts existing ranges without losing styles");
        var replace = RichText.Edit(mixed, "one strong word\nsecond line", 4, 8, 10, FreeTextStyle.Bold);
        check(RichText.Has(replace, 4, 10, FreeTextStyle.Bold) && RichText.Has(replace, 16, replace.Text.Length, FreeTextStyle.Underline), "Replacing a formatted selection styles inserted text and preserves following lines");
        var removed = RichText.Edit(mixed, mixed.Text[4..], 0, 4, 0, FreeTextStyle.None);
        check(RichText.Has(removed, 0, 4, FreeTextStyle.Bold) && RichText.Has(removed, 10, removed.Text.Length, FreeTextStyle.Underline), "Deleting text rebases remaining format ranges");
        var repeated = new FreeTextBox(3, "aaa", new(0, 0, 600, 100));
        var inserted = RichText.Edit(repeated, "aaaa", 0, 0, 1, FreeTextStyle.Italic);
        check(RichText.Styles(inserted)[0] == FreeTextStyle.Italic && RichText.Styles(inserted).Skip(1).All(s => s == FreeTextStyle.None), "Repeated characters do not move inserted formatting to the wrong end");
        var movedCaret = RichText.Edit(repeated, "aaaa", 0, 0, 3, FreeTextStyle.Underline);
        check(RichText.Styles(movedCaret).SequenceEqual(new[] { FreeTextStyle.None, FreeTextStyle.None, FreeTextStyle.Underline, FreeTextStyle.None }), "Caret movement before typing does not attach formatting to an earlier repeated character");
        check(RichText.CharacterIndex("é🐾 bold", 2) == 1 && RichText.CharacterIndex("é🐾 bold", 6) == 3 && RichText.CharacterIndex("é🐾 bold", 4) == 1,
            "Native UTF-8 selections map to complete Unicode characters in stored ranges");
        var wrapped = RichText.Lines(mixed, 6).SelectMany(l => l).ToArray();
        check(wrapped.Where(r => (r.Style & FreeTextStyle.Bold) != 0).Sum(r => r.Text.Trim().Length) == 4
            && wrapped.Where(r => (r.Style & FreeTextStyle.Underline) != 0).Sum(r => r.Text.Replace(" ", "").Length) == 10, "Word wrapping retains inline bold and underline styles");
        var mixedSheet = AnnotationPainting.Compose(original with
        {
            Scene = scene.WithFreeText([mixed])
        }, [], default);
        var mixedRestored = CompositionRegeneration.Rebuild(original, mixedSheet, default);
        check(mixedRestored.Scene!.FreeText.Single().Formats == mixed.Formats && mixedRestored.Rgb.SequenceEqual(mixedSheet.Rgb), "Regeneration preserves inline ranges and their exported pixels");
        var mixedHistory = new AnnotationDocument(scene);
        mixedHistory.Rebind(mixedSheet.Scene!, [], []);
        mixedHistory.Rebind(scene.WithFreeText([twice]), [], []);
        mixedHistory.Undo();
        check(mixedHistory.Scene.FreeText.Single() == mixed, "Document Undo restores partial formatting");
        mixedHistory.Redo();
        check(mixedHistory.Scene.FreeText.Single() == twice, "Document Redo restores a selection-format change");
        var editorBox = new FreeTextBox(9, "one two three\n\n\u00e9\U0001F43E", new(0, 0, NativeText.MonoWidth(32) * 6 + 16, 100));
        var editor = new TextEditorLayout(editorBox);
        check(editor.Lines.Select(l => string.Concat(l.Glyphs.Select(g => g.Text))).SequenceEqual(new[] { "one", "two", "three", "", "\u00e9\U0001F43E" }),
            "Formatted editor keeps word wraps, blank lines and Unicode source positions");
        foreach (int index in new[] { 0, 1, 3, 4, 7, 8, 13, 14, 15, 16, 18 })
            check(editor.Hit(editor.Caret(index)) == index, "Formatted hit testing returns the displayed caret index " + index);
        foreach (int alignment in new[] { 0, 1, 2 })
        {
            var aligned = new TextEditorLayout(editorBox with
            {
                Alignment = alignment
            });
            check(aligned.Hit(aligned.Caret(5)) == 5 && aligned.Selection(4, 7).Single().Width == 3 * aligned.CellWidth,
                "Selection and caret follow formatted line alignment " + alignment);
        }
        check(editor.Selection(13, 15).Count() == 2, "Selected newlines remain visible on blank lines");
        check(TextEditorLayout.WordRange("alpha beta", 7) == (6, 10) && TextEditorLayout.WordRange("\u00e9\U0001F43E", 2) == (0, 3),
            "Double-click word selection preserves whole Unicode characters");
        var viewport = FreeTextPainting.EditorPreview(mixed, 0, 560, 320, .9f, 12);
        check(viewport.Width == 560 && viewport.Height == 320 && viewport.Rgba.Length == 560 * 320 * 4,
            "Single-surface editor raster is bounded to its viewport");
        var hugeNote = mixed with
        {
            Text = string.Join('\n', Enumerable.Repeat("Long text", 200)),
            FontSize = 144
        };
        var lowerViewport = FreeTextPainting.EditorPreview(hugeNote, 2, 560, 320, .9f, 2000);
        check(lowerViewport.Rgba.Length == 560 * 320 * 4,
            "Scrolling a long formatted note never allocates its full text height");
        var preview = FreeTextPainting.Preview(mixed, 0);
        check(preview.Rgba.Length == preview.Width * preview.Height * 4 && preview.Width <= 1000 && preview.Height <= 4096, "Formatted preview uses bounded native-rendered pixels");
    }
}
