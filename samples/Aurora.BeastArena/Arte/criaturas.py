"""
As criaturas, uma função por carta e o estágio de evolução como parâmetro.

Convenção de todos: virado pra DIREITA (o jogo espelha quando anda pra esquerda), pé no chão em
y ~ 88 da tela 0..100, luz de cima à esquerda. Cada estágio precisa ser reconhecível de longe num
celular — por isso a evolução muda silhueta (juba, placas, cabeças), não só cor.

Nada aqui usa verde-água nem magenta: são as cores dos times, e o time é o aro no chão.
"""

import math

from pincel import (TINTA, Tela, clarear, cor, dentro, escurecer, menos, misturar, uniao)

OURO = cor("#F4C542")
BRASA = cor("#FFE27A")
RUBI = cor("#C73E3A")
AÇO = cor("#9AA6B2")
OSSO = (250, 246, 236)


def _olho(t, x, y, r, iris, brilhar=False):
    if brilhar:
        t.brilho(x, y, r * 3.5, iris, 0.6)
    t.colar(t.elipse(x, y, r * 1.05, r * 0.95), TINTA)
    t.colar(t.elipse(x, y, r * 0.85, r * 0.78), iris)
    t.colar(t.circulo(x + r * 0.25, y, r * 0.4), TINTA)
    t.colar(t.circulo(x - r * 0.25, y - r * 0.3, r * 0.26), (255, 255, 255))


def _coroa(t, x, y, largura, altura, angulo=0.0):
    """Coroa de ouro com rubi; (x, y) = meio da base."""
    a = math.radians(angulo)
    ux, uy = math.cos(a), math.sin(a)      # ao longo da base
    vx, vy = uy, -ux                       # pra cima
    h = largura / 2

    def p(s, u):
        return (x + ux * s + vx * u, y + uy * s + vy * u)

    base = t.poligono([p(-h, 0), p(h, 0), p(h, altura * 0.4), p(-h, altura * 0.4)])
    picos = []
    for i in range(3):
        s = -h + largura * (0.17 + i * 0.33)
        alto = altura * (1.0 if i == 1 else 0.8)
        picos.append(t.poligono([p(s - largura * 0.17, altura * 0.35), p(s, alto), p(s + largura * 0.17, altura * 0.35)]))
    t.pintar(uniao(base, *picos), OURO, contorno=0.8)
    cx, cy = p(0, altura * 0.22)
    t.colar(t.circulo(cx, cy, largura * 0.09), RUBI)


# =================================================================================== LOBO

