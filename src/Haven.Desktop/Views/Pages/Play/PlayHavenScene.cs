using Haven.Application.Play;
using Haven.UI;
using Haven.UI.Components;
using Container = Haven.UI.Components.Container;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Play;

/// <summary>
/// Haven.UI presentation for the local Games experience hub. The application-layer
/// PlaySessionService remains the source of truth for sessions and game state.
/// </summary>
internal sealed class PlayHavenScene
{
    private readonly Container _content;
    private readonly HavenText _status;

    public PlayHavenScene()
    {
        Root = new Page
        {
            Name = "Games.Root",
            Layout = HavenLayout.Grid,
            Columns = "1fr",
            Rows = "auto 1fr auto"
        };
        Set(Root, HavenProperties.Padding, HavenThickness.Parse("28px 32px"));
        Set(Root, HavenProperties.Gap, HavenLength.Px(18));
        Set(Root, HavenProperties.Background, "Transparent");

        var header = new Container { Name = "Games.Header", Layout = HavenLayout.Grid, Columns = "1fr auto", Rows = "auto" };
        Set(header, HavenProperties.Gap, HavenLength.Px(16));

        var heading = new Container { Layout = HavenLayout.Vertical };
        Set(heading, HavenProperties.Gap, HavenLength.Px(5));
        heading.Add(new HavenText("Games") { Name = "Games.Title", Level = TextLevel.H1 });
        heading.Add(Muted("Pick up a local game, continue where you left off, or create a new experience."));
        header.Add(heading);

        var create = Button("Games.Create", "Create game", ButtonVariant.Primary);
        create.Invoked += (_, _) => CreateRequested?.Invoke();
        header.Add(create);
        Root.Add(header);

        _content = new Container { Name = "Games.Content", Layout = HavenLayout.Vertical };
        Set(_content, HavenProperties.Gap, HavenLength.Px(18));
        Root.Add(_content);

        _status = new HavenText("Games run locally and keep their session state on this device.")
        {
            Name = "Games.Status",
            Level = TextLevel.Caption
        };
        Set(_status, HavenProperties.Foreground, "TextSecondary");
        Root.Add(_status);
    }

    public Page Root { get; }

    public event Action? CreateRequested;
    public event Action<PlayExperienceKind>? StartRequested;
    public event Action<Guid>? OpenSessionRequested;
    public event Action? BackRequested;
    public event Action? RestartRequested;
    public event Action<int>? ChessSquareRequested;
    public event Action<int>? QuizAnswerRequested;

    public void SetStatus(string text) => _status.Content = text;

    public void ShowHome(IReadOnlyList<PlaySessionSummary> recent)
    {
        Clear(_content);

        var hero = Card("Games.Hero");
        hero.Add(new HavenText("Play something") { Level = TextLevel.H2 });
        hero.Add(Muted("No setup screens or placeholder tiles — these experiences launch into real, persistent local sessions."));

        var featured = new Container
        {
            Name = "Games.Featured",
            Layout = HavenLayout.Grid,
            Columns = "1fr 1fr",
            Rows = "auto"
        };
        Set(featured, HavenProperties.Gap, HavenLength.Px(14));
        featured.Add(ExperienceCard(
            "Games.Chess",
            "Chess",
            "Strategy",
            "Legal move validation, check/checkmate state and a deterministic local Haven opponent.",
            PlayExperienceKind.Chess));
        featured.Add(ExperienceCard(
            "Games.Quiz",
            "Quick quiz",
            "Quiz",
            "Five local questions with persistent score and instant progression.",
            PlayExperienceKind.Quiz));
        hero.Add(featured);
        _content.Add(hero);

        var active = recent.FirstOrDefault(item => item.Status == PlaySessionStatus.Active);
        if (active is not null)
        {
            var resume = Card("Games.Continue");
            resume.Add(new HavenText("Continue playing") { Level = TextLevel.H2 });
            resume.Add(SessionRow(active, "Continue"));
            _content.Add(resume);
        }

        var recentCard = Card("Games.Recent");
        recentCard.Add(new HavenText("Recent games") { Level = TextLevel.H2 });
        if (recent.Count == 0)
        {
            recentCard.Add(Muted("Your recent sessions will appear here after you start a game."));
        }
        else
        {
            var list = new Container { Layout = HavenLayout.Vertical };
            Set(list, HavenProperties.Gap, HavenLength.Px(9));
            foreach (var item in recent.Take(8))
                list.Add(SessionRow(item, item.Status == PlaySessionStatus.Active ? "Resume" : "View"));
            recentCard.Add(list);
        }
        _content.Add(recentCard);
    }

