using Aurora.Runtime.Net;

namespace Aurora.Runtime.Tests;

/// <summary>
/// Canal de entrega garantida e em ordem por cima do UDP. É o que os RPCs usam: posição
/// perdida se conserta no próximo snapshot, mas "levou 30 de dano" perdido nunca volta.
/// </summary>
public class NetReliableTests
{
    /// <summary>Um par de canais ligados por um fio controlado pelo teste: dá pra segurar,
    /// descartar e reordenar pacote à mão, sem depender de sorte nem de tempo real.</summary>
    private sealed class Wire
    {
        private readonly List<(byte[] Packet, NetReliableChannel To)> _inFlight = [];

        public Wire()
        {
            A = new NetReliableChannel { Transmit = data => Queue(data, toB: true) };
            B = new NetReliableChannel { Transmit = data => Queue(data, toB: false) };

            A.Delivered += packet => DeliveredAtA.Add(Payload(packet));
            B.Delivered += packet => DeliveredAtB.Add(Payload(packet));
        }

        public NetReliableChannel A { get; }
        public NetReliableChannel B { get; }

        /// <summary>Nomeado pelo destino: o que A entregou veio de B.</summary>
        public List<string> DeliveredAtA { get; } = [];
        public List<string> DeliveredAtB { get; } = [];

        /// <summary>Descarta tudo que está no fio agora, como um cabo arrancado por um instante.</summary>
        public int DropAll()
        {
            int dropped = _inFlight.Count;
            _inFlight.Clear();
            return dropped;
        }

        /// <summary>Entrega o que está no fio. Repete porque a entrega gera confirmações, que
        /// também precisam trafegar.</summary>
        public void Flush(int rounds = 4)
        {
            for (int round = 0; round < rounds; round++)
            {
                if (_inFlight.Count == 0) return;

                var batch = _inFlight.ToArray();
                _inFlight.Clear();

                foreach (var (packet, to) in batch)
                    Route(packet, to);
            }
        }

        /// <summary>Entrega o que está no fio de trás pra frente — reordenação, o que UDP faz
        /// sozinho quando os pacotes pegam caminhos diferentes.</summary>
        public void FlushReversed()
        {
            var batch = _inFlight.ToArray();
            _inFlight.Clear();

            for (int i = batch.Length - 1; i >= 0; i--)
                Route(batch[i].Packet, batch[i].To);

            Flush();
        }

        private void Queue(ReadOnlyMemory<byte> data, bool toB)
            => _inFlight.Add((data.ToArray(), toB ? B : A));

        private static void Route(byte[] packet, NetReliableChannel to)
        {
            if (!NetReader.TryParse(packet, out var reader)) return;

            switch (reader.Type)
            {
                case NetMessageType.Reliable:
                    to.OnReliable(ref reader);
                    break;

                case NetMessageType.ReliableAck:
                    to.OnAck(ref reader);
                    break;
            }
        }
    }

    /// <summary>Um pacote de teste qualquer: tipo Ping carregando uma string.</summary>
    private static byte[] Message(string text)
    {
        var buffer = new byte[NetProtocol.MaxPacketSize];
        var writer = new NetWriter(buffer, NetMessageType.Ping);
        writer.WriteString(text);

        return writer.Written.ToArray();
    }

    private static string Payload(byte[] packet)
    {
        if (!NetReader.TryParse(packet, out var reader)) return "<inválido>";

        return reader.TryReadString(out string text) ? text : "<inválido>";
    }

    [Fact]
    public void MensagemChegaDoOutroLado()
    {
        var wire = new Wire();
        wire.A.Send(Message("oi"));
        wire.Flush();

        Assert.Equal(["oi"], wire.DeliveredAtB);
        Assert.Equal(0, wire.A.UnackedCount);
    }

