# -*- coding: utf-8 -*-
"""
Gerador de arte do Aurora Ninja.

Nao ha arte externa no projeto: todo sprite sai deste script. Para acrescentar uma fruta
nova basta somar uma entrada em FRUTAS aqui e outra em Assets/database/frutas.json --
nenhuma linha de C# muda.

Como funciona: cada fruta tem uma SILHUETA (mascara em tons de cinza) e um punhado de
cores. A silhueta inteira vira a fruta fechada; a mesma silhueta erodida (ImageFilter.
MinFilter) vira casca -> entrecasca -> polpa, e cortada ao meio vira a metade fatiada.
Por isso qualquer formato novo ganha metade coerente de graca.

Uso:  python tools/gerar_sprites.py
"""

import math
import os
import random

from PIL import Image, ImageDraw, ImageFilter

SS = 4                     # supersampling: desenha grande, reduz no fim (antialias)
TAM = 128                  # lado do sprite final da fruta, em pixels
RAIZ = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "sprites")

VAZIO = (0, 0, 0, 0)


def hx(c):
    c = c.lstrip("#")
    return tuple(int(c[i:i + 2], 16) for i in (0, 2, 4)) + (255,)


def mistura(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(4))


# ----------------------------------------------------------------- silhuetas

def silhueta(shape, lado, raio):
    """Mascara 'L' (0 = fora, 255 = dentro) do contorno da fruta, em resolucao SS."""
    m = Image.new("L", (lado, lado), 0)
    d = ImageDraw.Draw(m)
    c = lado / 2

    if shape == "redonda":
        d.ellipse([c - raio, c - raio, c + raio, c + raio], fill=255)

    elif shape == "oval":
        d.ellipse([c - raio * 0.84, c - raio, c + raio * 0.84, c + raio], fill=255)

    elif shape == "baga":                       # morango: ombro largo, ponta embaixo
        d.ellipse([c - raio, c - raio, c + raio, c + raio * 0.30], fill=255)
        d.polygon([(c - raio * 0.96, c - raio * 0.10), (c + raio * 0.96, c - raio * 0.10),
                   (c, c + raio)], fill=255)

    elif shape == "crescente":                  # banana
        d.ellipse([c - raio, c - raio * 0.95, c + raio, c + raio * 1.05], fill=255)
        d.ellipse([c - raio * 0.98, c - raio * 1.70, c + raio * 0.98, c + raio * 0.40], fill=0)

    elif shape == "cilindro":                   # abacaxi
        d.rounded_rectangle([c - raio * 0.66, c - raio * 0.70, c + raio * 0.66, c + raio],
                            radius=raio * 0.44, fill=255)

    return m


def erodir(mask, px):
    """Encolhe a mascara em px pixels (ja em resolucao SS). MinFilter so aceita kernel impar."""
    resto = int(px)
    if resto <= 0:
        return mask.copy()

    out = mask
    while resto > 0:
        passo = min(9, resto * 2 + 1)
        if passo % 2 == 0:
            passo += 1
        out = out.filter(ImageFilter.MinFilter(passo))
        resto -= passo // 2
    return out


def pintar(mask, cor):
    img = Image.new("RGBA", mask.size, VAZIO)
    img.paste(Image.new("RGBA", mask.size, cor), (0, 0), mask)
    return img


def dentro(camada, mask):
    """Recorta uma camada solta pela mascara da fruta."""
    return Image.composite(camada, Image.new("RGBA", mask.size, VAZIO), mask)


# ----------------------------------------------------------------- luz e brilho

def sombrear(img, mask, forca=0.55):
    """Gradiente radial: claro no alto a esquerda, escuro na borda oposta. E o que da volume."""
    lado = img.size[0]
    grad = Image.new("L", (lado, lado), 0)
    px = grad.load()
    cx, cy, r = lado * 0.36, lado * 0.32, lado * 0.80
    for y in range(lado):
        for x in range(lado):
            px[x, y] = max(0, min(255, int(255 * (1.0 - math.hypot(x - cx, y - cy) / r))))

    escuro = Image.new("RGBA", (lado, lado), (0, 0, 0, 255))
    escuro.putalpha(Image.eval(grad, lambda v: int((255 - v) * forca)))
    img.alpha_composite(dentro(escuro, mask))
    return img


