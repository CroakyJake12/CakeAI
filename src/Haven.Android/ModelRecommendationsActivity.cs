using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using Uri = Android.Net.Uri;

namespace Haven.Android;

[Activity(
    Label = "Recommended models",
    Theme = "@style/Theme.AppCompat.Light.NoActionBar",
    Exported = false)]
public sealed class ModelRecommendationsActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        BuildUi();
    }

    private void BuildUi()
    {
        var ramGb = GetTotalRamGb();
        var root = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent)
        };
        root.SetPadding(Dp(18), Dp(20), Dp(18), Dp(18));
        root.Background = HavenNativeSurface.Page();

        root.AddView(Label("Recommended models", 25, bold: true));

        var explanation = Label(
            $"CakeAI detected about {ramGb:0.#} GB of RAM. These shortcuts open public GGUF searches sized for this device. Downloads stay in the browser until you explicitly import a file through Android's system picker.",
            14);
        explanation.SetPadding(0, Dp(8), 0, Dp(14));
        root.AddView(explanation);

        var scrollContent = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical
        };

        AddRecommendation(
            scrollContent,
            "Balanced local model",
            "A general instruct model sized conservatively for this device.",
            BalancedQuery(ramGb));

        AddRecommendation(
            scrollContent,
            "Reasoning model",
            "A reasoning-focused GGUF option in a device-appropriate size band.",
            ReasoningQuery(ramGb));

        AddRecommendation(
            scrollContent,
            "Vision model",
            "A compact multimodal GGUF search. Check model-specific Android/runtime requirements before importing.",
            VisionQuery(ramGb));

        AddRecommendation(
            scrollContent,
            "Coding model",
            "A compact coding/instruct GGUF search suitable for local experimentation.",
            CodingQuery(ramGb));

        var allModels = ActionButton("Browse all public GGUF models", () => OpenCatalog("gguf instruct"));
        allModels.LayoutParameters = FullWidthButtonLayout(topMargin: Dp(8));
        scrollContent.AddView(allModels);

        var import = ActionButton("Import local GGUF files or folder", () =>
            StartActivity(new Intent(this, typeof(ModelImportActivity))));
        import.LayoutParameters = FullWidthButtonLayout(topMargin: Dp(8));
        scrollContent.AddView(import);

        var safety = Label(
            "CakeAI does not auto-download models from this screen. Review the model card, license and file size in the public catalog, then import only the GGUF file you intend to run.",
            12);
        safety.SetTextColor(Color.Argb(220, 214, 193, 255));
        safety.SetPadding(0, Dp(14), 0, Dp(4));
        scrollContent.AddView(safety);

        var scroll = new ScrollView(this);
        scroll.AddView(scrollContent);
        root.AddView(scroll, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            0,
            1f));

        SetContentView(root);
        AndroidTypography.ApplyTree(root);
    }

    private void AddRecommendation(LinearLayout parent, string title, string detail, string query)
    {
        var card = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical
        };
        card.SetPadding(Dp(12), Dp(10), Dp(12), Dp(10));
        card.Background = HavenNativeAccentPalette.Launcher.Tertiary.Create(Dp(18));

        card.AddView(Label(title, 17, bold: true));
        var copy = Label(detail, 13);
        copy.SetPadding(0, Dp(4), 0, Dp(8));
        card.AddView(copy);

        var open = ActionButton("Open recommendations", () => OpenCatalog(query));
        open.LayoutParameters = FullWidthButtonLayout();
        card.AddView(open);

        parent.AddView(card, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent)
        {
            BottomMargin = Dp(9)
        });
    }

    private void OpenCatalog(string query)
    {
        var url = "https://huggingface.co/models?search="
            + System.Uri.EscapeDataString(query)
            + "&library=gguf&sort=trending";
        StartActivity(new Intent(Intent.ActionView, Uri.Parse(url)));
    }

    private static string BalancedQuery(double ramGb)
        => ramGb < 4 ? "1b instruct gguf"
            : ramGb < 7 ? "2b instruct gguf"
            : ramGb < 12 ? "4b instruct gguf"
            : "7b instruct gguf";

    private static string ReasoningQuery(double ramGb)
        => ramGb < 4 ? "reasoning 1b gguf"
            : ramGb < 8 ? "reasoning 2b gguf"
            : "reasoning 7b gguf";

    private static string VisionQuery(double ramGb)
        => ramGb < 6 ? "vision 2b gguf"
            : ramGb < 12 ? "vision 3b gguf"
            : "vision 7b gguf";

    private static string CodingQuery(double ramGb)
        => ramGb < 4 ? "coder 1b instruct gguf"
            : ramGb < 8 ? "coder 3b instruct gguf"
            : "coder 7b instruct gguf";

    private double GetTotalRamGb()
    {
        var manager = GetSystemService(ActivityService) as ActivityManager;
        var info = new ActivityManager.MemoryInfo();
        manager?.GetMemoryInfo(info);
        return info.TotalMem <= 0 ? 4 : info.TotalMem / 1024d / 1024d / 1024d;
    }

    private TextView Label(string text, float size, bool bold = false)
    {
        var view = new TextView(this)
        {
            Text = text,
            TextSize = size,
            Typeface = bold ? Typeface.DefaultBold : Typeface.Default
        };
        view.SetTextColor(Color.White);
        return view;
    }

    private Button ActionButton(string label, Action action)
    {
        var button = new HavenNativeButton(this)
        {
            Text = label
        };
        button.Click += (_, _) => action();
        return button;
    }

    private LinearLayout.LayoutParams FullWidthButtonLayout(int topMargin = 0)
        => new(ViewGroup.LayoutParams.MatchParent, Dp(52))
        {
            TopMargin = topMargin
        };

    private int Dp(int value)
        => (int)Math.Round(value * (Resources?.DisplayMetrics?.Density ?? 1f));
}
