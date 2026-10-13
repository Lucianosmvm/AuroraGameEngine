# Bichinhos

Bichinho virtual no estilo Tamagotchi, com nível, evolução e batalha por turnos no estilo Pokémon.
Retrato (720x1280), feito pra celular.

```bash
dotnet run --project samples/Aurora.Bichinhos
```

## Como joga

1. **Escolha um ovo**: Brasinha (Fogo), Gotinha (Água) ou Brotinho (Planta). O ovo choca na hora,
   e você **dá um nome** ao bichinho (toque em "Outro nome" até gostar de um; não precisa de teclado).
2. **Cuide**: as barras do alto caem com o tempo, **inclusive com o jogo fechado** (até 24 h contam).

   | Botão | O que faz |
   |---|---|
   | Comer | Refeição (grátis) mata a fome; Doce (3 moedas) dá alegria. Cheio, ele recusa |
   | Brincar | Escolha entre **Pra que lado?** (5 rodadas, acertar 3 ganha) e **Pegar frutas** (20 s, veja abaixo) |
   | Limpar | Tira os cocôs e dá banho. Cocô no chão derruba a higiene rápido |
   | Remédio | Cura a doença. Bicho maltratado (fome, sujeira, cocô) perde saúde e adoece |
   | Dormir | Apaga a luz; ele recupera energia e acorda sozinho quando descansou |
   | Batalhar | Mato (fácil), Campo (normal) ou Arena (difícil) contra um bicho selvagem |

   Tocar no bicho é carinho. O balão sobre a cabeça mostra o que ele mais precisa agora.
3. **Suba de nível**: cuidar dá um pouco de XP, batalha dá bem mais. Nível novo pode ensinar golpe.
4. **Evolua**: nível 6 vira a segunda forma e nível 14 a forma final (ver **Ficha**).

### Pegar frutas

Brincadeira de habilidade: frutas, doces e estrelas caem e o bichinho segue o seu dedo. Maçã vale 1,
doce 3, estrela 5; **cocô tira 2**. Dura 20 segundos e vai acelerando; 10 pontos já contam como
vitória. Rende até 10 moedas e XP, e o recorde fica salvo. A regra está em `Game/PegaFrutas.cs`.

### Presente diário

Ao abrir o jogo no dia, aparece o **presente do dia**: 5, 7, 9, 11, 13, 15 e **30** moedas do dia 1
ao 7, e depois o ciclo recomeça. Abrir em dias seguidos mantém a sequência; faltar um dia zera (sem
outra punição). O dia é o do calendário do aparelho. Regra em `Game/Presente.cs`.

### Guarda-roupa

O botão de chapéu na casa abre o guarda-roupa. São 6 itens só visuais (não mudam atributo nenhum):

| Item | Lugar | Preço |
|---|---|---|
| Chapéu de festa, Laço rosa | cabeça | 15 |
| Óculos redondos | rosto | 20 |
| Lenço listrado | pescoço | 25 |
| Óculos de sol | rosto | 30 |
| **Coroa dourada** | cabeça | só no **7º dia seguido** do presente |

Um item por lugar (chapéu, laço e coroa disputam a cabeça); lugares diferentes convivem. Tocar num
item que você não tem compra e já veste; tocar num que tem veste ou tira. O guarda-roupa é do
**jogador**: recomeçar com outro ovo não perde os itens nem o que está vestido. O item aparece em
todas as telas onde o seu bichinho aparece (casa, brincadeiras, batalha, evolução).

Como o encaixe funciona: cada forma de cada espécie tem pontos de encaixe (topo da cabeça, pescoço)
em `Assets/database/ancoras.json`, **gerados** por `Arte/gerar_ancoras.py` a partir da silhueta do
sprite; os óculos usam os olhos que o `especies.json` já traz. O item acompanha a respiração, o pulo
e o espelhamento do bichinho. Veja `Arte/folha_itens.png` (cada forma usando cada item) sem abrir o jogo.

**Item novo:** desenhe em `Arte/itens.py`, registre em `gerar_sprites.py` e em `ITENS` de
`gerar_ancoras.py`, rode os dois scripts e acrescente o `Item` em `Game/Itens.cs`.
**Sprite de bicho novo ou alterado:** rode `python Arte/gerar_ancoras.py` de novo.

### Som

Efeitos em todo lugar (comer, carinho, golpe, "super eficaz", vitória, evolução...) e duas músicas:
calma em casa e agitada na batalha e em "Pegar frutas". O botão de nota na casa liga e desliga, e a
escolha fica no save. Os sons são sintetizados em `Arte/gerar_sons.py` (sem arquivo de terceiros).

### Batalha

Por turnos: o mais rápido ataca primeiro. Cada bicho usa os 4 últimos golpes que aprendeu.
Fogo vence Planta, Planta vence Água, Água vence Fogo (2x); ao contrário é 0,5x; Normal é neutro.
Golpe do mesmo tipo do bicho bate 1,5x. O botão do golpe avisa "super eficaz" / "pouco eficaz".
Bicho **feliz e alimentado luta até 10% melhor**, e doente, dormindo, muito cansado ou faminto
não luta. Poção (10 moedas) cura metade da vida e gasta o turno.

Perder não mata: o bicho volta triste e machucado. Aqui ninguém morre.

### Duelo com amigo (Wi-Fi)

**Batalhar → Amigo (Wi-Fi)**. Os dois celulares precisam estar na mesma rede:

