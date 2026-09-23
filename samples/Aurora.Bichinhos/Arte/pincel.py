"""
Pincel: o mínimo de "ilustrador vetorial" pra gerar os sprites do Beast Arena em código.

Tudo é desenhado em coordenadas de 0 a 100 (a tela inteira do sprite), em supersampling, e
reduzido no fim — o que dá borda suave sem antialias manual. Cada parte vira uma máscara; pintar
uma parte põe contorno escuro, cor chapada, faixa de sombra embaixo/direita e brilho em cima/esquerda
(luz vem de cima à esquerda em todos os sprites, pra conversarem entre si).
"""

import math

import numpy as np
from PIL import Image, ImageChops, ImageDraw, ImageFilter

SUPER = 3
TINTA = (28, 22, 26)


# ---------------------------------------------------------------------------------- cores

def cor(hexa):
    hexa = hexa.lstrip("#")
    return tuple(int(hexa[i:i + 2], 16) for i in (0, 2, 4))


def misturar(a, b, t):
    return tuple(round(x + (y - x) * t) for x, y in zip(a, b))


def escurecer(c, t=0.3):
    # Sombra puxa pro roxo frio em vez de pro preto: fica menos "sujo".
    return misturar(c, (34, 24, 48), t)


def clarear(c, t=0.3):
    return misturar(c, (255, 250, 232), t)


# ---------------------------------------------------------------------------------- tela

class Tela:
    def __init__(self, lado=256):
        self.lado = lado
        self.n = lado * SUPER
        self.k = self.n / 100.0
        self.img = Image.new("RGBA", (self.n, self.n), (0, 0, 0, 0))

    # ------------------------------------------------------------------ máscaras

    def vazia(self):
        return Image.new("L", (self.n, self.n), 0)

    def _p(self, x, y):
        return (x * self.k, y * self.k)

    def elipse(self, cx, cy, rx, ry, angulo=0.0):
        m = self.vazia()
        a = math.radians(angulo)
        pontos = []
        for i in range(96):
            t = math.tau * i / 96
            x, y = rx * math.cos(t), ry * math.sin(t)
            pontos.append(self._p(cx + x * math.cos(a) - y * math.sin(a), cy + x * math.sin(a) + y * math.cos(a)))
        ImageDraw.Draw(m).polygon(pontos, fill=255)
        return m

    def circulo(self, cx, cy, r):
        return self.elipse(cx, cy, r, r)

    def poligono(self, pontos):
        m = self.vazia()
        ImageDraw.Draw(m).polygon([self._p(x, y) for x, y in pontos], fill=255)
        return m

    def retangulo(self, x0, y0, x1, y1):
        return self.poligono([(x0, y0), (x1, y0), (x1, y1), (x0, y1)])

    def tubo(self, pontos, raios, passo=0.35):
        """Traço grosso por uma spline Catmull-Rom; raio interpolado de ponta a ponta.
        `raios` pode ser um número ou uma lista (um por ponto de controle)."""
        if not isinstance(raios, (list, tuple)):
            raios = [raios] * len(pontos)

        curva = _catmull(pontos, raios)
        m = self.vazia()
        d = ImageDraw.Draw(m)
        anterior = None
        for x, y, r in curva:
            if anterior and math.dist(anterior, (x, y)) < passo:
                continue
            anterior = (x, y)
            px, py = self._p(x, y)
            rr = r * self.k
            d.ellipse((px - rr, py - rr, px + rr, py + rr), fill=255)
        return m

    def linha(self, pontos, largura):
        m = self.vazia()
        ImageDraw.Draw(m).line([self._p(x, y) for x, y in pontos], fill=255, width=max(1, round(largura * self.k)), joint="curve")
        return m

    def espinho(self, bx, by, angulo, comprimento, largura):
        """Triângulo com base centrada em (bx,by), apontando pro ângulo (graus, 0 = direita)."""
        a = math.radians(angulo)
        dx, dy = math.cos(a), math.sin(a)
        nx, ny = -dy, dx
        return self.poligono([
            (bx + nx * largura / 2, by + ny * largura / 2),
            (bx + dx * comprimento, by + dy * comprimento),
            (bx - nx * largura / 2, by - ny * largura / 2),
        ])

    # ------------------------------------------------------------------ pintura

    def colar(self, m, c, opacidade=1.0):
        if opacidade < 1.0:
            m = m.point(lambda v: round(v * opacidade))
        camada = Image.new("RGBA", (self.n, self.n), c + (255,))
        camada.putalpha(m)
        self.img = Image.alpha_composite(self.img, camada)

    def faixa(self, cx, cy, angulo, desloc, largura, comprimento=60):
        """Retângulo atravessado no eixo `angulo` a `desloc` do centro — listra de vespa,
        placa de armadura. Use com `dentro(corpo, faixa)`."""
        a = math.radians(angulo)
        dx, dy = math.cos(a), math.sin(a)
        nx, ny = -dy, dx
        mx, my = cx + dx * desloc, cy + dy * desloc
        h, c = largura / 2, comprimento / 2
        return self.poligono([
            (mx - dx * h - nx * c, my - dy * h - ny * c),
            (mx + dx * h - nx * c, my + dy * h - ny * c),
            (mx + dx * h + nx * c, my + dy * h + ny * c),
            (mx - dx * h + nx * c, my - dy * h + ny * c),
        ])

    def pintar(self, m, c, contorno=1.1, sombra=0.32, luz=0.22, desloc=None, cor_sombra=None, opacidade=1.0):
        """Parte com contorno (em % da tela), cor chapada, sombra e brilho."""
        if opacidade < 1.0:
            # Translúcido (asa): só o aro do contorno, senão o miolo escurece duas vezes.
            if contorno:
                self.colar(menos(dilatar(m, contorno * self.k), m), TINTA, opacidade)
            self.colar(m, c, opacidade)
            return

        if contorno:
            self.colar(dilatar(m, contorno * self.k), TINTA)
        self.colar(m, c)

        caixa = m.getbbox()
        if not caixa:
            return
        tamanho = min(caixa[2] - caixa[0], caixa[3] - caixa[1])
        d = desloc * self.k if desloc is not None else tamanho * 0.16

        if sombra:
            faixa = ImageChops.subtract(m, deslocar(m, -d, -d))
            self.colar(faixa, cor_sombra or escurecer(c, sombra))
        if luz:
            faixa = ImageChops.subtract(m, deslocar(m, d * 0.55, d * 0.55))
            faixa = ImageChops.multiply(faixa, erodir(m, d * 0.25))
            self.colar(faixa, clarear(c, luz), 0.8)

    def contorno_externo(self, grossura=0.8):
        """Aro escuro em volta da silhueta toda: separa o bicho do chão escuro da arena."""
        # Limiar antes de crescer: brilho semitransparente não pode ganhar aro (vira mancha escura).
        alfa = self.img.getchannel("A").point(lambda v: 255 if v > 150 else 0)
        aro = dilatar(alfa, grossura * self.k)
        base = Image.new("RGBA", (self.n, self.n), TINTA + (255,))
        base.putalpha(aro)
        self.img = Image.alpha_composite(base, self.img)

    def brilho(self, cx, cy, r, c, forca=0.6):
        """Halo suave (olho que brilha, cristal); some por completo até a distância `r` —
        passar disso cortaria o degradê num quadrado na borda da tela."""
        m = self.circulo(cx, cy, r * 0.5).filter(ImageFilter.GaussianBlur(r * self.k * 0.22))
        self.colar(m, c, forca)

    # ------------------------------------------------------------------ saída

    def salvar(self, caminho):
        final = self.img.resize((self.lado, self.lado), Image.LANCZOS)
        final = sangrar_cor(final)
        final.save(caminho, optimize=True)
        return final


