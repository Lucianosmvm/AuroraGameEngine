"""
Os bichinhos: uma função por espécie, o estágio de evolução (0 = filhote, 1 = jovem, 2 = adulto)
como parâmetro.

Convenção: de FRENTE pra câmera (é um bichinho virtual, ele olha pra quem cuida), pé no chão em
y ~ 90 da tela 0..100, luz de cima à esquerda. Cada estágio muda a SILHUETA, não só a cor — no
celular o jogador precisa ver de longe que o bicho evoluiu.

Os olhos ficam abertos em todos os sprites; dormir, ficar triste e doente é desenhado pelo jogo por
cima (pálpebra, gotinha, cor), senão seriam 4 sprites por estágio.
"""

import math

from pincel import TINTA, Tela, clarear, cor, dentro, escurecer, menos, uniao

BRANCO = (255, 255, 255)
ROSA = cor("#FF8FA3")


# ============================================================================ peças comuns

def olhos(t, cx, cy, sep, r, iris=TINTA):
    """Olhão de bichinho virtual: oval escuro, dois brilhos."""
    for lado in (-1, 1):
        x = cx + lado * sep
        t.colar(t.elipse(x, cy, r * 0.82, r), iris)
        t.colar(t.circulo(x - r * 0.28, cy - r * 0.38, r * 0.34), BRANCO)
        t.colar(t.circulo(x + r * 0.3, cy + r * 0.35, r * 0.14), BRANCO)


def bochechas(t, cx, cy, sep, r):
    for lado in (-1, 1):
        t.colar(t.elipse(cx + lado * sep, cy, r, r * 0.6), ROSA, 0.75)


def sorriso(t, cx, cy, largura, grossura=0.9):
    pontos = []
    for i in range(9):
        a = math.pi * (0.15 + 0.7 * i / 8)
        pontos.append((cx - math.cos(a) * largura / 2, cy + math.sin(a) * largura * 0.32))
    t.colar(t.tubo(pontos, grossura), TINTA)


def boca_aberta(t, cx, cy, largura):
    m = t.elipse(cx, cy + largura * 0.18, largura / 2, largura * 0.36)
    corte = t.retangulo(cx - largura, cy - largura, cx + largura, cy + largura * 0.08)
    boca = menos(m, corte)
    t.colar(boca, TINTA)
    t.colar(dentro(boca, t.elipse(cx, cy + largura * 0.42, largura * 0.3, largura * 0.2)), cor("#E8586E"))


def pezinhos(t, cx, y, sep, rx, ry, c):
    for lado in (-1, 1):
        t.pintar(t.elipse(cx + lado * sep, y, rx, ry), c, contorno=0.9, desloc=1.2)


def chama(t, x, y, altura, largura, angulo=0.0):
    """Labareda de três camadas; (x, y) = base."""
    a = math.radians(angulo)

    def forma(escala):
        h, w = altura * escala, largura * escala
        pts = []
        for i in range(24):
            s = i / 23
            # gota de ponta fina, meio gordo
            raio = w / 2 * math.sin(math.pi * min(1.0, s * 1.15)) ** 0.8
            pts.append((raio, -h * s))
        contorno = [(p[0], p[1]) for p in pts] + [(-p[0], p[1]) for p in reversed(pts)]
        rot = [(x + px * math.cos(a) - py * math.sin(a), y + px * math.sin(a) + py * math.cos(a)) for px, py in contorno]
        return t.poligono(rot)

    t.pintar(forma(1.0), cor("#FF5A2A"), contorno=0.8, sombra=0.2, luz=0.1)
    t.colar(forma(0.7), cor("#FFA23A"))
    t.colar(forma(0.4), cor("#FFE27A"))