    public void ShowChess(
        PlaySessionSnapshot session,
        ChessGameState state,
        int? selectedSquare,
        IReadOnlyCollection<int> legalMoves)
    {
        Clear(_content);
        _content.Add(SessionToolbar(session.Title));

        var shell = Card("Games.Chess.Session");
        shell.Add(new HavenText(
            state.Result == "playing"
                ? state.WhiteToMove ? "White to move · You are White" : "Black to move · Haven's local opponent is thinking"
                : state.Result)
        {
            Name = "Games.Chess.Status",
            Level = TextLevel.H3
        });

        var board = new Container
        {
            Name = "Games.Chess.Board",
            Layout = HavenLayout.Grid,
            Columns = "auto auto auto auto auto auto auto auto",
            Rows = "auto auto auto auto auto auto auto auto"
        };
        Set(board, HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);
        Set(board, HavenProperties.Margin, HavenThickness.Parse("0px 12px"));

        var legalTargets = legalMoves.ToHashSet();
        for (var square = 0; square < 64; square++)
        {
            var captured = square;
            var piece = state.Board[square];
            var lightSquare = (square / 8 + square % 8) % 2 == 0;
            var cell = Button($"Games.Chess.Square.{square}", PieceGlyph(piece), ButtonVariant.Ghost);
            Set(cell, HavenProperties.Background,
                selectedSquare == square ? "Accent"
                    : legalTargets.Contains(square) ? "AccentMuted"
                    : lightSquare ? "SurfaceRaised" : "SurfaceSecondary");
            Set(cell, HavenProperties.FontSize, 26d);
            Set(cell, HavenProperties.Width, HavenLength.Px(48));
            Set(cell, HavenProperties.Height, HavenLength.Px(48));
            Set(cell, HavenProperties.Padding, HavenThickness.Zero);
            cell.SetState(HavenElementState.Disabled, state.Result != "playing" || !state.WhiteToMove);
            cell.Accessibility.AccessibleName = SquareName(square) +
                (piece == '.' ? string.Empty : $" {PieceName(piece)}") +
                (legalTargets.Contains(square) ? " · legal move" : string.Empty);
            cell.Invoked += (_, _) => ChessSquareRequested?.Invoke(captured);
            Set(cell, HavenProperties.Row, square / 8);
            Set(cell, HavenProperties.Column, square % 8);
            board.Add(cell);
        }
        shell.Add(board);

        shell.Add(Muted(state.Result == "playing"
            ? "Moves are validated locally on this device. Select a piece, then a highlighted square."
            : "The game has finished. Restart to begin again from the starting position."));
        _content.Add(shell);
    }

    public void ShowQuiz(PlaySessionSnapshot session, QuizGameState state)
    {
        Clear(_content);
        _content.Add(SessionToolbar(session.Title));

        var shell = Card("Games.Quiz.Session");
        shell.Add(new HavenText(
            state.Completed
                ? $"Finished · {state.Score}/{state.Questions.Count}"
                : $"Question {state.QuestionIndex + 1} of {state.Questions.Count} · {state.Score} correct")
        {
            Name = "Games.Quiz.Status",
            Level = TextLevel.H3
        });

        if (state.Completed)
        {
            shell.Add(Muted("Quiz complete. Restart to try the same local question set again."));
            _content.Add(shell);
            return;
        }

        if (state.Questions.Count == 0 || state.QuestionIndex < 0 || state.QuestionIndex >= state.Questions.Count)
        {
            shell.Add(Muted("This saved quiz has no playable question data."));
            _content.Add(shell);
            return;
        }

        var question = state.Questions[state.QuestionIndex];
        shell.Add(new HavenText(question.Prompt) { Name = "Games.Quiz.Prompt", Level = TextLevel.H2 });

        var options = new Container { Name = "Games.Quiz.Options", Layout = HavenLayout.Vertical };
        Set(options, HavenProperties.Gap, HavenLength.Px(8));
        for (var index = 0; index < question.Options.Count; index++)
        {
            var answer = index;
            var optionButton = Button($"Games.Quiz.Option.{answer}", question.Options[answer], ButtonVariant.Secondary);
            optionButton.Invoked += (_, _) => QuizAnswerRequested?.Invoke(answer);
            options.Add(optionButton);
        }
        shell.Add(options);
        shell.Add(Muted("Questions and scoring run locally on this device."));
        _content.Add(shell);
    }

