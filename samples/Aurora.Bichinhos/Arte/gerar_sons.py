"""Gera os sons e as músicas do Bichinhos em Assets/sounds/*.wav.

Tudo é sintetizado aqui (seno, triângulo, ruído), então não há arquivo de terceiros nem
licença pra conferir. Rodar de novo sobrescreve:

    python Arte/gerar_sons.py

Precisa de numpy. WAV mono 16 bits a 22,05 kHz: pequeno o bastante pra ir no APK, e é o
formato que o AudioManager da engine lê sem decodificador extra.
"""
import os
import wave

import numpy as np

SR = 22050
SAIDA = os.path.join(os.path.dirname(__file__), "..", "Assets", "sounds")
RNG = np.random.default_rng(7)


# ---------------------------------------------------------------- blocos de som

def t_(dur):
    return np.arange(int(SR * dur)) / SR


def nota_hz(nome):
    """'C5' -> frequência. Só sustenidos com '#'."""
    notas = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}
    base = notas[nome[0]]
    resto = nome[1:]
    if resto.startswith("#"):
        base += 1
        resto = resto[1:]
    oitava = int(resto)
    semitons = base + (oitava - 4) * 12 - 9
    return 440.0 * 2 ** (semitons / 12)


def caixa_de_musica(freq, dur, vol=1.0):
    """Seno com um harmônico que morre rápido: o timbre de caixinha de música."""
    t = t_(dur)
    s = np.sin(2 * np.pi * freq * t) + 0.35 * np.sin(2 * np.pi * freq * 2 * t) * np.exp(-t * 9)
    s += 0.12 * np.sin(2 * np.pi * freq * 3.01 * t) * np.exp(-t * 14)
    return s * np.exp(-t * 3.2) * np.minimum(1, t / 0.004) * vol


def triangulo(freq, dur, vol=1.0, queda=2.5):
    t = t_(dur)
    s = 2 * np.abs(2 * ((freq * t) % 1) - 1) - 1
    return s * np.exp(-t * queda) * np.minimum(1, t / 0.006) * vol


def quadrada(freq, dur, vol=1.0, queda=6.0):
    t = t_(dur)
    s = np.sign(np.sin(2 * np.pi * freq * t)) * 0.6 + 0.4 * np.sin(2 * np.pi * freq * t)
    return s * np.exp(-t * queda) * np.minimum(1, t / 0.003) * vol


def varredura(f0, f1, dur, vol=1.0, queda=3.0, onda="seno"):
    t = t_(dur)
    freq = f0 + (f1 - f0) * (t / dur)
    fase = 2 * np.pi * np.cumsum(freq) / SR
    s = np.sin(fase) if onda == "seno" else np.sign(np.sin(fase))
    return s * np.exp(-t * queda) * np.minimum(1, t / 0.004) * vol


def ruido(dur, vol=1.0, queda=20.0, suave=1):
    n = RNG.uniform(-1, 1, int(SR * dur))
    if suave > 1:
        n = np.convolve(n, np.ones(suave) / suave, mode="same")
    t = t_(dur)
    return n * np.exp(-t * queda) * vol


def mistura(*pecas):
    """Soma peças (offset_em_segundos, array)."""
    fim = max(int(o * SR) + len(a) for o, a in pecas)
    saida = np.zeros(fim)
    for o, a in pecas:
        i = int(o * SR)
        saida[i:i + len(a)] += a
    return saida


def soma(a, b):
    """Soma dois sons de tamanhos diferentes (o menor é completado com silêncio)."""
    return mistura((0, a), (0, b))


def normaliza(s, pico=0.85):
    m = np.max(np.abs(s))
    return s / m * pico if m > 0 else s


def fecha(s, ms=8):
    """Entrada e saída curtas pra não estalar."""
    n = int(SR * ms / 1000)
    s = s.copy()
    s[:n] *= np.linspace(0, 1, n)
    s[-n:] *= np.linspace(1, 0, n)
    return s


