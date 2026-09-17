"""
Gera Assets/scenes/campo.json a partir do MAPA em texto abaixo.

    python samples/Aurora.CaminhosDaFe/Arte/gerar_mapa.py

Cada caractere é um tile de 16px. Letras de entidade (D, J, 1, 2, 3, A, W, T, H) viram entidades
da cena em cima de um tile de grama. O resultado é uma cena normal da Aurora: dá pra abrir e
retocar no editor — mas rodar este script de novo SOBRESCREVE a cena, então escolha um dos dois
caminhos pra editar o mapa.
"""

import json
import os
import random

AQUI = os.path.dirname(os.path.abspath(__file__))
SAIDA = os.path.join(AQUI, "..", "Assets", "scenes", "campo.json")

TILE = 16

# Índices do tileset (ver tileset() em gerar_arte.py).
GRAMA, GRAMA_TUFO, GRAMA_FLOR, CAMINHO, AGUA, ROCHA, CERCA_H, CERCA_V, ARBUSTO, BASE_SOLIDA, CURRAL, AREIA = range(12)
SOLIDOS = [AGUA, ROCHA, CERCA_H, CERCA_V, ARBUSTO, BASE_SOLIDA]

LEGENDA = {
    ".": None,          # grama (sorteia a variação)
    ",": GRAMA_FLOR,
    "=": CAMINHO,
    "~": AGUA,
    "_": AREIA,
    "o": ROCHA,
    "-": CERCA_H,
    "|": CERCA_V,
    "#": ARBUSTO,
    "c": CURRAL,
}

#   D Davi      J Jessé       1 2 3 ovelhas perdidas     A ovelha do rebanho (já no curral)
#   W lobo      T árvore      H casa (marca o meio da parede de baixo)
MAPA = [
    "################################################",
    "#__________________________________..oooooooooo#",
    "#~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~_.o........o#",
    "#~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~__.o...W....o#",
    "#~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~__..o....T...o#",
    "#________________________________....o........o#",
    "#..1..T......,,.......T..........,...o..,.....o#",
    "#......T.............................oo......oo#",
    "#...======================================.....#",
    "#.......T...............=............,,....T...#",
    "#..#.............,......=......................#",
    "#..##..........T........=........T.......oo....#",
    "#...#...................=...............ooo....#",
    "#......T................=......T...............#",
    "#...........,,..........=....................T.#",
    "#.T.....................=..........T...........#",
    "#.......................=............T...2.....#",
    "#....T..................=.................T....#",
    "#.......................=......T.....T.........#",
    "#..###..................=......................#",
    "#.....#...-----------...=......................#",
    "#.T.......|cAccccccc|.J.=.....................T#",
    "#.........|ccccccAcc|...=..H...................#",
    "#....#....|ccccccccc=====......................#",
    "#.........|cccAccccc=====......T...............#",
    "#.T.......|ccccccccc|...D......................#",
    "#.........|ccccccccc|..............T...........#",
    "#..#......-----------..........................#",
    "#.3#.....T.........................,,..........#",
    "####..........................T................#",
    "#...............T..............................#",
    "################################################",
]


def centro(tx, ty):
    return tx * TILE + TILE / 2, ty * TILE + TILE / 2


def entidade(nome, x, y, *componentes):
    return {"Name": nome, "Components": [{"Type": "Transform", "X": x, "Y": y}, *componentes]}


def ordem_por_y(offset):
    return {"Type": "OrdemPorY", "OffsetY": offset}


def animator(largura, altura, colunas, clipes):
    return {
        "Type": "Animator",
        "FrameWidth": largura,
        "FrameHeight": altura,
        "SheetColumns": colunas,
        "Clips": [{"Name": n, "Duration": d, "Frames": f} for n, d, f in clipes],
    }


def davi(x, y):
    clipes = []
    for linha, direcao in enumerate(["baixo", "cima", "lado"]):
        base = linha * 4
        clipes += [
            (f"parado_{direcao}", 0.2, [base]),
            (f"andar_{direcao}", 0.12, [base + 1, base, base + 2, base]),
            (f"arremesso_{direcao}", 0.2, [base + 3]),
        ]
    return entidade(
        "Player", x, y,
        {"Type": "SpriteRenderer", "Texture": "sprites/davi.png"},
        animator(16, 16, 4, clipes),
        {"Type": "Collider", "Shape": "Circle", "Radius": 4, "OffsetY": 5},
        {"Type": "Health", "Max": 100, "InvulnerabilityAfterHit": 0.6, "DestroyOnDeath": False},
        {"Type": "Tags", "Value": "jogador"},
        {"Type": "TopDownController", "Speed": 72, "FlipSpriteByDirection": False, "AnimatorSpeedParameter": ""},
        {"Type": "Davi"},
        {"Type": "Funda"},
        ordem_por_y(8),
    )


def jesse(x, y):
    return entidade(
        "Jesse", x, y,
        {"Type": "SpriteRenderer", "Texture": "sprites/jesse.png"},
        animator(16, 16, 2, [("respirar", 0.7, [0, 1])]),
        {"Type": "Interagivel", "Raio": 24, "Verbo": "Falar"},
        {"Type": "Npc", "Id": "jesse"},
        ordem_por_y(8),
    )


NOMES_OVELHA = {"1": "Ovelha do Rio", "2": "Ovelha do Bosque", "3": "Ovelha dos Arbustos"}


