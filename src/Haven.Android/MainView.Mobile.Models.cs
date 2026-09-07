using Android.Content;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private static void LaunchAndroidModelRecommendations()
    {
        var context = global::Android.App.Application.Context;
        var intent = new Intent(context, typeof(Haven.Android.ModelRecommendationsActivity));
        intent.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(intent);
    }
}
