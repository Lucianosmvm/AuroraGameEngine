"""
Tudo que não é criatura: ninho, santuário, pedras, projéteis, feitiços e enfeites de chão.

Ninho, santuário e áreas de feitiço são vistos DE CIMA (centro da tela = centro da coisa). Pedra,
pilar e ícones são 3/4, igual às criaturas. Onde o time importa (ovo do ninho, gema do pilar) o
sprite é quase branco e o jogo tinge com a cor da equipe.
"""

import math
import random

from PIL import ImageFilter

from pincel import Tela, clarear, cor, dentro, erodir, escurecer, menos, uniao

MADEIRA = [cor("#6B5639"), cor("#57462F"), cor("#8A7048"), cor("#4A3B28")]
PEDRA = cor("#77705F")
MUSGO = cor("#5E7A3E")


def _blob(t, cx, cy, r, rng, irregular=0.18, pontos=18, achatar=1.0):
    """Polígono redondo e torto — poça, pedra, mancha."""
    raios = [r * (1 + rng.uniform(-irregular, irregular)) for _ in range(pontos)]
    suaves = [(raios[i - 1] + 2 * raios[i] + raios[(i + 1) % pontos]) / 4 for i in range(pontos)]
    vertices = []
    for i in range(pontos * 4):
        a = math.tau * i / (pontos * 4)
        f = i / 4
        j = int(f)
        rr = suaves[j % pontos] + (suaves[(j + 1) % pontos] - suaves[j % pontos]) * (f - j)
        vertices.append((cx + math.cos(a) * rr, cy + math.sin(a) * rr * achatar))
    return t.poligono(vertices)


# =================================================================================== NINHO

def ninho():
    t = Tela()
    rng = random.Random(7)

    t.pintar(t.circulo(50, 50, 45), MADEIRA[3], contorno=1.2, sombra=0.25, luz=0)
    t.pintar(t.circulo(50, 50, 29), cor("#2E271E"), contorno=0, sombra=0, luz=0)

    # gravetos trançados: tangentes ao anel, três voltas de dentro pra fora
    for r0, r1 in ((28, 34), (33, 39), (38, 44)):
        for _ in range(34):
            a = rng.uniform(0, math.tau)
            r = rng.uniform(r0, r1)
            comprimento = rng.uniform(10, 18)
            inclinacao = rng.uniform(-0.35, 0.35)
            x, y = 50 + math.cos(a) * r, 50 + math.sin(a) * r
            dx, dy = -math.sin(a + inclinacao), math.cos(a + inclinacao)
            m = t.linha([(x - dx * comprimento / 2, y - dy * comprimento / 2), (x + dx * comprimento / 2, y + dy * comprimento / 2)],
                        rng.uniform(1.6, 2.6))
            t.pintar(m, MADEIRA[rng.randrange(3)], contorno=0.5, sombra=0, luz=0)

    # fundo forrado de palha fina, mais escuro no meio (onde o ovo senta)
    t.colar(t.circulo(50, 50, 27).filter(ImageFilter.GaussianBlur(4 * t.k)), cor("#4A3D2C"))
    for _ in range(40):
        a, r = rng.uniform(0, math.tau), rng.uniform(6, 26)
        x, y = 50 + math.cos(a) * r, 50 + math.sin(a) * r
        b = rng.uniform(0, math.pi)
        t.colar(t.linha([(x - math.cos(b) * 4, y - math.sin(b) * 4), (x + math.cos(b) * 4, y + math.sin(b) * 4)], 0.7),
                cor("#8A7048"), 0.7)
    return t


def ovo():
    """Quase branco: o jogo tinge com a cor do time."""
    t = Tela(128)
    casca = (246, 246, 246)
    forma = uniao(t.elipse(50, 58, 30, 34), t.elipse(50, 44, 24, 30))
    t.pintar(forma, casca, contorno=1.8, sombra=0.25, luz=0.5)
    rng = random.Random(3)
    for _ in range(9):
        t.colar(t.elipse(rng.uniform(32, 68), rng.uniform(32, 84), rng.uniform(2, 4.5), rng.uniform(1.5, 3.5), rng.uniform(0, 180)),
                (196, 196, 196))
    t.colar(t.elipse(40, 36, 5, 9, 20), (255, 255, 255), 0.9)
    t.contorno_externo(1.0)
    return t


# =================================================================================== SANTUÁRIO

