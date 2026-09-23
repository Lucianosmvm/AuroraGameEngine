# Bichinhos

Bichinho virtual no estilo Tamagotchi, com nível, evolução e batalha por turnos no estilo Pokémon.
Retrato (720x1280), feito pra celular.

```bash
dotnet run --project samples/Aurora.Bichinhos
```

## Como joga

1. **Escolha um ovo**: Brasinha (Fogo), Gotinha (Água) ou Brotinho (Planta). O ovo choca na hora.
2. **Cuide**: as barras do alto caem com o tempo, **inclusive com o jogo fechado** (até 24 h contam).

   | Botão | O que faz |
   |---|---|
   | Comer | Refeição (grátis) mata a fome; Doce (3 moedas) dá alegria. Cheio, ele recusa |
   | Brincar | "Pra que lado ele vai?": 5 rodadas, acertar 3 dá alegria, XP e moedas |
   | Limpar | Tira os cocôs e dá banho. Cocô no chão derruba a higiene rápido |
   | Remédio | Cura a doença. Bicho maltratado (fome, sujeira, cocô) perde saúde e adoece |
   | Dormir | Apaga a luz; ele recupera energia e acorda sozinho quando descansou |
   | Batalhar | Mato (fácil), Campo (normal) ou Arena (difícil) contra um bicho selvagem |

   Tocar no bicho é carinho. O balão sobre a cabeça mostra o que ele mais precisa agora.
3. **Suba de nível**: cuidar dá um pouco de XP, batalha dá bem mais. Nível novo pode ensinar golpe.
4. **Evolua**: nível 6 vira a segunda forma e nível 14 a forma final (ver **Ficha**).

### Batalha

Por turnos: o mais rápido ataca primeiro. Cada bicho usa os 4 últimos golpes que aprendeu.
Fogo vence Planta, Planta vence Água, Água vence Fogo (2x); ao contrário é 0,5x; Normal é neutro.
Golpe do mesmo tipo do bicho bate 1,5x. O botão do golpe avisa "super eficaz" / "pouco eficaz".
Bicho **feliz e alimentado luta até 10% melhor**, e doente, dormindo, muito cansado ou faminto
não luta. Poção (10 moedas) cura metade da vida e gasta o turno.

Perder não mata: o bicho volta triste e machucado. Aqui ninguém morre.

## Android (APK)

```bash
cd samples/Aurora.Bichinhos.Android
dotnet build -c Release
```

Sai `bin/Release/net10.0-android/com.aurora.bichinhos-Signed.apk`, assinado com a chave de debug:
serve pra instalar e testar, não pra Play Store. Instalar com `adb install -r <apk>` (depuração
USB ligada) ou copiando o arquivo pro celular e abrindo (permitir "fontes desconhecidas").

O projeto Android não tem código de jogo: compila os `.cs` e empacota os `Assets/` desta pasta.

## Testar sem esperar

| Argumento | |
|---|---|
| `--rapido` | Relógio do bicho 60x mais rápido (1 s = 1 min): fome, cocô e sono sem esperar |
| `--save <pasta>` | Save em outra pasta, pra não mexer no seu bicho |
| `--demo <tela>` | Abre direto em `casa`, `noite`, `comer`, `lutar`, `ficha`, `batalha`, `brincar`, `evolucao` ou `escolha` com um bicho de exemplo. `casa:gotinha` troca a espécie |
| `--foto <arquivo.png>` | Grava a tela depois de ~1,5 s e fecha |

```bash
dotnet run --project samples/Aurora.Bichinhos -- --save /tmp/bichos --demo batalha
```

O save fica em `bichinho.json` na pasta de saves da engine (`%LOCALAPPDATA%/AuroraBichinhos/saves`
no Windows). Regras em `tests/Aurora.Bichinhos.Tests`.

## Onde mexer

```
Assets/database/especies.json  espécies, estágios, atributos, quem aprende qual golpe, golpes
Game/                          regra pura: Bicho (necessidades, XP, evolução), Batalha, Catalogo, Progresso (save)
Render/                        Tinta (painel, barra, botão, texto), BichoVisual (respira, pisca, dorme), Particulas
Telas/                         Escolha, Casa, Brincar, Batalha, Evolucao
BichinhosGame.cs               boot, relógio, save automático, troca de tela
Arte/                          gerador dos sprites (Python + Pillow): python Arte/gerar_sprites.py
```

- **Balancear** é mexer no JSON: `NivelEvolucao`, `MultiplicadorEstagio`, `Base` de cada espécie.
  As taxas de fome/sono/sujeira por hora estão no topo de `Game/Bicho.cs`.
- **Espécie nova**: função em `Arte/bichos.py` (sprite de frente, pé em y≈90), entrada em
  `ESPECIES` do `gerar_sprites.py` e no `especies.json`. `Olhos` é onde a pálpebra fecha quando
  ele dorme ou pisca, em coordenadas 0..100 do sprite, e `Pele` a cor dela.
- **Texto**: a fonte cobre ASCII + Latin-1. Acento e "ç" funcionam; travessão e reticências de um
  caractere só viram "?".