def lobo(estagio):
    t = Tela()
    pelo = [cor("#B8C4CC"), cor("#7F8D9C"), cor("#4D5566")][estagio]
    barriga = [cor("#E6EAEC"), cor("#C2CAD2"), cor("#8A93A3")][estagio]
    fundo = escurecer(pelo, 0.35)

    # cauda
    t.pintar(t.tubo([(28, 57), (17, 52), (10, 42), (11, 31)], [6, 6.5, 5, 2.4]), pelo)
    t.pintar(t.tubo([(12, 38), (11, 31)], [3.2, 1.8]), barriga, contorno=0, sombra=0, luz=0)

    # patas do lado de lá
    t.pintar(t.tubo([(38, 65), (40, 77), (38, 87)], [4.2, 3.6, 3.4]), fundo, luz=0)
    t.pintar(t.tubo([(62, 66), (67, 77), (68, 87)], [4, 3.4, 3.2]), fundo, luz=0)

    # corpo
    t.pintar(t.elipse(46, 60, 24, 13, -6), pelo)
    t.pintar(t.elipse(48, 67, 16, 5, -4), barriga, contorno=0, sombra=0.15, luz=0)

    # pata traseira
    t.pintar(t.elipse(33, 64, 10, 12, -15), pelo)
    t.pintar(t.tubo([(33, 71), (27, 80), (30, 87)], [4.6, 3.8, 3.6]), pelo)
    t.pintar(t.elipse(33, 88.5, 5.2, 2.6), barriga, luz=0)

    # peito
    t.pintar(t.elipse(64, 60, 10, 13, 15), barriga, contorno=0.6)

    # pata dianteira
    t.pintar(t.tubo([(61, 64), (59, 77), (60, 87)], [5.2, 4, 3.7]), pelo)
    t.pintar(t.elipse(62, 88.5, 5.2, 2.6), barriga, luz=0)

    # juba (evoluídos): colar de pontas em volta do pescoço — muda a silhueta de longe
    if estagio >= 1:
        juba = escurecer(pelo, 0.2)
        n = 9 if estagio == 1 else 12
        comprimento = 13 if estagio == 1 else 18
        pontas = [t.espinho(63, 49, 100 + i * (190 / (n - 1)), comprimento + (i % 2) * 5, 9) for i in range(n)]
        t.pintar(uniao(t.elipse(63, 49, 12, 15, 15), *pontas), juba)
        t.pintar(t.elipse(62, 56, 7, 8, 15), clarear(juba, 0.25), contorno=0, sombra=0, luz=0)

    # ombreira de ouro
    if estagio == 2:
        t.pintar(t.elipse(56, 57, 10, 8, -20), OURO)
        for i in range(3):
            t.pintar(t.espinho(50 + i * 6, 52 - i * 1.5, -115 + i * 14, 9, 4), clarear(OURO, 0.25), contorno=0.7)
        t.colar(t.circulo(57, 58, 2.2), RUBI)

    # orelha de lá
    t.pintar(t.poligono([(63, 40), (65, 23), (74, 35)]), fundo)

    # cabeça
    t.pintar(t.elipse(72, 44, 13, 11, -5), pelo)
    focinho = t.poligono([(76, 42), (95, 45), (97, 49), (92, 53), (78, 55)])
    t.pintar(uniao(focinho, t.elipse(80, 49, 8, 6)), pelo, sombra=0.25)
    t.pintar(t.elipse(84, 52.5, 9, 3.2, 8), barriga, contorno=0, sombra=0, luz=0)

    if estagio >= 1:
        # boca aberta com presas: o alfa rosna
        t.pintar(t.poligono([(80, 53), (95, 51), (90, 58), (80, 57)]), cor("#5A2230"), contorno=0.7, sombra=0, luz=0)
        for x in (84, 89):
            t.pintar(t.espinho(x, 52.5, 90, 3.5, 2.2), OSSO, contorno=0.4, sombra=0, luz=0)
    else:
        t.colar(t.linha([(83, 54.5), (91, 53)], 0.8), TINTA)

    # orelha de cá
    t.pintar(t.poligono([(69, 37), (76, 21), (82, 37)]), pelo)
    t.colar(t.poligono([(73, 34), (76, 26), (79, 34)]), escurecer(misturar(pelo, cor("#D98E8E"), 0.4), 0.2))

    t.colar(t.elipse(95, 46.5, 3, 2.4, 20), TINTA)
    _olho(t, 78, 41, 2.6, BRASA if estagio == 2 else cor("#F2B84B"), brilhar=estagio == 2)

    if estagio >= 1:
        t.colar(t.linha([(68, 39), (73, 49)], 0.9), cor("#E9D9D2"))   # cicatriz

    if estagio == 2:
        _coroa(t, 70, 35, 13, 9, -14)

    t.contorno_externo()
    return t


# =================================================================================== RINOCERONTE