def ovelha(nome, x, y, perdida):
    return entidade(
        nome, x, y,
        {"Type": "SpriteRenderer", "Texture": "sprites/ovelha.png"},
        animator(16, 16, 2, [("parado", 0.2, [0]), ("andar", 0.16, [0, 1])]),
        {"Type": "Collider", "Shape": "Circle", "Radius": 5, "OffsetY": 3},
        {"Type": "NavAgent", "Speed": 64, "Enabled": False},
        {"Type": "Interagivel", "Raio": 20, "Verbo": "Chamar", "Ativo": perdida},
        {"Type": "Ovelha", "Perdida": perdida},
        {"Type": "Tags", "Value": "ovelha"},
        ordem_por_y(6),
    )


def lobo(x, y):
    return entidade(
        "Lobo", x, y,
        {"Type": "SpriteRenderer", "Texture": "sprites/lobo.png"},
        animator(24, 16, 3, [("parado", 0.2, [0]), ("andar", 0.12, [0, 1]), ("bote", 0.2, [2])]),
        {"Type": "Collider", "Shape": "Circle", "Radius": 6, "OffsetY": 4},
        {"Type": "Health", "Max": 45, "InvulnerabilityAfterHit": 0.15, "DestroyOnDeath": False},
        {"Type": "NavAgent", "Speed": 60, "Enabled": False},
        {"Type": "Tags", "Value": "inimigo"},
        {"Type": "Lobo"},
        ordem_por_y(7),
    )


def arvore(n, tx, ty):
    x, _ = centro(tx, ty)
    return entidade(
        f"Arvore{n}", x, (ty + 1) * TILE - 2,
        {"Type": "SpriteRenderer", "Texture": "sprites/arvore.png", "OriginY": 1.0},
        ordem_por_y(0),
    )


def casa(tx, ty):
    x, _ = centro(tx, ty)
    return entidade(
        "Casa", x, (ty + 1) * TILE,
        {"Type": "SpriteRenderer", "Texture": "sprites/casa.png", "OriginY": 1.0},
        ordem_por_y(0),
    )


def main():
    altura, largura = len(MAPA), len(MAPA[0])
    for i, linha in enumerate(MAPA):
        if len(linha) != largura:
            raise ValueError(f"linha {i} do MAPA tem {len(linha)} colunas, esperado {largura}")

    rnd = random.Random(2026)
    tiles = []
    objetos = []
    curral = []
    arvores = 0

    for ty, linha in enumerate(MAPA):
        for tx, ch in enumerate(linha):
            x, y = centro(tx, ty)

            if ch in LEGENDA:
                tile = LEGENDA[ch]
                if tile is None:
                    tile = rnd.choices([GRAMA, GRAMA_TUFO, GRAMA_FLOR], weights=[70, 24, 6])[0]
                tiles.append(tile)
                if ch == "c":
                    curral.append((tx, ty))
                continue

            tiles.append(GRAMA)
            if ch == "D":
                objetos.append(davi(x, y))
            elif ch == "J":
                # O tile debaixo dele é sólido em vez de um Collider: assim o A* das ovelhas
                # e do lobo também desvia do Jessé, não só a física.
                tiles[-1] = BASE_SOLIDA
                objetos.append(jesse(x, y))
            elif ch in NOMES_OVELHA:
                objetos.append(ovelha(NOMES_OVELHA[ch], x, y, True))
            elif ch == "A":
                tiles[-1] = CURRAL
                curral.append((tx, ty))
                objetos.append(ovelha(f"Rebanho{len(curral)}", x, y, False))
            elif ch == "W":
                objetos.append(lobo(x, y))
            elif ch == "T":
                tiles[-1] = BASE_SOLIDA
                arvores += 1
                objetos.append(arvore(arvores, tx, ty))
            elif ch == "H":
                objetos.append(casa(tx, ty))
            else:
                raise ValueError(f"caractere desconhecido {ch!r} em ({tx}, {ty})")

    # Casa: 3 tiles de largura, as 2 fileiras de baixo bloqueiam (o telhado de cima fica
    # atravessável, e o OrdemPorY desenha o Davi atrás dele).
    for ty, linha in enumerate(MAPA):
        for tx, ch in enumerate(linha):
            if ch == "H":
                for dy in (-1, 0):
                    for dx in (-1, 0, 1):
                        tiles[(ty + dy) * largura + tx + dx] = BASE_SOLIDA

    xs = [c[0] for c in curral]
    ys = [c[1] for c in curral]
    x0, x1 = min(xs) * TILE, (max(xs) + 1) * TILE
    y0, y1 = min(ys) * TILE, (max(ys) + 1) * TILE

    chao = entidade(
        "Chao", 0, 0,
        {
            "Type": "Tilemap",
            "Texture": "sprites/tileset.png",
            "TileWidth": TILE,
            "TileHeight": TILE,
            "Width": largura,
            "Height": altura,
            "Layer": -10000,
            "SolidTiles": ", ".join(str(s) for s in SOLIDOS),
            "Tiles": tiles,
        },
    )
    curral_ent = entidade(
        "Curral", (x0 + x1) / 2, (y0 + y1) / 2,
        {"Type": "Curral", "Largura": x1 - x0, "Altura": y1 - y0},
    )
    camera = entidade(
        "Camera", 0, 0,
        {
            "Type": "CameraController", "Follow": "Player", "FollowSpeed": 8, "Zoom": 3,
            "ClampBounds": True, "BoundsWidth": 0, "BoundsHeight": 0,
        },
    )

    cena = {"Scene": "campo", "Objects": [chao, curral_ent, *objetos, camera]}
    with open(SAIDA, "w", encoding="utf-8") as f:
        json.dump(cena, f, ensure_ascii=False, indent=2)
    print(f"campo.json: {largura}x{altura} tiles, {len(objetos)} entidades")


if __name__ == "__main__":
    main()
