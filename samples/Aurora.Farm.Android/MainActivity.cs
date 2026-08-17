using System.Numerics;
using Android.App;
using Android.Content.PM;
using Android.Views;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Sdl.Android;

namespace AuroraFarm.Droid;

// SensorLandscape (mesma escolha já validada em device real pelo Aurora.Sandbox.Android):
// paisagem, girando entre normal/invertida com o sensor. Ver docs/GUIA-ANDROID.md se
// crashar ao rotacionar em algum aparelho — nesse caso troque para Landscape fixo.
[Activity(Label = "Aurora Farm", MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden,
    ScreenOrientation = ScreenOrientation.SensorLandscape)]
public class MainActivity : SilkActivity
{
    private volatile FarmGame? _game;

    protected override void OnRun()
    {
        var options = ViewOptions.Default with
        {
            API = new GraphicsAPI(ContextAPI.OpenGLES, ContextProfile.Compatability,
                ContextFlags.Default, new APIVersion(3, 0)),
        };

        using var view = Silk.NET.Windowing.Window.GetView(options);
        using var game = new FarmGame();
        _game = game;
        game.AssetSource = new AndroidAssetSource();
        game.Run(view);
        _game = null;
    }

    // Toque não vira evento de mouse sozinho nesse binding Silk.NET/SDL Android (testado em
    // device real, ver docs/GUIA-ANDROID.md). OnTouchEvent não funciona — SilkActivity estende
    // SDLActivity (Java), cuja SurfaceView já consome o toque antes. DispatchTouchEvent roda
    // ANTES de qualquer view filha, sempre — intercepta aqui e injeta manual no InputManager.
    public override bool DispatchTouchEvent(MotionEvent? e)
    {
        if (e is not null && _game is not null)
        {
            // Caminho de 1 ponto só — o que UIManager usa pro clique de UiButton.
            switch (e.Action)
            {
                case MotionEventActions.Down:
                case MotionEventActions.Move:
                    _game.Input.SetPointer(new Vector2(e.GetX(), e.GetY()), true);
                    break;
                case MotionEventActions.Up:
                case MotionEventActions.Cancel:
                    _game.Input.SetPointer(null, false);
                    break;
            }

            // Multi-toque de verdade — joystick com um dedo e botão de ação com outro.
            switch (e.ActionMasked)
            {
                case MotionEventActions.Down:
                case MotionEventActions.PointerDown:
                {
                    int idx = e.ActionIndex;
                    _game.Input.SetTouch(e.GetPointerId(idx), new Vector2(e.GetX(idx), e.GetY(idx)), true);
                    break;
                }
                case MotionEventActions.Move:
                    for (int i = 0; i < e.PointerCount; i++)
                        _game.Input.SetTouch(e.GetPointerId(i), new Vector2(e.GetX(i), e.GetY(i)), true);
                    break;
                case MotionEventActions.Up:
                case MotionEventActions.PointerUp:
                case MotionEventActions.Cancel:
                {
                    int idx = e.ActionIndex;
                    _game.Input.SetTouch(e.GetPointerId(idx), Vector2.Zero, false);
                    break;
                }
            }
        }

        return base.DispatchTouchEvent(e);
    }
}