def rinoceronte(estagio):
    t = Tela()
    couro = [cor("#9C9486"), cor("#857D72")][estagio]
    fundo = escurecer(couro, 0.35)
    unha = cor("#E3DccB")

    # cauda
    t.pintar(t.tubo([(17, 52), (11, 58), (10, 64)], [2.2, 1.8, 1.4]), couro, luz=0)
    t.pintar(t.elipse(10, 66, 2.4, 3.2), fundo, luz=0)

    # patas de lá
    t.pintar(t.tubo([(36, 70), (37, 86)], [6.5, 6]), fundo, luz=0)
    t.pintar(t.tubo([(64, 70), (66, 86)], [6.5, 6]), fundo, luz=0)

    # corpo + corcova
    corpo = uniao(t.elipse(42, 60, 28, 20, -3), t.elipse(58, 49, 16, 12, -10))
    t.pintar(corpo, couro)
    t.pintar(t.elipse(44, 72, 20, 5), escurecer(couro, 0.12), contorno=0, sombra=0, luz=0)

    # dobras do couro
    dobra = escurecer(couro, 0.3)
    t.colar(t.linha([(62, 40), (65, 52), (63, 66)], 0.9), dobra)
    t.colar(t.linha([(26, 46), (24, 58), (27, 70)], 0.9), dobra)

    # patas de cá
    for x0, x1 in ((28, 26), (57, 55)):
        t.pintar(t.tubo([(x0, 68), (x1, 86)], [7.5, 6.8]), couro)
        for dx in (-3.5, 0, 3.5):
            t.pintar(t.elipse(x1 + dx, 88.5, 2, 1.6), unha, contorno=0.5, sombra=0, luz=0)

    # placas de armadura (Blindado)
    if estagio == 1:
        casco = uniao(t.elipse(42, 58, 29, 21, -3), t.elipse(58, 48, 17, 13, -10))
        for x0, x1 in ((12, 32), (30, 50), (48, 72)):
            placa = dentro(casco, t.poligono([(x0, 20), (x1 + 3, 20), (x1, 60), (x0 - 3, 60)]))
            t.pintar(placa, AÇO, contorno=0.9)
            t.colar(dentro(placa, t.faixa(0, 58, 90, 0, 3.5, 200)), OURO)
            for i in range(3):
                t.colar(t.circulo(x0 + 3 + i * (x1 - x0 - 6) / 2, 55.5, 1.2), clarear(AÇO, 0.5))

    # orelha
    t.pintar(t.poligono([(66, 46), (61, 34), (71, 42)]), couro)

    # cabeça, baixa, pronta pra investir
    cabeca = uniao(t.elipse(76, 58, 15, 12, 18), t.elipse(88, 67, 9, 8, 10))
    t.pintar(cabeca, couro)
    if estagio == 1:
        t.pintar(dentro(cabeca, t.poligono([(64, 40), (84, 44), (88, 58), (70, 60)])), AÇO, contorno=0.8)
        t.colar(t.circulo(76, 51, 1.3), clarear(AÇO, 0.5))
        t.colar(t.circulo(82, 54, 1.3), clarear(AÇO, 0.5))

    # chifres
    chifre = OSSO if estagio == 0 else clarear(AÇO, 0.25)
    t.pintar(t.poligono([(83, 62), (95, 64), (98, 44), (95, 34), (92, 46)]), chifre)
    t.pintar(t.poligono([(77, 55), (84, 57), (83, 45)]), chifre)
    if estagio == 1:
        t.pintar(t.poligono([(95.5, 40), (98, 44), (95, 34)]), OURO, contorno=0.5, sombra=0, luz=0)

    t.colar(t.elipse(94, 70, 1.4, 1), TINTA)    # narina
    t.colar(t.linha([(84, 73), (93, 74)], 0.8), TINTA)
    _olho(t, 76, 57, 1.8, cor("#2C2420"))
    t.colar(t.linha([(72, 53), (79, 55)], 1.0), TINTA)   # sobrancelha brava

    t.contorno_externo()
    return t


# =================================================================================== VESPAS