def santuario():
    """Plataforma de pedra vista de cima. O anel de contas do jogo cai no sulco em r ~ 32."""
    t = Tela()
    rng = random.Random(11)

    t.pintar(t.circulo(50, 50, 48), cor("#3A3F34"), contorno=1.0, sombra=0.2, luz=0)

    # anel de lajes
    n = 16
    for i in range(n):
        a0 = math.tau * (i + 0.06) / n
        a1 = math.tau * (i + 0.94) / n
        pontos = []
        for j in range(7):
            a = a0 + (a1 - a0) * j / 6
            pontos.append((50 + math.cos(a) * 47, 50 + math.sin(a) * 47))
        for j in range(7):
            a = a1 - (a1 - a0) * j / 6
            pontos.append((50 + math.cos(a) * 37.5, 50 + math.sin(a) * 37.5))
        tom = clarear(PEDRA, rng.uniform(-0.05, 0.15)) if rng.random() > 0.5 else escurecer(PEDRA, rng.uniform(0, 0.12))
        t.pintar(t.poligono(pontos), tom, contorno=0.5, sombra=0.25, luz=0.2)

    # piso interno
    t.pintar(t.circulo(50, 50, 36), cor("#565C4C"), contorno=0.6, sombra=0.15, luz=0)
    for _ in range(10):
        a, r = rng.uniform(0, math.tau), rng.uniform(8, 30)
        t.colar(_blob(t, 50 + math.cos(a) * r, 50 + math.sin(a) * r, rng.uniform(2.5, 5), rng, 0.3, 10), MUSGO, 0.55)

    # sulco das contas
    t.colar(menos(t.circulo(50, 50, 34), t.circulo(50, 50, 30)), cor("#2C3027"))

    # runas entalhadas
    runa = cor("#3A3F34")
    for i in range(8):
        a = math.tau * i / 8 + math.pi / 8
        x, y = 50 + math.cos(a) * 22, 50 + math.sin(a) * 22
        ux, uy = math.cos(a), math.sin(a)
        tipo = i % 3
        if tipo == 0:
            pts = [(x - uy * 3, y + ux * 3), (x + ux * 3, y + uy * 3), (x + uy * 3, y - ux * 3)]
        elif tipo == 1:
            pts = [(x - ux * 3, y - uy * 3), (x + ux * 3, y + uy * 3)]
            t.colar(t.linha([(x - uy * 3, y + ux * 3), (x + uy * 3, y - ux * 3)], 1.2), runa)
        else:
            pts = [(x - uy * 3 - ux * 2, y + ux * 3 - uy * 2), (x + ux * 2, y + uy * 2), (x + uy * 3 - ux * 2, y - ux * 3 - uy * 2)]
        t.colar(t.linha(pts, 1.2), runa)

    # estrela entalhada e soquete central
    estrela = []
    for i in range(12):
        a = -math.pi / 2 + math.tau * i / 12
        r = 15 if i % 2 == 0 else 8
        estrela.append((50 + math.cos(a) * r, 50 + math.sin(a) * r))
    t.colar(menos(t.poligono(estrela), erodir(t.poligono(estrela), 1.2 * t.k)), runa)   # só o traço da borda
    t.pintar(t.circulo(50, 50, 9), cor("#2A2E26"), contorno=0.8, sombra=0, luz=0)
    return t


def pilar():
    """Pedra em pé do santuário, 3/4. A gema (cor do dono) o jogo desenha no soquete em (50, 26)."""
    t = Tela(128)
    corpo = t.poligono([(30, 90), (27, 40), (33, 22), (50, 16), (67, 22), (73, 40), (70, 90)])
    t.pintar(corpo, PEDRA, contorno=2.0, sombra=0.35, luz=0.3)
    t.colar(dentro(corpo, t.faixa(50, 60, 90, 0, 5, 100)), escurecer(PEDRA, 0.2))
    t.colar(t.linha([(40, 48), (44, 62), (41, 74)], 1.4), escurecer(PEDRA, 0.4))
    t.colar(dentro(corpo, _blob(t, 42, 22, 12, random.Random(2), 0.25, 12)), MUSGO, 0.85)
    t.pintar(t.elipse(50, 26, 11, 8), cor("#2A2E26"), contorno=1.2, sombra=0, luz=0)
    t.contorno_externo(1.2)
    return t


# =================================================================================== PEDRAS