1. Um toca **Criar sala**.
2. O outro toca **Procurar sala** e escolhe a sala do amigo na lista.
3. Não apareceu? Toque **Digitar IP** e digite o número que aparece na tela de quem criou.

**Sem Wi-Fi por perto:** um dos dois liga o **roteador do celular** (hotspot / ponto de acesso) e o
outro conecta nele. Funciona igual, sem internet e sem gastar dados.

Cada um escolhe o golpe no próprio celular; o turno só anda quando os dois escolheram. Não tem
poção (seria vantagem de quem tem mais moedas) e "Fugir" vira **Desistir**. Os dois ganham XP:
quem vence leva o XP cheio e moedas, quem perde leva um terço do XP.

Se a sala não aparecer na busca mas digitar o IP funciona, o roteador está com **isolamento de
clientes** ("AP isolation") ligado — comum em Wi-Fi de empresa, escola e rede de visitantes. Use o
hotspot de um dos celulares. Bluetooth e partida pela internet não são suportados.

## Android (APK)

```bash
cd samples/Aurora.Bichinhos.Android
dotnet build -c Release
```

Sai `bin/Release/net10.0-android/com.aurora.bichinhos-Signed.apk`, assinado com a chave de debug:
serve pra instalar e testar, não pra Play Store. Instalar com `adb install -r <apk>` (depuração
USB ligada) ou copiando o arquivo pro celular e abrindo (permitir "fontes desconhecidas").

O projeto Android não tem código de jogo: compila os `.cs` e empacota os `Assets/` desta pasta.

**Som no Android:** o áudio da engine é OpenAL e o Android não traz `libopenal.so`, então o projeto
referencia o pacote `MonoGame.Library.OpenAL` (e o exportador da engine faz o mesmo). Sem ele o jogo
roda mudo, sem erro nenhum. Se o jogo abrir sem som, procure no logcat a linha
`[Bichinhos] Áudio indisponível`. Esta parte ainda não foi testada num aparelho.

## Testar sem esperar

| Argumento | |
|---|---|
| `--rapido` | Relógio do bicho 60x mais rápido (1 s = 1 min): fome, cocô e sono sem esperar |
| `--save <pasta>` | Save em outra pasta, pra não mexer no seu bicho |
| `--demo <tela>` | Abre direto em `casa`, `noite`, `comer`, `lutar`, `ficha`, `jogos`, `roupas`, `batalha`, `brincar`, `pegar`, `presente`, `evolucao`, `escolha` ou `nasceu` com um bicho de exemplo. `casa:gotinha` troca a espécie |
| `--foto <arquivo.png>` | Grava a tela depois de ~1,5 s e fecha |
| `--duelo host` / `--duelo <ip>` | Cria a sala / entra na sala direto, sem passar pelos menus |
| `--robo` | Na batalha escolhe golpe sozinho e fecha no fim (testar duelo com duas janelas) |

Duelo com duas janelas no mesmo PC:

```bash
dotnet run --project samples/Aurora.Bichinhos -- --save /tmp/a --demo casa --duelo host --robo
dotnet run --project samples/Aurora.Bichinhos -- --save /tmp/b --demo casa:gotinha --duelo 127.0.0.1 --robo
```

```bash
dotnet run --project samples/Aurora.Bichinhos -- --save /tmp/bichos --demo batalha
```

O save fica em `bichinho.json` na pasta de saves da engine (`%LOCALAPPDATA%/AuroraBichinhos/saves`
no Windows). Regras em `tests/Aurora.Bichinhos.Tests`.

## Onde mexer

```
Assets/database/especies.json  espécies, estágios, atributos, quem aprende qual golpe, golpes
Game/                          regra pura: Bicho (necessidades, XP, evolução), Batalha, Catalogo, Progresso (save)
Rede/Duelo.cs                  duelo em rede (lockstep: host sorteia a semente, os dois simulam o mesmo turno)
Render/                        Tinta (painel, barra, botão, texto), BichoVisual (respira, pisca, dorme), Particulas
Telas/                         Escolha, Casa, Brincar, Batalha, Evolucao, Sala (criar/procurar/IP do duelo)
BichinhosGame.cs               boot, relógio, save automático, troca de tela
Arte/                          gerador dos sprites (Python + Pillow): python Arte/gerar_sprites.py
                               gerador dos sons (Python + numpy): python Arte/gerar_sons.py -> Assets/sounds/
                               itens vestíveis: Arte/itens.py (desenho) e Arte/gerar_ancoras.py (encaixe)
Assets/sounds/                 efeitos e músicas (WAV mono 16 bits); o teste confere que todo som citado no código existe
```

- **Balancear** é mexer no JSON: `NivelEvolucao`, `MultiplicadorEstagio`, `Base` de cada espécie.
  As taxas de fome/sono/sujeira por hora estão no topo de `Game/Bicho.cs`.
- **Espécie nova**: função em `Arte/bichos.py` (sprite de frente, pé em y≈90), entrada em
  `ESPECIES` do `gerar_sprites.py` e no `especies.json`. `Olhos` é onde a pálpebra fecha quando
  ele dorme ou pisca, em coordenadas 0..100 do sprite, e `Pele` a cor dela.
- **Texto**: a fonte cobre ASCII + Latin-1. Acento e "ç" funcionam; travessão e reticências de um
  caractere só viram "?".
- **Mexeu na regra da batalha** (dano, golpes, `especies.json`)? Suba `Duelo.Versao`: celulares com
  versões diferentes simulariam turnos diferentes, e assim eles avisam em vez de dessincronizar.