def folha(t, x, y, comprimento, largura, angulo, c=None):
    """Folha pontuda com nervura; (x, y) = cabinho."""
    c = c or cor("#5DBB4A")
    a = math.radians(angulo)
    dx, dy = math.cos(a), math.sin(a)
    nx, ny = -dy, dx
    pts = []
    for i in range(20):
        s = i / 19
        w = largura / 2 * math.sin(math.pi * s)
        pts.append((x + dx * comprimento * s + nx * w, y + dy * comprimento * s + ny * w))
    for i in range(19, -1, -1):
        s = i / 19
        w = largura / 2 * math.sin(math.pi * s)
        pts.append((x + dx * comprimento * s - nx * w, y + dy * comprimento * s - ny * w))
    t.pintar(t.poligono(pts), c, contorno=0.8, desloc=1.0)
    t.colar(t.linha([(x, y), (x + dx * comprimento * 0.8, y + dy * comprimento * 0.8)], 0.7), escurecer(c, 0.3))


def florzinha(t, x, y, r, petala=None, miolo=None):
    petala = petala or cor("#FFB3C7")
    miolo = miolo or cor("#FFD54F")
    pet = uniao(*[t.circulo(x + math.cos(math.tau * i / 5) * r * 0.62, y + math.sin(math.tau * i / 5) * r * 0.62, r * 0.48)
                  for i in range(5)])
    t.pintar(pet, petala, contorno=0.6, sombra=0.15, luz=0.1)
    t.colar(t.circulo(x, y, r * 0.36), miolo)


def sombra_chao(t, cx, largura):
    t.colar(t.elipse(cx, 91, largura / 2, 3.2), (0, 0, 0), 0.22)


# ============================================================================ BRASINHA (fogo)

def brasinha(estagio):
    t = Tela()
    pele = [cor("#FF8A3D"), cor("#F4702A"), cor("#D9482B")][estagio]
    barriga = [cor("#FFE0B0"), cor("#FFD39A"), cor("#F7C07E")][estagio]

    if estagio == 0:
        sombra_chao(t, 50, 46)
        pezinhos(t, 50, 86, 11, 7, 4.5, pele)
        corpo = t.circulo(50, 64, 24)
        t.pintar(corpo, pele)
        t.colar(dentro(corpo, t.elipse(50, 74, 14, 12)), barriga)
        chama(t, 50, 43, 20, 16)
        olhos(t, 50, 60, 9, 5)
        bochechas(t, 50, 68, 15, 4)
        sorriso(t, 50, 69, 6)

    elif estagio == 1:
        sombra_chao(t, 50, 52)
        # cauda com chama na ponta, saindo pra direita
        t.pintar(t.tubo([(58, 80), (72, 82), (82, 74), (84, 64)], [6, 5, 4, 3]), pele)
        chama(t, 84, 64, 16, 12, 15)
        pezinhos(t, 50, 86, 12, 8, 5, escurecer(pele, 0.1))
        corpo = t.elipse(50, 64, 21, 23)
        t.pintar(corpo, pele)
        t.colar(dentro(corpo, t.elipse(50, 72, 13, 14)), barriga)
        # bracinhos
        t.pintar(t.tubo([(31, 62), (26, 70)], 4), pele, contorno=0.9, desloc=1)
        t.pintar(t.tubo([(69, 62), (74, 70)], 4), pele, contorno=0.9, desloc=1)
        # cabeça separada, maior (fofura)
        cabeca = t.elipse(50, 40, 22, 18)
        # chifrinhos
        t.pintar(t.espinho(38, 26, -120, 9, 6), cor("#FFE9C4"), contorno=0.8)
        t.pintar(t.espinho(62, 26, -60, 9, 6), cor("#FFE9C4"), contorno=0.8)
        t.pintar(cabeca, pele)
        chama(t, 50, 24, 13, 11)
        olhos(t, 50, 40, 9, 4.8)
        bochechas(t, 50, 48, 15, 3.6)
        boca_aberta(t, 50, 47, 7)

    else:
        sombra_chao(t, 50, 64)
        asa = cor("#8E2A3A")
        # asas atrás do corpo
        for lado in (-1, 1):
            pts = [(50 + lado * 14, 50), (50 + lado * 44, 22), (50 + lado * 40, 40), (50 + lado * 46, 46),
                   (50 + lado * 36, 54), (50 + lado * 40, 62), (50 + lado * 20, 62)]
            t.pintar(t.poligono(pts), asa, contorno=1.0, sombra=0.25)
        t.pintar(t.tubo([(62, 80), (78, 84), (90, 76), (92, 64)], [8, 6, 5, 4]), pele)
        chama(t, 92, 64, 20, 15, 10)
        pezinhos(t, 50, 86, 14, 10, 6, escurecer(pele, 0.15))
        corpo = t.elipse(50, 66, 24, 22)
        t.pintar(corpo, pele)
        barr = dentro(corpo, t.elipse(50, 72, 15, 15))
        t.colar(barr, barriga)
        for i in range(3):
            t.colar(dentro(barr, t.faixa(50, 62, 90, i * 6, 1.0, 40)), escurecer(barriga, 0.25))
        t.pintar(t.tubo([(28, 60), (22, 70)], 5), pele, contorno=0.9, desloc=1)
        t.pintar(t.tubo([(72, 60), (78, 70)], 5), pele, contorno=0.9, desloc=1)
        cabeca = t.elipse(50, 38, 21, 17)
        chifre = cor("#FFE9C4")
        t.pintar(t.tubo([(36, 28), (30, 18), (32, 9)], [3.6, 2.6, 1.2]), chifre, contorno=0.8)
        t.pintar(t.tubo([(64, 28), (70, 18), (68, 9)], [3.6, 2.6, 1.2]), chifre, contorno=0.8)
        t.pintar(cabeca, pele)
        chama(t, 50, 23, 15, 12)
        olhos(t, 50, 38, 8.5, 4.4, iris=cor("#3A1010"))
        # sobrancelha brava: adulto é mais sério
        for lado in (-1, 1):
            t.colar(t.linha([(50 + lado * 4, 31.5), (50 + lado * 13, 30)], 1.3), TINTA)
        boca_aberta(t, 50, 46, 8)
        t.colar(t.espinho(46, 46.2, 90, 2.4, 2), BRANCO)
        t.colar(t.espinho(54, 46.2, 90, 2.4, 2), BRANCO)

    t.contorno_externo(0.6)
    return t