def vespas(estagio):
    t = Tela()
    rainha = estagio == 1
    amarelo = cor("#E2B93B") if not rainha else cor("#F2A23A")
    preto = cor("#3A2C26")
    asa = cor("#D8F1FF")

    # asa de lá
    t.pintar(t.elipse(40, 30, 16 + 4 * rainha, 7, -60), asa, contorno=0.6, opacidade=0.5)
    if rainha:
        t.pintar(t.elipse(34, 40, 13, 5.5, -25), asa, contorno=0.6, opacidade=0.45)

    # patinhas
    for pts in ([(52, 58), (48, 66), (50, 73)], [(57, 58), (58, 67), (62, 73)], [(61, 56), (66, 63), (69, 68)]):
        t.colar(t.linha(pts, 1.4), preto)

    # abdômen listrado com ferrão
    comprimento = 20 if not rainha else 25
    cx, cy, ang = 34 - 3 * rainha, 62 + 2 * rainha, -25
    abdomen = t.elipse(cx, cy, comprimento, 13 + 2 * rainha, ang)
    ponta = (cx - math.cos(math.radians(ang)) * comprimento, cy - math.sin(math.radians(ang)) * comprimento)
    t.pintar(t.espinho(ponta[0] + 3, ponta[1] - 1.3, 155, 9 + 3 * rainha, 5), preto if not rainha else cor("#5B2A6E"))
    t.pintar(abdomen, amarelo)
    for d in ((-12, -4, 5) if not rainha else (-16, -8, 0, 8)):
        t.colar(dentro(abdomen, t.faixa(cx, cy, ang, d, 4)), preto)
    t.colar(dentro(abdomen, t.faixa(cx, cy, ang, -comprimento + 3, 6)), preto)

    # tórax peludinho
    t.pintar(t.elipse(56, 50, 11, 10), preto)
    t.colar(t.elipse(54, 46, 5, 3, -20), amarelo)
    if rainha:
        pelos = [t.espinho(56, 50, 200 + i * 30, 14, 5) for i in range(6)]
        t.pintar(uniao(*pelos), cor("#F8E6B0"), contorno=0.6)
        t.pintar(t.elipse(56, 50, 10, 9), preto, contorno=0.6)

    # cabeça
    t.pintar(t.elipse(69, 44, 9, 9), amarelo)
    t.pintar(t.elipse(73, 42, 4.5, 5.5, 15), cor("#6B1F22"), contorno=0.5, sombra=0.2)
    t.colar(t.circulo(71.8, 40, 1.4), (255, 255, 255))
    t.pintar(t.espinho(74, 51, 70, 5, 3), preto, contorno=0.5, sombra=0, luz=0)   # mandíbula

    # antenas
    t.colar(t.linha([(65, 37), (62, 27), (67, 20)], 1.1), preto)
    t.colar(t.linha([(69, 36), (70, 26), (77, 21)], 1.1), preto)

    if rainha:
        _coroa(t, 67, 36, 12, 9, -8)

    # asa de cá, por cima do corpo
    t.pintar(t.elipse(44, 34, 19 + 5 * rainha, 8 + rainha, -38), asa, contorno=0.6, opacidade=0.55)
    t.colar(t.linha([(56, 44), (44, 34), (30, 26)], 0.5), escurecer(asa, 0.3))

    t.contorno_externo(0.6)
    return t


# =================================================================================== SERPENTE