def pedra(variante):
    t = Tela()
    rng = random.Random(100 + variante)
    base = [cor("#5E5A52"), cor("#625C50"), cor("#585752")][variante]

    corpo = _blob(t, 50, 56, 40, rng, 0.14, 11, 0.78)
    t.pintar(corpo, escurecer(base, 0.25), contorno=1.4, sombra=0.3, luz=0)

    # face de cima (mais clara) e lascas
    topo = dentro(corpo, _blob(t, 46, 46, 34, rng, 0.2, 9, 0.62))
    t.pintar(topo, base, contorno=0.6, sombra=0.2, luz=0.3)
    for _ in range(3):
        x, y = rng.uniform(30, 64), rng.uniform(34, 52)
        t.pintar(dentro(topo, _blob(t, x, y, rng.uniform(7, 12), rng, 0.3, 7, 0.7)), clarear(base, 0.15), contorno=0.4, sombra=0, luz=0.2)

    for _ in range(2):
        x, y = rng.uniform(34, 66), rng.uniform(56, 74)
        t.colar(dentro(corpo, t.linha([(x, y), (x + rng.uniform(-6, 6), y + 6), (x + rng.uniform(-4, 4), y + 11)], 1.0)), escurecer(base, 0.5))

    # musgo
    for _ in range(2 + variante):
        x, y = rng.uniform(30, 62), rng.uniform(30, 44)
        m = dentro(topo, _blob(t, x, y, rng.uniform(7, 12), rng, 0.35, 10, 0.7))
        t.pintar(m, MUSGO, contorno=0, sombra=0.25, luz=0.2)

    t.contorno_externo(0.6)
    return t


# =================================================================================== PROJÉTEIS

def espinho():
    """Aponta pra direita. Claro: o jogo tinge de ouro no Rei dos Espinhos."""
    t = Tela(128)
    t.pintar(t.poligono([(8, 50), (20, 42), (94, 50), (20, 58)]), cor("#EFE3C8"), contorno=2.5, sombra=0.3, luz=0.3)
    t.colar(t.poligono([(62, 47.5), (94, 50), (62, 52.5)]), cor("#7A5230"))
    return t


def orbe():
    t = Tela(128)
    halo = t.circulo(50, 50, 26).filter(ImageFilter.GaussianBlur(8 * t.k))
    t.colar(halo, cor("#B98CFF"), 0.9)
    t.colar(t.circulo(50, 50, 22), cor("#C9A2FF"))
    t.colar(t.circulo(50, 50, 15), cor("#EBDDFF"))
    t.colar(t.circulo(44, 44, 6), (255, 255, 255))
    return t


# =================================================================================== FEITIÇOS

def lamacal_area():
    t = Tela()
    rng = random.Random(21)
    lama = cor("#7A5634")

    t.pintar(_blob(t, 50, 50, 46, rng, 0.1, 22), escurecer(lama, 0.15), contorno=0.8, sombra=0, luz=0)
    t.pintar(_blob(t, 50, 50, 40, rng, 0.14, 16), lama, contorno=0, sombra=0.2, luz=0.25)
    for _ in range(6):
        a, r = rng.uniform(0, math.tau), rng.uniform(5, 26)
        t.colar(_blob(t, 50 + math.cos(a) * r, 50 + math.sin(a) * r, rng.uniform(5, 9), rng, 0.3, 10), escurecer(lama, 0.3), 0.8)

    for _ in range(9):
        a, r = rng.uniform(0, math.tau), rng.uniform(0, 33)
        x, y, s = 50 + math.cos(a) * r, 50 + math.sin(a) * r, rng.uniform(1.8, 4)
        t.pintar(t.circulo(x, y, s), clarear(lama, 0.15), contorno=0.5, sombra=0.3, luz=0)
        t.colar(t.circulo(x - s * 0.35, y - s * 0.35, s * 0.3), (255, 240, 210), 0.8)
    return t


