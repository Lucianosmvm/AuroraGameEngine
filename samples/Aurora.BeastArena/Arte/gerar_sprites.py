"""
Gera todos os PNGs de Assets/sprites/ do Beast Arena.

    python samples/Aurora.BeastArena/Arte/gerar_sprites.py            # todos
    python samples/Aurora.BeastArena/Arte/gerar_sprites.py lobo       # só os que começam com "lobo"

Precisa de Pillow e numpy. Também escreve Arte/folha.png — todos os sprites sobre o chão da
arena, pra conferir de olho sem abrir o jogo.
"""

import os
import sys

from PIL import Image, ImageDraw

import cenario
import criaturas

AQUI = os.path.dirname(os.path.abspath(__file__))
SAIDA = os.path.join(AQUI, "..", "Assets", "sprites")

#: id da carta -> (função, número de estágios). Os nomes dos arquivos seguem a convenção que
#: Render/Sprites.cs procura: criaturas/<id>_<estágio>, cartas/<id> (ícone de feitiço),
#: efeitos/<id> (área do feitiço) e efeitos/<id>_projetil.
CRIATURAS = {
    "lobo": (criaturas.lobo, 3),
    "rinoceronte": (criaturas.rinoceronte, 2),
    "vespas": (criaturas.vespas, 2),
    "serpente": (criaturas.serpente, 3),
    "ourico": (criaturas.ourico, 3),
    "coruja": (criaturas.coruja, 2),
}

SPRITES = {}
for _id, (_funcao, _estagios) in CRIATURAS.items():
    for _e in range(_estagios):
        SPRITES[f"criaturas/{_id}_{_e}"] = (lambda f=_funcao, e=_e: f(e))

SPRITES.update({
    "arena/ninho": cenario.ninho,
    "arena/ovo": cenario.ovo,
    "arena/santuario": cenario.santuario,
    "arena/pilar": cenario.pilar,
    "arena/pedra_0": lambda: cenario.pedra(0),
    "arena/pedra_1": lambda: cenario.pedra(1),
    "arena/pedra_2": lambda: cenario.pedra(2),
    "arena/tufo_0": lambda: cenario.tufo(0),
    "arena/tufo_1": lambda: cenario.tufo(1),
    "arena/flor_0": lambda: cenario.flor(0),
    "arena/flor_1": lambda: cenario.flor(1),
    "efeitos/ourico_projetil": cenario.espinho,
    "efeitos/coruja_projetil": cenario.orbe,
    "efeitos/lamacal": cenario.lamacal_area,
    "efeitos/queimada": cenario.queimada_area,
    "cartas/lamacal": cenario.lamacal_icone,
    "cartas/queimada": cenario.queimada_icone,
})


def folha(imagens, colunas=6, celula=180):
    linhas = (len(imagens) + colunas - 1) // colunas
    img = Image.new("RGBA", (colunas * celula, linhas * celula), (52, 64, 47, 255))
    d = ImageDraw.Draw(img)
    for i, (nome, sprite) in enumerate(imagens):
        x, y = i % colunas * celula, i // colunas * celula
        d.rectangle((x, y, x + celula - 1, y + celula - 1), outline=(40, 50, 36, 255))
        s = sprite.resize((celula - 20, celula - 20), Image.LANCZOS)
        img.alpha_composite(s, (x + 10, y + 4))
        d.text((x + 6, y + celula - 14), nome.split("/")[-1], fill=(220, 220, 200, 255))
    return img


def main():
    filtro = sys.argv[1] if len(sys.argv) > 1 else ""
    geradas = []
    for nome, gerar in SPRITES.items():
        if not nome.split("/")[-1].startswith(filtro):
            continue
        caminho = os.path.join(SAIDA, nome + ".png")
        os.makedirs(os.path.dirname(caminho), exist_ok=True)
        geradas.append((nome, gerar().salvar(caminho)))
        print("ok", nome)

    folha(geradas).save(os.path.join(AQUI, "folha.png"))


if __name__ == "__main__":
    main()
