using System.Numerics;

namespace BeastArena.Sim;

// Andar, contornar pedra e ninho, e não empilhar unidades.
//
// Sem A* de propósito: os obstáculos são poucos círculos fixos, e "ande reto e desvie pela
// tangente" é mais previsível pro jogador do que um caminho ótimo — ele precisa conseguir
// antecipar por onde a tropa inimiga vai passar.
public sealed partial class Batalha
{
    private void Mover(Unidade unidade, Vector2 destino)
    {
        var delta = destino - unidade.Posicao;
        float distancia = delta.Length();

        if (distancia < 0.0001f)
            return;

        var direcao = delta / distancia;
        if (!unidade.Voa)
            direcao = DesviarDeObstaculos(unidade, direcao);

        unidade.Posicao += direcao * MathF.Min(distancia, unidade.Velocidade * Passo);
    }

    /// <summary>
    /// Obstáculo no meio da trajetória: puxa a direção pra tangente. Só a separação resolveria
    /// empurrando, mas uma unidade exatamente alinhada com o centro da pedra seria empurrada de
    /// volta pra trás e ficaria parada ali pra sempre.
    /// </summary>
    private static Vector2 DesviarDeObstaculos(Unidade unidade, Vector2 direcao)
    {
        foreach (var obstaculo in Campo.Obstaculos)
        {
            var paraObstaculo = obstaculo.Centro - unidade.Posicao;
            float folga = obstaculo.Raio + unidade.Raio + 0.15f;

            if (paraObstaculo.LengthSquared() > (folga + 1.2f) * (folga + 1.2f) || Vector2.Dot(paraObstaculo, direcao) <= 0f)
                continue;

            float lateral = direcao.X * paraObstaculo.Y - direcao.Y * paraObstaculo.X;
            if (MathF.Abs(lateral) >= folga)
                continue;

            var tangente = new Vector2(-direcao.Y, direcao.X);
            if (Vector2.Dot(tangente, paraObstaculo) > 0f)
                tangente = -tangente;

            direcao = Vector2.Normalize(direcao * 0.35f + tangente);
        }

        return direcao;
    }

    /// <summary>
    /// Desfaz sobreposições (unidade com unidade na mesma camada, unidade de chão com obstáculo) e
    /// aplica as bordas da arena.
    /// </summary>
    private void Separar()
    {
        for (int i = 0; i < _unidades.Count; i++)
        {
            var a = _unidades[i];
            if (!a.Viva)
                continue;

            for (int j = i + 1; j < _unidades.Count; j++)
            {
                var b = _unidades[j];
                if (!b.Viva || a.Voa != b.Voa)
                    continue;

                var delta = b.Posicao - a.Posicao;
                float minimo = a.Raio + b.Raio;
                float distanciaAoQuadrado = delta.LengthSquared();

                if (distanciaAoQuadrado >= minimo * minimo)
                    continue;

                float distancia = MathF.Sqrt(distanciaAoQuadrado);
                var normal = distancia > 0.0001f ? delta / distancia : new Vector2(1f, 0f);
                float sobreposicao = minimo - distancia;

                // Metade da correção por passo: resolver tudo de uma vez faz grupos grandes
                // tremerem, porque cada par desfeito empurra o vizinho pra dentro de outro.
                float massaA = a.Carta.Massa, massaB = b.Carta.Massa;
                float total = MathF.Max(0.001f, massaA + massaB);
                a.Posicao -= normal * (sobreposicao * 0.5f * massaB / total);
                b.Posicao += normal * (sobreposicao * 0.5f * massaA / total);
            }
        }

        foreach (var unidade in _unidades)
        {
            if (unidade.Viva)
                AplicarParedes(unidade);
        }
    }

    private static void AplicarParedes(Unidade unidade)
    {
        var posicao = unidade.Posicao;

        if (!unidade.Voa)
        {
            foreach (var obstaculo in Campo.Obstaculos)
            {
                var delta = posicao - obstaculo.Centro;
                float minimo = obstaculo.Raio + unidade.Raio;
                float distancia = delta.Length();

                if (distancia >= minimo)
                    continue;

                // Nasceu exatamente no centro (implantou em cima do ninho): sai pro lado do meio
                // do mapa, que é pra onde ela ia de qualquer jeito.
                var normal = distancia > 0.0001f
                    ? delta / distancia
                    : new Vector2(0f, obstaculo.Centro.Y > Campo.CentroY ? -1f : 1f);
                posicao = obstaculo.Centro + normal * minimo;
            }
        }

        float raio = unidade.Raio;
        unidade.Posicao = new Vector2(
            Math.Clamp(posicao.X, raio, Campo.Largura - raio),
            Math.Clamp(posicao.Y, raio, Campo.Altura - raio));
    }
}
