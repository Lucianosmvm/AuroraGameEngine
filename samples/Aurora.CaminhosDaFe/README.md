# Caminhos da Fé — protótipo 0.1 (Capítulo 1: Davi)

Primeira fatia jogável do GDD, em cima da Aurora Engine: uma tela de campo com Davi, o pai Jessé,
três ovelhas perdidas, o curral e um lobo. Dá 5–10 minutos de jogo, de "fale com seu pai" a
"capítulo concluído".

## Rodar

```bash
dotnet run --project samples/Aurora.CaminhosDaFe
```

Direto no campo, sem passar pelo menu:

```bash
dotnet run --project samples/Aurora.CaminhosDaFe -- --scene scenes/campo.json
```

## Controles

| | |
|---|---|
| Andar | WASD / setas / analógico esquerdo |
| Interagir / passar a fala | `E` (ou `A` no controle); Espaço e Enter também passam a fala |
| Funda | segure o botão esquerdo do mouse pra girar, solte pra arremessar na direção do cursor. `J` ou gatilho direito arremessa pra onde o Davi olha |
| Escolha no diálogo | `W`/`S` ou setas, confirma com Espaço |
| Pausa | `ESC` |

## O roteiro (Missão "O Rebanho")

1. **Jessé** pede para buscar três ovelhas. Uma escolha no diálogo dá +1 de **Fé**.
2. **As ovelhas**: perto de uma, `[E] Chamar`. Ela segue o Davi desviando de cerca e rocha e,
   perto do curral, entra sozinha pela porteira.
3. De volta ao Jessé: um uivo. O **lobo** acorda na toca de pedras ao nordeste e desce rondando
   o curral.
4. **Combate**: o lobo caça o Davi, para, se encolhe (fica avermelhado) e salta. Sair da frente
   ou acertar uma pedra no preparo interrompe o bote. Mais carga na funda = mais alcance e dano.
5. Contar ao Jessé dá +1 de **Coragem** e fecha o capítulo.

## Mapa dos arquivos

```
CaminhosGame.cs         telas (menu, HUD, pausa, fim), diálogo pausando o mundo, desenho da mira
                        e das dicas "[E] Falar" / "Bééé!"
Game/Missao.cs          o roteiro: falas por estágio, objetivo da HUD, virtudes  ← histórias aqui

Scripts/Davi.cs         animação em 4 direções, tecla E, vida pra HUD
Scripts/Funda.cs        carregar / mirar / arremessar
Scripts/Pedra.cs        projétil: atravessa ovelha e NPC, para em inimigo e tile sólido
Scripts/Ovelha.cs       pastando → seguindo → no curral
Scripts/Lobo.cs         dormindo → rondando → caçando → preparo → bote → recuo
Scripts/Interagivel.cs  "dá pra apertar E aqui" + interface IInteracao
Scripts/Npc.cs          repassa a conversa pra Missao
Scripts/Curral.cs       área do curral
Scripts/OrdemPorY.cs    quem está mais embaixo é desenhado por cima

Arte/gerar_arte.py      toda a pixel art, desenhada em texto → Assets/sprites/ (+ Arte/previa.png)
Arte/gerar_mapa.py      o mapa em ASCII → Assets/scenes/campo.json
Assets/scenes/*.json    campo, menu e as telas de UI
Assets/prefabs/pedra.json
```

## Mexer no mapa ou na arte

O campo é um mapa de texto em `Arte/gerar_mapa.py` (48x32 tiles de 16px, câmera com zoom 3).
Cada letra vira tile ou entidade: `~` água, `o` rocha, `#` arbusto, `-`/`|` cerca, `c` curral,
`=` caminho, `T` árvore, `H` casa, `D` Davi, `J` Jessé, `1 2 3` ovelhas perdidas, `A` ovelha do
rebanho, `W` lobo.

```bash
python samples/Aurora.CaminhosDaFe/Arte/gerar_mapa.py
python samples/Aurora.CaminhosDaFe/Arte/gerar_arte.py
```

Rodar `gerar_mapa.py` **sobrescreve** `campo.json` — ou se edita pelo script, ou pelo editor da
Aurora, não os dois.

Tudo que precisa ser desviado pelo pathfinding (tronco de árvore, casa, o Jessé) é **tile sólido**
e não `Collider`: a engine monta a grade do A* só a partir do Tilemap.

## Regras deste projeto

1. **O roteiro mora na `Missao`.** Scripts avisam o que aconteceu (`OvelhaRecolhida`,
   `LoboDerrotado`, `FalarCom`); a Missao decide o que isso significa na história.
2. **Diálogo congela o mundo.** Por isso o avanço por `E` fica no `OnUpdate` do jogo, que roda
   com o mundo parado.
3. **A fonte só tem Latin-1.** Acentos funcionam; travessão (—), aspas curvas e símbolos como ✝
   aparecem como `?` na tela.

## Fora do protótipo (próximos passos do GDD)

Inventário, XP e níveis, save, sons, lobo atacando o rebanho, outros mapas e a Missão 03
("A Mensagem"). Os ganchos naturais: `Inventory`/`Save` da engine, novas fases de estágio na
`Missao`, novos `.json` de cena ligados por ação `ChangeScene`.