def salva(nome, s):
    os.makedirs(SAIDA, exist_ok=True)
    dados = (np.clip(s, -1, 1) * 32767).astype("<i2")
    caminho = os.path.join(SAIDA, nome + ".wav")
    with wave.open(caminho, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(dados.tobytes())
    print(f"{nome}.wav  {len(dados) / SR:5.1f}s  {os.path.getsize(caminho) // 1024} KB")


# ---------------------------------------------------------------- efeitos

def efeitos():
    salva("toque", fecha(normaliza(caixa_de_musica(1046, 0.09), 0.5)))

    salva("comer", fecha(normaliza(mistura(
        *[(i * 0.13, soma(varredura(220, 110, 0.1, 1.0, 14), ruido(0.08, 0.5, 30, 6))) for i in range(3)]), 0.8)))

    salva("carinho", fecha(normaliza(mistura(
        (0.0, caixa_de_musica(nota_hz("E5"), 0.3)),
        (0.11, caixa_de_musica(nota_hz("A5"), 0.4))), 0.6)))

    salva("moeda", fecha(normaliza(mistura(
        (0.0, quadrada(nota_hz("B5"), 0.09, 0.7, 4)),
        (0.08, quadrada(nota_hz("E6"), 0.3, 0.7, 5))), 0.6)))

    salva("nivel", fecha(normaliza(mistura(
        *[(i * 0.1, caixa_de_musica(nota_hz(n), 0.5)) for i, n in enumerate(["C5", "E5", "G5", "C6"])],
        (0.4, caixa_de_musica(nota_hz("E6"), 0.8, 0.7))), 0.8)))

    # Evolução: sobe girando e abre num acorde.
    t = t_(1.6)
    vibrato = np.sin(2 * np.pi * 7 * t) * 0.02
    subida = varredura(220, 1200, 1.6, 0.5, 0.0) * (0.5 + 0.5 * np.sin(2 * np.pi * 9 * t))
    acorde = mistura(*[(1.4 + i * 0.03, caixa_de_musica(nota_hz(n), 1.4, 0.7)) for i, n in enumerate(["C5", "E5", "G5", "C6"])])
    salva("evolucao", fecha(normaliza(mistura((0.0, subida * (1 + vibrato)), (0.0, acorde)), 0.85)))

    salva("erro", fecha(normaliza(varredura(220, 90, 0.28, 1.0, 5, "quadrada"), 0.5)))
    salva("eca", fecha(normaliza(soma(varredura(260, 70, 0.35, 1.0, 4, "quadrada"), ruido(0.3, 0.4, 8, 3)), 0.55)))

    salva("golpe", fecha(normaliza(mistura(
        (0.0, ruido(0.18, 1.0, 18, 5)), (0.0, varredura(160, 55, 0.2, 1.0, 14))), 0.85)))

    salva("super", fecha(normaliza(mistura(
        (0.0, ruido(0.2, 1.0, 16, 4)), (0.0, varredura(200, 50, 0.25, 1.0, 12)),
        (0.12, caixa_de_musica(nota_hz("E6"), 0.4, 0.6)), (0.2, caixa_de_musica(nota_hz("A6"), 0.5, 0.6))), 0.9)))

    salva("fraco", fecha(normaliza(mistura(
        (0.0, ruido(0.12, 0.6, 30, 12)), (0.0, varredura(110, 70, 0.14, 0.8, 18))), 0.6)))

    salva("vitoria", fecha(normaliza(mistura(
        (0.00, caixa_de_musica(nota_hz("C5"), 0.4)), (0.15, caixa_de_musica(nota_hz("C5"), 0.4)),
        (0.30, caixa_de_musica(nota_hz("C5"), 0.4)), (0.45, caixa_de_musica(nota_hz("G5"), 0.9)),
        (0.45, caixa_de_musica(nota_hz("E5"), 0.9, 0.6)), (0.75, caixa_de_musica(nota_hz("C6"), 1.2, 0.9))), 0.85)))

    salva("derrota", fecha(normaliza(mistura(
        (0.0, triangulo(nota_hz("E4"), 0.5, 1, 3)), (0.35, triangulo(nota_hz("D4"), 0.5, 1, 3)),
        (0.7, triangulo(nota_hz("C4"), 0.9, 1, 3))), 0.7)))

    salva("choca", fecha(normaliza(mistura(
        *[(i, soma(ruido(0.05, 1.0, 60, 2), varredura(900, 500, 0.05, 0.5, 40))) for i in (0.0, 0.5, 0.9, 1.2, 1.4, 1.55, 1.7)]), 0.7)))

    salva("presente", fecha(normaliza(mistura(
        *[(i * 0.09, caixa_de_musica(nota_hz(n), 0.9)) for i, n in enumerate(["G5", "B5", "D6", "G6"])],
        (0.45, caixa_de_musica(nota_hz("B6"), 1.0, 0.5))), 0.8)))

    salva("pegar", fecha(normaliza(varredura(420, 980, 0.09, 1.0, 10), 0.6)))
    salva("pegar_doce", fecha(normaliza(mistura(
        (0.0, varredura(500, 1100, 0.08, 1.0, 10)), (0.07, caixa_de_musica(nota_hz("E6"), 0.3, 0.8))), 0.7)))
    salva("tictac", fecha(normaliza(ruido(0.04, 1.0, 80, 3), 0.4)))


# ---------------------------------------------------------------- músicas

def trilha_casa():
    """Calma, caixinha de música, 8 compassos em 84 bpm (dá ~23 s, fecha em loop)."""
    bpm = 84
    beat = 60 / bpm
    acordes = [("C", ["C4", "E4", "G4"]), ("A", ["A3", "C4", "E4"]), ("F", ["F3", "A3", "C4"]), ("G", ["G3", "B3", "D4"])] * 2
    pentatonica = ["C5", "D5", "E5", "G5", "A5", "C6"]
    rng = np.random.default_rng(11)
    total = beat * 4 * len(acordes)
    pecas = []

    for c, (nome, notas) in enumerate(acordes):
        inicio = c * 4 * beat
        raiz = nota_hz(notas[0]) / 2
        pecas.append((inicio, triangulo(raiz, beat * 1.9, 0.55, 1.8)))
        pecas.append((inicio + 2 * beat, triangulo(raiz * 1.5, beat * 1.9, 0.4, 1.8)))
        # Harpa: arpejo suave subindo e descendo.
        arpejo = [notas[0], notas[1], notas[2], notas[1]] * 2
        for i, n in enumerate(arpejo):
            pecas.append((inicio + i * beat / 2, caixa_de_musica(nota_hz(n), 0.9, 0.22)))
        # Melodia: pouca nota, com respiros.
        pos = rng.integers(0, len(pentatonica))
        for tempo in (0, 1.5, 2, 3):
            if rng.random() < 0.7:
                pos = int(np.clip(pos + rng.choice([-2, -1, 0, 1, 2]), 0, len(pentatonica) - 1))
                pecas.append((inicio + tempo * beat, caixa_de_musica(nota_hz(pentatonica[pos]), 1.4, 0.5)))

    musica = mistura(*pecas)
    return loop_perfeito(musica, total)


def trilha_batalha():
    """Mais rápida e tensa: Am F C G, baixo em colcheias, bumbo e chimbal. 140 bpm, ~14 s."""
    bpm = 140
    beat = 60 / bpm
    acordes = [("A", "A2", ["A4", "C5", "E5"]), ("F", "F2", ["F4", "A4", "C5"]),
               ("C", "C3", ["C5", "E5", "G5"]), ("G", "G2", ["G4", "B4", "D5"])] * 2
    rng = np.random.default_rng(5)
    total = beat * 4 * len(acordes)
    pecas = []

    for c, (_, baixo, notas) in enumerate(acordes):
        inicio = c * 4 * beat
        for i in range(8):
            pecas.append((inicio + i * beat / 2, quadrada(nota_hz(baixo), beat / 2 * 0.9, 0.28, 9)))
        for i in range(4):
            pecas.append((inicio + i * beat, varredura(130, 45, 0.18, 0.9, 16)))
            pecas.append((inicio + i * beat + beat / 2, ruido(0.05, 0.22, 60, 2)))
        pecas.append((inicio + 1 * beat, ruido(0.12, 0.35, 28, 4)))
        pecas.append((inicio + 3 * beat, ruido(0.12, 0.35, 28, 4)))
        # Arpejo de lead em semicolcheias com acento.
        ordem = [0, 1, 2, 1, 0, 2, 1, 2]
        for i, k in enumerate(ordem):
            if rng.random() < 0.9:
                pecas.append((inicio + i * beat / 2, quadrada(nota_hz(notas[k]), beat / 2 * 0.8, 0.14, 7)))

    return loop_perfeito(mistura(*pecas), total)


def loop_perfeito(musica, duracao):
    """Corta no tamanho exato e dobra a cauda (notas que passam do fim) no começo,
    pra o loop não ter silêncio nem estalo na emenda."""
    n = int(duracao * SR)
    corpo = musica[:n].copy()
    cauda = musica[n:]
    corpo[:len(cauda)] += cauda[:n]
    return fecha(normaliza(corpo, 0.7), 4)


if __name__ == "__main__":
    efeitos()
    salva("musica_casa", trilha_casa())
    salva("musica_batalha", trilha_batalha())