def brilho_especular(img, mask, raio_rel=0.20):
    lado = img.size[0]
    b = Image.new("RGBA", (lado, lado), VAZIO)
    d = ImageDraw.Draw(b)
    r = lado * raio_rel
    cx, cy = lado * 0.35, lado * 0.30
    d.ellipse([cx - r, cy - r * 0.72, cx + r, cy + r * 0.72], fill=(255, 255, 255, 78))
    b = b.filter(ImageFilter.GaussianBlur(lado * 0.03))
    img.alpha_composite(dentro(b, mask))
    return img


# ----------------------------------------------------------------- decoracao

def sementes(img, mask_polpa, cor, quantidade, tamanho, rng):
    lado = img.size[0]
    d = ImageDraw.Draw(img)
    px = mask_polpa.load()
    postas, tentativas = 0, 0
    while postas < quantidade and tentativas < quantidade * 300:
        tentativas += 1
        x, y = rng.randint(0, lado - 1), rng.randint(0, lado - 1)
        if px[x, y] < 200:
            continue
        d.ellipse([x - tamanho * 0.60, y - tamanho, x + tamanho * 0.60, y + tamanho], fill=cor)
        postas += 1


def listras(img, mask, escuro, raio):
    """Casca listrada da melancia: meridianos, nao raios. Linhas saindo do centro dariam
    bola de praia -- a listra da melancia acompanha a curvatura, de polo a polo."""
    lado = img.size[0]
    faixa = Image.new("RGBA", (lado, lado), VAZIO)
    d = ImageDraw.Draw(faixa)
    c = lado / 2
    grossura = int(lado * 0.045)

    d.line([(c, c - raio), (c, c + raio)], fill=escuro, width=grossura)
    for k in (0.34, 0.66, 0.92):
        d.ellipse([c - raio * k, c - raio, c + raio * k, c + raio],
                  outline=escuro, width=grossura)

    img.alpha_composite(dentro(faixa.filter(ImageFilter.GaussianBlur(lado * 0.005)), mask))


def gomos(img, mask, cor, quantos=9):
    """Linhas radiais separando os gomos da polpa de citrico."""
    lado = img.size[0]
    camada = Image.new("RGBA", (lado, lado), VAZIO)
    d = ImageDraw.Draw(camada)
    c = lado / 2
    for i in range(quantos):
        a = math.tau * i / quantos
        d.line([(c, c), (c + math.cos(a) * lado, c + math.sin(a) * lado)],
               fill=cor, width=int(lado * 0.018))
    img.alpha_composite(dentro(camada, mask))


def grade(img, mask, cor):
    """Xadrez da casca do abacaxi."""
    lado = img.size[0]
    camada = Image.new("RGBA", (lado, lado), VAZIO)
    d = ImageDraw.Draw(camada)
    passo = lado * 0.11
    for i in range(-14, 28):
        d.line([(i * passo, 0), (i * passo + lado, lado)], fill=cor, width=int(lado * 0.012))
        d.line([(i * passo, lado), (i * passo + lado, 0)], fill=cor, width=int(lado * 0.012))
    img.alpha_composite(dentro(camada, mask))


def cabo(img, cor_talo, folha, lado, altura=0.30):
    d = ImageDraw.Draw(img)
    c = lado / 2
    topo = lado * (0.5 - altura)
    d.line([(c, topo + lado * 0.06), (c + lado * 0.02, c - lado * 0.22)],
           fill=cor_talo, width=int(lado * 0.035))
    if folha:
        d.ellipse([c + lado * 0.01, topo, c + lado * 0.21, topo + lado * 0.10], fill=folha)


def coroa(img, cor, lado):
    d = ImageDraw.Draw(img)
    c = lado / 2
    base = lado * 0.22
    for off in (-0.11, -0.04, 0.04, 0.11):
        alt = 0.22 - abs(off) * 0.8
        d.polygon([(c + lado * off - lado * 0.036, base + lado * 0.08),
                   (c + lado * off + lado * 0.036, base + lado * 0.08),
                   (c + lado * off, base - lado * alt)], fill=cor)