def serpente(estagio):
    t = Tela()
    escama = [cor("#66BB6A"), cor("#5E8F3A"), cor("#2F6B4F")][estagio]
    ventre = [cor("#D9EFA8"), cor("#E8D58A"), cor("#E8C25A")][estagio]
    mancha = escurecer(escama, 0.35)
    fundo = escurecer(escama, 0.2)

    # espiral no chão
    t.pintar(t.tubo([(58, 84), (40, 90), (22, 86), (18, 77), (30, 71), (48, 74)], [6.5, 7.5, 7.5, 7, 6.5, 6]), fundo)
    t.pintar(t.tubo([(6, 88), (14, 90), (26, 91), (44, 90), (60, 86), (64, 78), (58, 70)], [1.2, 3, 5.5, 7.5, 8, 7.5, 7]), escama)

    def pescoco(pts, raios, lado=1):
        corpo = t.tubo(pts, raios)
        t.pintar(corpo, escama)
        barriga = [(x + 3.2 * lado, y + 1) for x, y in pts[1:]]
        t.pintar(dentro(corpo, t.tubo(barriga, [r * 0.55 for r in raios[1:]])), ventre, contorno=0, sombra=0.12, luz=0)
        for (x, y), r in list(zip(pts, raios))[1:-1]:
            t.colar(t.elipse(x - r * 0.35 * lado, y, r * 0.35, r * 0.28), mancha)

    def cabeca(hx, hy, s, olho_cor, chifres=False):
        if chifres:
            t.pintar(t.espinho(hx - 4 * s, hy - 4 * s, -130, 9 * s, 3.5 * s), OSSO, contorno=0.6)
        forma = uniao(t.elipse(hx, hy, 10 * s, 7 * s, 8), t.elipse(hx + 7 * s, hy + 2 * s, 6 * s, 4.5 * s, 12))
        t.pintar(forma, escama)
        t.colar(t.linha([(hx + 3 * s, hy + 4.5 * s), (hx + 12 * s, hy + 4.2 * s)], 0.7), TINTA)
        # língua bifurcada
        lx, ly = hx + 13 * s, hy + 4 * s
        t.colar(t.linha([(lx, ly), (lx + 6 * s, ly + 1 * s)], 0.9), cor("#D8344A"))
        t.colar(t.linha([(lx + 6 * s, ly + 1 * s), (lx + 9 * s, ly - 1 * s)], 0.8), cor("#D8344A"))
        t.colar(t.linha([(lx + 6 * s, ly + 1 * s), (lx + 9 * s, ly + 3 * s)], 0.8), cor("#D8344A"))
        _olho(t, hx + 3 * s, hy - 1.5 * s, 2.3 * s, olho_cor, brilhar=estagio == 2)
        t.colar(t.linha([(hx - 1 * s, hy - 4.5 * s), (hx + 6 * s, hy - 3 * s)], 0.9), TINTA)

    if estagio < 2:
        pts = [(56, 76), (54, 62), (58, 48), (66, 38), (72, 32)]
        raios = [7, 6.5, 6, 5.5, 5]
        if estagio == 1:
            # capuz da naja, com os "óculos" dourados
            capuz = uniao(t.elipse(58, 44, 16, 19, 18), t.elipse(66, 34, 11, 11))
            t.pintar(capuz, escurecer(escama, 0.1))
            t.pintar(menos(capuz, t.elipse(60, 44, 12.5, 15.5, 18)), OURO, contorno=0, sombra=0, luz=0)
            for y in (36, 47):
                t.colar(t.elipse(49, y, 3.4, 2.8), TINTA)
                t.colar(t.elipse(49, y, 1.8, 1.4), OURO)
        pescoco(pts, raios)
        cabeca(76, 30, 1.0, cor("#F4D35E"))
    else:
        # hidra: três pescoços saindo da espiral
        for pts, raios, (hx, hy, s) in (
            ([(40, 78), (32, 62), (30, 46), (36, 34)], [6, 5.5, 5, 4.5], (40, 30, 0.85)),
            ([(62, 78), (74, 70), (80, 60), (84, 54)], [6, 5.5, 5, 4.5], (88, 52, 0.85)),
            ([(52, 76), (50, 58), (56, 40), (62, 26), (66, 20)], [7, 6.5, 6, 5.5, 5], (70, 18, 1.0)),
        ):
            pescoco(pts, raios)
            cabeca(hx, hy, s, BRASA, chifres=True)

    t.contorno_externo()
    return t


# =================================================================================== OURIÇO