# ============================================================================ GOTINHA (água)

def gotinha(estagio):
    t = Tela()
    pele = [cor("#5EC8F2"), cor("#4AA8E8"), cor("#3574D4")][estagio]
    claro = clarear(pele, 0.45)
    guelra = cor("#FF8FB8")

    if estagio == 0:
        sombra_chao(t, 50, 44)
        corpo = uniao(t.circulo(50, 66, 23),
                      t.poligono([(50, 24), (70, 58), (30, 58)]))
        t.pintar(corpo, pele)
        t.colar(dentro(corpo, t.elipse(40, 52, 5, 9, -25)), BRANCO, 0.55)
        olhos(t, 50, 66, 9, 5)
        bochechas(t, 50, 74, 15, 3.8)
        sorriso(t, 50, 75, 6)

    elif estagio == 1:
        sombra_chao(t, 50, 54)
        # cauda de peixe atrás
        t.pintar(t.tubo([(58, 82), (74, 84), (86, 76)], [7, 5, 3]), pele)
        t.pintar(t.poligono([(84, 78), (95, 64), (92, 84)]), claro, contorno=0.8)
        pezinhos(t, 50, 86, 12, 8, 4.5, pele)
        corpo = t.elipse(50, 70, 20, 17)
        t.pintar(corpo, pele)
        t.colar(dentro(corpo, t.elipse(50, 76, 12, 10)), claro)
        cabeca = t.elipse(50, 46, 27, 19)
        # guelras de axolote, três de cada lado
        for lado in (-1, 1):
            for i, ang in enumerate((-40, -10, 20)):
                a = math.radians(ang)
                bx, by = 50 + lado * 22, 40 + i * 5
                ex, ey = bx + lado * math.cos(a) * 14, by + math.sin(a) * 14
                t.pintar(t.tubo([(bx, by), ((bx + ex) / 2, (by + ey) / 2 - 2), (ex, ey)], [3, 2.6, 2]), guelra, contorno=0.8, desloc=0.8)
        t.pintar(cabeca, pele)
        olhos(t, 50, 45, 12, 4.6)
        bochechas(t, 50, 53, 19, 3.4)
        sorriso(t, 50, 53, 9)

    else:
        sombra_chao(t, 50, 66)
        t.pintar(t.tubo([(62, 84), (80, 86), (92, 74)], [9, 6, 4]), pele)
        t.pintar(t.poligono([(90, 76), (99, 58), (98, 84)]), claro, contorno=0.8)
        pezinhos(t, 50, 86, 15, 10, 5.5, escurecer(pele, 0.1))
        corpo = t.elipse(50, 68, 25, 21)
        t.pintar(corpo, pele)
        t.colar(dentro(corpo, t.elipse(50, 74, 16, 13)), claro)
        # pintinhas nas costas
        for x, y in ((32, 62), (68, 62), (36, 76), (64, 76)):
            t.colar(dentro(corpo, t.circulo(x, y, 2.4)), escurecer(pele, 0.3))
        t.pintar(t.tubo([(27, 64), (20, 74)], 5), pele, contorno=0.9, desloc=1)
        t.pintar(t.tubo([(73, 64), (80, 74)], 5), pele, contorno=0.9, desloc=1)
        # crista de barbatana na cabeça
        crista = t.poligono([(38, 30), (44, 12), (50, 26), (56, 8), (62, 28)])
        t.pintar(crista, claro, contorno=0.9)
        cabeca = t.elipse(50, 40, 28, 18)
        for lado in (-1, 1):
            for i, ang in enumerate((-50, -20, 10)):
                a = math.radians(ang)
                bx, by = 50 + lado * 24, 34 + i * 5
                ex, ey = bx + lado * math.cos(a) * 18, by + math.sin(a) * 18
                t.pintar(t.tubo([(bx, by), ((bx + ex) / 2, (by + ey) / 2 - 3), (ex, ey)], [3.6, 3, 2.2]), guelra, contorno=0.8, desloc=0.8)
        t.pintar(cabeca, pele)
        olhos(t, 50, 39, 12, 4.4, iris=cor("#0E2140"))
        for lado in (-1, 1):
            t.colar(t.linha([(50 + lado * 7, 32.5), (50 + lado * 16, 31.5)], 1.2), TINTA)
        boca_aberta(t, 50, 46, 9)

    t.contorno_externo(0.6)
    return t


