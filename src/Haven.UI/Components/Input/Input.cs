using System.Globalization;

namespace Haven.UI.Components;

public static class InputDefaults
{
    public const string SystemClass = "Input";
    public const string FocusTransition = "InputFocus";
}

public sealed class Input : HavenElement
{
    public static readonly HavenProperty<string> TextProperty = HavenPropertyRegistry.Register(new HavenProperty<string>("Input.Text", string.Empty));
    public static readonly HavenProperty<string> PlaceholderProperty = HavenPropertyRegistry.Register(new HavenProperty<string>("Input.Placeholder", string.Empty));
    public static readonly HavenProperty<bool> MultilineProperty = HavenPropertyRegistry.Register(new HavenProperty<bool>("Input.Multiline", false));
    public static readonly HavenProperty<bool> SubmitOnEnterProperty = HavenPropertyRegistry.Register(new HavenProperty<bool>("Input.SubmitOnEnter", false));
    public static readonly HavenProperty<bool> IsSecretProperty = HavenPropertyRegistry.Register(new HavenProperty<bool>("Input.IsSecret", false));
    public static readonly HavenProperty<bool> RevealSecretProperty = HavenPropertyRegistry.Register(new HavenProperty<bool>("Input.RevealSecret", false));
    public static readonly HavenProperty<bool> AllowSecretClipboardProperty = HavenPropertyRegistry.Register(new HavenProperty<bool>("Input.AllowSecretClipboard", false));
    public static readonly HavenProperty<int> CaretIndexProperty = HavenPropertyRegistry.Register(new HavenProperty<int>("Input.CaretIndex", 0));

