using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Speech;
using Microsoft.Maui.ApplicationModel;

namespace QatFarm.Mobile;

[Activity(Theme="@style/Maui.SplashTheme", MainLauncher=true, LaunchMode=LaunchMode.SingleTop,
 ConfigurationChanges=ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
 ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    private const int SpeechRequestCode = 7721;
    private static TaskCompletionSource<string?>? speechCompletion;

    public static Task<string?> CaptureSpeechAsync(CancellationToken cancellationToken = default)
    {
        var activity = Platform.CurrentActivity as MainActivity
            ?? throw new InvalidOperationException("تعذر الوصول إلى نافذة التطبيق الحالية.");

        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var previous = Interlocked.Exchange(ref speechCompletion, completion);
        previous?.TrySetCanceled();

        CancellationTokenRegistration registration = default;
        if (cancellationToken.CanBeCanceled)
        {
            registration = cancellationToken.Register(() =>
            {
                var pending = Interlocked.Exchange(ref speechCompletion, null);
                pending?.TrySetCanceled(cancellationToken);
            });
        }

        completion.Task.ContinueWith(_ => registration.Dispose(), TaskScheduler.Default);

        activity.RunOnUiThread(() =>
        {
            try
            {
                var intent = new Intent(RecognizerIntent.ActionRecognizeSpeech);
                intent.PutExtra(RecognizerIntent.ExtraLanguageModel, RecognizerIntent.LanguageModelFreeForm);
                intent.PutExtra(RecognizerIntent.ExtraLanguage, "ar-YE");
                intent.PutExtra(RecognizerIntent.ExtraPrompt, "تحدث الآن");
                intent.PutExtra(RecognizerIntent.ExtraMaxResults, 3);
                activity.StartActivityForResult(intent, SpeechRequestCode);
            }
            catch (Exception ex)
            {
                var pending = Interlocked.Exchange(ref speechCompletion, null);
                pending?.TrySetException(ex);
            }
        });

        return completion.Task;
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != SpeechRequestCode) return;

        var completion = Interlocked.Exchange(ref speechCompletion, null);
        if (completion is null) return;

        if (resultCode != Result.Ok)
        {
            completion.TrySetResult(null);
            return;
        }

        var matches = data?.GetStringArrayListExtra(RecognizerIntent.ExtraResults);
        completion.TrySetResult(matches?.FirstOrDefault());
    }
}