# ============================================================================ BROTINHO (planta)

def brotinho(estagio):
    t = Tela()
    pele = [cor("#8ED66B"), cor("#6CC45A"), cor("#4E9E4A")][estagio]
    barriga = [cor("#EAF7C8"), cor("#DDF0B4"), cor("#C9E39C")][estagio]

    if estagio == 0:
        sombra_chao(t, 50, 46)
        pezinhos(t, 50, 86, 11, 7, 4.5, pele)
        # broto: caule e duas folhas
        t.colar(t.tubo([(50, 44), (50, 34)], 1.3), cor("#3F8A34"))
        folha(t, 50, 34, 15, 9, -150)
        folha(t, 50, 34, 15, 9, -30)
        corpo = t.elipse(50, 65, 25, 23)
        t.pintar(corpo, pele)
        t.colar(dentro(corpo, t.elipse(50, 75, 14, 11)), barriga)
        olhos(t, 50, 62, 9, 5)
        bochechas(t, 50, 70, 15, 4)
        sorriso(t, 50, 70, 6)

    elif estagio == 1:
        sombra_chao(t, 50, 50)
        # orelhas de folha, compridas
        folha(t, 40, 34, 30, 12, -110, cor("#5DBB4A"))
        folha(t, 60, 34, 30, 12, -70, cor("#5DBB4A"))
        florzinha(t, 64, 12, 5)
        pezinhos(t, 50, 86, 11, 8, 5, pele)
        corpo = t.elipse(50, 70, 19, 18)
        t.pintar(corpo, pele)
        t.colar(dentro(corpo, t.elipse(50, 74, 11, 12)), barriga)
        # rabinho de pompom
        t.pintar(t.circulo(70, 78, 5), barriga, contorno=0.8)
        cabeca = t.elipse(50, 46, 20, 17)
        t.pintar(cabeca, pele)
        olhos(t, 50, 45, 8, 4.6)
        bochechas(t, 50, 52, 14, 3.4)
        t.colar(t.elipse(50, 50, 1.8, 1.3), cor("#E26D7E"))
        sorriso(t, 50, 52, 5)

    else:
        sombra_chao(t, 50, 60)
        galho = cor("#7A5537")
        # galhadas com flores
        for lado in (-1, 1):
            base = (50 + lado * 10, 30)
            meio = (50 + lado * 20, 16)
            ponta = (50 + lado * 16, 4)
            t.pintar(t.tubo([base, meio, ponta], [2.6, 2, 1.2]), galho, contorno=0.7, desloc=0.6)
            t.pintar(t.tubo([meio, (50 + lado * 32, 10)], [1.8, 1.1]), galho, contorno=0.7, desloc=0.6)
            florzinha(t, 50 + lado * 32, 10, 4.5)
            florzinha(t, 50 + lado * 16, 5, 4, cor("#FFFFFF"))
            folha(t, 50 + lado * 21, 18, 9, 5, -90 + lado * 60, cor("#6CC45A"))
        # pernas
        for x in (36, 44, 56, 64):
            t.pintar(t.tubo([(x, 72), (x, 86)], 3.2), escurecer(pele, 0.15), contorno=0.8, desloc=0.8)
        corpo = t.elipse(50, 68, 22, 13)
        t.pintar(corpo, pele)
        t.colar(dentro(corpo, t.elipse(50, 74, 14, 6)), barriga)
        # juba de folhas no pescoço
        for i, ang in enumerate(range(200, 350, 25)):
            a = math.radians(ang)
            folha(t, 50 + math.cos(a) * 10, 54 - math.sin(a) * 4, 11, 6, ang - 180 + 90 * 0, cor("#4FA83F"))
        cabeca = t.elipse(50, 40, 16, 15)
        for lado in (-1, 1):
            t.pintar(t.elipse(50 + lado * 18, 34, 7, 3.5, lado * 20), pele, contorno=0.8)
        t.pintar(cabeca, pele)
        t.colar(dentro(cabeca, t.elipse(50, 48, 8, 5)), barriga)
        olhos(t, 50, 38, 7, 4, iris=cor("#1E3316"))
        t.colar(t.elipse(50, 46, 2, 1.4), TINTA)
        sorriso(t, 50, 48, 5)

    t.contorno_externo(0.6)
    return t