    public Input()
    {
        Accessibility.Role = HavenAccessibleRole.Input;
        Accessibility.Focusable = true;
        SetValue(HavenProperties.Hover, true, HavenValueSource.Default);
        SetValue(HavenProperties.MinHeight, HavenLength.Px(48), HavenValueSource.Default);
        SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(24)), HavenValueSource.Default);
        SetValue(HavenProperties.Background, "SurfaceRaised", HavenValueSource.Default);
        SetValue(HavenProperties.Padding, HavenThickness.Parse("0px 18px"), HavenValueSource.Default);
        SetValue(HavenProperties.Transition, InputDefaults.FocusTransition, HavenValueSource.Default);
    }

    public event EventHandler? TextChanged;

    public string Text
    {
        get => GetValue(TextProperty);
        set
        {
            var next = value ?? string.Empty;
            if (string.Equals(Text, next, StringComparison.Ordinal)) return;
            Update(() =>
            {
                SetValue(TextProperty, next);
                SetValue(CaretIndexProperty, NormalizeCaret(next, GetValue(CaretIndexProperty)));
            });
            TextChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string Placeholder
    {
        get => GetValue(PlaceholderProperty);
        set
        {
            var next = value ?? string.Empty;
            SetValue(PlaceholderProperty, next);
            if (string.IsNullOrWhiteSpace(Accessibility.AccessibleName)) Accessibility.AccessibleName = next;
        }
    }

    public bool Multiline { get => GetValue(MultilineProperty); set => SetValue(MultilineProperty, value); }
    public bool SubmitOnEnter { get => GetValue(SubmitOnEnterProperty); set => SetValue(SubmitOnEnterProperty, value); }
    public bool IsSecret
    {
        get => GetValue(IsSecretProperty);
        set
        {
            if (IsSecret == value) return;
            SetValue(IsSecretProperty, value);
            Accessibility.IsPassword = value;
            if (!value && RevealSecret) SetValue(RevealSecretProperty, false);
        }
    }
    public bool RevealSecret { get => GetValue(RevealSecretProperty); set => SetValue(RevealSecretProperty, value); }
    public bool AllowSecretClipboard { get => GetValue(AllowSecretClipboardProperty); set => SetValue(AllowSecretClipboardProperty, value); }
    public string DisplayText => IsSecret && !RevealSecret ? new string('•', Text.Length) : Text;
    public bool CanExposeSecretToClipboard => !IsSecret || AllowSecretClipboard;
    private int _selectionAnchor = -1;
    private readonly Stack<EditState> _undo = new();
    private readonly Stack<EditState> _redo = new();

    public int CaretIndex => GetValue(CaretIndexProperty);
    public int SelectionAnchor => _selectionAnchor >= 0 ? NormalizeCaret(Text, _selectionAnchor) : CaretIndex;
    public bool HasSelection => _selectionAnchor >= 0 && SelectionAnchor != CaretIndex;
    public int SelectionStart => HasSelection ? Math.Min(SelectionAnchor, CaretIndex) : CaretIndex;
    public int SelectionEnd => HasSelection ? Math.Max(SelectionAnchor, CaretIndex) : CaretIndex;
    public int SelectionLength => Math.Max(0, SelectionEnd - SelectionStart);
    public string SelectedText => HasSelection ? Text.Substring(SelectionStart, SelectionLength) : string.Empty;

    public void PlaceCaretAtEnd(bool extendSelection = false) => SetCaretWithSelection(Text.Length, extendSelection);
    public void PlaceCaretAtStart(bool extendSelection = false) => SetCaretWithSelection(0, extendSelection);

    public void SetSelection(int anchor, int caret)
    {
        _selectionAnchor = NormalizeCaret(Text, anchor);
        SetCaret(caret);
        if (SelectionAnchor == CaretIndex) _selectionAnchor = -1;
    }

    public void SelectAll()
    {
        if (Text.Length == 0) { _selectionAnchor = -1; SetCaret(0); return; }
        SetSelection(0, Text.Length);
    }

    public void ClearSelection() => _selectionAnchor = -1;

    public bool MoveCaret(int direction, bool extendSelection = false)
    {
        if (direction == 0) return false;
        if (!extendSelection && HasSelection)
        {
            CollapseSelectionAt(direction < 0 ? SelectionStart : SelectionEnd);
            return true;
        }
        var next = direction < 0 ? PreviousBoundary(Text, CaretIndex) : NextBoundary(Text, CaretIndex);
        if (next == CaretIndex) return false;
        SetCaretWithSelection(next, extendSelection);
        return true;
    }

    public bool InsertText(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        var insertion = Multiline ? value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n') : value.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", string.Empty, StringComparison.Ordinal);
        if (insertion.Length == 0) return false;
        PushUndoState();
        var start = SelectionStart;
        var end = SelectionEnd;
        Update(() =>
        {
            Text = HasSelection
                ? Text.Remove(start, end - start).Insert(start, insertion)
                : Text.Insert(CaretIndex, insertion);
            CollapseSelectionAt(start + insertion.Length);
        });
        return true;
    }

    public bool Backspace()
    {
        if (HasSelection)
        {
            PushUndoState();
            DeleteSelectionCore();
            return true;
        }
        var index = NormalizeCaret(Text, CaretIndex);
        if (index <= 0) return false;
        PushUndoState();
        var start = PreviousBoundary(Text, index);
        Update(() =>
        {
            Text = Text.Remove(start, index - start);
            CollapseSelectionAt(start);
        });
        return true;
    }

    public bool Delete()
    {
        if (HasSelection)
        {
            PushUndoState();
            DeleteSelectionCore();
            return true;
        }
        var index = NormalizeCaret(Text, CaretIndex);
        if (index >= Text.Length) return false;
        PushUndoState();
        var end = NextBoundary(Text, index);
        Update(() =>
        {
            Text = Text.Remove(index, end - index);
            CollapseSelectionAt(index);
        });
        return true;
    }

    public bool CutSelection()
    {
        if (!HasSelection) return false;
        PushUndoState();
        DeleteSelectionCore();
        return true;
    }

    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        var current = CaptureState();
        var previous = _undo.Pop();
        _redo.Push(current);
        RestoreState(previous);
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0) return false;
        var current = CaptureState();
        var next = _redo.Pop();
        _undo.Push(current);
        RestoreState(next);
        return true;
    }

    private void SetCaretWithSelection(int index, bool extendSelection)
    {
        if (extendSelection)
        {
            if (_selectionAnchor < 0) _selectionAnchor = CaretIndex;
        }
        else
        {
            _selectionAnchor = -1;
        }
        SetCaret(index);
        if (_selectionAnchor >= 0 && SelectionAnchor == CaretIndex) _selectionAnchor = -1;
    }

    private void CollapseSelectionAt(int index)
    {
        _selectionAnchor = -1;
        SetCaret(index);
    }

    private void DeleteSelectionCore()
    {
        var start = SelectionStart;
        var length = SelectionLength;
        Update(() =>
        {
            Text = Text.Remove(start, length);
            CollapseSelectionAt(start);
        });
    }

    private void PushUndoState()
    {
        _undo.Push(CaptureState());
        _redo.Clear();
    }

    private EditState CaptureState() => new(Text, CaretIndex, _selectionAnchor);

    private void RestoreState(EditState state)
    {
        Text = state.Text;
        _selectionAnchor = state.SelectionAnchor;
        SetCaret(state.CaretIndex);
        if (_selectionAnchor >= 0 && SelectionAnchor == CaretIndex) _selectionAnchor = -1;
    }

    private sealed record EditState(string Text, int CaretIndex, int SelectionAnchor);

    public override HavenComponentMetadata Metadata => new(
        "Input",
        "Components/Input/Input.cs",
        [InputDefaults.SystemClass],
        [InputDefaults.FocusTransition],
        "Haven owns field chrome, text editing, caret state, focus state, and rendering; platform backends only translate platform input events into Haven input events.");

    protected override void OnStateChanged()
    {
        ClearValue(HavenProperties.BorderColor, HavenValueSource.State);
        ClearValue(HavenProperties.BorderWidth, HavenValueSource.State);
        ClearValue(HavenProperties.Glow, HavenValueSource.State);
        ClearValue(HavenProperties.Opacity, HavenValueSource.State);
        ClearValue(HavenProperties.Transition, HavenValueSource.State);

        if (State.HasFlag(HavenElementState.Disabled))
        {
            SetValue(HavenProperties.Opacity, .52d, HavenValueSource.State);
            return;
        }

        if (State.HasFlag(HavenElementState.Focused))
        {
            SetValue(HavenProperties.BorderColor, "AccentSecondary", HavenValueSource.State);
            SetValue(HavenProperties.BorderWidth, HavenLength.Px(2), HavenValueSource.State);
            SetValue(HavenProperties.Glow, "AccentTertiaryGlow", HavenValueSource.State);
            SetValue(HavenProperties.Transition, InputDefaults.FocusTransition, HavenValueSource.State);
            return;
        }

        if (State.HasFlag(HavenElementState.Hover))
        {
            SetValue(HavenProperties.BorderColor, "AccentSecondary", HavenValueSource.State);
            SetValue(HavenProperties.BorderWidth, HavenLength.Px(1), HavenValueSource.State);
            SetValue(HavenProperties.Transition, InputDefaults.FocusTransition, HavenValueSource.State);
        }
    }

    private void SetCaret(int index) => SetValue(CaretIndexProperty, NormalizeCaret(Text, index));

    /// <summary>Caret moves never change text metrics, so they repaint without a measure pass.</summary>
    protected internal override HavenInvalidationKinds ClassifyValueChange(HavenProperty property) =>
        ReferenceEquals(property, CaretIndexProperty)
            ? HavenInvalidationKinds.Paint
            : base.ClassifyValueChange(property);

    private static int NormalizeCaret(string text, int index)
    {
        index = Math.Clamp(index, 0, text.Length);
        if (index == 0 || index == text.Length) return index;
        var boundaries = StringInfo.ParseCombiningCharacters(text);
        var previous = 0;
        foreach (var boundary in boundaries)
        {
            if (boundary == index) return index;
            if (boundary > index) return previous;
            previous = boundary;
        }
        return text.Length;
    }

    private static int PreviousBoundary(string text, int index)
    {
        index = NormalizeCaret(text, index);
        if (index <= 0) return 0;
        var previous = 0;
        foreach (var boundary in StringInfo.ParseCombiningCharacters(text))
        {
            if (boundary >= index) break;
            previous = boundary;
        }
        return previous;
    }

    private static int NextBoundary(string text, int index)
    {
        index = NormalizeCaret(text, index);
        foreach (var boundary in StringInfo.ParseCombiningCharacters(text))
            if (boundary > index) return boundary;
        return text.Length;
    }
}