# ----------------------------------------------------------------- montagem

def compor(f, lado, metade):
    """Desenha uma fruta -- inteira, ou a metade esquerda dela -- em resolucao SS."""
    raio = lado * 0.5 * f.get("escala", 0.86)
    mask = silhueta(f["shape"], lado, raio)

    m_pith = erodir(mask, lado * f.get("casca", 0.055))
    m_polpa = erodir(m_pith, lado * f.get("entrecasca", 0.030))

    img = Image.new("RGBA", (lado, lado), VAZIO)
    img.alpha_composite(pintar(mask, hx(f["casca_cor"])))

    if not metade:
        if f.get("listras"):
            listras(img, mask, hx(f["casca_escura"]), raio)
        if f.get("grade"):
            grade(img, mask, hx(f["casca_escura"]))
        if f.get("pontinhos"):
            sementes(img, erodir(mask, lado * 0.02), hx(f["casca_escura"]), 90,
                     lado * 0.007, random.Random(f["id"] + "casca"))
        if f.get("sementes_fora"):
            sementes(img, erodir(mask, lado * 0.035), hx(f["semente"]), 26,
                     lado * 0.013, random.Random(f["id"] + "fora"))
    else:
        img.alpha_composite(pintar(m_pith, hx(f["entrecasca_cor"])))
        img.alpha_composite(pintar(m_polpa, hx(f["polpa"])))
        if f.get("gomos"):
            gomos(img, m_polpa, hx(f["entrecasca_cor"]))
        if f.get("miolo"):
            c, r = lado / 2, lado * 0.09
            ImageDraw.Draw(img).ellipse([c - r, c - r, c + r, c + r],
                                        fill=hx(f["entrecasca_cor"]))
        if f.get("semente_dentro"):
            sementes(img, erodir(m_polpa, lado * 0.025), hx(f["semente"]),
                     f.get("qtd_sementes", 14), lado * f.get("tam_semente", 0.017),
                     random.Random(f["id"] + "dentro"))

    sombrear(img, mask, 0.42 if metade else 0.55)
    if not metade:
        brilho_especular(img, mask)

    if f.get("talo"):
        cabo(img, hx(f.get("talo_cor", "#6d4c2f")),
             hx(f["folha"]) if f.get("folha") else None, lado, f.get("talo_alt", 0.30))
    if f.get("coroa"):
        coroa(img, hx(f["coroa"]), lado)

    if metade:
        # Descarta a metade direita e clareia a face do corte, pro talho ficar visivel.
        # O jogo desenha esta mesma imagem espelhada (FlipX) como a outra metade.
        corte = Image.new("L", (lado, lado), 0)
        ImageDraw.Draw(corte).rectangle([0, 0, lado // 2, lado], fill=255)
        img.putalpha(Image.composite(img.split()[3], Image.new("L", (lado, lado), 0), corte))
        ImageDraw.Draw(img).rectangle(
            [lado // 2 - int(lado * 0.014), 0, lado // 2, lado], fill=(255, 255, 255, 46))

    return img


def salvar(img, caminho, tam=TAM):
    os.makedirs(os.path.dirname(caminho), exist_ok=True)
    img.resize((tam, tam), Image.LANCZOS).save(caminho)
    print("   " + os.path.relpath(caminho, RAIZ).replace("\\", "/"))


# ----------------------------------------------------------------- catalogo de arte
# Uma entrada aqui = a arte de uma fruta. O jogo so precisa que o caminho do arquivo bata
# com o que estiver escrito em Assets/database/frutas.json.

FRUTAS = [
    dict(id="melancia", shape="redonda", escala=0.98, casca=0.070, entrecasca=0.030,
         casca_cor="#2f7d32", casca_escura="#1b5e20", entrecasca_cor="#f0f7dd",
         polpa="#e8384f", semente="#241109", listras=True, semente_dentro=True,
         qtd_sementes=16, tam_semente=0.019),

    dict(id="laranja", shape="redonda", escala=0.80, casca=0.055, entrecasca=0.030,
         casca_cor="#f2900c", casca_escura="#c96a00", entrecasca_cor="#ffe6bd",
         polpa="#ffa62b", pontinhos=True, gomos=True, talo=True, talo_cor="#5d7a2a",
         talo_alt=0.32, folha="#4c8b2b"),

    dict(id="maca", shape="oval", escala=0.82, casca=0.060, entrecasca=0.022,
         casca_cor="#d62828", casca_escura="#9a1414", entrecasca_cor="#fbeec2",
         polpa="#fdf6d8", semente="#3b2412", miolo=True, semente_dentro=True,
         qtd_sementes=5, tam_semente=0.022, talo=True, folha="#4c8b2b"),

    dict(id="kiwi", shape="redonda", escala=0.72, casca=0.055, entrecasca=0.028,
         casca_cor="#7a5230", casca_escura="#523a22", entrecasca_cor="#f3f8dc",
         polpa="#8cc63f", semente="#22301a", pontinhos=True, miolo=True,
         semente_dentro=True, qtd_sementes=22, tam_semente=0.012),

    dict(id="morango", shape="baga", escala=0.80, casca=0.050, entrecasca=0.020,
         casca_cor="#e02222", casca_escura="#a01212", entrecasca_cor="#ffd7d7",
         polpa="#ff9d9d", semente="#f6e07a", sementes_fora=True, coroa="#3f8f2f"),

    dict(id="abacaxi", shape="cilindro", escala=0.94, casca=0.060, entrecasca=0.026,
         casca_cor="#d99b1c", casca_escura="#8f5f0d", entrecasca_cor="#ffe9a8",
         polpa="#ffd24a", grade=True, miolo=True, coroa="#3f8f2f"),

    dict(id="banana", shape="crescente", escala=0.98, casca=0.050, entrecasca=0.022,
         casca_cor="#f2c31d", casca_escura="#b28a08", entrecasca_cor="#fff3c4",
         polpa="#fff6d5"),
]

# Bananas especiais, como no Fruit Ninja: mesmo formato, cor diferente. Qual efeito cada
# uma dispara e assunto do frutas.json -- aqui e so a pintura.
PODERES = [
    dict(id="banana_congelar", base="banana", casca_cor="#4fc3f7", casca_escura="#1976d2",
         entrecasca_cor="#d6f2ff", polpa="#eaf9ff"),
    dict(id="banana_frenesi", base="banana", casca_cor="#ef5350", casca_escura="#b71c1c",
         entrecasca_cor="#ffd9d6", polpa="#fff0ee"),
    dict(id="banana_dobro", base="banana", casca_cor="#ffd54f", casca_escura="#c79100",
         entrecasca_cor="#fff4cc", polpa="#fffbe8"),
]


def gerar_frutas():
    lado = TAM * SS
    print("frutas:")
    tabela = {f["id"]: f for f in FRUTAS}
    todas = list(FRUTAS)
    for p in PODERES:
        base = dict(tabela[p["base"]])
        base.update({k: v for k, v in p.items() if k != "base"})
        todas.append(base)

    for f in todas:
        salvar(compor(f, lado, metade=False), os.path.join(RAIZ, "frutas", f"{f['id']}.png"))
        salvar(compor(f, lado, metade=True), os.path.join(RAIZ, "frutas", f"{f['id']}_metade.png"))


# ----------------------------------------------------------------- bomba

def gerar_bomba():
    lado = TAM * SS
    img = Image.new("RGBA", (lado, lado), VAZIO)
    c = lado / 2
    r = lado * 0.34

    caixa = [c - r, c - r * 0.96, c + r, c + r * 1.04]
    mask = Image.new("L", (lado, lado), 0)
    ImageDraw.Draw(mask).ellipse(caixa, fill=255)
    img.alpha_composite(pintar(mask, hx("#1d1f25")))
    sombrear(img, mask, 0.60)
    brilho_especular(img, mask, 0.24)

    d = ImageDraw.Draw(img)
    d.rounded_rectangle([c - lado * 0.09, c - r - lado * 0.09, c + lado * 0.09, c - r + lado * 0.05],
                        radius=lado * 0.02, fill=hx("#3a3f48"))

    pavio = [(c + lado * 0.02, c - r - lado * 0.06), (c + lado * 0.13, c - r - lado * 0.15),
             (c + lado * 0.09, c - r - lado * 0.24), (c + lado * 0.18, c - r - lado * 0.30)]
    d.line(pavio, fill=hx("#8a6a3a"), width=int(lado * 0.026), joint="curve")

    faisca = Image.new("RGBA", (lado, lado), VAZIO)
    fd = ImageDraw.Draw(faisca)
    fx, fy = pavio[-1]
    for rr, cor in ((lado * 0.075, (255, 170, 40, 210)), (lado * 0.040, (255, 240, 160, 255))):
        fd.ellipse([fx - rr, fy - rr, fx + rr, fy + rr], fill=cor)
    img.alpha_composite(faisca.filter(ImageFilter.GaussianBlur(lado * 0.012)))

    print("bomba:")
    salvar(img, os.path.join(RAIZ, "bomba.png"))


# ----------------------------------------------------------------- cenario e HUD

def gerar_fundo(largura=720, altura=1280):
    img = Image.new("RGBA", (largura, altura))
    px = img.load()
    topo, base = hx("#241a30"), hx("#0b0910")
    for y in range(altura):
        cor = mistura(topo, base, (y / altura) ** 0.85)
        for x in range(largura):
            px[x, y] = cor

    halo = Image.new("RGBA", (largura, altura), VAZIO)
    ImageDraw.Draw(halo).ellipse([-largura * 0.35, altura * 0.10, largura * 1.35, altura * 0.95],
                                 fill=(90, 60, 140, 60))
    img.alpha_composite(halo.filter(ImageFilter.GaussianBlur(120)))

    vinheta = Image.new("L", (largura, altura), 0)
    ImageDraw.Draw(vinheta).ellipse([-largura * 0.25, -altura * 0.10, largura * 1.25, altura * 1.10],
                                    fill=255)
    vinheta = vinheta.filter(ImageFilter.GaussianBlur(150))
    escuro = Image.new("RGBA", (largura, altura), (0, 0, 0, 255))
    escuro.putalpha(Image.eval(vinheta, lambda v: 190 - int(v * 0.74)))
    img.alpha_composite(escuro)

    print("cenario:")
    img.save(os.path.join(RAIZ, "fundo.png"))
    print("   fundo.png")


def gerar_hud():
    lado = 64 * SS
    for nome, cor, alpha in (("marca", "#e23b3b", 255), ("marca_vazia", "#6a5f7a", 120)):
        img = Image.new("RGBA", (lado, lado), VAZIO)
        d = ImageDraw.Draw(img)
        c, r, w = lado / 2, lado * 0.30, int(lado * 0.10)
        d.ellipse([c - r * 1.30, c - r * 1.30, c + r * 1.30, c + r * 1.30], fill=(255, 255, 255, 18))
        rgba = hx(cor)[:3] + (alpha,)
        d.line([(c - r, c - r), (c + r, c + r)], fill=rgba, width=w)
        d.line([(c - r, c + r), (c + r, c - r)], fill=rgba, width=w)
        salvar(img, os.path.join(RAIZ, f"{nome}.png"), tam=64)

    lado = 32 * SS
    p = Image.new("RGBA", (lado, lado), VAZIO)
    ImageDraw.Draw(p).ellipse([0, 0, lado - 1, lado - 1], fill=(255, 255, 255, 255))
    salvar(p.filter(ImageFilter.GaussianBlur(lado * 0.06)),
           os.path.join(RAIZ, "particula.png"), tam=32)


if __name__ == "__main__":
    os.makedirs(RAIZ, exist_ok=True)
    gerar_frutas()
    gerar_bomba()
    print("HUD:")
    gerar_hud()
    gerar_fundo()
    print("pronto.")