# ============================================================================ PELUCINHO (normal)

def pelucinho(estagio):
    t = Tela()
    pele = [cor("#E8C49A"), cor("#D6A774"), cor("#A87850")][estagio]
    barriga = [cor("#FFF3E0"), cor("#FBE6C8"), cor("#EFD3AE")][estagio]

    def fofo(cx, cy, r, n=14):
        # borda de pelo: círculo com tufinhos
        return uniao(t.circulo(cx, cy, r * 0.9),
                     *[t.circulo(cx + math.cos(math.tau * i / n) * r * 0.86, cy + math.sin(math.tau * i / n) * r * 0.86, r * 0.2)
                       for i in range(n)])

    if estagio == 0:
        sombra_chao(t, 50, 44)
        for lado in (-1, 1):
            t.pintar(t.circulo(50 + lado * 16, 44, 7), pele, contorno=0.9)
            t.colar(t.circulo(50 + lado * 16, 44, 4), ROSA, 0.8)
        corpo = fofo(50, 66, 25)
        t.pintar(corpo, pele)
        t.colar(dentro(corpo, t.elipse(50, 74, 13, 11)), barriga)
        olhos(t, 50, 62, 9, 4.6)
        bochechas(t, 50, 70, 15, 3.8)
        t.colar(t.elipse(50, 67, 1.8, 1.3), cor("#8A4B3A"))
        sorriso(t, 50, 70, 5)

    elif estagio == 1:
        sombra_chao(t, 50, 52)
        t.pintar(t.tubo([(64, 80), (78, 74), (82, 62)], [5, 5, 6]), pele)
        for lado in (-1, 1):
            t.pintar(t.espinho(50 + lado * 15, 34, -90 + lado * 25, 16, 13), pele, contorno=0.9)
            t.colar(t.espinho(50 + lado * 15, 34.5, -90 + lado * 25, 10, 7), ROSA, 0.8)
        pezinhos(t, 50, 86, 12, 8, 5, escurecer(pele, 0.1))
        corpo = fofo(50, 68, 22)
        t.pintar(corpo, pele)
        t.colar(dentro(corpo, t.elipse(50, 74, 12, 11)), barriga)
        cabeca = fofo(50, 46, 19, 12)
        t.pintar(cabeca, pele)
        for i in range(3):
            t.colar(dentro(cabeca, t.faixa(50, 30, 0, (i - 1) * 4, 1.6, 12)), escurecer(pele, 0.3))
        olhos(t, 50, 46, 8, 4.4)
        bochechas(t, 50, 53, 13, 3.2)
        t.colar(t.elipse(50, 51, 1.8, 1.3), cor("#8A4B3A"))
        sorriso(t, 50, 53, 5)

    else:
        sombra_chao(t, 50, 66)
        for lado in (-1, 1):
            t.pintar(t.circulo(50 + lado * 19, 22, 7), pele, contorno=0.9)
            t.colar(t.circulo(50 + lado * 19, 22, 4), escurecer(pele, 0.3))
        pezinhos(t, 50, 86, 16, 11, 6, escurecer(pele, 0.2))
        corpo = fofo(50, 64, 30, 18)
        t.pintar(corpo, pele)
        t.pintar(dentro(corpo, fofo(50, 68, 17, 10)), barriga, contorno=0, sombra=0.15, luz=0)
        t.pintar(t.tubo([(24, 58), (18, 70)], 6), pele, contorno=0.9, desloc=1)
        t.pintar(t.tubo([(76, 58), (82, 70)], 6), pele, contorno=0.9, desloc=1)
        cabeca = fofo(50, 36, 19, 12)
        t.pintar(cabeca, pele)
        t.colar(dentro(cabeca, t.elipse(50, 43, 9, 6)), barriga)
        olhos(t, 50, 33, 8, 3.8)
        for lado in (-1, 1):
            t.colar(t.linha([(50 + lado * 4, 27), (50 + lado * 12, 26)], 1.2), TINTA)
        t.colar(t.elipse(50, 40, 2.4, 1.6), cor("#5A2E22"))
        sorriso(t, 50, 43, 6)

    t.contorno_externo(0.6)
    return t