def queimada_area():
    t = Tela()
    rng = random.Random(33)

    t.colar(_blob(t, 50, 50, 42, rng, 0.15, 20).filter(ImageFilter.GaussianBlur(2 * t.k)), cor("#241A16"), 0.85)

    labaredas = []
    for i in range(14):
        a = math.tau * i / 14 + rng.uniform(-0.1, 0.1)
        labaredas.append(t.espinho(50 + math.cos(a) * 14, 50 + math.sin(a) * 14, math.degrees(a), rng.uniform(26, 36), 12))
    fogo = uniao(t.circulo(50, 50, 20), *labaredas)
    t.brilho(50, 50, 50, cor("#FF7043"), 0.7)
    t.pintar(fogo, cor("#F0582B"), contorno=0, sombra=0, luz=0)

    miolo = []
    for i in range(10):
        a = math.tau * (i + 0.5) / 10
        miolo.append(t.espinho(50 + math.cos(a) * 8, 50 + math.sin(a) * 8, math.degrees(a), rng.uniform(16, 22), 9))
    t.colar(uniao(t.circulo(50, 50, 13), *miolo), cor("#FFA53A"))
    t.colar(t.circulo(50, 50, 9).filter(ImageFilter.GaussianBlur(2 * t.k)), cor("#FFF1B0"))

    for _ in range(18):
        a, r = rng.uniform(0, math.tau), rng.uniform(30, 46)
        t.colar(t.circulo(50 + math.cos(a) * r, 50 + math.sin(a) * r, rng.uniform(0.8, 1.8)), cor("#FFD27A"))
    return t


def lamacal_icone():
    t = Tela(128)
    rng = random.Random(5)
    lama = cor("#8A6340")
    monte = uniao(_blob(t, 50, 72, 38, rng, 0.08, 14, 0.42), t.elipse(50, 58, 24, 22))
    t.pintar(monte, lama, contorno=2.2, sombra=0.35, luz=0.3)
    for x, y, s in ((38, 52, 6), (60, 60, 4.5), (48, 70, 3.5), (68, 72, 3)):
        t.pintar(t.circulo(x, y, s), clarear(lama, 0.2), contorno=1.2, sombra=0.3, luz=0)
        t.colar(t.circulo(x - s * 0.35, y - s * 0.35, s * 0.3), (255, 240, 210))
    for x, y in ((22, 30), (78, 26), (84, 46)):
        t.pintar(uniao(t.circulo(x, y, 4), t.espinho(x, y - 2, -90, 7, 5)), lama, contorno=1.6, sombra=0.3, luz=0)
    t.contorno_externo(1.0)
    return t


def queimada_icone():
    t = Tela(128)

    def chama(escala, c, dy=0.0):
        s = escala
        pontos = [(50, 90 + dy)]
        for x, y in ((24, 74), (22, 52), (32, 34), (36, 48), (44, 18), (54, 38), (62, 10), (72, 40), (78, 32), (80, 56), (76, 76)):
            pontos.append((50 + (x - 50) * s, 90 + dy + (y - 90) * s))
        return t.poligono(pontos), c

    t.brilho(50, 56, 48, cor("#FF7043"), 0.6)
    for escala, c, dy in ((1.0, cor("#E8442A"), 0), (0.72, cor("#FF9A2E"), 0), (0.42, cor("#FFE27A"), 0)):
        m, cc = chama(escala, c, dy)
        t.pintar(m, cc, contorno=2.0 if escala == 1.0 else 0, sombra=0.2 if escala == 1.0 else 0, luz=0)
    t.contorno_externo(1.0)
    return t


# =================================================================================== ENFEITES DE CHÃO

def tufo(variante):
    t = Tela(64)
    rng = random.Random(50 + variante)
    verde = [cor("#4C6236"), cor("#56693A")][variante]
    folhas = []
    for i in range(5 + variante):
        base_x = 34 + i * (32 / (4 + variante)) + rng.uniform(-3, 3)
        angulo = -90 + (base_x - 50) * 1.4 + rng.uniform(-10, 10)
        folhas.append(t.espinho(base_x, 84, angulo, rng.uniform(40, 62), 10))
    t.pintar(uniao(*folhas), verde, contorno=2.2, sombra=0.3, luz=0.25)
    return t


def flor(variante):
    t = Tela(64)
    petala = [cor("#E9E1C8"), cor("#E5B94E")][variante]
    t.colar(t.linha([(50, 88), (50, 56)], 3.5), cor("#3E5230"))
    for i in range(5):
        a = math.tau * i / 5 - math.pi / 2
        t.pintar(t.elipse(50 + math.cos(a) * 12, 46 + math.sin(a) * 12, 10, 7, math.degrees(a)), petala, contorno=3.0, sombra=0.25, luz=0)
    t.pintar(t.circulo(50, 46, 7), cor("#D9822B") if variante == 0 else cor("#7A4A22"), contorno=2.0, sombra=0, luz=0)
    return t
