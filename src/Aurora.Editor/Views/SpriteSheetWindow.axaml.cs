using Aurora.Editor.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Aurora.Editor.Views;

/// <summary>
/// Editor de sprite sheet: escolhe a imagem, define o recorte (grade regular ou retângulos
/// desenhados à mão), monta os clipes de animação vendo a grade numerada em cima da arte e
/// grava um <c>.sheet.json</c> em <c>Assets/spritesheets/</c>.
///
/// <para>Janela própria e não-modal, igual ao banco de dados: recortar uma folha é tarefa longa
/// que se faz junto com a cena aberta — inclusive porque o botão "Aplicar" liga a folha no
/// Animator da entidade que está selecionada lá.</para>
/// </summary>
public partial class SpriteSheetWindow : Window
{
    private readonly MainViewModel _main;

    private SpriteSheetViewModel ViewModel => (SpriteSheetViewModel)DataContext!;

    /// <summary>Construtor sem parâmetro pro designer do Avalonia.</summary>
    public SpriteSheetWindow()
        : this(new MainViewModel())
    {
    }

    public SpriteSheetWindow(MainViewModel main)
    {
        InitializeComponent();
        _main = main;
        DataContext = new SpriteSheetViewModel(main);

        // O botão "Aplicar" nomeia a entidade selecionada na cena; trocar de entidade com a
        // janela aberta precisa atualizar o rótulo, senão ele mente sobre onde vai gravar.
        _main.PropertyChanged += OnMainPropertyChanged;
    }

    private void OnMainPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.SelectedEntity))
            ViewModel.RaiseApplyLabel();
        else if (e.PropertyName is nameof(MainViewModel.AssetsRootDisplay))
        {
            ViewModel.ReloadImageAssets();
            ViewModel.ReloadSheetFiles();
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _main.PropertyChanged -= OnMainPropertyChanged;
        ViewModel.Dispose();
        base.OnClosed(e);
    }
}
