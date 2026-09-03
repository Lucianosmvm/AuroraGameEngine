using System.Numerics;
using Android.App;
using Android.Content.PM;
using Android.Views;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Sdl.Android;

namespace FruitNinja.Droid;

// Orientação escolhida no export (Inspector → Orientação Android). Landscape/Portrait
// são fixos (nunca giram). SensorLandscape/SensorPortrait/Sensor giram com o aparelho —
// um bug antigo do Silk.NET/SDL no Android ("You cannot call Reset inside of the render
// loop!", Silk.NET.Windowing.Internals.ViewImplementationBase.Reset/Dispose) já causou
// crash real ao girar; testado de novo manualmente em device Android 14 real (rotação
// completa incluindo retrato) sem reproduzir o crash, mas isso pode variar por
// aparelho/versão de Android/driver — se crashar no seu device, volte pra Landscape fixo.
[Activity(Label = "Aurora Ninja", MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden,
    ScreenOrientation = ScreenOrientation.Portrait)]
public class MainActivity : SilkActivity
{
    private volatile FruitNinja.NinjaGame? _game;

    protected override void OnRun()
    {
        var options = ViewOptions.Default with
        {
            API = new GraphicsAPI(ContextAPI.OpenGLES, ContextProfile.Compatability,
                ContextFlags.Default, new APIVersion(3, 0)),
        };

        using var view = Silk.NET.Windowing.Window.GetView(options);
        using var game = new FruitNinja.NinjaGame();
        _game = game;
        game.AssetSource = new AndroidAssetSource();
        game.Run(view);
        _game = null;
    }

    // Toque não vira evento de mouse sozinho nesse binding Silk.NET/SDL Android
    // (testado em device real). OnTouchEvent não funciona - SilkActivity estende
    // SDLActivity (Java), cuja SurfaceView já consome o toque antes. DispatchTouchEvent
    // roda ANTES de qualquer view filha, sempre - intercepta aqui e injeta manual.
    public override bool DispatchTouchEvent(MotionEvent? e)
    {
        if (e is not null && _game is not null)
        {
            // Caminho antigo (1 ponto só) - é o que UIManager usa pro clique de
            // UiButton (menu/HUD só olha um toque, não precisa de mais que isso).
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

            // Multi-toque de verdade (joystick + botão ao mesmo tempo) - cada dedo
            // com seu id (MotionEvent.GetPointerId), independente do caminho acima.
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