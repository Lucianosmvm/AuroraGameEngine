using Aurora.Editor.ViewModels;
using Aurora.Editor.Views;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Aurora.Editor;

public class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainViewModel();
            desktop.MainWindow = new MainWindow { DataContext = viewModel };

            // Abre cena passada por argumento (conveniência de desenvolvimento).
            string? scenePath = desktop.Args?.FirstOrDefault(a => a.EndsWith(".json"));
            if (scenePath is not null && File.Exists(scenePath))
                viewModel.OpenScene(Path.GetFullPath(scenePath));

            // Verificação automatizada de CRUD: cria 2, deleta 1, salva-como.
            int crudIndex = Array.IndexOf(desktop.Args ?? [], "--test-crud");
            if (crudIndex >= 0 && desktop.Args!.Length > crudIndex + 1)
            {
                viewModel.CreateEntity(60, -40);
                viewModel.CreateEntity(200, 100);
                viewModel.DeleteSelectedEntity();
                viewModel.SaveSceneAs(desktop.Args[crudIndex + 1]);
            }

            // Modo de verificação automatizada: captura a janela e sai.
            int screenshotIndex = Array.IndexOf(desktop.Args ?? [], "--screenshot");
            if (screenshotIndex >= 0 && desktop.Args!.Length > screenshotIndex + 1)
                CaptureAndExit(desktop, desktop.Args[screenshotIndex + 1]);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void CaptureAndExit(IClassicDesktopStyleApplicationLifetime desktop, string outputPath)
    {
        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var window = desktop.MainWindow!;
            var size = new Avalonia.PixelSize((int)window.ClientSize.Width, (int)window.ClientSize.Height);
            using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(size, new Avalonia.Vector(96, 96));
            bitmap.Render(window);
            bitmap.Save(outputPath);
            desktop.Shutdown();
        };
        timer.Start();
    }
}