# ============================================================================ PIPIO (normal, ave)

def pipio(estagio):
    t = Tela()
    pena = [cor("#FFE066"), cor("#FFC93C"), cor("#E8A33A")][estagio]
    bico = cor("#FF9A3C")
    barriga = clarear(pena, 0.5)

    if estagio == 0:
        sombra_chao(t, 50, 40)
        for lado in (-1, 1):
            t.pintar(t.tubo([(50 + lado * 7, 82), (50 + lado * 7, 88)], 1.4), bico, contorno=0.7)
        corpo = t.elipse(50, 66, 21, 21)
        t.pintar(corpo, pena)
        t.colar(dentro(corpo, t.elipse(50, 74, 12, 10)), barriga)
        # topete
        for ang in (-110, -90, -70):
            t.pintar(t.espinho(50, 46, ang, 9, 4), pena, contorno=0.7)
        for lado in (-1, 1):
            t.pintar(t.elipse(50 + lado * 20, 68, 5, 8, lado * -20), escurecer(pena, 0.15), contorno=0.8)
        olhos(t, 50, 61, 8, 4.4)
        t.pintar(t.poligono([(46, 67), (54, 67), (50, 72)]), bico, contorno=0.7, sombra=0.2, luz=0)
        bochechas(t, 50, 68, 14, 3.2)

    elif estagio == 1:
        sombra_chao(t, 50, 48)
        for lado in (-1, 1):
            t.pintar(t.tubo([(50 + lado * 8, 80), (50 + lado * 8, 88)], 1.6), bico, contorno=0.7)
        # cauda
        for ang in (60, 75, 90):
            t.pintar(t.espinho(62, 78, ang - 40, 16, 6), escurecer(pena, 0.2), contorno=0.8)
        corpo = t.elipse(50, 66, 20, 20)
        t.pintar(corpo, pena)
        t.colar(dentro(corpo, t.elipse(50, 72, 12, 12)), barriga)
        for lado in (-1, 1):
            asa = t.poligono([(50 + lado * 16, 56), (50 + lado * 34, 62), (50 + lado * 30, 70), (50 + lado * 18, 74)])
            t.pintar(asa, escurecer(pena, 0.12), contorno=0.9)
        cabeca = t.circulo(50, 40, 15)
        crista = uniao(*[t.espinho(50 + dx, 27, -90 + dx * 6, 12, 5) for dx in (-4, 0, 4)])
        t.pintar(crista, cor("#FF6B4A"), contorno=0.8)
        t.pintar(cabeca, pena)
        olhos(t, 50, 38, 7, 4)
        t.pintar(t.poligono([(45, 44), (55, 44), (50, 51)]), bico, contorno=0.7, sombra=0.2, luz=0)
        bochechas(t, 50, 45, 12, 2.8)

    else:
        sombra_chao(t, 50, 60)
        for lado in (-1, 1):
            t.pintar(t.tubo([(50 + lado * 9, 78), (50 + lado * 9, 88)], 2.2), bico, contorno=0.8)
        # asas abertas
        for lado in (-1, 1):
            pts = [(50 + lado * 12, 50)]
            for i in range(5):
                pts.append((50 + lado * (26 + i * 5), 22 + i * 9))
                pts.append((50 + lado * (22 + i * 4), 30 + i * 9))
            pts.append((50 + lado * 16, 72))
            t.pintar(t.poligono(pts), escurecer(pena, 0.1), contorno=1.0)
        for ang in (60, 90, 120):
            t.pintar(t.espinho(50, 80, ang, 14, 6), escurecer(pena, 0.25), contorno=0.8)
        corpo = t.elipse(50, 62, 18, 21)
        t.pintar(corpo, pena)
        t.colar(dentro(corpo, t.elipse(50, 68, 11, 13)), barriga)
        cabeca = t.circulo(50, 34, 14)
        crista = uniao(*[t.espinho(50 + dx, 22, -90 + dx * 7, 16, 5) for dx in (-6, -2, 2, 6)])
        t.pintar(crista, cor("#E8452C"), contorno=0.8)
        t.pintar(cabeca, pena)
        olhos(t, 50, 33, 6.5, 3.6, iris=cor("#2A1A08"))
        for lado in (-1, 1):
            t.colar(t.linha([(50 + lado * 3, 27.5), (50 + lado * 11, 26)], 1.2), TINTA)
        t.pintar(t.poligono([(45, 38), (55, 38), (50, 46)]), bico, contorno=0.7, sombra=0.2, luz=0)

    t.contorno_externo(0.6)
    return t


# ============================================================================ ovos

def ovo(manchas):
    t = Tela()
    sombra_chao(t, 50, 40)
    casca = t.elipse(50, 58, 25, 32)
    t.pintar(casca, cor("#FFF6E4"), sombra=0.18)
    c = cor(manchas)
    for x, y, r in ((40, 44, 6), (60, 56, 7.5), (44, 72, 5.5), (58, 36, 4), (33, 60, 3.5)):
        t.colar(dentro(casca, t.circulo(x, y, r)), c)
    t.colar(dentro(casca, t.elipse(40, 44, 4, 8, -20)), BRANCO, 0.5)
    t.contorno_externo(0.6)
    return t
