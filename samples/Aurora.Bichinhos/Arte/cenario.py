"""
Fundos (720x1280, a tela inteira em retrato) e ícones dos botões.

Os fundos são pintados direto em pixel, sem o Pincel: são grandes, planos e não precisam de
contorno. A noite NÃO tem fundo próprio — o jogo escurece a casa com um véu azul por cima, então
a luz apagada funciona em qualquer cômodo que vier depois.
"""

import math
import random

from PIL import Image, ImageDraw, ImageFilter

from pincel import TINTA, Tela, clarear, cor, dentro, escurecer, menos, uniao

L, A = 720, 1280


def _degrade(img, y0, y1, c0, c1):
    d = ImageDraw.Draw(img)
    for y in range(y0, y1):
        t = (y - y0) / max(1, y1 - y0 - 1)
        d.line([(0, y), (L, y)], fill=tuple(round(a + (b - a) * t) for a, b in zip(c0, c1)))


# ============================================================================ fundos

def casa():
    img = Image.new("RGB", (L, A))
    _degrade(img, 0, 760, cor("#FBE9D0"), cor("#F4D3B0"))
    d = ImageDraw.Draw(img)

    # papel de parede de bolinhas
    for y in range(40, 740, 70):
        for x in range((y // 70 % 2) * 35 + 20, L, 70):
            d.ellipse((x - 5, y - 5, x + 5, y + 5), fill=cor("#F2C9A0"))

    # rodapé
    d.rectangle((0, 740, L, 772), fill=cor("#C98A5B"))
    d.rectangle((0, 740, L, 748), fill=cor("#E0A677"))

    # janela com céu e nuvem
    jx0, jy0, jx1, jy1 = 430, 250, 650, 470
    d.rounded_rectangle((jx0 - 14, jy0 - 14, jx1 + 14, jy1 + 14), 18, fill=cor("#9C6B45"))
    ceu = Image.new("RGB", (jx1 - jx0, jy1 - jy0))
    _degrade_local = ImageDraw.Draw(ceu)
    for y in range(jy1 - jy0):
        t = y / (jy1 - jy0)
        _degrade_local.line([(0, y), (jx1 - jx0, y)], fill=tuple(round(a + (b - a) * t) for a, b in zip(cor("#7EC8F5"), cor("#CDEBFB"))))
    _degrade_local.ellipse((30, 60, 110, 110), fill=(255, 255, 255))
    _degrade_local.ellipse((70, 40, 150, 105), fill=(255, 255, 255))
    _degrade_local.ellipse((110, 65, 180, 112), fill=(255, 255, 255))
    _degrade_local.ellipse((150, 150, 200, 200), fill=cor("#FFE27A"))
    img.paste(ceu, (jx0, jy0))
    d.rectangle(((jx0 + jx1) // 2 - 6, jy0, (jx0 + jx1) // 2 + 6, jy1), fill=cor("#9C6B45"))
    d.rectangle((jx0, (jy0 + jy1) // 2 - 6, jx1, (jy0 + jy1) // 2 + 6), fill=cor("#9C6B45"))
    d.rounded_rectangle((jx0 - 26, jy1 + 10, jx1 + 26, jy1 + 30), 8, fill=cor("#B57C50"))

    # quadrinho de coração
    d.rounded_rectangle((90, 290, 250, 430), 10, fill=cor("#B57C50"))
    d.rectangle((104, 304, 236, 416), fill=cor("#FFF6E4"))
    d.ellipse((140, 330, 172, 362), fill=cor("#FF8FA3"))
    d.ellipse((164, 330, 196, 362), fill=cor("#FF8FA3"))
    d.polygon([(142, 352), (194, 352), (168, 392)], fill=cor("#FF8FA3"))

    # piso de tábuas
    _degrade(img, 772, A, cor("#D9A06C"), cor("#B97E4E"))
    for y in range(772, A, 64):
        d.line([(0, y), (L, y)], fill=cor("#A86E43"), width=3)
        desloc = (y // 64 % 2) * 120
        for x in range(desloc, L + 240, 240):
            d.line([(x, y), (x, y + 64)], fill=cor("#A86E43"), width=3)

    # tapete sob o bichinho
    tapete = Image.new("L", (L, A), 0)
    ImageDraw.Draw(tapete).ellipse((150, 800, 570, 900), fill=255)
    cam = Image.new("RGB", (L, A), cor("#7FB3E0"))
    img.paste(cam, (0, 0), tapete)
    anel = Image.new("L", (L, A), 0)
    ImageDraw.Draw(anel).ellipse((180, 812, 540, 888), outline=255, width=6)
    img.paste(Image.new("RGB", (L, A), cor("#A9CDEE")), (0, 0), anel)
    return img


def batalha():
    img = Image.new("RGB", (L, A))
    _degrade(img, 0, 560, cor("#8FD3FF"), cor("#DDF3FF"))
    d = ImageDraw.Draw(img)
    rnd = random.Random(7)

    # nuvens
    for cx, cy, s in ((140, 120, 1.0), (520, 200, 1.3), (330, 60, 0.7)):
        for dx, dy, r in ((-40, 10, 32), (0, 0, 44), (44, 12, 30)):
            d.ellipse((cx + (dx - r) * s, cy + (dy - r) * s, cx + (dx + r) * s, cy + (dy + r) * s), fill=(255, 255, 255))

    # morros ao longe
    for base, c, amp, fase in ((520, cor("#A7D9A0"), 50, 0.0), (560, cor("#86C47D"), 36, 1.7)):
        pts = [(0, A)]
        for x in range(0, L + 20, 20):
            pts.append((x, base - amp * (0.5 + 0.5 * math.sin(x / 110 + fase))))
        pts.append((L, A))
        d.polygon(pts, fill=c)

    _degrade(img, 580, A, cor("#79C06A"), cor("#5AA54E"))
    d = ImageDraw.Draw(img)
    for _ in range(260):
        x, y = rnd.randint(0, L), rnd.randint(590, A)
        h = rnd.randint(6, 14)
        d.line([(x, y), (x + rnd.randint(-3, 3), y - h)], fill=cor("#4E9444"), width=2)

    # plataformas: inimigo em cima à direita, o seu embaixo à esquerda
    for cx, cy, rx, ry in ((500, 470, 170, 42), (230, 850, 200, 50)):
        d.ellipse((cx - rx, cy - ry + 8, cx + rx, cy + ry + 8), fill=cor("#4A8A3F"))
        d.ellipse((cx - rx, cy - ry, cx + rx, cy + ry), fill=cor("#C9E7A8"))
        d.ellipse((cx - rx + 24, cy - ry + 10, cx + rx - 24, cy + ry - 8), fill=cor("#B5DB8E"))
    return img


def escolha():
    img = Image.new("RGB", (L, A))
    _degrade(img, 0, A, cor("#2B2350"), cor("#5A3E7A"))
    d = ImageDraw.Draw(img)
    rnd = random.Random(3)
    for _ in range(90):
        x, y = rnd.randint(0, L), rnd.randint(0, A)
        r = rnd.choice((1, 1, 2, 2, 3))
        d.ellipse((x - r, y - r, x + r, y + r), fill=(255, 250, 220))
    return img.filter(ImageFilter.GaussianBlur(0.6))


# ============================================================================ ícones

def _icone():
    return Tela(128)


def maca():
    t = _icone()
    t.pintar(t.tubo([(50, 30), (54, 16)], 2.2), cor("#6B4226"), contorno=0.8)
    from bichos import folha
    folha(t, 54, 22, 18, 10, -20)
    fruta = uniao(t.circulo(38, 58, 24), t.circulo(62, 58, 24), t.elipse(50, 70, 28, 20))
    t.pintar(fruta, cor("#EF4B4B"))
    t.colar(dentro(fruta, t.elipse(34, 48, 6, 10, -30)), (255, 255, 255), 0.6)
    t.contorno_externo(0.8)
    return t


def doce():
    t = _icone()
    # bolinho com cobertura e cereja
    base = t.poligono([(22, 58), (78, 58), (70, 88), (30, 88)])
    t.pintar(base, cor("#E7A35B"))
    for x in range(30, 74, 9):
        t.colar(dentro(base, t.faixa(x, 70, 0, 0, 2, 40)), cor("#C98640"))
    cobertura = uniao(t.elipse(50, 54, 30, 14), *[t.circulo(x, 60, 6) for x in (28, 40, 52, 64, 74)], t.elipse(50, 42, 20, 14))
    t.pintar(cobertura, cor("#FFB3C7"))
    t.pintar(t.circulo(50, 24, 8), cor("#E0344A"))
    t.contorno_externo(0.8)
    return t


def bola():
    t = _icone()
    m = t.circulo(50, 52, 34)
    t.pintar(m, cor("#FFFFFF"), sombra=0.2)
    t.colar(dentro(m, t.faixa(50, 52, 0, 0, 14, 100)), cor("#4AA8E8"))
    t.colar(dentro(m, t.faixa(50, 52, 90, 0, 14, 100)), cor("#FF6B4A"))
    t.colar(dentro(m, t.circulo(38, 38, 8)), (255, 255, 255), 0.7)
    t.contorno_externo(0.8)
    return t


def bolhas():
    t = _icone()
    for x, y, r in ((40, 60, 24), (68, 40, 16), (70, 72, 12), (30, 30, 9)):
        m = t.circulo(x, y, r)
        t.pintar(m, cor("#B8E6FF"), sombra=0.15, luz=0.3)
        t.colar(dentro(m, t.circulo(x - r * 0.35, y - r * 0.35, r * 0.28)), (255, 255, 255), 0.9)
    t.contorno_externo(0.8)
    return t


def pilula():
    t = _icone()
    a = t.tubo([(28, 72), (50, 50)], 16)
    b = t.tubo([(50, 50), (72, 28)], 16)
    t.pintar(a, cor("#FFFFFF"), sombra=0.18)
    t.pintar(b, cor("#EF4B4B"))
    t.colar(t.tubo([(58, 30), (66, 24)], 3), (255, 255, 255), 0.7)
    t.contorno_externo(0.8)
    return t


def lampada():
    t = _icone()
    t.brilho(50, 44, 40, cor("#FFE27A"), 0.5)
    t.pintar(t.retangulo(38, 64, 62, 84), cor("#9AA6B2"))
    for y in (70, 77):
        t.colar(t.retangulo(38, y, 62, y + 2), escurecer(cor("#9AA6B2"), 0.3))
    t.pintar(uniao(t.circulo(50, 40, 26), t.poligono([(36, 56), (64, 56), (60, 66), (40, 66)])), cor("#FFE27A"))
    t.colar(t.tubo([(44, 50), (50, 40), (56, 50)], 1.4), cor("#E8A33A"))
    t.contorno_externo(0.8)
    return t


def espadas():
    t = _icone()
    for lado in (-1, 1):
        a = t.tubo([(50 - lado * 30, 16), (50 + lado * 16, 70)], [3.5, 5])
        t.pintar(a, cor("#DCE4EC"), sombra=0.25)
        t.pintar(t.tubo([(50 + lado * 6, 62), (50 + lado * 26, 78)], 3.2), cor("#C98A2B"), contorno=0.8)
        t.pintar(t.tubo([(50 + lado * 18, 76), (50 + lado * 28, 88)], 3.6), cor("#6B4226"), contorno=0.8)
    t.contorno_externo(0.8)
    return t


def moeda():
    t = _icone()
    m = t.circulo(50, 50, 38)
    t.pintar(m, cor("#F4C542"))
    t.pintar(t.circulo(50, 50, 26), cor("#FFD95A"), contorno=0.6, sombra=0.2, luz=0.1)
    t.colar(t.tubo([(44, 34), (44, 66)], 3.2), cor("#C99A1E"))
    t.colar(t.tubo([(44, 36), (58, 42), (44, 50), (58, 58), (44, 64)], 3.2), cor("#C99A1E"))
    t.contorno_externo(0.8)
    return t


def coracao():
    t = _icone()
    m = uniao(t.circulo(36, 40, 20), t.circulo(64, 40, 20), t.poligono([(18, 48), (82, 48), (50, 86)]))
    t.pintar(m, cor("#FF5C7A"))
    t.colar(dentro(m, t.elipse(32, 34, 6, 9, -30)), (255, 255, 255), 0.7)
    t.contorno_externo(0.8)
    return t


def coco():
    t = _icone()
    c = cor("#8A5A3B")
    t.pintar(t.elipse(50, 78, 34, 12), c)
    t.pintar(t.elipse(50, 62, 26, 11), c)
    t.pintar(t.elipse(50, 46, 17, 10), c)
    t.pintar(t.espinho(52, 38, -80, 16, 12), c, contorno=0.9)
    from bichos import olhos
    olhos(t, 50, 62, 9, 3.2)
    t.contorno_externo(0.8)
    return t


def raio():
    t = _icone()
    m = t.poligono([(58, 8), (24, 56), (46, 56), (38, 92), (76, 40), (54, 40), (66, 8)])
    t.pintar(m, cor("#FFD54F"))
    t.contorno_externo(0.8)
    return t


def cruz():
    t = _icone()
    m = uniao(t.retangulo(38, 14, 62, 86), t.retangulo(14, 38, 86, 62))
    t.pintar(m, cor("#4FC3A1"))
    t.contorno_externo(0.8)
    return t


def estrela():
    t = _icone()
    pts = []
    for i in range(10):
        r = 42 if i % 2 == 0 else 18
        a = -math.pi / 2 + i * math.pi / 5
        pts.append((50 + math.cos(a) * r, 54 + math.sin(a) * r))
    m = t.poligono(pts)
    t.pintar(m, cor("#FFD54F"))
    t.contorno_externo(0.8)
    return t


def gota_suor():
    t = _icone()
    m = uniao(t.circulo(50, 62, 22), t.poligono([(50, 12), (70, 54), (30, 54)]))
    t.pintar(m, cor("#7FD0FF"))
    t.colar(dentro(m, t.elipse(42, 56, 5, 9, -20)), (255, 255, 255), 0.7)
    t.contorno_externo(0.8)
    return t


def nota():
    t = _icone()
    c = cor("#7B61FF")
    t.pintar(uniao(t.elipse(32, 74, 13, 10, -20), t.elipse(70, 64, 13, 10, -20),
                   t.retangulo(40, 20, 46, 74), t.retangulo(78, 12, 84, 64),
                   t.poligono([(40, 20), (84, 10), (84, 22), (40, 32)])), c)
    t.contorno_externo(0.8)
    return t


# ============================================================================ texturas de interface

def painel():
    """Retângulo arredondado branco, pra fatiar em 9 no jogo (canto de 24 px)."""
    img = Image.new("RGBA", (96 * 4, 96 * 4), (255, 255, 255, 0))
    ImageDraw.Draw(img).rounded_rectangle((0, 0, 96 * 4 - 1, 96 * 4 - 1), 24 * 4, fill=(255, 255, 255, 255))
    return img.resize((96, 96), Image.LANCZOS)


def circulo_branco():
    img = Image.new("RGBA", (256, 256), (255, 255, 255, 0))
    ImageDraw.Draw(img).ellipse((2, 2, 253, 253), fill=(255, 255, 255, 255))
    return img.resize((128, 128), Image.LANCZOS)
