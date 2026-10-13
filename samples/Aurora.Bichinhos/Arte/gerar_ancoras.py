"""
Descobre onde cada item vestível encaixa em cada forma de cada bichinho e grava
Assets/database/ancoras.json. Também monta Arte/folha_itens.png: cada forma usando cada item, com a
MESMA conta de posição do jogo (Render/Roupa.cs), pra conferir o encaixe de olho sem abrir o jogo.

    python Arte/gerar_ancoras.py

Âncoras (todas em % do sprite, 0..100, y para baixo):
  Cabeca  [x, y, largura]  y = onde a BASE do chapéu pousa (topo da cabeça); largura = da cabeça
  Pescoco [x, y, largura]  o MEIO do lenço fica em y; largura = do corpo naquela altura
Óculos não têm âncora: usam os olhos que o especies.json já traz.

O topo da cabeça é a primeira linha em que a silhueta tem um trecho contínuo largo o bastante
(chifre, chama, orelha comprida e antena são finos demais e ficam de fora). Se alguma forma ficar
torta, ajuste em AJUSTES: valores ali somam ao que foi detectado.
"""

import json
import os

import numpy as np
from PIL import Image, ImageDraw

AQUI = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(AQUI, "..", "Assets")
LADO = 256

#: o trecho acima dos olhos continua sendo "cabeça" enquanto tiver ao menos esta fração da largura
#: da cabeça (e nunca menos que MINIMO_TOPO do sprite).
FRACAO_TOPO = 0.40
MINIMO_TOPO = 0.12

#: largura máxima da cabeça, como fração do sprite (asa/braço grudados não contam).
LIMITE_CABECA = 0.42

#: (dx, dy, escala_largura) somados à âncora de Cabeca/Pescoco por forma, depois de olhar a folha.
AJUSTES = {}

#: Escalas do jogo por item (Game/Itens.cs): largura do item = largura da âncora * escala.
ITENS = {
    "chapeu": dict(slot="Cabeca", escala=0.62, dx=0.0, rot=0.0),
    "coroa": dict(slot="Cabeca", escala=0.66, dx=0.0, rot=0.0),
    "laco": dict(slot="Cabeca", escala=0.46, dx=0.34, rot=0.35),
    "lenco": dict(slot="Pescoco", escala=0.85, dx=0.0, rot=0.0),
    "oculos": dict(slot="Rosto", escala=1.0, dx=0.0, rot=0.0),
    "oculos_sol": dict(slot="Rosto", escala=1.0, dx=0.0, rot=0.0),
}


def maior_trecho(linha):
    """(inicio, fim) do maior trecho contínuo de True numa linha, ou (0, 0)."""
    melhor, ini = (0, 0), None
    for x, v in enumerate(list(linha) + [False]):
        if v and ini is None:
            ini = x
        elif not v and ini is not None:
            if x - ini > melhor[1] - melhor[0]:
                melhor = (ini, x)
            ini = None
    return melhor


def trecho_em(linha, x):
    """Trecho contínuo de True que contém a coluna x (ou o maior, se x cair num vazio)."""
    if 0 <= x < len(linha) and linha[x]:
        a = b = x
        while a > 0 and linha[a - 1]:
            a -= 1
        while b < len(linha) - 1 and linha[b + 1]:
            b += 1
        return a, b + 1
    return maior_trecho(linha)


def detectar(nome, olhos):
    alfa = np.array(Image.open(os.path.join(ASSETS, "sprites", "bichos", nome + ".png")).convert("RGBA"))[:, :, 3] > 128

    ox, oy = olhos[0] / 100 * LADO, olhos[1] / 100 * LADO
    xi, yi = int(ox), int(oy)

    # Largura da cabeça na altura dos olhos (limitada: asa e braço grudados não são cabeça).
    a, b = trecho_em(alfa[yi], xi)
    largura_cabeca = min(b - a, LIMITE_CABECA * LADO)
    limite = max(FRACAO_TOPO * largura_cabeca, MINIMO_TOPO * LADO)

    # Sobe do olho enquanto a coluna do rosto continua opaca e o trecho continua largo: chama,
    # broto, crista e orelha comprida são mais finos que isso e ficam de fora do "topo da cabeça".
    topo = yi
    while topo > 0 and alfa[topo - 1][xi]:
        ia, ib = trecho_em(alfa[topo - 1], xi)
        if ib - ia < limite:
            break
        topo -= 1

    ta, tb = trecho_em(alfa[min(LADO - 1, topo + 2)], xi)
    cx = (ta + tb) / 2

    # Queixo: abaixo dos olhos, na distância dos olhos até o topo, um pouco menos.
    queixo = oy + (oy - topo) * 0.62
    pa, pb = trecho_em(alfa[int(min(LADO - 1, queixo))], xi)
    largura_pescoco = min(pb - pa, 1.25 * largura_cabeca)

    return {
        "Cabeca": [round(cx / LADO * 100, 1), round((topo + 3) / LADO * 100, 1), round(largura_cabeca / LADO * 100, 1)],
        "Pescoco": [round(((pa + pb) / 2) / LADO * 100, 1), round(queixo / LADO * 100, 1), round(largura_pescoco / LADO * 100, 1)],
    }


