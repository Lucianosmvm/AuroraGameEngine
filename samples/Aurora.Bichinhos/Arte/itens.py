"""
Itens vestíveis do Bichinhos. Cada item é um quadrado de 128 px desenhado em coordenadas 0..100
(como os ícones), feito pra ser esticado até a LARGURA da âncora da forma do bicho em
Assets/database/ancoras.json (ver gerar_ancoras.py).

Convenção de encaixe, que o jogo (Render/Roupa.cs) repete:
  - chapéu, coroa, laço: a BASE do desenho (y ~ 92) fica no ponto de âncora da cabeça;
  - lenço: o MEIO do desenho (y ~ 40) fica no ponto de âncora do pescoço;
  - óculos: o desenho é UMA lente (quadrada, centrada); o jogo põe uma em cada olho.
"""

import math

from pincel import Tela, cor, clarear, escurecer, uniao, menos, dentro


def _item():
    return Tela(128)


def _listras(t, corpo, c, passo=22, grossura=9, inicio=-10):
    """Listras diagonais da cor `c`, cortadas pela silhueta `corpo`."""
    for i in range(7):
        x = inicio + i * passo
        faixa = t.poligono([(x, 100), (x + grossura, 100), (x + grossura + 40, 0), (x + 40, 0)])
        t.colar(dentro(corpo, faixa), c)


def chapeu():
    """Chapéu de festa: cone rosa com listras amarelas e pompom."""
    t = _item()
    cone = t.poligono([(16, 92), (84, 92), (50, 12)])
    t.pintar(cone, cor("#FF5C7A"))
    _listras(t, cone, cor("#FFE27A"), passo=26, grossura=9, inicio=-8)
    t.pintar(t.elipse(50, 91, 36, 6), cor("#FFE27A"), contorno=1.0)
    t.pintar(t.circulo(50, 12, 8), cor("#FFF6D6"), contorno=1.0)
    t.contorno_externo(0.8)
    return t


def coroa():
    """Coroa dourada com três pedras. É o item exclusivo do 7º dia do presente."""
    t = _item()
    corpo = t.poligono([(12, 92), (88, 92), (91, 32), (72, 56), (50, 16), (28, 56), (9, 32)])
    t.pintar(corpo, cor("#F4C542"))
    faixa = t.retangulo(12, 74, 88, 92)
    t.colar(dentro(corpo, faixa), cor("#C99A1E"))
    for x, c in ((30, "#4FC3F7"), (50, "#FF5C7A"), (70, "#4FC3F7")):
        t.pintar(t.circulo(x, 83, 5.2), cor(c), contorno=0.8, sombra=0.25)
    for x, y in ((10, 32), (50, 16), (90, 32)):
        t.pintar(t.circulo(x, y, 5.5), cor("#FFF6D6"), contorno=0.9, sombra=0.2)
    t.contorno_externo(0.8)
    return t


def laco():
    """Laço rosa, para a lateral da cabeça (o jogo gira e desloca)."""
    t = _item()
    rosa = cor("#FF8FB1")
    t.pintar(t.poligono([(44, 58), (30, 94), (46, 86), (50, 62)]), cor("#E0608A"), contorno=0.9)
    t.pintar(t.poligono([(56, 58), (70, 94), (54, 86), (50, 62)]), cor("#E0608A"), contorno=0.9)
    t.pintar(t.elipse(26, 44, 24, 17, -18), rosa)
    t.pintar(t.elipse(74, 44, 24, 17, 18), rosa)
    t.colar(t.elipse(20, 38, 7, 3.5, -25), (255, 255, 255), 0.55)
    t.colar(t.elipse(68, 38, 7, 3.5, 25), (255, 255, 255), 0.55)
    t.pintar(t.circulo(50, 46, 10), cor("#E0608A"), contorno=1.0)
    t.contorno_externo(0.8)
    return t


def lenco():
    """Lenço de pescoço azul listrado com a ponta caída do lado direito."""
    t = _item()
    azul, branco = cor("#3F95E0"), cor("#F4F0EC")
    ponta = t.poligono([(58, 44), (82, 46), (88, 96), (64, 90)])
    t.pintar(ponta, azul)
    _listras(t, ponta, branco, passo=20, grossura=6, inicio=-30)
    faixa = t.tubo([(15, 32), (50, 46), (85, 32)], 12)
    t.pintar(faixa, azul)
    _listras(t, faixa, branco, passo=18, grossura=6, inicio=-6)
    t.contorno_externo(0.8)
    return t


def oculos():
    """Uma lente de óculos de aro roxo. O jogo desenha duas e liga com uma ponte."""
    t = _item()
    aro = menos(t.circulo(50, 50, 47), t.circulo(50, 50, 37))
    t.colar(t.circulo(50, 50, 37), (200, 230, 255), 0.20)
    t.pintar(aro, cor("#5A4670"), contorno=0.9, sombra=0.25)
    t.colar(t.elipse(34, 33, 9, 4, -40), (255, 255, 255), 0.85)
    return t


def oculos_sol():
    """Lente escura de óculos de sol."""
    t = _item()
    lente = t.circulo(50, 50, 46)
    t.pintar(lente, cor("#2B2438"), contorno=1.0, sombra=0.0, luz=0.0)
    t.colar(dentro(lente, t.poligono([(18, 30), (40, 20), (30, 46), (14, 52)])), (255, 255, 255), 0.55)
    t.colar(dentro(lente, t.poligono([(46, 24), (54, 22), (36, 62), (30, 60)])), (255, 255, 255), 0.18)
    return t