def ourico(estagio):
    t = Tela()
    corpo_cor = [cor("#D4A15A"), cor("#B98A4A"), cor("#C27A3E")][estagio]
    espinho = [cor("#7A5230"), AÇO, cor("#5B2A3A")][estagio]
    ponta = [cor("#F3E3C0"), cor("#E6EEF4"), OURO][estagio]
    rosto = cor("#F0D9B0")
    comprimento = [22, 24, 32][estagio]

    cx, cy = 44, 68

    def camada(inicio, fim, n, escala, cor_base, raio_base):
        for i in range(n):
            a = inicio + (fim - inicio) * i / (n - 1)
            rad = math.radians(a)
            bx, by = cx + math.cos(rad) * raio_base, cy + math.sin(rad) * raio_base * 0.7
            comp = comprimento * escala * (0.85 + 0.15 * ((i * 7) % 3))
            t.pintar(t.espinho(bx, by, a, comp, 7), cor_base, contorno=0.7, sombra=0.25, luz=0)
            dx, dy = math.cos(rad), math.sin(rad)
            t.pintar(t.espinho(bx + dx * comp * 0.62, by + dy * comp * 0.62, a, comp * 0.38, 2.9), ponta, contorno=0, sombra=0, luz=0)

    # espinhos de trás
    camada(165, 345, 11, 1.0, escurecer(espinho, 0.15), 14)

    # patas
    t.pintar(t.elipse(36, 86, 6, 4), escurecer(corpo_cor, 0.35), luz=0)
    t.pintar(t.elipse(62, 86, 6, 4), escurecer(corpo_cor, 0.35), luz=0)

    # corpo
    t.pintar(t.elipse(cx + 2, cy + 2, 28, 18), corpo_cor)

    # espinhos da frente (mais curtos, caem por cima do corpo)
    camada(185, 320, 8, 0.8, espinho, 8)

    t.pintar(t.elipse(28, 88, 6, 3.4), escurecer(corpo_cor, 0.1), luz=0)
    t.pintar(t.elipse(54, 88, 6, 3.4), escurecer(corpo_cor, 0.1), luz=0)

    # rosto
    t.pintar(uniao(t.elipse(72, 73, 13, 11, -10), t.elipse(84, 76, 7, 5.5, 5)), rosto)
    t.pintar(t.circulo(67, 64, 3.5), corpo_cor, contorno=0.7)
    t.colar(t.circulo(67, 64, 1.8), cor("#C98A7A"))
    t.colar(t.circulo(91, 75, 3), TINTA)
    t.colar(t.circulo(90, 74, 1), (255, 255, 255))
    t.colar(t.linha([(80, 81), (86, 80.5)], 0.8), TINTA)
    t.colar(t.circulo(76, 78, 2.3), cor("#F2A7A0"))   # bochecha
    _olho(t, 78, 70, 2.3, cor("#3A2A20") if estagio < 2 else BRASA, brilhar=estagio == 2)

    if estagio == 1:
        # capacete de ferro
        capacete = dentro(t.elipse(72, 73, 14, 12, -10), t.retangulo(55, 50, 95, 68))
        t.pintar(capacete, AÇO, contorno=0.8)
        for x in (64, 70, 76):
            t.colar(t.circulo(x, 65, 1.1), clarear(AÇO, 0.5))
    elif estagio == 2:
        _coroa(t, 70, 62, 14, 10, -10)

    t.contorno_externo()
    return t


# =================================================================================== XAMÃ-CORUJA

