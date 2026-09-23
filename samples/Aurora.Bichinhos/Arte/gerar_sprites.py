"""
Gera todos os PNGs de Assets/sprites/ dos Bichinhos.

    python samples/Aurora.Bichinhos/Arte/gerar_sprites.py            # todos
    python samples/Aurora.Bichinhos/Arte/gerar_sprites.py brasinha   # só os que começam com "brasinha"

Precisa de Pillow e numpy. Também escreve Arte/folha.png — todos os bichos lado a lado, pra
conferir de olho sem abrir o jogo.
"""

import os
import sys

from PIL import Image, ImageDraw

import bichos
import cenario

AQUI = os.path.dirname(os.path.abspath(__file__))
SAIDA = os.path.join(AQUI, "..", "Assets", "sprites")

#: id da espécie -> função. O nome do arquivo segue o que Game/Especies.cs procura:
#: bichos/<id>_<estágio>.png.
ESPECIES = {
    "brasinha": bichos.brasinha,
    "gotinha": bichos.gotinha,
    "brotinho": bichos.brotinho,
    "pelucinho": bichos.pelucinho,
    "pipio": bichos.pipio,
}

SPRITES = {}
for _id, _funcao in ESPECIES.items():
    for _e in range(3):
        SPRITES[f"bichos/{_id}_{_e}"] = (lambda f=_funcao, e=_e: f(e))

SPRITES.update({
    "bichos/ovo_brasinha": lambda: bichos.ovo("#FF8A3D"),
    "bichos/ovo_gotinha": lambda: bichos.ovo("#5EC8F2"),
    "bichos/ovo_brotinho": lambda: bichos.ovo("#8ED66B"),
    "fundos/casa": cenario.casa,
    "fundos/batalha": cenario.batalha,
    "fundos/escolha": cenario.escolha,
    "icones/maca": cenario.maca,
    "icones/doce": cenario.doce,
    "icones/bola": cenario.bola,
    "icones/bolhas": cenario.bolhas,
    "icones/pilula": cenario.pilula,
    "icones/lampada": cenario.lampada,
    "icones/espadas": cenario.espadas,
    "icones/moeda": cenario.moeda,
    "icones/coracao": cenario.coracao,
    "icones/coco": cenario.coco,
    "icones/raio": cenario.raio,
    "icones/cruz": cenario.cruz,
    "icones/estrela": cenario.estrela,
    "icones/gota": cenario.gota_suor,
    "icones/nota": cenario.nota,
    "ui/painel": cenario.painel,
    "ui/circulo": cenario.circulo_branco,
})


def gerar(nome, funcao):
    caminho = os.path.join(SAIDA, nome + ".png")
    os.makedirs(os.path.dirname(caminho), exist_ok=True)
    resultado = funcao()
    if isinstance(resultado, Image.Image):
        resultado.save(caminho, optimize=True)
        return resultado
    return resultado.salvar(caminho)


def folha(imagens, colunas=6, celula=200):
    linhas = (len(imagens) + colunas - 1) // colunas
    img = Image.new("RGBA", (colunas * celula, linhas * celula), (240, 228, 210, 255))
    d = ImageDraw.Draw(img)
    for i, (nome, im) in enumerate(imagens):
        x, y = (i % colunas) * celula, (i // colunas) * celula
        miniatura = im.copy()
        miniatura.thumbnail((celula - 20, celula - 30))
        img.alpha_composite(miniatura.convert("RGBA"), (x + (celula - miniatura.width) // 2, y + 4))
        d.text((x + 6, y + celula - 20), nome.split("/")[-1], fill=(40, 30, 30, 255))
    return img


def main():
    filtro = sys.argv[1] if len(sys.argv) > 1 else ""
    feitos = []
    for nome, funcao in SPRITES.items():
        if filtro and not nome.split("/")[-1].startswith(filtro):
            continue
        feitos.append((nome, gerar(nome, funcao)))
        print("ok", nome)

    bichos_feitos = [(n, im) for n, im in feitos if n.startswith("bichos/")]
    if bichos_feitos:
        folha(bichos_feitos).save(os.path.join(AQUI, "folha.png"))


if __name__ == "__main__":
    main()