# ---------------------------------------------------------------------------------- operações de máscara

def uniao(*ms):
    r = ms[0]
    for m in ms[1:]:
        r = ImageChops.lighter(r, m)
    return r


def menos(a, b):
    return ImageChops.subtract(a, b)


def dentro(a, b):
    return ImageChops.multiply(a, b)


def deslocar(m, dx, dy):
    # ImageChops.offset dá a volta na borda; aqui o que sai some.
    saida = Image.new("L", m.size, 0)
    saida.paste(m, (round(dx), round(dy)))
    return saida


def dilatar(m, raio):
    if raio <= 0:
        return m
    borrada = m.filter(ImageFilter.GaussianBlur(raio / 2.0))
    # Limiar baixo no borrão = cresce ~2 sigmas; o ganho deixa ~1 px de degradê na borda.
    return borrada.point(lambda v: max(0, min(255, (v - 6) * 24)))


def erodir(m, raio):
    if raio <= 0:
        return m
    borrada = m.filter(ImageFilter.GaussianBlur(raio / 2.0))
    return borrada.point(lambda v: max(0, min(255, (v - 249) * 40)))


def _catmull(pontos, raios, amostras=40):
    if len(pontos) == 1:
        return [(pontos[0][0], pontos[0][1], raios[0])]

    p = [pontos[0]] + list(pontos) + [pontos[-1]]
    r = [raios[0]] + list(raios) + [raios[-1]]
    saida = []
    for i in range(1, len(p) - 2):
        for s in range(amostras + 1):
            t = s / amostras
            t2, t3 = t * t, t * t * t
            x = 0.5 * ((2 * p[i][0]) + (-p[i - 1][0] + p[i + 1][0]) * t
                       + (2 * p[i - 1][0] - 5 * p[i][0] + 4 * p[i + 1][0] - p[i + 2][0]) * t2
                       + (-p[i - 1][0] + 3 * p[i][0] - 3 * p[i + 1][0] + p[i + 2][0]) * t3)
            y = 0.5 * ((2 * p[i][1]) + (-p[i - 1][1] + p[i + 1][1]) * t
                       + (2 * p[i - 1][1] - 5 * p[i][1] + 4 * p[i + 1][1] - p[i + 2][1]) * t2
                       + (-p[i - 1][1] + 3 * p[i][1] - 3 * p[i + 1][1] + p[i + 2][1]) * t3)
            saida.append((x, y, r[i] + (r[i + 1] - r[i]) * t))
    return saida


def sangrar_cor(img):
    """Pinta o RGB dos pixels transparentes com a cor dos vizinhos opacos.

    A textura é amostrada com filtro linear e mipmap no jogo; sem isto, o transparente (preto)
    vaza pra dentro da borda e todo sprite ganha um halo escuro quando encolhe."""
    a = np.asarray(img).astype(np.float32)
    rgb, alfa = a[..., :3], a[..., 3:] / 255.0

    soma = rgb * alfa
    peso = alfa.copy()
    for _ in range(6):
        soma = _borrar(soma)
        peso = _borrar(peso)

    preenchido = soma / np.maximum(peso, 1e-6)
    vazio = (a[..., 3:] == 0)
    a[..., :3] = np.where(vazio, preenchido, rgb)
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGBA")


def _borrar(x):
    pad = np.pad(x, ((2, 2), (2, 2), (0, 0)), mode="edge")
    acc = np.zeros_like(x)
    for dy in range(5):
        for dx in range(5):
            acc += pad[dy:dy + x.shape[0], dx:dx + x.shape[1]]
    return acc / 25.0
