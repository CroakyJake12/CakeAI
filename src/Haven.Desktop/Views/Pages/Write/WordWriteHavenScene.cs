using Haven.Application;
using Haven.Core;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Write;

internal enum WordWriteRibbonTab { Home, Insert, Layout, Review }

/// <summary>Word-class Haven.UI surface for Write. Platform code only hosts this scene.</summary>
internal sealed partial class WordWriteHavenScene : IDisposable
{
    private bool _suppress;
    private bool _disposed;
    private WordWriteRibbonTab _tab = WordWriteRibbonTab.Home;
    private WriteDocumentEditor? _editor;
    private IReadOnlyList<NotesDocumentSummary> _libraryDocuments = [];
    private readonly Dictionary<Guid, Input> _blockInputs = [];
    private string _find = string.Empty, _replace = string.Empty, _comment = string.Empty, _citationTitle = string.Empty, _citationAuthors = string.Empty, _citationUrl = string.Empty;
    private string _aiInstruction = string.Empty; private bool _allowAiDocumentContext; private NotesAiChange? _pendingAiChange; private IReadOnlyList<string> _aiModels = []; private string _selectedAiModel = string.Empty;
    private readonly Haven.Desktop.HavenUI.Runtime.TrailingDebouncer _statsDebouncer = new(TimeSpan.FromMilliseconds(300));

