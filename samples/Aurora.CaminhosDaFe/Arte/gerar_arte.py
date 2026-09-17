"""
Gera os PNGs de Assets/sprites/ do Caminhos da Fé — pixel art desenhada em texto.

    python samples/Aurora.CaminhosDaFe/Arte/gerar_arte.py

Precisa só de Pillow. Cada sprite é uma grade de caracteres; cada caractere é uma cor da PALETA
("." = transparente). Pra retocar um boneco, edite a grade e rode de novo. Também grava
Arte/previa.png (tudo ampliado 4x) pra conferir de olho sem abrir o jogo.

A engine desenha textura com filtro Nearest e a câmera da cena usa Zoom 3, então 1 pixel daqui
vira um quadrado de 3x3 na tela — por isso tudo é pequeno (tile e personagem de 16px).
"""

import os
import random

from PIL import Image

AQUI = os.path.dirname(os.path.abspath(__file__))
SAIDA = os.path.join(AQUI, "..", "Assets", "sprites")

PALETA = {
    ".": None,
    "k": (43, 29, 20),      # contorno
    "s": (226, 170, 122),   # pele
    "S": (184, 124, 82),    # pele na sombra
    "e": (26, 20, 20),      # olho
    "h": (122, 62, 30),     # cabelo ruivo do Davi
    "H": (88, 44, 22),      # cabelo na sombra
    "t": (238, 232, 212),   # túnica de linho
    "T": (184, 172, 146),   # túnica na sombra
    "b": (104, 66, 36),     # cinto / sandália
    "w": (244, 240, 228),   # lã
    "W": (204, 196, 176),   # lã na sombra
    "g": (126, 126, 132),   # pelo do lobo
    "G": (80, 80, 90),      # pelo escuro
    "l": (186, 182, 176),   # barriga do lobo
    "y": (240, 200, 60),    # olho do lobo
    "r": (120, 80, 48),     # manto do Jessé
    "R": (86, 56, 34),      # manto na sombra
    "c": (216, 206, 176),   # pano da cabeça
    "C": (170, 160, 132),   # pano na sombra
    "x": (214, 214, 206),   # barba grisalha
    "m": (150, 104, 60),    # cajado
}


def desenhar(linhas):
    """Grade de caracteres -> Image RGBA. Falha alto se as linhas tiverem larguras diferentes:
    uma linha torta desloca o resto do desenho sem erro nenhum, e é chato de achar no olho."""
    largura = len(linhas[0])
    for i, linha in enumerate(linhas):
        if len(linha) != largura:
            raise ValueError(f"linha {i} tem {len(linha)} colunas, esperado {largura}: {linha!r}")

    img = Image.new("RGBA", (largura, len(linhas)), (0, 0, 0, 0))
    for y, linha in enumerate(linhas):
        for x, ch in enumerate(linha):
            cor = PALETA[ch]
            if cor is not None:
                img.putpixel((x, y), cor + (255,))
    return img