def coruja(estagio):
    t = Tela()
    ancia = estagio == 1
    pena = cor("#9A86C9") if not ancia else cor("#D9D2EA")
    rosto = cor("#D8CCF0") if not ancia else cor("#F6F2FF")
    asa = escurecer(pena, 0.25)
    manto = cor("#3E2D5E")
    madeira = cor("#6B4A2E")
    cristal = cor("#D6A8FF")

    # cajado (atrás)
    t.pintar(t.linha([(79, 90), (83, 28)], 3.2), madeira, contorno=0.8, sombra=0, luz=0)
    if not ancia:
        t.brilho(84, 22, 15, cristal, 0.7)
        t.pintar(t.poligono([(84, 12), (89, 22), (84, 31), (79, 22)]), cristal, contorno=0.8)
        for dy in (0, 5):
            t.pintar(t.elipse(80, 34 + dy, 1.5, 3.5, 20), cor("#E8743B"), contorno=0.4, sombra=0, luz=0)
    else:
        t.brilho(84, 21, 15, BRASA, 0.7)
        lua = menos(t.circulo(84, 21, 9), t.circulo(88, 18, 8))
        t.pintar(lua, OURO, contorno=0.8)
        t.pintar(t.circulo(84, 26, 3), cristal, contorno=0.6)

    # manto da anciã
    if ancia:
        t.pintar(t.poligono([(24, 44), (68, 44), (74, 86), (46, 90), (18, 86)]), manto)
        for x in (30, 46, 62):
            t.colar(t.poligono([(x - 2, 80), (x, 76), (x + 2, 80), (x, 84)]), OURO)

    # pés
    for x in (38, 54):
        t.pintar(t.tubo([(x - 3, 88), (x, 86), (x + 3, 88)], 1.6), cor("#E8A43B"), contorno=0.6, sombra=0, luz=0)

    # corpo
    t.pintar(t.elipse(46, 62, 22, 25), pena)
    peito = t.elipse(46, 68, 14, 17)
    t.pintar(peito, clarear(pena, 0.45), contorno=0, sombra=0.12, luz=0)
    for linha_y in (60, 67, 74):
        for x in (40, 46, 52):
            t.colar(t.linha([(x - 2.2, linha_y), (x, linha_y + 2), (x + 2.2, linha_y)], 0.6), escurecer(pena, 0.1))

    # asas
    t.pintar(t.elipse(26, 64, 8, 19, 12), asa)
    t.pintar(t.elipse(67, 62, 8, 17, -24), asa)
    t.pintar(t.elipse(78, 58, 4.5, 3.5), asa, contorno=0.8)   # "mão" no cajado

    # colar de xamã
    for i in range(7):
        a = math.radians(30 + i * 20)
        x, y = 46 + math.cos(a) * 15, 46 + math.sin(a) * 9
        t.pintar(t.circulo(x, y, 1.9), [cor("#E8743B"), OSSO, cor("#5FA8D3")][i % 3], contorno=0.4, sombra=0, luz=0)
    t.pintar(t.espinho(46, 56, 90, 9, 3.2), OSSO, contorno=0.5)

    # cabeça
    tufos = uniao(t.espinho(33, 30, -120, 13, 7), t.espinho(59, 30, -60, 13, 7))
    t.pintar(uniao(t.elipse(46, 38, 20, 16), tufos), pena)
    if ancia:
        capuz = menos(t.elipse(46, 36, 23, 19), t.elipse(46, 41, 18, 15))
        t.pintar(dentro(capuz, t.retangulo(0, 0, 100, 44)), manto)
    t.pintar(uniao(t.elipse(38.5, 40, 8.5, 8), t.elipse(53.5, 40, 8.5, 8)), rosto, contorno=0.6, sombra=0.12, luz=0)

    olho = BRASA if ancia else cor("#F6C945")
    _olho(t, 39, 40, 4.6, olho, brilhar=ancia)
    _olho(t, 53, 40, 4.6, olho, brilhar=ancia)
    t.pintar(t.espinho(46, 44, 90, 7, 5), cor("#E8A43B"), contorno=0.6, sombra=0.2, luz=0)

    # pintura de guerra
    t.colar(t.linha([(31, 46), (35, 49)], 1.0), cor("#E8743B"))
    t.colar(t.linha([(61, 46), (57, 49)], 1.0), cor("#E8743B"))

    if ancia:
        # barba de penas
        barba = uniao(*[t.espinho(40 + i * 4, 49, 90 + (i - 1.5) * 10, 10 + (i % 2) * 3, 5) for i in range(4)])
        t.pintar(barba, rosto, contorno=0.7)

    t.contorno_externo()
    return t
