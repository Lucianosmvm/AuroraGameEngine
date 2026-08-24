namespace Aurora.Runtime.Ecs.Components;

/// <summary>
/// Marca uma entidade cujo destino é LEMBRADO entre visitas à cena e entre save e load: o
/// inimigo morto continua morto, o baú aberto continua aberto, a porta destrancada continua
/// destrancada.
///
/// <para>Sem esta marca, cada vez que a cena é carregada tudo volta como está no arquivo — que
/// é o comportamento certo pra bicho comum (voltar num mapa e achá-lo vazio é pior que
/// reencontrar os inimigos) e errado pra chefe, baú e alavanca. Não existe padrão universal
/// aqui, então a escolha é por entidade: sem componente, respawna; com componente, lembra.</para>
///
/// <para>O que é lembrado são FATOS de "já aconteceu": destruída, gatilho <c>Once</c> já
/// disparado, efeito ligado/desligado por <c>SetActive</c>. Vida e posição de propósito NÃO
/// entram: são estado contínuo de simulação, e restaurar o chefe machucado no ponto exato em
/// que você fugiu é outra decisão de design, não uma consequência desta.</para>
///
/// <para>A identidade é o NOME da entidade dentro da cena — a mesma que
/// <see cref="World.TryFind"/>, <c>Transform.Parent</c> e os marcadores de spawn já usam. Dois
/// objetos persistentes com o mesmo nome na mesma cena compartilham o destino; o editor já
/// garante nome único ao criar, duplicar e colar.</para>
/// </summary>
public sealed class Persistent : IComponent
{
}