    private Container SessionToolbar(string title)
    {
        var bar = new Container { Name = "Games.SessionBar", Layout = HavenLayout.Grid, Columns = "Auto 1fr Auto", Rows = "auto" };
        Set(bar, HavenProperties.Gap, HavenLength.Px(10));

        var back = Button("Games.Session.Back", "Back to games", ButtonVariant.Secondary);
        back.Invoked += (_, _) => BackRequested?.Invoke();
        bar.Add(back);

        var titleText = new HavenText(title) { Level = TextLevel.H2 };
        Set(titleText, HavenProperties.Column, 1);
        bar.Add(titleText);

        var restart = Button("Games.Session.Restart", "Restart", ButtonVariant.Tertiary);
        restart.Invoked += (_, _) => RestartRequested?.Invoke();
        Set(restart, HavenProperties.Column, 2);
        bar.Add(restart);
        return bar;
    }

    private Container SessionRow(PlaySessionSummary item, string actionLabel)
    {
        var row = new Container { Layout = HavenLayout.Grid, Columns = "1fr Auto", Rows = "auto" };
        Set(row, HavenProperties.Gap, HavenLength.Px(12));

        var details = new Container { Layout = HavenLayout.Vertical };
        Set(details, HavenProperties.Gap, HavenLength.Px(3));
        details.Add(new HavenText(item.Title) { Level = TextLevel.H4 });
        details.Add(Muted(item.Subtitle));
        row.Add(details);

        var open = Button("Games.SessionRow.Open." + item.Id.ToString("N"), actionLabel, ButtonVariant.Tertiary);
        open.Invoked += (_, _) => OpenSessionRequested?.Invoke(item.Id);
        Set(open, HavenProperties.Column, 1);
        row.Add(open);
        return row;
    }

    private Container ExperienceCard(string name, string title, string category, string description, PlayExperienceKind kind)
    {
        var card = Card(name);
        card.Add(new HavenText(title) { Level = TextLevel.H3 });
        card.Add(Muted(category + " · Runs locally"));
        card.Add(new HavenText(description));
        var start = Button(name + ".Start", "Start game", ButtonVariant.Secondary);
        start.Invoked += (_, _) => StartRequested?.Invoke(kind);
        card.Add(start);
        return card;
    }

    private static void Clear(Container container)
    {
        foreach (var child in container.Children.ToArray()) container.Remove(child);
    }

    private static Container Card(string name)
    {
        var card = new Container { Name = name, Layout = HavenLayout.Vertical };
        Set(card, HavenProperties.Width, HavenLength.Percent(100));
        Set(card, HavenProperties.Background, "SurfaceRaised");
        Set(card, HavenProperties.BorderColor, "Border");
        Set(card, HavenProperties.BorderWidth, HavenLength.Px(1));
        Set(card, HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(18)));
        Set(card, HavenProperties.Padding, HavenThickness.Parse("16px"));
        Set(card, HavenProperties.Gap, HavenLength.Px(9));
        Set(card, HavenProperties.Shadow, "Card");
        return card;
    }

    private static HavenText Muted(string content)
    {
        var text = new HavenText(content) { Level = TextLevel.Paragraph };
        Set(text, HavenProperties.Foreground, "TextSecondary");
        return text;
    }

    private static HavenButton Button(string name, string content, ButtonVariant variant)
    {
        var button = new HavenButton { Name = name, Content = content, Variant = variant };
        button.Accessibility.AccessibleName = content;
        return button;
    }

    private static void Set<T>(HavenElement element, HavenProperty<T> property, T value) => element.SetValue(property, value);

    private static string PieceGlyph(char piece) => piece switch
    {
        'K' => "♔", 'Q' => "♕", 'R' => "♖", 'B' => "♗", 'N' => "♘", 'P' => "♙",
        'k' => "♚", 'q' => "♛", 'r' => "♜", 'b' => "♝", 'n' => "♞", 'p' => "♟",
        _ => string.Empty
    };

    private static string PieceName(char piece) => char.ToLowerInvariant(piece) switch
    {
        'k' => "king", 'q' => "queen", 'r' => "rook", 'b' => "bishop", 'n' => "knight", 'p' => "pawn", _ => "empty"
    };

    private static string SquareName(int square) => $"{(char)('a' + square % 8)}{8 - square / 8}";
}