def ler_especies():
    dados = json.load(open(os.path.join(ASSETS, "database", "especies.json"), encoding="utf-8"))
    for e in dados["Especies"]:
        for i, est in enumerate(e["Estagios"]):
            yield f"{e['Id']}_{i}", est["Olhos"]


# ---------------------------------------------------------------------------------- posição (igual ao jogo)

def colocar(base, sprite_forma, olhos, ancora, item, escala_x=1.0):
    """Cola `item` sobre `base` (imagem da forma, LADO x LADO). Mesma conta de Render/Roupa.cs."""
    cfg = ITENS[item]
    img = Image.open(os.path.join(ASSETS, "sprites", "itens", item + ".png")).convert("RGBA")
    if cfg["slot"] == "Rosto":
        ox, oy, sep, r = olhos
        lado_lente = r * 1.35 * 2 * 1.12 / 100 * LADO
        for s in (-1, 1):
            lente = img.resize((int(lado_lente), int(lado_lente)), Image.LANCZOS)
            cx = (ox + s * sep) / 100 * LADO
            cy = oy / 100 * LADO
            base.alpha_composite(lente, (int(cx - lente.width / 2), int(cy - lente.height / 2)))
        # ponte
        d = ImageDraw.Draw(base)
        x0 = (ox - sep) / 100 * LADO + lado_lente / 2 * 0.9
        x1 = (ox + sep) / 100 * LADO - lado_lente / 2 * 0.9
        d.line([(x0, oy / 100 * LADO), (x1, oy / 100 * LADO)], fill=(60, 46, 74, 255), width=max(2, int(LADO * 0.012)))
        return

    x, y, w = ancora
    largura = w / 100 * LADO * cfg["escala"]
    altura = largura * img.height / img.width
    item_img = img.resize((max(1, int(largura)), max(1, int(altura))), Image.LANCZOS)
    pivo_y = 0.92 if cfg["slot"] == "Cabeca" else 0.40
    if cfg["rot"]:
        item_img = item_img.rotate(-np.degrees(cfg["rot"]), resample=Image.BICUBIC, expand=True)
    px = x / 100 * LADO + cfg["dx"] * w / 100 * LADO
    py = y / 100 * LADO
    base.alpha_composite(item_img, (int(px - item_img.width / 2), int(py - item_img.height * (pivo_y if not cfg["rot"] else 0.75))))


def main():
    ancoras = {}
    for nome, olhos in ler_especies():
        a = detectar(nome, olhos)
        for slot, (dx, dy, k) in AJUSTES.get(nome, {}).items():
            a[slot] = [round(a[slot][0] + dx, 1), round(a[slot][1] + dy, 1), round(a[slot][2] * k, 1)]
        ancoras[nome] = a

    destino = os.path.join(ASSETS, "database", "ancoras.json")
    with open(destino, "w", encoding="utf-8") as f:
        json.dump({"_comentario": "Gerado por Arte/gerar_ancoras.py. Pontos de encaixe dos itens vestíveis, em % do sprite.",
                   "Formas": ancoras, "Itens": ITENS}, f, ensure_ascii=False, indent=1)
    print("gravado", destino)

    # Folha de conferência: uma linha por forma, uma coluna por item.
    nomes = list(ancoras)
    itens = list(ITENS)
    cel = 150
    folha = Image.new("RGBA", (cel * (len(itens) + 1), cel * len(nomes)), (240, 228, 210, 255))
    d = ImageDraw.Draw(folha)
    olhos_por_forma = dict(ler_especies())
    for r, nome in enumerate(nomes):
        forma = Image.open(os.path.join(ASSETS, "sprites", "bichos", nome + ".png")).convert("RGBA")
        for c, item in enumerate([None] + itens):
            im = forma.copy()
            if item:
                colocar(im, nome, olhos_por_forma[nome], ancoras[nome][ITENS[item]["slot"]] if ITENS[item]["slot"] != "Rosto" else None, item)
            im = im.resize((cel, cel), Image.LANCZOS)
            folha.alpha_composite(im, (c * cel, r * cel))
        d.text((4, r * cel + 2), nome, fill=(40, 30, 30, 255))
    caminho = os.path.join(AQUI, "folha_itens.png")
    folha.save(caminho)
    print("folha", caminho)


if __name__ == "__main__":
    main()