def folha(quadros, colunas):
    """Monta frames do mesmo tamanho numa grade — o formato que o Animator recorta."""
    w, h = quadros[0].size
    linhas = (len(quadros) + colunas - 1) // colunas
    img = Image.new("RGBA", (w * colunas, h * linhas), (0, 0, 0, 0))
    for i, q in enumerate(quadros):
        img.paste(q, ((i % colunas) * w, (i // colunas) * h))
    return img


# ------------------------------------------------------------------ Davi
#
# 16x16. Folha de 4 colunas (parado, passo A, passo B, arremesso) por 3 linhas (baixo, cima,
# lado). O lado olha pra direita; pra esquerda o script espelha o sprite.

DAVI_FRENTE = [
    "................",
    ".....kkkkkk.....",
    "....khhhhhhk....",
    "....khHhhHhk....",
    "....khsssshk....",
    "....ksesseSk....",
    "....kssssssk....",
    ".....kSSSSk.....",
    "....kkttttkk....",
    "...ksttttttsk...",
    "...ksbbbbbbsk...",
    "....kttttttk....",
    "....kTttttTk....",
]
DAVI_COSTAS = [
    "................",
    ".....kkkkkk.....",
    "....khhhhhhk....",
    "....khhhhhhk....",
    "....khhhhhhk....",
    "....kHhhhhHk....",
    "....kHHhhHHk....",
    ".....kSSSSk.....",
    "....kkttttkk....",
    "...ksttttttsk...",
    "...ksbbbbbbsk...",
    "....kttttttk....",
    "....kTttttTk....",
]
DAVI_LADO = [
    "................",
    ".....kkkkk......",
    "....khhhhhk.....",
    "....khhhhhhk....",
    "....khhhsssk....",
    "....khhssesk....",
    "....khssssssk...",
    ".....kSSSSk.....",
    ".....kttttk.....",
    "....kttttsk.....",
    "....kbbbbsk.....",
    ".....ktttk......",
    ".....kTttk......",
]
DAVI_LADO_ARREMESSO = DAVI_LADO[:8] + [
    ".....kttttkkk...",
    "....kttttkssk...",
    "....kbbbbk.kk...",
    ".....ktttk......",
    ".....kTttk......",
]

PERNAS_FRENTE = {
    "parado": ["....kSk..kSk....", "....kSk..kSk....", "....kbk..kbk...."],
    "passoA": ["....kSk..kSk....", "....kSk...kk....", "....kbk........."],
    "passoB": ["....kSk..kSk....", "....kk...kSk....", ".........kbk...."],
}
PERNAS_LADO = {
    "parado": [".....kSSk.......", ".....kSSk.......", ".....kbbk......."],
    "passoA": ["....kSkkSk......", "...kSk..kSk.....", "...kbk..kbk....."],
    "passoB": [".....kSSk.......", "......kSk.......", "......kbk......."],
}


def davi():
    quadros = []
    for corpo, pernas in ((DAVI_FRENTE, PERNAS_FRENTE), (DAVI_COSTAS, PERNAS_FRENTE), (DAVI_LADO, PERNAS_LADO)):
        arremesso = DAVI_LADO_ARREMESSO if corpo is DAVI_LADO else corpo
        quadros.append(desenhar(corpo + pernas["parado"]))
        quadros.append(desenhar(corpo + pernas["passoA"]))
        quadros.append(desenhar(corpo + pernas["passoB"]))
        quadros.append(desenhar(arremesso + pernas["parado"]))
    return folha(quadros, 4)


# ------------------------------------------------------------------ Jessé (pai do Davi)

JESSE = [
    "................",
    ".....kkkkkk.....",
    "....kccccccK....",
    "...kccCCCCcck...",
    "...kcksssskck...",
    "...kcsesseSck...",
    "...kCxssssxCk...",
    "....kxxxxxxk....",
    "...krkxxxxkrk.m.",
    "..krrrkxxkrrrkm.",
    "..ksrrrrrrrrskm.",
    "...krrrrrrrrk.m.",
    "...kRrrrrrrRk.m.",
    "...kRrrrrrrRk.m.",
    "....kbk..kbk..m.",
    "....kkk..kkk....",
]


def jesse():
    base = [linha.replace("K", "k") for linha in JESSE]
    respira = base[:]
    # Segundo quadro: a barba e os ombros descem 1px — respiração de NPC parado.
    respira[7] = "....kxxxxxxk...."
    respira[8] = "...krkxxxxkrk.m."
    return folha([desenhar(base), desenhar(respira)], 2)


# ------------------------------------------------------------------ ovelha (olha pra direita)

OVELHA_CORPO = [
    "................",
    "................",
    "................",
    "................",
    "....kkkkkk......",
    "...kwwwwwwk.kk..",
    "..kwwwwwwwwkGGk.",
    ".kwwwWwwwwwkGeGk",
    ".kwwwwwwwwwwkGk.",
    ".kWwwwwwwwWWk...",
    "..kWWWWWWWWk....",
]
OVELHA_PERNAS = [
    ["...kGk..kGk.....", "...kGk..kGk.....", "...kk...kk......", "................", "................"],
    ["..kGk....kGk....", "..kGk....kGk....", "..kk.....kk.....", "................", "................"],
]


def ovelha():
    return folha([desenhar(OVELHA_CORPO + p) for p in OVELHA_PERNAS], 2)


# ------------------------------------------------------------------ lobo (24x16, olha pra direita)

LOBO_CORPO = [
    "........................",
    "........................",
    "........................",
    "...................k.k..",
    "..................kGkGk.",
    "..k..............kgggggk",
    ".kGk.kkkkkkkkkkkkgggygkk",
    "..kgggggggggggggggggggGk",
    "..kGgggggggggggggggggkkk",
    "...kGggggglllllllgggk...",
    "...kGGgglllllllllggk....",
    "...kGGkkkkkkkkkkkGGk....",
]
LOBO_PERNAS = [
    ["...kGk.kGk....kGk.kGk...", "...kGk.kGk....kGk.kGk...", "...kk..kk.....kk..kk....", "........................"],
    ["..kGk...kGk..kGk...kGk..", "..kGk...kGk..kGk...kGk..", "..kk....kk...kk....kk...", "........................"],
]


def lobo():
    quadros = [desenhar(LOBO_CORPO + p) for p in LOBO_PERNAS]
    # Terceiro quadro: o bote — boca aberta e corpo esticado pra frente.
    bote = LOBO_CORPO[:]
    bote[8] = "..kGgggggggggggggggggk.k"
    bote[7] = "..kgggggggggggggggggggkk"
    quadros.append(desenhar(bote + LOBO_PERNAS[1]))
    return folha(quadros, 3)


# ------------------------------------------------------------------ cenário

def ruido(img, cores, densidade, semente):
    rnd = random.Random(semente)
    w, h = img.size
    for _ in range(int(w * h * densidade)):
        img.putpixel((rnd.randrange(w), rnd.randrange(h)), rnd.choice(cores) + (255,))


GRAMA = (111, 154, 69)
GRAMA_ESCURA = (92, 134, 58)
GRAMA_CLARA = (134, 176, 82)


def tile_grama(semente, flores=False, tufos=False):
    img = Image.new("RGBA", (16, 16), GRAMA + (255,))
    ruido(img, [GRAMA_ESCURA, GRAMA_CLARA], 0.18, semente)
    rnd = random.Random(semente * 7 + 1)
    if tufos:
        for _ in range(3):
            x, y = rnd.randrange(1, 14), rnd.randrange(3, 15)
            for dx, dy in ((0, 0), (1, -1), (2, 0), (1, -2)):
                img.putpixel((x + dx, y + dy), GRAMA_ESCURA + (255,))
    if flores:
        for _ in range(4):
            x, y = rnd.randrange(1, 15), rnd.randrange(1, 15)
            cor = rnd.choice([(250, 246, 230), (244, 210, 80), (220, 120, 150)])
            img.putpixel((x, y), cor + (255,))
    return img


def tile_terra(semente, base=(176, 138, 90), pedrinha=(140, 108, 70), claro=(196, 160, 110)):
    img = Image.new("RGBA", (16, 16), base + (255,))
    ruido(img, [pedrinha, claro], 0.14, semente)
    return img


def tile_agua():
    img = Image.new("RGBA", (16, 16), (63, 127, 181, 255))
    ruido(img, [(56, 114, 166)], 0.2, 41)
    for x, y in ((2, 3), (3, 3), (4, 3), (9, 9), (10, 9), (11, 9), (5, 13), (6, 13)):
        img.putpixel((x, y), (150, 200, 232, 255))
    return img


def tile_sobre_grama(sprite, semente, verde=False):
    """Cola um desenho de 16x16 em cima de um tile de grama (pedra, arbusto, cerca)."""
    img = tile_grama(semente)
    desenho = desenhar(sprite)
    img.alpha_composite(arbusto_verde(desenho) if verde else desenho)
    return img


ROCHA = [
    "................",
    "................",
    ".....kkkkk......",
    "....kgggggkk....",
    "...kggllggggk...",
    "..kgglllgggggk..",
    "..kgglgggggGGk..",
    ".kggggggggGGGGk.",
    ".kgggggggGGGGGk.",
    ".kGgggggGGGGGGk.",
    ".kGGggGGGGGGGGk.",
    "..kGGGGGGGGGGk..",
    "...kkkkkkkkkk...",
    "................",
    "................",
    "................",
]
ARBUSTO = [
    "................",
    ".....kkkkk......",
    "...kkGGGGGkk....",
    "..kGGGGGGGGGk...",
    ".kGGGGGGGGGGGk..",
    ".kGGGGGGGGGGGGk.",
    "kGGGGGGGGGGGGGGk",
    "kGGGGGGGGGGGGGGk",
    "kGGGGGGGGGGGGGGk",
    ".kGGGGGGGGGGGGk.",
    "..kkGGGGGGGGkk..",
    "....kkkkkkkk....",
    "................",
    "................",
    "................",
    "................",
]
CERCA_H = [
    "................",
    "................",
    "................",
    "..kk........kk..",
    ".kmmk......kmmk.",
    "kkmmkkkkkkkkmmkk",
    "mmmmmmmmmmmmmmmm",
    "bbmmbbbbbbbbmmbb",
    "kkmmkkkkkkkkmmkk",
    "mmmmmmmmmmmmmmmm",
    "bbmmbbbbbbbbmmbb",
    "kkmmkkkkkkkkmmkk",
    ".kmmk......kmmk.",
    ".kbbk......kbbk.",
    "..kk........kk..",
    "................",
]
CERCA_V = [
    "......kkkk......",
    ".....kmbmbk.....",
    ".....kmbmbk.....",
    ".....kmbmbk.....",
    "....kkmmmmkk....",
    "....kmmbbmmk....",
    ".....kmbmbk.....",
    ".....kmbmbk.....",
    ".....kmbmbk.....",
    ".....kmbmbk.....",
    "....kkmmmmkk....",
    "....kmmbbmmk....",
    ".....kmbmbk.....",
    ".....kmbmbk.....",
    ".....kmbmbk.....",
    "......kkkk......",
]


def tileset():
    """4x4 tiles de 16px. O índice de cada tile (esquerda->direita, cima->baixo) é o que o
    mapa usa — gerar_mapa.py tem a tabela LEGENDA com os mesmos números."""
    tiles = [
        tile_grama(1),                      # 0 grama
        tile_grama(2, tufos=True),          # 1 grama com tufos
        tile_grama(3, flores=True),         # 2 grama com flores
        tile_terra(4),                      # 3 caminho de terra
        tile_agua(),                        # 4 água (sólido)
        tile_sobre_grama(ROCHA, 5),         # 5 rocha (sólido)
        tile_sobre_grama(CERCA_H, 6),       # 6 cerca horizontal (sólido)
        tile_sobre_grama(CERCA_V, 7),       # 7 cerca vertical (sólido)
        tile_sobre_grama(ARBUSTO, 8, verde=True),     # 8 arbusto (sólido)
        tile_grama(9),                      # 9 grama sob tronco/casa (sólido, invisível)
        tile_terra(10, (200, 170, 104), (170, 140, 80), (226, 200, 130)),  # 10 chão do curral (palha)
        tile_terra(11, (216, 196, 138), (190, 170, 116), (232, 216, 166)), # 11 areia da margem
    ]
    img = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
    for i, t in enumerate(tiles):
        img.paste(t, ((i % 4) * 16, (i // 4) * 16))
    return img


def arbusto_verde(img):
    """O ARBUSTO usa a letra G (cinza do lobo) — troca por verde escuro depois de desenhar."""
    px = img.load()
    for y in range(img.height):
        for x in range(img.width):
            if px[x, y][:3] == PALETA["G"]:
                px[x, y] = (58, 104, 50, 255)
    return img


def arvore():
    img = Image.new("RGBA", (32, 40), (0, 0, 0, 0))
    copa = [
        "...........kkkkkkkk.............",
        "........kkkGGGGGGGGkkk..........",
        "......kkGGGGGGGGGGGGGGkk........",
        ".....kGGGGGGllGGGGGGGGGGk.......",
        "....kGGGGGllllGGGGGGGGGGGk......",
        "...kGGGGGGGllGGGGGGGGllGGGk.....",
        "..kGGGGGGGGGGGGGGGGGllllGGGk....",
        "..kGGGGGGGGGGGGGGGGGGllGGGGk....",
        ".kGGGGllGGGGGGGGGGGGGGGGGGGGk...",
        ".kGGGllllGGGGGGGGGGGGGGGGGGGk...",
        ".kGGGGllGGGGGGGGGGGGGGGGGGGGGk..",
        "kGGGGGGGGGGGGGGGGGllGGGGGGGGGk..",
        "kGGGGGGGGGGGGGGGGllllGGGGGGGGk..",
        "kGGGGGGGGGGGGGGGGGllGGGGGGGGGk..",
        "kGGGGGGGGGGGGGGGGGGGGGGGGGGGGk..",
        ".kGGGGGGGGGGGGGGGGGGGGGGGGGGk...",
        ".kGGGGGGGGGGGGGGGGGGGGGGGGGGk...",
        "..kGGGGGGGGGGGGGGGGGGGGGGGGk....",
        "...kkGGGGGGGGGGGGGGGGGGGGkk.....",
        ".....kkkGGGGGGGGGGGGGGkkk.......",
        "........kkkkkkkkkkkkkk..........",
    ]
    tronco = [
        "............kmmbk...............",
        "............kmmbk...............",
        "............kmmbk...............",
        "............kmmbk...............",
        "............kmmbbk..............",
        "...........kmmmbbk..............",
        "...........kmmmbbk..............",
        "..........kmmmmbbbk.............",
        "..........kkkkkkkkk.............",
    ]
    corpo = desenhar(copa + tronco)
    px = corpo.load()
    for y in range(corpo.height):
        for x in range(corpo.width):
            if px[x, y][:3] == PALETA["G"]:
                px[x, y] = (62, 112, 52, 255)
            elif px[x, y][:3] == PALETA["l"]:
                px[x, y] = (96, 146, 68, 255)
    img.paste(corpo, (0, 40 - corpo.height), corpo)
    return img


def casa():
    """48x48, base centralizada. Pedra clara com telhado de palha e porta escura."""
    img = Image.new("RGBA", (48, 48), (0, 0, 0, 0))
    px = img.load()
    rnd = random.Random(77)

    def ret(x0, y0, x1, y1, cor):
        for y in range(y0, y1):
            for x in range(x0, x1):
                px[x, y] = cor + (255,)

    # Paredes de pedra.
    ret(4, 18, 44, 46, (190, 172, 140))
    for _ in range(90):
        x, y = rnd.randrange(5, 43), rnd.randrange(19, 45)
        px[x, y] = rnd.choice([(168, 150, 120), (206, 190, 158)]) + (255,)
    for y in range(22, 46, 5):
        for x in range(4, 44):
            if (x + y // 5 * 4) % 9 == 0:
                px[x, y] = (140, 124, 98, 255)
    # Telhado de palha (trapézio).
    for y in range(4, 20):
        recuo = max(0, (19 - y) // 2)
        for x in range(1 + recuo, 47 - recuo):
            listra = (x + y) % 4 == 0
            px[x, y] = ((168, 128, 60) if listra else (196, 154, 80)) + (255,)
    for x in range(1, 47):
        px[x, 19] = (120, 86, 40, 255)
    # Porta e janela.
    ret(20, 30, 28, 46, (70, 44, 26))
    ret(21, 31, 27, 46, (92, 60, 36))
    ret(33, 26, 40, 32, (56, 40, 30))
    # Contorno.
    for y in range(48):
        for x in range(48):
            if px[x, y][3] == 0:
                continue
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nx, ny = x + dx, y + dy
                if not (0 <= nx < 48 and 0 <= ny < 48) or px[nx, ny][3] == 0:
                    px[x, y] = PALETA["k"] + (255,)
                    break
    return img


def pedra_funda():
    return desenhar([
        ".kkk.",
        "kgllk",
        "kggGk",
        "kGGGk",
        ".kkk.",
    ])


def retrato(folha_img, quadro, largura, fundo):
    """64x64: o rosto do sprite ampliado 4x sobre um fundo liso — é o que a caixa de diálogo
    mostra ao lado da fala."""
    rosto = folha_img.crop((quadro[0], quadro[1], quadro[0] + largura, quadro[1] + 16))
    rosto = rosto.crop((0, 0, 16, 12)).resize((64, 48), Image.NEAREST)
    img = Image.new("RGBA", (64, 64), fundo + (255,))
    img.alpha_composite(rosto, (0, 12))
    return img


def disco(tamanho, anel):
    """Controles de toque, brancos pra receber cor no desenho. anel=True: aro com miolo
    translúcido (base do joystick); False: disco cheio (o botão que o dedo arrasta). Contorno
    escuro pra aparecer tanto na grama clara quanto na água."""
    escala = 4
    grande = tamanho * escala
    img = Image.new("RGBA", (grande, grande), (0, 0, 0, 0))
    px = img.load()
    c = (grande - 1) / 2
    r = grande / 2 - 1
    for y in range(grande):
        for x in range(grande):
            d = ((x - c) ** 2 + (y - c) ** 2) ** 0.5
            if d > r:
                continue
            if d > r - 2 * escala:
                px[x, y] = (30, 20, 12, 200)
            elif not anel:
                px[x, y] = (255, 255, 255, 235)
            elif d > r - 7 * escala:
                px[x, y] = (255, 255, 255, 200)
            else:
                px[x, y] = (255, 255, 255, 60)
    return img.resize((tamanho, tamanho), Image.LANCZOS)


def fundo_menu():
    """320x180: céu de fim de tarde, morros e o rebanho em silhueta."""
    img = Image.new("RGBA", (320, 180))
    px = img.load()
    for y in range(180):
        t = y / 180
        cor = (int(240 - 90 * t), int(170 - 60 * t), int(110 + 20 * t))
        for x in range(320):
            px[x, y] = cor + (255,)
    import math
    for camada, (altura, cor, amp, freq) in enumerate([
        (110, (150, 110, 90), 14, 0.018),
        (130, (104, 96, 70), 10, 0.03),
        (150, (70, 86, 50), 6, 0.05),
    ]):
        for x in range(320):
            topo = int(altura + amp * math.sin(x * freq + camada * 2))
            for y in range(topo, 180):
                px[x, y] = cor + (255,)
    ov = desenhar(OVELHA_CORPO + OVELHA_PERNAS[0])
    silhueta = Image.new("RGBA", ov.size, (40, 50, 30, 255))
    for i, (x, y) in enumerate([(60, 148), (84, 152), (230, 150), (250, 146)]):
        mask = ov if i % 2 == 0 else ov.transpose(Image.FLIP_LEFT_RIGHT)
        img.paste(silhueta, (x, y), mask)
    return img


def main():
    os.makedirs(SAIDA, exist_ok=True)

    folha_davi = davi()
    folha_jesse = jesse()

    sprites = {
        "davi": folha_davi,
        "jesse": folha_jesse,
        "ovelha": ovelha(),
        "lobo": lobo(),
        "tileset": tileset(),
        "arvore": arvore(),
        "casa": casa(),
        "pedra": pedra_funda(),
        "retrato_davi": retrato(folha_davi, (0, 0), 16, (92, 120, 150)),
        "retrato_jesse": retrato(folha_jesse, (0, 0), 16, (120, 100, 80)),
        "fundo_menu": fundo_menu(),
        "toque_anel": disco(128, True),
        "toque_botao": disco(64, False),
    }

    for nome, img in sprites.items():
        img.save(os.path.join(SAIDA, f"{nome}.png"))
        print(f"  sprites/{nome}.png  {img.width}x{img.height}")

    # Prévia: tudo lado a lado, ampliado, sobre grama.
    escala = 4
    x = 0
    altura = max(i.height for i in sprites.values() if i.width <= 64) * escala
    itens = [i for n, i in sprites.items() if n != "fundo_menu" and not n.startswith("toque_")]
    largura = sum(i.width * escala + 8 for i in itens)
    previa = Image.new("RGBA", (largura, altura), GRAMA + (255,))
    for img in itens:
        grande = img.resize((img.width * escala, img.height * escala), Image.NEAREST)
        previa.alpha_composite(grande, (x, 0))
        x += grande.width + 8
    previa.save(os.path.join(AQUI, "previa.png"))


if __name__ == "__main__":
    main()