    [Fact]
    public void MensagemPerdidaEReenviadaAteChegar()
    {
        var wire = new Wire();
        wire.A.Send(Message("dano"));

        Assert.Equal(1, wire.DropAll());
        wire.Flush();
        Assert.Empty(wire.DeliveredAtB);

        // Ninguém confirmou: passado o intervalo, o canal reenvia sozinho.
        wire.A.Update(0.3f);
        wire.Flush();

        Assert.Equal(["dano"], wire.DeliveredAtB);
        Assert.Equal(0, wire.A.UnackedCount);
    }

    [Fact]
    public void NaoReenviaAntesDoIntervalo()
    {
        var wire = new Wire();
        wire.A.Send(Message("x"));
        wire.DropAll();

        wire.A.Update(0.1f);
        wire.Flush();

        Assert.Empty(wire.DeliveredAtB);
        Assert.Equal(1, wire.A.UnackedCount);
    }

    [Fact]
    public void ChegandoForaDeOrdemAEntregaEsperaAAnterior()
    {
        var wire = new Wire();
        wire.A.Send(Message("primeira"));
        wire.A.Send(Message("segunda"));
        wire.A.Send(Message("terceira"));

        wire.FlushReversed();

        // Chegaram ao contrário no fio, mas "nasceu" não pode ser entregue depois de "morreu".
        Assert.Equal(["primeira", "segunda", "terceira"], wire.DeliveredAtB);
    }

    [Fact]
    public void MensagemRepetidaNaoEEntregueDuasVezes()
    {
        var wire = new Wire();
        wire.A.Send(Message("som"));
        wire.Flush();

        // Reenvio por confirmação perdida: o outro lado recebe de novo e tem que ignorar.
        wire.A.Update(0.3f);
        wire.A.Update(0.3f);
        wire.Flush();

        Assert.Equal(["som"], wire.DeliveredAtB);
    }

    [Fact]
    public void ConfirmacaoPerdidaNaoTravaNada()
    {
        var wire = new Wire();
        wire.A.Send(Message("a"));

        // Deixa a mensagem chegar, mas descarta a confirmação.
        wire.Flush(rounds: 1);
        wire.DropAll();

        Assert.Equal(["a"], wire.DeliveredAtB);
        Assert.Equal(1, wire.A.UnackedCount);

        // A confirmação é acumulada, então a próxima resolve a pendência antiga também.
        wire.A.Send(Message("b"));
        wire.Flush();

        Assert.Equal(["a", "b"], wire.DeliveredAtB);
        Assert.Equal(0, wire.A.UnackedCount);
    }

    [Fact]
    public void CanalFuncionaNasDuasDirecoesAoMesmoTempo()
    {
        var wire = new Wire();
        wire.A.Send(Message("de A"));
        wire.B.Send(Message("de B"));
        wire.Flush();

        Assert.Equal(["de A"], wire.DeliveredAtB);
        Assert.Equal(["de B"], wire.DeliveredAtA);
    }

    [Fact]
    public void ResetLimpaAsSequenciasDaSessaoAnterior()
    {
        var wire = new Wire();
        wire.A.Send(Message("velha"));
        wire.Flush();

        wire.A.Reset();
        wire.B.Reset();

        wire.A.Send(Message("nova"));
        wire.Flush();

        // Sem o Reset dos dois lados, "nova" sairia com sequência 1 de novo e seria descartada
        // como repetição da sessão anterior.
        Assert.Equal(["velha", "nova"], wire.DeliveredAtB);
    }

    [Fact]
    public void RajadaComPerdaPesadaChegaInteiraENaOrdem()
    {
        var wire = new Wire();
        var esperado = new List<string>();

        for (int i = 0; i < 20; i++)
        {
            string texto = $"evento{i}";
            esperado.Add(texto);
            wire.A.Send(Message(texto));

            // Metade dos envios some antes de sair do fio.
            if (i % 2 == 0) wire.DropAll();

            wire.Flush();
        }

        // Reenvios até esvaziar a lista de não-confirmados.
        for (int i = 0; i < 10 && wire.A.UnackedCount > 0; i++)
        {
            wire.A.Update(0.3f);
            wire.Flush();
        }

        Assert.Equal(esperado, wire.DeliveredAtB);
        Assert.Equal(0, wire.A.UnackedCount);
    }
}