    public WordWriteHavenScene()
    {
        Root = new Page { Name = "Write.Word.Root", Layout = HavenLayout.Grid, Columns = "1fr", Rows = "Auto 1fr Auto" };
        Root.SetValue(HavenProperties.Background, "Surface");
        Root.SetValue(HavenProperties.Overflow, HavenOverflow.Clip);

        Chrome = new Container { Name = "Write.Word.Chrome", Layout = HavenLayout.Vertical };
        Chrome.SetValue(HavenProperties.Background, "SurfaceRaised");
        Chrome.SetValue(HavenProperties.Gap, HavenLength.Px(0));

        Header = new Container { Name = "Write.Word.Header", Layout = HavenLayout.Grid, Columns = "Auto 1fr Auto", Rows = "Auto" };
        Header.SetValue(HavenProperties.Background, "SurfaceRaised");
        Header.SetValue(HavenProperties.Padding, HavenThickness.Parse("7px 16px 5px 16px"));
        Header.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        var product = new HavenText("Write") { Name = "Write.Word.Product", Level = TextLevel.Paragraph };
        product.SetValue(HavenProperties.Foreground, "ButtonTextPrimary");
        product.SetValue(HavenProperties.FontWeight, 700);
        product.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        Header.Add(product);
        TitleInput = Field("Write.Word.Title", "Document title", "Untitled document");
        TitleInput.SetValue(HavenProperties.Column, 1);
        TitleInput.SetValue(HavenProperties.FontSize, 15d);
        TitleInput.SetValue(HavenProperties.FontWeight, 600);
        TitleInput.SetValue(HavenProperties.MinHeight, HavenLength.Px(32));
        TitleInput.SetValue(HavenProperties.Background, "Transparent");
        TitleInput.SetValue(HavenProperties.Foreground, "ButtonTextPrimary");
        TitleInput.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(4)));
        Header.Add(TitleInput);
        DocumentPositionText = Caption("Local document");
        DocumentPositionText.SetValue(HavenProperties.Column, 2);
        DocumentPositionText.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        Header.Add(DocumentPositionText);
        Chrome.Add(Header);

        QuickBar = Bar("Write.Word.Quick", 0);
        QuickBar.SetValue(HavenProperties.Background, "SurfaceRaised");
        QuickBar.SetValue(HavenProperties.Padding, HavenThickness.Parse("2px 16px 5px 16px"));
        PreviousButton = Btn("Write.Previous", "Previous"); NextButton = Btn("Write.Next", "Next");
        NewButton = Btn("Write.New", "New"); ImportButton = Btn("Write.Import", "Open"); ExportButton = Btn("Write.Export", "Export");
        SaveButton = Btn("Write.Save", "Save", ButtonVariant.Primary); UndoButton = Btn("Write.Undo", "Undo"); RedoButton = Btn("Write.Redo", "Redo");
        foreach (var value in new[] { SaveButton, UndoButton, RedoButton, NewButton, ImportButton, ExportButton, PreviousButton, NextButton }) QuickBar.Add(value);
        Chrome.Add(QuickBar);
        Ribbon = new Container { Name = "Write.Word.Ribbon", Layout = HavenLayout.Vertical }; Ribbon.SetValue(HavenProperties.Background, "SurfaceRaised"); Ribbon.SetValue(HavenProperties.Padding, HavenThickness.Parse("5px 18px 9px 18px")); Ribbon.SetValue(HavenProperties.Gap, HavenLength.Px(5)); RibbonTabs = Bar("Write.Word.RibbonTabs", 0); HomeTab = Btn("Write.Tab.Home", "Home"); InsertTab = Btn("Write.Tab.Insert", "Insert"); LayoutTab = Btn("Write.Tab.Layout", "Layout"); ReviewTab = Btn("Write.Tab.Review", "Review"); foreach (var value in new[] { HomeTab, InsertTab, LayoutTab, ReviewTab }) RibbonTabs.Add(value); Ribbon.Add(RibbonTabs); RibbonContent = new Container { Name = "Write.Word.RibbonContent", Layout = HavenLayout.Horizontal }; RibbonContent.SetValue(HavenProperties.Gap, HavenLength.Px(7)); RibbonContent.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll); Ribbon.Add(RibbonContent); Chrome.Add(Ribbon);
        Ruler = CreateRuler();
        Chrome.Add(Ruler);
        Root.Add(Chrome);

        Scroller = new Container { Name = "Write.Word.Scroller", Layout = HavenLayout.Vertical };
        Scroller.SetValue(HavenProperties.Row, 1);
        Scroller.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        Scroller.SetValue(HavenProperties.Padding, HavenThickness.Parse("18px 18px 34px 18px"));
        Scroller.SetValue(HavenProperties.Background, "Surface");
        DocumentHost = new Container { Name = "Write.Word.Document", Layout = HavenLayout.Vertical };
        DocumentHost.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        DocumentHost.SetValue(HavenProperties.Background, "Transparent");
        DocumentHost.SetValue(HavenProperties.Gap, HavenLength.Px(0));
        DocumentSurface = new WriteDocumentSurface();
        DocumentSurface.SelectionChanged += OnDocumentSelectionChanged;
        DocumentHost.Add(DocumentSurface);
        Scroller.Add(DocumentHost);
        Root.Add(Scroller);

        StatusBar = new Container { Name = "Write.StatusBar", Layout = HavenLayout.Grid, Columns = "1fr Auto Auto Auto", Rows = "Auto" };
        StatusBar.SetValue(HavenProperties.Row, 2);
        StatusBar.SetValue(HavenProperties.Background, "SurfaceRaised");
        StatusBar.SetValue(HavenProperties.Padding, HavenThickness.Parse("5px 16px"));
        StatusBar.SetValue(HavenProperties.Gap, HavenLength.Px(5));
        StatusText = Caption("Opening local documents…"); StatusText.Name = "Write.Status"; StatusBar.Add(StatusText);
        StatusZoomOutButton = Btn("Write.Status.ZoomOut", "−"); StatusZoomOutButton.Accessibility.AccessibleName = "Zoom out"; StatusZoomOutButton.SetValue(HavenProperties.Column, 1);
        ZoomStatusText = Caption("100%"); ZoomStatusText.Name = "Write.Status.Zoom"; ZoomStatusText.SetValue(HavenProperties.Column, 2); ZoomStatusText.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        StatusZoomInButton = Btn("Write.Status.ZoomIn", "+"); StatusZoomInButton.Accessibility.AccessibleName = "Zoom in"; StatusZoomInButton.SetValue(HavenProperties.Column, 3);
        StatusBar.Add(StatusZoomOutButton); StatusBar.Add(ZoomStatusText); StatusBar.Add(StatusZoomInButton); Root.Add(StatusBar);
        TitleInput.Invalidated += OnTitleInvalidated; PreviousButton.Invoked += (_, _) => PreviousRequested?.Invoke(this, EventArgs.Empty); NextButton.Invoked += (_, _) => NextRequested?.Invoke(this, EventArgs.Empty); NewButton.Invoked += (_, _) => NewRequested?.Invoke(this, EventArgs.Empty); ImportButton.Invoked += (_, _) => ImportRequested?.Invoke(this, EventArgs.Empty); ExportButton.Invoked += (_, _) => ExportRequested?.Invoke(this, EventArgs.Empty); SaveButton.Invoked += (_, _) => SaveRequested?.Invoke(this, EventArgs.Empty); UndoButton.Invoked += (_, _) => Apply(editor => editor.Undo()); RedoButton.Invoked += (_, _) => Apply(editor => editor.Redo()); HomeTab.Invoked += (_, _) => SetTab(WordWriteRibbonTab.Home); InsertTab.Invoked += (_, _) => SetTab(WordWriteRibbonTab.Insert); LayoutTab.Invoked += (_, _) => SetTab(WordWriteRibbonTab.Layout); ReviewTab.Invoked += (_, _) => SetTab(WordWriteRibbonTab.Review); StatusZoomOutButton.Invoked += (_, _) => SetDocumentZoom(DocumentSurface.Zoom - .1); StatusZoomInButton.Invoked += (_, _) => SetDocumentZoom(DocumentSurface.Zoom + .1);
    }

    public event EventHandler? LibraryRequested; public event Action<Guid>? DocumentOpenRequested; public event Action<string, bool, string>? AiProposalRequested; public event EventHandler? AiApplyRequested; public event EventHandler? AiRejectRequested; public event EventHandler? PreviousRequested; public event EventHandler? NextRequested; public event EventHandler? NewRequested; public event EventHandler? ImportRequested; public event EventHandler? ExportRequested; public event EventHandler? SaveRequested; public event EventHandler? ImageRequested; public event EventHandler? DocumentChanged;
    public event Action<string>? TitleChanged { add { } remove { } }
    public event Action<WriteBlockTextChangedEventArgs>? BlockTextChanged { add { } remove { } }
    public Page Root { get; } public Container Chrome { get; } public Container Header { get; } public Container QuickBar { get; } public Container Ribbon { get; } public Container RibbonTabs { get; } public Container RibbonContent { get; } public Container Ruler { get; } public Container Scroller { get; } public Container DocumentHost { get; } public WriteDocumentSurface DocumentSurface { get; } public Container StatusBar { get; }
    public Input TitleInput { get; } public HavenText DocumentPositionText { get; } public HavenText StatusText { get; } public HavenText ZoomStatusText { get; }
    public HavenButton PreviousButton { get; } public HavenButton NextButton { get; } public HavenButton NewButton { get; } public HavenButton ImportButton { get; } public HavenButton ExportButton { get; } public HavenButton SaveButton { get; } public HavenButton UndoButton { get; } public HavenButton RedoButton { get; } public HavenButton HomeTab { get; } public HavenButton InsertTab { get; } public HavenButton LayoutTab { get; } public HavenButton ReviewTab { get; } public HavenButton StatusZoomOutButton { get; } public HavenButton StatusZoomInButton { get; }
    public IReadOnlyDictionary<Guid, Input> BlockInputs => _blockInputs; public NotesDocument? Document => _editor?.Document;
    public Guid? SelectedBlockId => _editor?.SelectedBlockId; public string SelectedText => _editor?.SelectedText ?? string.Empty;

    public void SetDocument(NotesDocument document, int index, int count) { if (_editor is not null) _editor.Changed -= OnEditorChanged; _editor = new WriteDocumentEditor(document); _editor.Changed += OnEditorChanged; _pendingAiChange = document.AiChanges.LastOrDefault(change => change.Status == NotesAiChangeStatus.Proposed); _suppress = true; try { TitleInput.Text = document.Title; TitleInput.SetValue(HavenProperties.Enabled, true); } finally { _suppress = false; } DocumentPositionText.Content = $"{index + 1} of {Math.Max(1, count)} · v{document.Version}"; RebuildAll(); }
    public void SetLibrary(IReadOnlyList<NotesDocumentSummary> documents)
    {
        if (_editor is not null) _editor.Changed -= OnEditorChanged;
        _editor = null; _pendingAiChange = null; _libraryDocuments = documents ?? [];
        _suppress = true; try { TitleInput.Text = "Write"; TitleInput.SetValue(HavenProperties.Enabled, false); } finally { _suppress = false; }
        DocumentPositionText.Content = _libraryDocuments.Count == 1 ? "1 local document" : $"{_libraryDocuments.Count} local documents";
        RebuildAll();
    }
    public void SetAiModels(IReadOnlyList<string> models) { _aiModels = models ?? []; if (!_aiModels.Contains(_selectedAiModel, StringComparer.Ordinal)) _selectedAiModel = _aiModels.FirstOrDefault() ?? string.Empty; if (_tab == WordWriteRibbonTab.Review) RebuildRibbon(); }
    public void SetPendingAiChange(NotesAiChange? change) { _pendingAiChange = change; if (_tab == WordWriteRibbonTab.Review) RebuildRibbon(); }
    public bool ApplyPendingAiChange() { if (_editor is null || _pendingAiChange is null || !_editor.ApplyAiChange(_pendingAiChange)) return false; _pendingAiChange = null; RebuildAll(); return true; }
    public bool RejectPendingAiChange() { if (_editor is null || _pendingAiChange is null || !_editor.RejectAiChange(_pendingAiChange)) return false; _pendingAiChange = null; RebuildAll(); return true; }
    public void SetTitleFromModel(string title) { _suppress = true; try { TitleInput.Text = title ?? string.Empty; } finally { _suppress = false; } }
    public void SetStatus(string text) => StatusText.Content = text ?? string.Empty;
    public void InsertMedia(NotesMediaData media) { if (_editor is null) return; _editor.InsertMedia(media); RebuildAll(); }
    public void SetBusy(bool busy) { foreach (var button in Root.DescendantsAndSelf().OfType<HavenButton>()) button.SetValue(HavenProperties.Enabled, !busy); TitleInput.SetValue(HavenProperties.Enabled, !busy && _editor is not null); if (!busy) RefreshCommands(); }

    private void OnTitleInvalidated(object? sender, EventArgs e) { if (_suppress || _editor is null || TitleInput.Text == _editor.Document.Title) return; _editor.SetTitle(TitleInput.Text); }
    private void OnEditorChanged(object? sender, EventArgs e) { if (_editor is null) return; _suppress = true; try { if (TitleInput.Text != _editor.Document.Title) TitleInput.Text = _editor.Document.Title; } finally { _suppress = false; } DocumentSurface.InvalidateDocument(); // Word counts over the whole document are expensive: settle for 300ms while typing.
_statsDebouncer.Schedule(UpdateStats); RefreshCommands(); DocumentChanged?.Invoke(this, EventArgs.Empty); }
    private void SetTab(WordWriteRibbonTab tab) { _tab = tab; RebuildRibbon(); }
    private void RebuildAll() { RebuildRibbon(); RebuildDocument(); UpdateStats(); RefreshCommands(); }
    private void RefreshCommands() { if (_editor is null) return; UndoButton.SetValue(HavenProperties.Enabled, _editor.CanUndo); RedoButton.SetValue(HavenProperties.Enabled, _editor.CanRedo); StyleRibbonTab(HomeTab, _tab == WordWriteRibbonTab.Home); StyleRibbonTab(InsertTab, _tab == WordWriteRibbonTab.Insert); StyleRibbonTab(LayoutTab, _tab == WordWriteRibbonTab.Layout); StyleRibbonTab(ReviewTab, _tab == WordWriteRibbonTab.Review); }
    private void RebuildRibbon() { RibbonContent.Children.ToList().ForEach(child => child.Parent?.Remove(child)); if (_editor is null) return; switch (_tab) { case WordWriteRibbonTab.Home: HomeModern(); break; case WordWriteRibbonTab.Insert: InsertModern(); break; case WordWriteRibbonTab.Layout: LayoutModern(); break; case WordWriteRibbonTab.Review: Review(); AddReadAloudControls(); break; } BuildRibbonGroups(); RefreshCommands(); }

    private void Home()
    {
        var editor = _editor!; var block = editor.SelectedBlock; var run = editor.ActiveRun; var styles = editor.Document.Styles; var styleIndex = block is null ? -1 : styles.FindIndex(value => value.Id.Equals(block.StyleId, StringComparison.OrdinalIgnoreCase)); var style = Choice("Write.Home.Style", "Paragraph style", styles.Select(value => value.Id).ToArray(), styleIndex); style.SelectionChanged += (_, _) => { if (style.SelectedItem is { } id) Apply(value => { value.ApplyStyle(id); return true; }); }; RibbonContent.Add(style);
        var font = Field("Write.Home.Font", "Font family", "Font"); font.Text = run?.FontFamily ?? "Montserrat"; font.SetValue(HavenProperties.Width, HavenLength.Px(145)); font.Invalidated += (_, _) => { if (!_suppress && run is not null && !font.Text.Equals(run.FontFamily, StringComparison.Ordinal)) editor.SetFontFamily(font.Text); }; RibbonContent.Add(font);
        RibbonContent.Add(NumberField("Write.Home.Size", "Font size", run?.FontSize ?? 14, editor.SetFontSize, 68)); RibbonContent.Add(Format("Write.Home.Bold", "B", run?.Bold == true, WriteCharacterFormat.Bold)); RibbonContent.Add(Format("Write.Home.Italic", "I", run?.Italic == true, WriteCharacterFormat.Italic)); RibbonContent.Add(Format("Write.Home.Underline", "U", run?.Underline == true, WriteCharacterFormat.Underline)); RibbonContent.Add(Format("Write.Home.Strike", "S", run?.StrikeThrough == true, WriteCharacterFormat.StrikeThrough));
        var fore = Field("Write.Home.Foreground", "Text colour", "#FF000000"); fore.Text = run?.Foreground ?? "#FFEEEEEE"; fore.SetValue(HavenProperties.Width, HavenLength.Px(112)); fore.Invalidated += (_, _) => { if (!_suppress) editor.SetForeground(fore.Text); }; RibbonContent.Add(fore); var high = Field("Write.Home.Highlight", "Highlight colour", "#FFFFFF00"); high.Text = run?.Background ?? "#00000000"; high.SetValue(HavenProperties.Width, HavenLength.Px(112)); high.Invalidated += (_, _) => { if (!_suppress) editor.SetBackground(high.Text); }; RibbonContent.Add(high); var link = Field("Write.Home.Link", "Hyperlink", "https://"); link.Text = run?.Link ?? string.Empty; link.SetValue(HavenProperties.Width, HavenLength.Px(150)); link.Invalidated += (_, _) => { if (!_suppress) editor.SetLink(link.Text); }; RibbonContent.Add(link);
        foreach (var alignment in Enum.GetValues<NotesTextAlignment>()) { var button = Btn("Write.Align." + alignment, alignment.ToString(), block?.Paragraph.Alignment == alignment ? ButtonVariant.Primary : ButtonVariant.Tertiary); button.Invoked += (_, _) => Apply(value => { value.SetAlignment(alignment); return true; }); RibbonContent.Add(button); }
        RibbonContent.Add(NumberField("Write.Home.LineSpacing", "Line spacing", block?.Paragraph.LineSpacing ?? 1.25, editor.SetLineSpacing, 78)); RibbonContent.Add(NumberField("Write.Home.Indent", "Left indent", block?.Paragraph.IndentLeft ?? 0, editor.SetLeftIndent, 78)); RibbonContent.Add(NumberField("Write.Home.FirstIndent", "First-line indent", block?.Paragraph.FirstLineIndent ?? 0, editor.SetFirstLineIndent, 86));
        var split = Btn("Write.Home.SplitRun", "Split run"); split.Invoked += (_, _) => Apply(value => value.SplitRunAtCaret()); RibbonContent.Add(split); var merge = Btn("Write.Home.MergeRun", "Merge run"); merge.Invoked += (_, _) => Apply(value => value.MergeRunWithPrevious()); RibbonContent.Add(merge); var up = Btn("Write.Home.Up", "↑ Block"); up.Invoked += (_, _) => Apply(value => value.MoveSelected(-1)); RibbonContent.Add(up); var down = Btn("Write.Home.Down", "↓ Block"); down.Invoked += (_, _) => Apply(value => value.MoveSelected(1)); RibbonContent.Add(down); var delete = Btn("Write.Home.Delete", "Delete block", ButtonVariant.Danger); delete.Invoked += (_, _) => Apply(value => value.DeleteSelected()); RibbonContent.Add(delete);
    }

    internal void ShowInsertForTest() => Insert();
    internal void ShowReviewForTest() => Review();

    private void Insert() { AddInsert("Paragraph", NotesBlockKind.Paragraph); AddInsert("Heading 1", NotesBlockKind.Heading, style: "heading-1"); AddInsert("Heading 2", NotesBlockKind.Heading, style: "heading-2"); AddInsert("Quote", NotesBlockKind.Quote); AddInsert("Code", NotesBlockKind.Code); AddInsert("Bullets", NotesBlockKind.List, NotesListKind.Bulleted); AddInsert("Numbering", NotesBlockKind.List, NotesListKind.Numbered); AddInsert("Checklist", NotesBlockKind.List, NotesListKind.Checklist); var tableRows = 3d; var tableColumns = 3d; RibbonContent.Add(NumberField("Write.Insert.TableRows", "Table rows", tableRows, value => tableRows = Math.Clamp(Math.Round(value), 1, 100), 72)); RibbonContent.Add(NumberField("Write.Insert.TableColumns", "Table columns", tableColumns, value => tableColumns = Math.Clamp(Math.Round(value), 1, 50), 82)); var table = Btn("Write.Insert.Table", "Insert table"); table.Invoked += (_, _) => { if (_editor is null) return; _editor.InsertTable((int)tableRows, (int)tableColumns); RebuildAll(); }; RibbonContent.Add(table); var image = Btn("Write.Insert.Image", "Image"); image.Invoked += (_, _) => ImageRequested?.Invoke(this, EventArgs.Empty); RibbonContent.Add(image); var shape = Btn("Write.Insert.CustomShape", "Custom shape"); shape.Invoked += (_, _) => { if (_editor is null) return; _editor.InsertCustomShape(DocumentVectorShapes.CreateEditableStarter()); RebuildAll(); }; RibbonContent.Add(shape); AddInsert("Equation", NotesBlockKind.Equation); var pageBreak = Btn("Write.Insert.PageBreak", "Page break"); pageBreak.Invoked += (_, _) => { if (_editor is null) return; _editor.InsertPageBreak(); RebuildAll(); }; RibbonContent.Add(pageBreak); AddInsert("Divider", NotesBlockKind.Divider); }
    private void AddInsert(string label, NotesBlockKind kind, NotesListKind listKind = NotesListKind.Bulleted, string? style = null) { var button = Btn("Write.Insert." + label.Replace(" ", string.Empty, StringComparison.Ordinal), label); button.Invoked += (_, _) => { if (_editor is null) return; var block = _editor.InsertBlock(kind, listKind); if (style is not null) { _editor.SelectBlock(block.Id); _editor.ApplyStyle(style); } RebuildAll(); }; RibbonContent.Add(button); }

    private void Layout()
    {
        var editor = _editor!; var a4 = Btn("Write.Layout.A4", "A4"); a4.Invoked += (_, _) => Apply(value => { value.SetPagePreset("A4"); return true; }); RibbonContent.Add(a4); var letter = Btn("Write.Layout.Letter", "Letter"); letter.Invoked += (_, _) => Apply(value => { value.SetPagePreset("Letter"); return true; }); RibbonContent.Add(letter); foreach (var orientation in new[] { "Portrait", "Landscape" }) { var button = Btn("Write.Layout." + orientation, orientation, editor.Document.PageSetup.Orientation.Equals(orientation, StringComparison.OrdinalIgnoreCase) ? ButtonVariant.Primary : ButtonVariant.Tertiary); button.Invoked += (_, _) => Apply(value => { value.SetOrientation(orientation); return true; }); RibbonContent.Add(button); } RibbonContent.Add(NumberField("Write.Layout.Margins", "Margins in points", editor.Document.PageSetup.MarginTopPoints, editor.SetMargins, 96)); var numbers = new Toggle { Name = "Write.Layout.PageNumbers", IsChecked = editor.Document.PageSetup.ShowPageNumbers }; numbers.Accessibility.AccessibleName = "Show page numbers"; numbers.CheckedChanged += (_, _) => { if (!_suppress) editor.SetPageNumbers(numbers.IsChecked); }; RibbonContent.Add(numbers); foreach (var mode in new[] { NotesLayoutMode.Paginated, NotesLayoutMode.Continuous }) { var button = Btn("Write.Layout.Mode." + mode, mode.ToString(), editor.Document.LayoutMode == mode ? ButtonVariant.Primary : ButtonVariant.Tertiary); button.Invoked += (_, _) => Apply(value => { value.SetLayout(mode); return true; }); RibbonContent.Add(button); } RibbonContent.Add(Caption($"{editor.Document.PageSetup.WidthPoints:0} × {editor.Document.PageSetup.HeightPoints:0} pt"));
    }

    private void Review()
    {
        var editor = _editor!; var stats = editor.Statistics; RibbonContent.Add(Caption($"{stats.Words} words · {stats.Characters} chars · {stats.Paragraphs} paragraphs · {stats.ReadingMinutes} min")); var modelIndex = _aiModels.ToList().FindIndex(value => value.Equals(_selectedAiModel, StringComparison.Ordinal)); var aiModel = Choice("Write.Review.AiModel", "AI model", _aiModels, modelIndex); aiModel.SelectionChanged += (_, _) => _selectedAiModel = aiModel.SelectedItem ?? string.Empty; RibbonContent.Add(aiModel); var aiInstruction = Field("Write.Review.AiInstruction", "AI edit instruction", "Describe the edit"); aiInstruction.Text = _aiInstruction; aiInstruction.SetValue(HavenProperties.Width, HavenLength.Px(220)); aiInstruction.Invalidated += (_, _) => _aiInstruction = aiInstruction.Text; RibbonContent.Add(aiInstruction); var aiContext = new Toggle { Name = "Write.Review.AiContext", IsChecked = _allowAiDocumentContext }; aiContext.Accessibility.AccessibleName = "Allow full document context for AI proposal"; aiContext.CheckedChanged += (_, _) => _allowAiDocumentContext = aiContext.IsChecked; RibbonContent.Add(aiContext); var propose = Btn("Write.Review.AiPropose", "Propose edit", ButtonVariant.Primary); propose.Invoked += (_, _) => { if (string.IsNullOrWhiteSpace(_aiInstruction) || string.IsNullOrWhiteSpace(_selectedAiModel)) { SetStatus("Choose a model and describe the edit first."); return; } AiProposalRequested?.Invoke(_aiInstruction.Trim(), _allowAiDocumentContext, _selectedAiModel); }; RibbonContent.Add(propose); var find = Field("Write.Review.Find", "Find text", "Find"); find.Text = _find; find.SetValue(HavenProperties.Width, HavenLength.Px(140)); find.Invalidated += (_, _) => _find = find.Text; RibbonContent.Add(find); var replace = Field("Write.Review.Replace", "Replacement text", "Replace with"); replace.Text = _replace; replace.SetValue(HavenProperties.Width, HavenLength.Px(140)); replace.Invalidated += (_, _) => _replace = replace.Text; RibbonContent.Add(replace); var findButton = Btn("Write.Review.FindButton", "Find"); findButton.Invoked += (_, _) => SetStatus(editor.Find(_find).Count + " match(es)"); RibbonContent.Add(findButton); var replaceAll = Btn("Write.Review.ReplaceAll", "Replace all"); replaceAll.Invoked += (_, _) => { var count = editor.ReplaceAll(_find, _replace); RebuildAll(); SetStatus($"Replaced {count} match(es)"); }; RibbonContent.Add(replaceAll);
        var comment = Field("Write.Review.Comment", "Comment", "Comment on selected block"); comment.Text = _comment; comment.SetValue(HavenProperties.Width, HavenLength.Px(190)); comment.Invalidated += (_, _) => _comment = comment.Text; RibbonContent.Add(comment); var addComment = Btn("Write.Review.AddComment", "Add comment"); addComment.Invoked += (_, _) => { editor.AddComment(_comment); _comment = string.Empty; RebuildAll(); }; RibbonContent.Add(addComment);
        var sourceTitle = Field("Write.Review.SourceTitle", "Source title", "Source title"); sourceTitle.Text = _citationTitle; sourceTitle.SetValue(HavenProperties.Width, HavenLength.Px(150)); sourceTitle.Invalidated += (_, _) => _citationTitle = sourceTitle.Text; RibbonContent.Add(sourceTitle); var authors = Field("Write.Review.Authors", "Source authors", "Authors"); authors.Text = _citationAuthors; authors.SetValue(HavenProperties.Width, HavenLength.Px(120)); authors.Invalidated += (_, _) => _citationAuthors = authors.Text; RibbonContent.Add(authors); var url = Field("Write.Review.Url", "Source URL", "https://"); url.Text = _citationUrl; url.SetValue(HavenProperties.Width, HavenLength.Px(150)); url.Invalidated += (_, _) => _citationUrl = url.Text; RibbonContent.Add(url); var cite = Btn("Write.Review.AddSource", "Add source"); cite.Invoked += (_, _) => { editor.AddCitation(_citationTitle, _citationAuthors, _citationUrl); _citationTitle = _citationAuthors = _citationUrl = string.Empty; RebuildAll(); }; RibbonContent.Add(cite); if (_pendingAiChange is { Status: NotesAiChangeStatus.Proposed } pending) { var original = Field("Write.Review.AiOriginal", "Original content", "Original"); original.Text = pending.OriginalContent; original.Multiline = true; original.SetValue(HavenProperties.Width, HavenLength.Px(230)); original.SetValue(HavenProperties.Enabled, false); RibbonContent.Add(original); var proposed = Field("Write.Review.AiProposed", "Proposed content", "Proposal"); proposed.Text = pending.ProposedContent; proposed.Multiline = true; proposed.SetValue(HavenProperties.Width, HavenLength.Px(260)); proposed.SetValue(HavenProperties.Enabled, false); RibbonContent.Add(proposed); RibbonContent.Add(Caption(string.IsNullOrWhiteSpace(pending.Explanation) ? "AI proposal ready for review. Nothing has been applied." : pending.Explanation)); var apply = Btn("Write.Review.AiApply", "Apply proposal", ButtonVariant.Primary); apply.Invoked += (_, _) => { if (ApplyPendingAiChange()) AiApplyRequested?.Invoke(this, EventArgs.Empty); }; RibbonContent.Add(apply); var reject = Btn("Write.Review.AiReject", "Reject", ButtonVariant.Danger); reject.Invoked += (_, _) => { if (RejectPendingAiChange()) AiRejectRequested?.Invoke(this, EventArgs.Empty); }; RibbonContent.Add(reject); } RibbonContent.Add(Caption($"Comments {editor.Document.Comments.Count} · Sources {editor.Document.Citations.Count} · Revisions {editor.Document.Revisions.Count}"));
    }

    private void RebuildDocument()
    {
        _blockInputs.Clear(); foreach (var child in DocumentHost.Children.ToList()) child.Parent?.Remove(child);
        DocumentHost.SetValue(HavenProperties.MaxWidth, HavenLength.Auto); DocumentHost.SetValue(HavenProperties.Padding, HavenThickness.Parse("0px"));
        if (_editor is null) {
            var actions = Pill("Write.Library.Actions"); var create = Btn("Write.Pill.New", "New", ButtonVariant.Primary); create.Invoked += (_, _) => NewRequested?.Invoke(this, EventArgs.Empty); var import = Btn("Write.Pill.Import", "Import"); import.Invoked += (_, _) => ImportRequested?.Invoke(this, EventArgs.Empty); actions.Add(create); actions.Add(import); DocumentHost.Add(actions);
            foreach (var document in _libraryDocuments) { var card = Btn($"Write.Library.Card.{document.Id:N}", $"{document.Title} � {document.WordCount} words � v{document.Version}"); card.Invoked += (_, _) => DocumentOpenRequested?.Invoke(document.Id); DocumentHost.Add(card); } return;
        }
        var documentActions = Pill("Write.Document.Actions"); var library = Btn("Write.Pill.Library", "Library"); library.Invoked += (_, _) => LibraryRequested?.Invoke(this, EventArgs.Empty); var save = Btn("Write.Pill.Save", "Save", ButtonVariant.Primary); save.Invoked += (_, _) => SaveRequested?.Invoke(this, EventArgs.Empty); documentActions.Add(library); documentActions.Add(save); DocumentHost.Add(documentActions);
        DocumentSurface.SetEditor(_editor); DocumentHost.Add(DocumentSurface);
    }

    private HavenElement Block(NotesBlock block)
    {
        var card = new Container { Name = $"Write.Word.Block.{block.Id:N}", Layout = HavenLayout.Vertical }; card.SetValue(HavenProperties.Gap, HavenLength.Px(5)); card.SetValue(HavenProperties.Padding, HavenThickness.Parse("5px")); if (_editor?.SelectedBlockId == block.Id) { card.SetValue(HavenProperties.BorderColor, "AccentSecondary"); card.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1)); card.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(6))); } if (block.Paragraph.PageBreakBefore) card.Add(Caption("Page break")); var header = Bar($"Write.Word.Block.{block.Id:N}.Header", 0); var select = Btn($"Write.Word.Block.{block.Id:N}.Select", Label(block), _editor?.SelectedBlockId == block.Id ? ButtonVariant.Primary : ButtonVariant.Ghost); select.Invoked += (_, _) => { _editor?.SelectBlock(block.Id); RebuildAll(); }; header.Add(select); if (block.Runs.Count > 1) header.Add(Caption($"{block.Runs.Count} formatted runs · ribbon targets run at caret")); card.Add(header); if (_editor?.SelectedBlockId == block.Id) card.Add(ContextPill(block)); switch (block.Kind) { case NotesBlockKind.Paragraph or NotesBlockKind.Heading or NotesBlockKind.Quote or NotesBlockKind.Code: card.Add(TextBlock(block)); break; case NotesBlockKind.List: ListBlock(card, block); break; case NotesBlockKind.Table: TableBlock(card, block); break; case NotesBlockKind.Equation: EquationBlock(card, block); break; case NotesBlockKind.Image or NotesBlockKind.Audio or NotesBlockKind.Video: MediaBlock(card, block); break; case NotesBlockKind.Shape: ShapeBlock(card, block); break; case NotesBlockKind.Divider: var line = new Container(); line.SetValue(HavenProperties.Height, HavenLength.Px(1)); line.SetValue(HavenProperties.Background, "Border"); card.Add(line); break; default: card.Add(Caption($"{block.Kind} is preserved in the native document and remains available to its specialist editor.")); break; } return card;
    }

    private Input TextBlock(NotesBlock block)
    {
        var input = Field($"Write.Word.Block.{block.Id:N}.Input", block.Kind + " text", block.Kind == NotesBlockKind.Heading ? "Heading" : "Type here…"); input.Multiline = true; input.Text = block.Runs.Count > 0 ? string.Concat(block.Runs.Select(run => run.Text)) : block.PlainText; input.SetValue(HavenProperties.MinHeight, HavenLength.Px(block.Kind == NotesBlockKind.Heading ? 56 : 84)); var run = block.Runs.FirstOrDefault(); if (run is not null) { input.SetValue(HavenProperties.FontFamily, run.FontFamily); input.SetValue(HavenProperties.FontSize, run.FontSize); input.SetValue(HavenProperties.FontWeight, run.Bold ? 700 : 400); } else if (block.Kind == NotesBlockKind.Heading) { input.SetValue(HavenProperties.FontSize, 26d); input.SetValue(HavenProperties.FontWeight, 700); } if (block.Kind == NotesBlockKind.Code) input.SetValue(HavenProperties.FontFamily, "Cascadia Mono"); input.Invalidated += (_, _) => { if (_suppress || _editor is null) return; _editor.SelectBlock(block.Id, input.CaretIndex, input.SelectionStart, input.SelectionEnd); _editor.ReplaceSelectedText(input.Text, input.CaretIndex); }; _blockInputs[block.Id] = input; return input;
    }

    private void ListBlock(Container card, NotesBlock block)
    {
        if (block.List is null) return; var type = Choice($"Write.Word.List.{block.Id:N}.Type", "List type", Enum.GetNames<NotesListKind>(), (int)block.List.Kind); type.SelectionChanged += (_, _) => { if (_editor is null || type.SelectedItem is null || !Enum.TryParse<NotesListKind>(type.SelectedItem, out var kind)) return; _editor.SelectBlock(block.Id); _editor.SetListKind(kind); RebuildAll(); }; card.Add(type); foreach (var item in block.List.Items) { var row = Bar($"Write.Word.List.{item.Id:N}", 0); if (block.List.Kind == NotesListKind.Checklist) { var check = new Toggle { IsChecked = item.Checked }; check.Accessibility.AccessibleName = "Checklist item complete"; check.CheckedChanged += (_, _) => { _editor?.SelectBlock(block.Id); _editor?.ToggleListItem(item.Id, check.IsChecked); }; row.Add(check); } var input = Field($"Write.Word.List.{item.Id:N}.Text", "List item", "List item"); input.Text = item.Text; input.SetValue(HavenProperties.Width, HavenLength.Px(440)); input.Invalidated += (_, _) => { if (_suppress) return; _editor?.SelectBlock(block.Id); _editor?.UpdateListItem(item.Id, input.Text); }; row.Add(input); row.Add(NumberField($"Write.Word.List.{item.Id:N}.Level", "Nesting level", item.Level, value => { _editor?.SelectBlock(block.Id); _editor?.SetListItemLevel(item.Id, (int)value); }, 68)); card.Add(row); } var add = Btn($"Write.Word.List.{block.Id:N}.Add", "+ List item"); add.Invoked += (_, _) => { _editor?.SelectBlock(block.Id); _editor?.AddListItem(); RebuildAll(); }; card.Add(add);
    }

    private void TableBlock(Container card, NotesBlock block)
    {
        if (block.Table is null || block.Table.Rows.Count == 0) return; var controls = Bar($"Write.Word.Table.{block.Id:N}.Controls", 0); foreach (var pair in new[] { ("+ Row", 1), ("− Row", 2), ("+ Column", 3), ("− Column", 4) }) { var button = Btn($"Write.Word.Table.{block.Id:N}.{pair.Item2}", pair.Item1); button.Invoked += (_, _) => { if (_editor is null) return; _editor.SelectBlock(block.Id); switch (pair.Item2) { case 1: _editor.AddTableRow(); break; case 2: _editor.RemoveTableRow(); break; case 3: _editor.AddTableColumn(); break; case 4: _editor.RemoveTableColumn(); break; } RebuildAll(); }; controls.Add(button); } var merge = Btn($"Write.Word.Table.{block.Id:N}.Merge", "Merge right"); merge.Invoked += (_, _) => { _editor?.SelectBlock(block.Id); Apply(value => value.MergeTableCellRight()); }; controls.Add(merge); var split = Btn($"Write.Word.Table.{block.Id:N}.Split", "Split cell"); split.Invoked += (_, _) => { _editor?.SelectBlock(block.Id); Apply(value => value.SplitTableCell()); }; controls.Add(split); card.Add(controls); var columns = block.Table.Rows.Max(row => row.Cells.Sum(cell => Math.Max(1, cell.ColumnSpan))); var grid = new Container { Name = $"Write.Word.Table.{block.Id:N}", Layout = HavenLayout.Grid, Columns = string.Join(' ', Enumerable.Repeat("1fr", columns)), Rows = string.Join(' ', Enumerable.Repeat("Auto", block.Table.Rows.Count)) }; grid.SetValue(HavenProperties.Gap, HavenLength.Px(3)); for (var row = 0; row < block.Table.Rows.Count; row++) for (var column = 0; column < block.Table.Rows[row].Cells.Count; column++) { var cell = block.Table.Rows[row].Cells[column]; var input = Field($"Write.Word.Table.Cell.{cell.Id:N}", $"Table cell {row + 1}, {column + 1}", "Cell"); input.Text = cell.Text; input.Multiline = true; input.SetValue(HavenProperties.Row, row); input.SetValue(HavenProperties.Column, column); input.SetValue(HavenProperties.RowSpan, Math.Max(1, cell.RowSpan)); input.SetValue(HavenProperties.ColumnSpan, Math.Max(1, cell.ColumnSpan)); input.SetValue(HavenProperties.MinHeight, HavenLength.Px(48)); if (_editor?.SelectedTableCellId == cell.Id) { input.SetValue(HavenProperties.BorderColor, "AccentSecondary"); input.SetValue(HavenProperties.BorderWidth, HavenLength.Px(2)); } if (row == 0 && block.Table.HeaderRow) input.SetValue(HavenProperties.FontWeight, 700); input.Invalidated += (_, _) => { if (_suppress) return; _editor?.SelectTableCell(block.Id, cell.Id); _editor?.UpdateTableCell(cell.Id, input.Text); }; grid.Add(input); } card.Add(grid);
    }

    private void ShapeBlock(Container card, NotesBlock block)
    {
        if (block.VectorShape is not { } shape) { card.Add(Caption("This shape has no editable vector geometry.")); return; }
        var info = Caption($"Native vector · {shape.Paths.Count} path(s) · {shape.Paths.Sum(path => path.Subpaths.Sum(subpath => subpath.Nodes.Count))} node(s) · {shape.ConnectorPoints.Count} connector point(s)");
        card.Add(info);
        var name = Field($"Write.Word.Shape.{block.Id:N}.Name", "Custom shape name", "Custom shape"); name.Text = shape.Name; name.SetValue(HavenProperties.Width, HavenLength.Px(220));
        name.Invalidated += (_, _) => { if (_suppress || _editor is null || name.Text == shape.Name) return; _editor.SelectBlock(block.Id); if (_editor.UpdateSelectedCustomShape(editor => editor.SetName(name.Text))) RebuildAll(); }; card.Add(name);
        var alt = Field($"Write.Word.Shape.{block.Id:N}.Alt", "Custom shape alternative text", "Describe this shape"); alt.Text = shape.AccessibilityDescription; alt.SetValue(HavenProperties.Width, HavenLength.Px(300));
        alt.Invalidated += (_, _) => { if (_suppress || _editor is null || alt.Text == shape.AccessibilityDescription) return; _editor.SelectBlock(block.Id); if (_editor.UpdateSelectedCustomShape(editor => editor.SetAccessibilityDescription(alt.Text))) RebuildAll(); }; card.Add(alt);
        if (shape.Paths.FirstOrDefault() is { } firstPath)
        {
            var style = Bar($"Write.Word.Shape.{block.Id:N}.Style", 0);
            var fill = Field($"Write.Word.Shape.{block.Id:N}.Fill", "Shape fill colour", "#FFE9EEF8"); fill.Text = firstPath.Fill.Color; fill.SetValue(HavenProperties.Width, HavenLength.Px(115));
            var stroke = Field($"Write.Word.Shape.{block.Id:N}.Stroke", "Shape stroke colour", "#FF384860"); stroke.Text = firstPath.Stroke.Color; stroke.SetValue(HavenProperties.Width, HavenLength.Px(115));
            void UpdateStyle() { if (_suppress || _editor is null) return; _editor.SelectBlock(block.Id); if (_editor.UpdateSelectedCustomShape(editor => editor.SetPathStyle(firstPath.Id, fill.Text, stroke.Text, firstPath.Stroke.Width))) RebuildAll(); }
            fill.Invalidated += (_, _) => { if (fill.Text != firstPath.Fill.Color) UpdateStyle(); }; stroke.Invalidated += (_, _) => { if (stroke.Text != firstPath.Stroke.Color) UpdateStyle(); }; style.Add(fill); style.Add(stroke); card.Add(style);
        }
        var transforms = Bar($"Write.Word.Shape.{block.Id:N}.Transform", 0);
        foreach (var pair in new[] { ("Rotate −15°", -15d, 1d), ("Rotate +15°", 15d, 1d), ("Scale 90%", 0d, .9d), ("Scale 110%", 0d, 1.1d) })
        {
            var button = Btn($"Write.Word.Shape.{block.Id:N}.Transform.{pair.Item1.Replace(" ", string.Empty, StringComparison.Ordinal)}", pair.Item1);
            button.Invoked += (_, _) => { if (_editor is null || block.VectorShape is not { } current) return; _editor.SelectBlock(block.Id); var transform = current.Transform; _editor.UpdateSelectedCustomShape(editor => editor.SetTransform(new DocumentVectorTransform { TranslateX = transform.TranslateX, TranslateY = transform.TranslateY, ScaleX = transform.ScaleX * pair.Item3, ScaleY = transform.ScaleY * pair.Item3, RotationDegrees = transform.RotationDegrees + pair.Item2, OriginX = transform.OriginX, OriginY = transform.OriginY })); RebuildAll(); };
            transforms.Add(button);
        }
        card.Add(transforms);
    }

    private void EquationBlock(Container card, NotesBlock block) { if (block.Equation is null) return; var source = Field($"Write.Word.Equation.{block.Id:N}.Source", "Equation source", "LaTeX"); source.Text = block.Equation.Source; source.Multiline = true; source.SetValue(HavenProperties.FontFamily, "Cascadia Mono"); var alt = Field($"Write.Word.Equation.{block.Id:N}.Alt", "Equation accessible description", "Accessible description"); alt.Text = block.Equation.AccessibleAlternative; alt.Multiline = true; void Update() { if (_suppress || _editor is null) return; _editor.SelectBlock(block.Id); _editor.UpdateEquation(source.Text, alt.Text); } source.Invalidated += (_, _) => Update(); alt.Invalidated += (_, _) => Update(); card.Add(source); card.Add(alt); }
    private void MediaBlock(Container card, NotesBlock block) { if (block.Media is null) return; card.Add(Caption($"{block.Media.OriginalName} · {block.Media.MediaType} · {block.Media.Width:0}×{block.Media.Height:0}")); var alt = Field($"Write.Word.Media.{block.Id:N}.Alt", "Image alternative text", "Alternative text"); alt.Text = block.Media.AltText; var caption = Field($"Write.Word.Media.{block.Id:N}.Caption", "Image caption", "Caption"); caption.Text = block.Media.Caption; var wrapItems = new[] { "Inline", "Square", "Tight", "Behind text", "In front of text" }; var wrap = Choice($"Write.Word.Media.{block.Id:N}.Wrap", "Text wrapping", wrapItems, Math.Max(0, Array.IndexOf(wrapItems, block.Media.Wrapping))); void Update() { if (_suppress || _editor is null) return; _editor.SelectBlock(block.Id); _editor.UpdateMedia(alt.Text, caption.Text, wrap.SelectedItem ?? "Inline"); } alt.Invalidated += (_, _) => Update(); caption.Invalidated += (_, _) => Update(); wrap.SelectionChanged += (_, _) => Update(); card.Add(alt); card.Add(caption); card.Add(wrap); }

    private Container ContextPill(NotesBlock block)
    {
        var pill = Pill($"Write.Word.ContextPill.{block.Id:N}");
        var insert = Btn($"Write.Context.{block.Id:N}.Insert", "+"); insert.Accessibility.AccessibleName = "Insert after selection"; insert.Invoked += (_, _) => SetTab(WordWriteRibbonTab.Insert); pill.Add(insert);
        if (block.Kind is NotesBlockKind.Paragraph or NotesBlockKind.Heading or NotesBlockKind.Quote or NotesBlockKind.Code)
        {
            foreach (var pair in new[] { ("B", WriteCharacterFormat.Bold), ("I", WriteCharacterFormat.Italic), ("U", WriteCharacterFormat.Underline) }) { var button = Btn($"Write.Context.{block.Id:N}.{pair.Item1}", pair.Item1); button.Invoked += (_, _) => Apply(editor => { editor.ToggleSelectionCharacter(pair.Item2); return true; }); pill.Add(button); }
            var review = Btn($"Write.Context.{block.Id:N}.Comment", "Comment"); review.Invoked += (_, _) => SetTab(WordWriteRibbonTab.Review); pill.Add(review);
        }
        else if (block.Kind == NotesBlockKind.Table)
        {
            var addRow = Btn($"Write.Context.{block.Id:N}.AddRow", "+ Row"); addRow.Invoked += (_, _) => Apply(editor => { editor.AddTableRow(); return true; }); pill.Add(addRow);
            var addColumn = Btn($"Write.Context.{block.Id:N}.AddColumn", "+ Column"); addColumn.Invoked += (_, _) => Apply(editor => { editor.AddTableColumn(); return true; }); pill.Add(addColumn);
            var merge = Btn($"Write.Context.{block.Id:N}.Merge", "Merge right"); merge.Invoked += (_, _) => Apply(editor => editor.MergeTableCellRight()); pill.Add(merge);
            var split = Btn($"Write.Context.{block.Id:N}.Split", "Split cell"); split.Invoked += (_, _) => Apply(editor => editor.SplitTableCell()); pill.Add(split);
        }
        else
        {
            var up = Btn($"Write.Context.{block.Id:N}.Up", "Move up"); up.Invoked += (_, _) => Apply(editor => editor.MoveSelected(-1)); pill.Add(up);
            var down = Btn($"Write.Context.{block.Id:N}.Down", "Move down"); down.Invoked += (_, _) => Apply(editor => editor.MoveSelected(1)); pill.Add(down);
        }
        var delete = Btn($"Write.Context.{block.Id:N}.Delete", "Delete", ButtonVariant.Danger); delete.Invoked += (_, _) => Apply(editor => editor.DeleteSelected()); pill.Add(delete);
        return pill;
    }

    private HavenButton Format(string name, string label, bool selected, WriteCharacterFormat format) { var button = Btn(name, label, selected ? ButtonVariant.Primary : ButtonVariant.Tertiary); button.SetValue(HavenProperties.FontWeight, 800); button.Invoked += (_, _) => Apply(editor => { editor.ToggleSelectionCharacter(format); return true; }); return button; }
    private Input NumberField(string name, string accessible, double value, Action<double> apply, double width) { var input = Field(name, accessible, value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)); input.Text = value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture); input.SetValue(HavenProperties.Width, HavenLength.Px(width)); input.Invalidated += (_, _) => { if (_suppress || !double.TryParse(input.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)) return; apply(parsed); }; return input; }
    private void Apply(Func<WriteDocumentEditor, bool> action) { if (_editor is null || !action(_editor)) return; RebuildAll(); }
    private void UpdateStats() { if (_editor is null) return; var stats = _editor.Statistics; StatusText.Content = $"Page 1 · {stats.Words} words · {stats.Characters} characters · {stats.ReadingMinutes} min read · autosave on"; ZoomStatusText.Content = $"{Math.Round(DocumentSurface.Zoom * 100)}%"; }
    private void SetDocumentZoom(double zoom) { DocumentSurface.SetZoom(zoom); UpdateStats(); }
    private static string Label(NotesBlock block) => block.Kind switch { NotesBlockKind.Heading => block.StyleId == "heading-2" ? "Heading 2" : "Heading 1", NotesBlockKind.List when block.List is not null => block.List.Kind + " list", _ => block.Kind.ToString() };
    private static Container Pill(string name) { var pill = new Container { Name = name, Layout = HavenLayout.Horizontal }; pill.SetValue(HavenProperties.Background, "SurfaceRaised"); pill.SetValue(HavenProperties.BorderColor, "Border"); pill.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1)); pill.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(999))); pill.SetValue(HavenProperties.Padding, HavenThickness.Parse("5px 8px")); pill.SetValue(HavenProperties.Gap, HavenLength.Px(4)); return pill; }
    private static Container Bar(string name, int row) { var bar = new Container { Name = name, Layout = HavenLayout.Horizontal }; bar.SetValue(HavenProperties.Row, row); bar.SetValue(HavenProperties.Gap, HavenLength.Px(6)); bar.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll); bar.SetValue(HavenProperties.Padding, HavenThickness.Parse("4px 18px")); return bar; }
    private static HavenButton Btn(string name, string label, ButtonVariant variant = ButtonVariant.Ghost) { var button = new HavenButton { Name = name, Content = label, Variant = variant }; button.Accessibility.AccessibleName = label; button.SetValue(HavenProperties.Foreground, "ButtonTextSecondary"); button.SetValue(HavenProperties.FontSize, 12d); button.SetValue(HavenProperties.FontWeight, 600); button.SetValue(HavenProperties.MinHeight, HavenLength.Px(30)); button.SetValue(HavenProperties.Padding, HavenThickness.Parse("5px 9px")); button.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(6))); return button; }
    private static Input Field(string name, string accessible, string placeholder) { var input = new Input { Name = name, Placeholder = placeholder }; input.Accessibility.AccessibleName = accessible; input.SetValue(HavenProperties.Foreground, "ButtonTextSecondary"); input.SetValue(HavenProperties.MinHeight, HavenLength.Px(40)); return input; }
    private static Select Choice(string name, string accessible, IReadOnlyList<string> items, int index) { var select = new Select { Name = name, Items = items, SelectedIndex = index }; select.Accessibility.AccessibleName = accessible; select.SetValue(HavenProperties.Foreground, "ButtonTextSecondary"); select.SetValue(HavenProperties.MinWidth, HavenLength.Px(116)); return select; }
    private static HavenText Caption(string text) { var value = new HavenText(text) { Level = TextLevel.Caption }; value.SetValue(HavenProperties.Foreground, "ButtonTextSecondary"); return value; }
    public void Dispose() { if (_disposed) return; _disposed = true; _statsDebouncer.Dispose(); TitleInput.Invalidated -= OnTitleInvalidated; if (_editor is not null) _editor.Changed -= OnEditorChanged; PreviousRequested = null; NextRequested = null; NewRequested = null; ImportRequested = null; ExportRequested = null; SaveRequested = null; ImageRequested = null; DocumentChanged = null; }
}
