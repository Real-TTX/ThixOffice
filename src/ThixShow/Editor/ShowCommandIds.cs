namespace ThixShow.Editor;

/// <summary>
/// The ids of ThixShow's commands (<c>show.&lt;name&gt;</c>; menu entries <c>show.&lt;menu&gt;.&lt;entry&gt;</c>). Value commands note
/// their value: a colour "#rrggbb", a number invariant, a gallery entry's id.
/// </summary>
public static class ShowCommandIds
{
    // Zwischenablage
    public const string Paste = "show.paste";
    public const string PasteText = "show.pasteText";
    public const string Cut = "show.cut";
    public const string Copy = "show.copy";
    public const string Duplicate = "show.duplicate";
    public const string SelectAll = "show.selectAll";
    public const string Delete = "show.delete";

    // Folien
    public const string NewSlide = "show.newSlide";
    public const string NewSlideLayout = "show.newSlideLayout"; // value: layout id
    public const string Layout = "show.layout"; // value: layout id
    public const string ResetSlide = "show.resetSlide";
    public const string DuplicateSlide = "show.duplicateSlide";
    public const string DeleteSlide = "show.deleteSlide";
    public const string HideSlide = "show.hideSlide";
    public const string AddSection = "show.addSection";
    public const string RenameSection = "show.renameSection";
    public const string RemoveSection = "show.removeSection";
    public const string Section = "show.section";

    // Schriftart
    public const string FontName = "show.fontName"; // value: family
    public const string FontSize = "show.fontSize"; // value: points
    public const string GrowFont = "show.growFont";
    public const string ShrinkFont = "show.shrinkFont";
    public const string Bold = "show.bold";
    public const string Italic = "show.italic";
    public const string Underline = "show.underline";
    public const string Strikethrough = "show.strikethrough";
    public const string Superscript = "show.superscript";
    public const string Subscript = "show.subscript";
    public const string FontColor = "show.fontColor"; // value: colour
    public const string Highlight = "show.highlight"; // value: colour
    public const string ClearFormat = "show.clearFormat";

    // Absatz
    public const string Bullets = "show.bullets";
    public const string Numbering = "show.numbering";
    public const string DecreaseLevel = "show.decreaseLevel";
    public const string IncreaseLevel = "show.increaseLevel";
    public const string AlignLeft = "show.alignLeft";
    public const string AlignCenter = "show.alignCenter";
    public const string AlignRight = "show.alignRight";
    public const string Justify = "show.justify";
    public const string LineSpacing = "show.lineSpacing";
    public const string TextTop = "show.textTop";
    public const string TextMiddle = "show.textMiddle";
    public const string TextBottom = "show.textBottom";
    public const string AlignText = "show.alignText";

    // Zeichnen / Anordnen
    public const string Shapes = "show.shapes"; // value: preset ("rect", "ellipse" …), drawn by drag
    public const string TextBox = "show.textBox";
    public const string Arrange = "show.arrange";
    public const string BringToFront = "show.bringToFront";
    public const string BringForward = "show.bringForward";
    public const string SendBackward = "show.sendBackward";
    public const string SendToBack = "show.sendToBack";
    public const string Group = "show.group";
    public const string Ungroup = "show.ungroup";
    public const string AlignShapes = "show.alignShapes";
    public const string AlignShapesLeft = "show.alignShapes.left";
    public const string AlignShapesCenter = "show.alignShapes.center";
    public const string AlignShapesRight = "show.alignShapes.right";
    public const string AlignShapesTop = "show.alignShapes.top";
    public const string AlignShapesMiddle = "show.alignShapes.middle";
    public const string AlignShapesBottom = "show.alignShapes.bottom";
    public const string DistributeHorizontally = "show.distribute.horizontal";
    public const string DistributeVertically = "show.distribute.vertical";
    public const string Rotate = "show.rotate";
    public const string RotateRight = "show.rotate.right";
    public const string RotateLeft = "show.rotate.left";
    public const string FlipHorizontal = "show.flip.horizontal";
    public const string FlipVertical = "show.flip.vertical";
    public const string QuickStyles = "show.quickStyles"; // value: style id
    public const string ShapeFill = "show.shapeFill"; // value: colour (null = Keine Füllung)
    public const string ShapeOutline = "show.shapeOutline"; // value: colour (null = Keine Kontur)
    public const string ShapeShadow = "show.shapeShadow";
    public const string ChangeShape = "show.changeShape"; // value: preset

    // Bearbeiten
    public const string Find = "show.find";
    public const string Replace = "show.replace";

    // Einfügen
    public const string InsertTable = "show.insertTable"; // value: "rows x columns"
    public const string InsertPicture = "show.insertPicture";
    public const string InsertChart = "show.insertChart"; // value: chart template id
    public const string InsertLink = "show.insertLink";
    public const string HeaderFooter = "show.headerFooter";
    public const string InsertSlideNumber = "show.insertSlideNumber";
    public const string InsertDate = "show.insertDate";
    public const string InsertSymbol = "show.insertSymbol";

    // Entwurf
    public const string Design = "show.design"; // value: design id
    public const string SlideSize = "show.slideSize";
    public const string SlideSizeStandard = "show.slideSize.standard";
    public const string SlideSizeWidescreen = "show.slideSize.widescreen";
    public const string SlideSizeCustom = "show.slideSize.custom";
    public const string FormatBackground = "show.formatBackground";

    // Übergänge
    public const string Transition = "show.transition"; // value: transition id
    public const string TransitionDuration = "show.transitionDuration"; // value: seconds
    public const string ApplyToAll = "show.applyToAll";
    public const string AdvanceOnClick = "show.advanceOnClick";
    public const string AdvanceAfter = "show.advanceAfter"; // value: seconds
    public const string PreviewTransition = "show.previewTransition";

    // Animationen
    public const string Animation = "show.animation"; // value: preset id
    public const string AnimationPane = "show.animationPane";
    public const string AnimationStart = "show.animationStart"; // value: "click" | "with" | "after"
    public const string AnimationDuration = "show.animationDuration"; // value: seconds
    public const string AnimationDelay = "show.animationDelay"; // value: seconds
    public const string PreviewAnimation = "show.previewAnimation";

    // Bildschirmpräsentation
    public const string StartShow = "show.start";
    public const string StartFromCurrent = "show.startFromCurrent";
    public const string PresenterView = "show.presenterView";
    public const string ChooseScreen = "show.chooseScreen"; // value: "Automatisch", a screen's label ("Bildschirm 2 – …") or its index
    public const string TestPresenterView = "show.testPresenterView";
    public const string LoopShow = "show.loop";

    // Ansicht
    public const string ViewNormal = "show.view.normal";
    public const string ViewSorter = "show.view.sorter";
    public const string ViewReading = "show.view.reading";
    public const string ToggleNotes = "show.view.notes";
    public const string Gridlines = "show.view.gridlines";
    public const string SmartGuides = "show.view.smartGuides";
    public const string FitToWindow = "show.view.fit";
    public const string Zoom = "show.view.zoom";

    // Datei
    public const string Print = "show.print";
}
