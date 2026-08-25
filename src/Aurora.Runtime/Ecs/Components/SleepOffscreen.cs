namespace Aurora.Runtime.Ecs.Components;

/// <summary>
/// Faz os <see cref="Behavior"/> desta entidade pararem de rodar enquanto ela estiver fora da
/// vista da câmera. Serve pro mapa grande cheio de bicho: sem isso, os duzentos inimigos do outro
/// canto do mapa gastam frame patrulhando onde ninguém olha.
///
/// <para><b>Opcional de propósito, e é uma decisão de jogo, não de performance.</b> Um inimigo
/// dormindo não persegue, não atira e não anda — quem chega perto o pega parado no lugar onde
/// saiu da tela. Pra ninho que precisa produzir enquanto o jogador está longe, pra chefe que
/// carrega ataque fora da tela, ou pra plataforma móvel que tem que estar no lugar certo quando
/// você volta, NÃO ponha este componente.</para>
///
/// <para>Colisão, dano, vida e hierarquia continuam valendo — o que dorme é só o Update dos
/// comportamentos. E nada dorme sem câmera (ferramenta, teste, servidor sem tela).</para>
/// </summary>
public sealed class SleepOffscreen : IComponent
{
    /// <summary>
    /// Folga em pixels além da borda da tela antes de dormir. Zero faria o bicho acordar no
    /// instante em que aparece — visivelmente parado no primeiro frame. O padrão dá quase uma
    /// tela de margem, tempo de ele já estar andando quando entra no quadro.
    /// </summary>
    public float Margin = 256f;

    /// <summary>Só pra depuração/HUD: se ele está dormindo agora.</summary>
    public bool Sleeping { get; internal set; }
}
