using System.Numerics;
using Android.App;
using Android.Content.PM;
using Android.Net.Wifi;
using Android.OS;
using Android.Views;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Sdl.Android;

// Sockets UDP.
[assembly: UsesPermission(Android.Manifest.Permission.Internet)]

// Necessárias pro MulticastLock abaixo — sem ele o aparelho ignora os pacotes de broadcast e
// quem hospeda some da busca dos outros celulares.
[assembly: UsesPermission(Android.Manifest.Permission.AccessWifiState)]
[assembly: UsesPermission(Android.Manifest.Permission.ChangeWifiMulticastState)]

namespace Aurora.Coop.Droid;

[Activity(Label = "Aurora Coop", MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden,
    ScreenOrientation = ScreenOrientation.SensorLandscape)]
public class MainActivity : SilkActivity
{
    private volatile CoopGame? _game;
    private WifiManager.MulticastLock? _multicastLock;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // O Wi-Fi do Android descarta pacote de broadcast e multicast que não seja endereçado
        // diretamente ao aparelho, pra economizar bateria. A busca de salas é justamente um
        // broadcast, então SEM este lock o celular que hospeda nunca recebe a pergunta e não
        // aparece na lista de ninguém — entrar digitando o IP continuaria funcionando, o que
        // deixa a falha ainda mais confusa de diagnosticar.
        if (Application.Context.GetSystemService(WifiService) is WifiManager wifi)
        {
            _multicastLock = wifi.CreateMulticastLock("aurora-coop-discovery");
            _multicastLock?.Acquire();
        }
    }

    protected override void OnDestroy()
    {
        if (_multicastLock is { IsHeld: true })
            _multicastLock.Release();

        _multicastLock?.Dispose();
        _multicastLock = null;

        base.OnDestroy();
    }

    protected override void OnRun()
    {
        var options = ViewOptions.Default with
        {
            API = new GraphicsAPI(ContextAPI.OpenGLES, ContextProfile.Compatability,
                ContextFlags.Default, new APIVersion(3, 0)),
        };

        using var view = Silk.NET.Windowing.Window.GetView(options);
        using var game = new CoopGame();
        _game = game;
        game.AssetSource = new AndroidAssetSource();
        game.Run(view);
        _game = null;
    }

    // Toque não vira evento de mouse sozinho nesse binding Silk.NET/SDL Android (testado em
    // device real). OnTouchEvent não funciona — SilkActivity estende SDLActivity (Java), cuja
    // SurfaceView já consome o toque antes. DispatchTouchEvent roda ANTES de qualquer view
    // filha, sempre — intercepta aqui e injeta manual.
    public override bool DispatchTouchEvent(MotionEvent? e)
    {
        if (e is not null && _game is not null)
        {
            // Caminho de um ponto só: é o que vira "clique" e move os botões do menu.
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

            // Multi-toque de verdade, cada dedo com seu id — é o que deixa o analógico
            // funcionar com um dedo enquanto o outro aperta um botão.
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
