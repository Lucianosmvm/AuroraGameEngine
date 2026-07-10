using Android.App;
using Android.Content.PM;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Sdl.Android;

namespace Aurora.Sandbox.Droid;

[Activity(Label = "Aurora Sandbox", MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden,
    ScreenOrientation = ScreenOrientation.SensorLandscape)]
public class MainActivity : SilkActivity
{
    protected override void OnRun()
    {
        var options = ViewOptions.Default with
        {
            API = new GraphicsAPI(ContextAPI.OpenGLES, ContextProfile.Compatability,
                ContextFlags.Default, new APIVersion(3, 0)),
        };

        using var view = Silk.NET.Windowing.Window.GetView(options);
        using var game = new SandboxGame();
        game.Run(view);
    }
}
