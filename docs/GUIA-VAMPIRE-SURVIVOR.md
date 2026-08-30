# Guia passo a passo — jogo estilo Vampire Survivors na Aurora

Construção **do zero, em 14 passos**, de um survivor completo: menu, arena com ondas de inimigos,
arma que atira sozinha, gema de XP, subida de nível com escolha de melhoria, pausa, tela de
derrota e loja permanente entre partidas.

Cada passo acrescenta poucos arquivos e termina com **"rode agora e você deve ver…"** — inclusive
os passos em que o resultado certo é "aparece, mas errado", que é onde se entende por que a linha
seguinte existe. **Rode a cada passo.** Pular etapa transforma um erro pequeno e óbvio em três
erros misturados.

O resultado pronto está em [`samples/Aurora.Survivors/`](../samples/Aurora.Survivors/) — use pra
comparar quando algo não bater. Este guia é o "como se chega lá", com o porquê de cada decisão.

| Quando bater dúvida em… | Vá para |
|---|---|
| Assinatura exata de um componente/sistema | [REFERENCIA-SCRIPTS-RPG.md](REFERENCIA-SCRIPTS-RPG.md) |
| Menu, troca de cena, pausa, build, Android | [GUIA-JOGO-BASE.md](GUIA-JOGO-BASE.md), [GUIA-ANDROID.md](GUIA-ANDROID.md) |
| Golpe corpo-a-corpo e inimigo do zero | [TUTORIAL-ATAQUE-INIMIGO.md](TUTORIAL-ATAQUE-INIMIGO.md) |
| Sobrevivência com construção/craft/stamina | [GUIA-RPG-SURVIVOR.md](GUIA-RPG-SURVIVOR.md) |

### Índice

| | | |
|---|---|---|
| [0. O desenho do jogo](#0-o-desenho-do-jogo-leia-antes) | [5. HUD](#passo-5--hud) | [10. Menu, pausa e derrota](#passo-10--menu-pausa-e-derrota) |
| [1. Projeto e esqueleto](#passo-1--projeto-e-esqueleto) | [6. Inimigo](#passo-6--o-inimigo) | [11. Loja permanente e save](#passo-11--loja-permanente-e-save) |
| [2. Sprites](#passo-2--sprites-placeholder) | [7. Spawner e dificuldade](#passo-7--spawner-e-curva-de-dificuldade) | [12. Arma desbloqueável](#passo-12--uma-arma-que-se-desbloqueia) |
| [3. Arena e jogador](#passo-3--arena-e-jogador) | [8. Arma automática](#passo-8--a-arma-automática) | [13. Balanceamento](#passo-13--balanceamento-onde-mexer) |
| [4. Ficha de atributos](#passo-4--a-ficha-de-atributos) | [9. XP, moeda e level up](#passo-9--xp-moeda-e-subida-de-nível) | [14. Testar sem janela](#passo-14--testar-sem-abrir-a-janela) |
| | | [Checklist de erros](#checklist--por-que-não-funciona) |

---

## 0. O desenho do jogo (leia antes)

Um survivor é um jogo de números crescendo. Antes de escrever qualquer script, decida **onde cada
número mora** — é isso que decide se acrescentar uma habilidade nova depois vai custar uma linha
ou uma refatoração.

### As três camadas deste projeto

| Camada | Onde fica | Do que cuida |
|---|---|---|
| **Fluxo de telas** | `SurvivorsGame.cs` (o `Game`) | menu → loja → partida → level up → pausa → derrota |
| **Regras da partida** | `Game/*.cs` (C# comum) | curva de XP, catálogo de melhorias, preços da loja |
| **O que acontece na arena** | `Scripts/*.cs` (`[SceneScript]`) | jogador, arma, inimigo, spawner, coletável |

A regra que evita a maior parte dos erros: **componente nativo guarda dado, `Behavior` guarda
decisão, e o `Game` guarda o fluxo.**

### A ficha manda, o componente obedece

Todo número de balanceamento do jogador vive num único componente, o `PlayerStats`. Upgrade de
nível e melhoria da loja **só escrevem nele**. Quem copia esses valores pros componentes nativos
(`Health.Max`, `TopDownController.Speed`) é um script só, o `PlayerRunner`.

Sem essa regra, "+10% de velocidade" precisaria saber que existe um `TopDownController`, e
"+20 de vida" precisaria mexer em `Health` respeitando i-frames. Com ela, uma melhoria nova é uma
linha: `s => s.MoveSpeed *= 1.1f`.

### O contrato de dados

Fixe esta tabela no começo do projeto — é o único "banco de dados" do jogo, e é por esses nomes
que HUD, scripts e save conversam.

**Variáveis do `GameState`** (números globais, entram no save):

| Nome | Quem escreve | Quem lê |
|---|---|---|
| `Vida`, `VidaMax`, `VidaPct` | `PlayerRunner` (espelha o `Health`) | HUD (`UiBar`/`UiText`) |
| `Xp`, `XpPct` | `Pickup` (gema) e `RunManager` | HUD, `RunManager` |
| `Nivel`, `Tempo`, `Kills` | `RunManager` e `EnemyChaser` | HUD, tela de derrota |
| `MetaVida`, `MetaDano`, `MetaVelocidade`, `MetaColeta` | loja (`MetaShop`) | `MetaShop.AplicarEm` |

`Vida` é **espelho, não fonte**: quem manda na vida é o componente `Health` (por causa dos
i-frames e do `OnDeath`). O script só copia o valor pra variável, porque `UiBar` lê `GameState`,
não componente.

**Itens do `InventoryManager`** (contagem por nome, entra no save):

| Item | De onde vem | Pra que serve |
|---|---|---|
| `Moeda` | drop de inimigo | comprar melhoria permanente na loja |

**Etiquetas (`Tags`)** — é assim que se diz "isto é um inimigo" sem depender do nome:

| Etiqueta | Quem tem | Quem usa |
|---|---|---|
| `inimigo` | todo prefab de inimigo | `Projectile.TargetPrefix`, `ContactDamage.TargetPrefix`, a busca de alvo das armas |
| `jogador` | o Player | seus scripts futuros |

**Camadas de desenho (`Layer` do `SpriteRenderer`)**: `-10` chão · `5` itens caídos · `10`
jogador e inimigos · `11` projéteis e efeitos.

---

## Passo 1 — projeto e esqueleto

### 1.1 Criar o projeto

Pelo editor: **Arquivo → Novo Projeto…** (`Ctrl+Shift+N`), escolha a pasta e o nome. Ele já gera
csproj, `Program.cs`, a classe do `Game`, `aurora.project.json`, a fonte e uma cena de exemplo.

À mão, a estrutura mínima é esta (é o que o projeto pronto usa):

```
Aurora.Survivors/
  Aurora.Survivors.csproj      referencia src/Aurora.Runtime
  Program.cs                    cria o Game e roda
  SurvivorsGame.cs              a subclasse de Game (fluxo de telas)
  Game/                         regras em C# comum (sem componente)
  Scripts/                      os [SceneScript] da cena
  Assets/
    scenes/    cenas e telas de UI
    prefabs/   inimigo, tiro, gema, moeda, lâmina
    sprites/   PNGs
    fonts/DejaVuSans.ttf
  aurora.project.json
```

`Aurora.Survivors.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>Survivors</RootNamespace>
    <AssemblyName>Survivors</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Aurora.Runtime\Aurora.Runtime.csproj" />
  </ItemGroup>
  <ItemGroup>
    <None Include="Assets\**" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

`Program.cs`:

```csharp
using Survivors;

using var game = new SurvivorsGame();
game.ParseArgs(args);
game.Run("Aurora Survivors", 1280, 720);
```

### 1.2 A classe do jogo, versão mínima

`SurvivorsGame.cs` — por enquanto só abre uma janela e carrega uma cena:

```csharp
using Aurora.Runtime;
using Aurora.Runtime.Graphics;
using Silk.NET.Maths;

namespace Survivors;

public sealed class SurvivorsGame : Game
{
    private Font _font = null!;

    public SurvivorsGame()
    {
        GameName = "AuroraSurvivors";                        // pasta do save
        DesignResolution = new Vector2D<int>(1280, 720);      // ver aviso abaixo
        ClearColor = Color.FromBytes(12, 12, 20);
    }

    protected override void OnLoad()
    {
        _font = Assets.LoadFont("fonts/DejaVuSans.ttf", 22f);
        LoadScene(BootScene ?? "scenes/arena.json");
    }

    protected override void OnRenderUI(float deltaTime)
    {
        UI.Draw(SpriteBatch, _font, State, Inventory, Quests, ScreenSize.X, ScreenSize.Y);
        Dialogue.Draw(SpriteBatch, _font, ScreenSize.X, ScreenSize.Y);
    }
}
```

> **`DesignResolution` e o `aurora.project.json` têm que bater.** O editor desenha o preview da UI
> contra `designWidth`/`designHeight`; o jogo desenha contra o `DesignResolution`. Divergiu, um
> botão ancorado em `Center` cai num lugar no editor e em outro no jogo. Mesmo par de números nos
> dois lados, sempre:
>
> ```json
> { "gameProject": "D:/.../Aurora.Survivors.csproj",
>   "designWidth": 1280, "designHeight": 720,
>   "uiFont": "fonts/DejaVuSans.ttf", "uiFontSize": 22 }
> ```
>
> `uiFont`/`uiFontSize` também precisam bater com o `Assets.LoadFont(...)` do `OnLoad`: é com essa
> fonte que o editor mede o texto pra resolver a âncora.

**Rode agora:** `dotnet run --project samples/Aurora.Survivors`. Janela quase preta e um erro no
console dizendo que `scenes/arena.json` não existe — é o esperado, ainda não criamos a cena. O que
este passo prova é que a janela abre e a fonte carrega.

---

## Passo 2 — sprites placeholder

Você precisa de arte pra ver qualquer coisa: **o runtime não desenha placeholder**, sprite sem
textura é sprite invisível (e sem erro nenhum apontando o motivo).

Sete PNGs, todos minúsculos:

| Arquivo | Tamanho | O quê |
|---|---|---|
| `player.png` | 24×24 | o jogador |
| `morcego.png` | 22×16 | o inimigo |
| `tiro.png` | 10×10 | o projétil |
| `gema.png` | 12×12 | a gema de XP |
| `moeda.png` | 12×12 | a moeda |
| `lamina.png` | 18×18 | a lâmina orbital |
| `chao.png` | 64×32 | tileset do chão: **dois** tiles de 32×32 lado a lado |
| `fundo.png` | 320×180 | fundo do menu |

Use qualquer arte sua. O projeto de exemplo gera as dele por código (quadrados, discos e ruído) —
o que interessa aqui é a **convenção do tileset**: `chao.png` é uma fileira de tiles do mesmo
tamanho, e o índice do tile no `Tilemap` é a posição dele nessa fileira (0 = primeiro, 1 = segundo).

**Rode agora:** nada muda ainda. Só confira que os arquivos estão em `Assets/sprites/` e que o
`.csproj` copia `Assets\**` pra saída (senão o jogo compilado não acha nada em runtime).

---

## Passo 3 — arena e jogador

### 3.1 A cena

`Assets/scenes/arena.json` tem quatro entidades. Você pode montar tudo no editor (**+ Nova** →
**+Add Componente**) ou escrever o JSON direto — é o mesmo arquivo.

```json
{
  "Scene": "arena",
  "Objects": [
    { "Name": "Chao", "Components": [
        { "Type": "Transform", "X": -1024, "Y": -1024 },
        { "Type": "Tilemap", "Texture": "sprites/chao.png",
          "TileWidth": 32, "TileHeight": 32, "Width": 64, "Height": 64,
          "Layer": -10, "Tiles": [0, 1, 0, 0, 1, "…4096 índices…"] } ] },

    { "Name": "Player", "Components": [
        { "Type": "Transform", "X": 0, "Y": 0 },
        { "Type": "SpriteRenderer", "Texture": "sprites/player.png", "Layer": 10 },
        { "Type": "Collider", "Shape": "Circle", "Radius": 9, "Width": 18, "Height": 20 },
        { "Type": "Health", "Max": 100, "InvulnerabilityAfterHit": 0.35, "DestroyOnDeath": false },
        { "Type": "Tags", "Value": "jogador" },
        { "Type": "TopDownController", "Speed": 145, "Movement": "Free" } ] },

    { "Name": "Camera", "Components": [
        { "Type": "Transform", "X": 0, "Y": 0 },
        { "Type": "CameraController", "Follow": "Player", "FollowSpeed": 7, "Zoom": 1,
          "ClampBounds": true, "BoundsX": -1024, "BoundsY": -1024,
          "BoundsWidth": 2048, "BoundsHeight": 2048 } ] }
  ]
}
```

Três detalhes que valem o parágrafo:

- **O `Transform` do `Tilemap` é o canto superior esquerdo do mapa**, não o centro. 64 tiles de
  32px = 2048px de lado, então `-1024, -1024` centraliza o mapa em `(0,0)`. Se você mover essa
  entidade sem querer no editor, o chão sai de baixo dos limites da câmera e do jogador — e o
  sintoma é "tem uma faixa preta na borda do mapa".
- **`DestroyOnDeath: false` no jogador.** O padrão do `Health` é destruir a entidade ao chegar a
  zero. Se o jogador sumir, não dá pra ler nada dele na tela de derrota — e vários scripts que
  fazem `TryFind("Player")` passam a falhar em silêncio.
- **`ClampBounds` na câmera** prende a visão dentro do mapa, então nunca aparece o vazio fora do
  chão.

Não vale a pena escrever 4096 índices à mão: pinte no editor (painel de tiles), ou gere o array
com um script de 3 linhas em qualquer linguagem. No projeto de exemplo, 75% de tile 0 e 25% de
tile 1, sorteado.

### 3.2 O jogador anda de graça

Repare que **não há script de movimento**. `TopDownController` é nativo: lê WASD/setas, analógico
do gamepad e (mais pra frente) o joystick de toque, normaliza a diagonal, vira o sprite e ainda
alimenta o `Animator` se houver. `Movement: "Free"` é o modo de direção contínua, que é o de
survivor/roguelike.

**Rode agora:** você anda pelo mapa com WASD e a câmera segue, com o chão desenhado embaixo. Se o
jogador não aparecer, é textura errada (veja o console) ou `Layer` do chão maior que o do player.

---

## Passo 4 — a ficha de atributos

### 4.1 `Scripts/PlayerStats.cs` — só dados

```csharp
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Scenes;

namespace Survivors;

[SceneScript]
public sealed class PlayerStats : Behavior
{
    public float MaxHealth = 100f;
    public float MoveSpeed = 145f;
    public float DamageMultiplier = 1f;      // multiplica o dano de toda arma
    public float FireRateMultiplier = 1f;    // multiplica a cadência de toda arma
    public float ProjectileSpeed = 340f;
    public int ProjectileCount = 1;          // projéteis por disparo (leque)
    public float PickupRadius = 75f;
    public float Armor;                      // fração do dano anulada (0..0,8)
    public float XpMultiplier = 1f;
    public float RegenPerSecond;
    public int OrbitBlades;                  // 0 = arma orbital dormindo
    public float OrbitDamage = 9f;
}
```

Três requisitos do discovery de script, e o motivo de cada um:

1. **`[SceneScript]`** — é o que faz o editor listar o script no "+Add Componente" e o runtime
   registrá-lo no serializador de cena (`Game.AutoRegisterScripts` varre o assembly). Sem o
   atributo, o componente no JSON vira um aviso no console e é ignorado.
2. **`sealed` e construtor sem parâmetro** — o editor instancia a classe pra ler os valores padrão.
3. **Só `float`, `int`, `bool`, `string`** viram campo editável. `Vector2`, enum, lista e
   `Entity` continuam funcionando em C#, mas não aparecem no Inspector nem são salvos na cena.

### 4.2 `Scripts/PlayerRunner.cs` — a cola

Ficha sem ninguém que a leia é número morto. Este script é o único ponto onde os atributos viram
comportamento:

```csharp
using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Scenes;

namespace Survivors;

[SceneScript]
public sealed class PlayerRunner : Behavior
{
    public float ArenaHalfWidth = 1000f;
    public float ArenaHalfHeight = 1000f;

    public override void Start()
    {
        var stats = Get<PlayerStats>();
        if (stats is null || World?.State is null) return;

        MetaShop.AplicarEm(stats, World.State);   // bônus da loja (Passo 11) — antes da vida

        if (Get<Health>() is { } health)
        {
            health.Max = stats.MaxHealth;
            health.Current = stats.MaxHealth;
        }
    }

    public override void Update(float deltaTime)
    {
        var stats = Get<PlayerStats>();
        if (stats is null || World is null) return;

        if (Get<TopDownController>() is { } controller)
            controller.Speed = stats.MoveSpeed;

        if (Get<Health>() is { } health)
        {
            if (stats.RegenPerSecond > 0f && health.Current < health.Max)
                World.Heal(Entity, stats.RegenPerSecond * deltaTime);

            World.State?.SetVariable("Vida", MathF.Round(health.Current));
            World.State?.SetVariable("VidaMax", MathF.Round(health.Max));
            World.State?.SetVariable("VidaPct",
                health.Max > 0f ? health.Current / health.Max * 100f : 0f);
        }

        if (Get<Transform>() is { } transform)
            transform.Position = new Vector2(
                Math.Clamp(transform.Position.X, -ArenaHalfWidth, ArenaHalfWidth),
                Math.Clamp(transform.Position.Y, -ArenaHalfHeight, ArenaHalfHeight));
    }

    // Armadura: devolve a fração blindada do dano JÁ aplicado.
    public override void OnDamaged(float amount, Entity? source)
    {
        var stats = Get<PlayerStats>();
        if (stats is null || stats.Armor <= 0f) return;
        World?.Heal(Entity, amount * Math.Clamp(stats.Armor, 0f, 0.8f));
    }
}
```

> **Por que a armadura cura em vez de reduzir?** `World.Damage` não tem gancho de "antes do dano".
> Reduzir de verdade exigiria reimplementar dano, i-frames, `OnDamaged` e `OnDeath` no seu script.
> Curar a fração blindada no mesmo frame dá exatamente o mesmo saldo de vida e mantém knockback,
> i-frames e morte automática nativos funcionando. Se um dia você quiser mostrar o número do dano
> na tela, é aqui que ele passa.

Acrescente os dois componentes na entidade `Player` da cena:

```json
{ "Type": "PlayerStats" },
{ "Type": "PlayerRunner", "ArenaHalfWidth": 1000, "ArenaHalfHeight": 1000 }
```

**Rode agora:** nada visível muda — mas o jogador já não sai do chão (tente andar até a borda), e
as variáveis `Vida`/`VidaPct` já existem. O próximo passo mostra elas na tela.

> Se `MetaShop` ainda não existe, comente a linha do `Start` e volte nela no Passo 11.

---

## Passo 5 — HUD

Tela de UI é um JSON **no mesmo formato de cena**, com `"UI": true` e componentes `Ui*`. Ela vive
fora do `World`: `LoadScene` não a apaga, e ela não segue a câmera.

`Assets/scenes/Hud.json` (resumido — o arquivo completo está no projeto de exemplo):

```json
{ "Scene": "Hud", "UI": true, "Objects": [
  { "Name": "BarraVida", "Components": [
      { "Type": "UiBar", "X": 20, "Y": 20, "Width": 320, "Height": 20,
        "Variable": "VidaPct", "Max": 100, "FillColor": "#C83C3CFF", "BackColor": "#0A0A12CC" } ] },
  { "Name": "TextoVida", "Components": [
      { "Type": "UiText", "X": 30, "Y": 20, "Text": "", "Color": "#FFE8E8FF" } ] },
  { "Name": "BarraXp", "Components": [
      { "Type": "UiBar", "X": 20, "Y": 46, "Width": 320, "Height": 10,
        "Variable": "XpPct", "Max": 100, "FillColor": "#4CC8F0FF" } ] },
  { "Name": "TextoTempo", "Components": [
      { "Type": "UiText", "X": 0, "Y": 18, "AnchorX": "Center", "Text": "00:00", "Scale": 1.4 } ] },
  { "Name": "TextoKills", "Components": [
      { "Type": "UiText", "X": 20, "Y": 18, "AnchorX": "Right", "Text": "Mortes: {Kills}" } ] },
  { "Name": "TextoMoedas", "Components": [
      { "Type": "UiText", "X": 20, "Y": 46, "AnchorX": "Right", "Text": "Moedas: {Item:Moeda}" } ] },
  { "Name": "Joystick", "Components": [
      { "Type": "UiJoystick", "X": 40, "Y": 40, "AnchorX": "Left", "AnchorY": "Bottom",
        "Radius": 86, "BaseColor": "#FFFFFF22", "KnobColor": "#FFFFFF55" } ] }
] }
```

Duas coisas que economizam código:

- **Tokens do `UiText`** resolvidos a cada frame: `{Kills}` é variável do `GameState`,
  `{Item:Moeda}` é contagem de inventário, `{Quest:X}` é estágio de quest. Nada disso precisa de
  script.
- **`UiBar` lê variável**, não componente — é a razão do `PlayerRunner` espelhar `VidaPct`.

O que **não** dá pra fazer por token é texto formatado (um relógio `03:47`, ou `45 / 120`): a
variável vira número cru. Esses o código escreve, no `Game` (Passo 10).

Carregue a tela no `OnLoad`:

```csharp
UI.Load("scenes/Hud.json", Assets);
```

E ligue o joystick de toque no controlador, na cena — o mesmo componente serve desktop e celular:

```json
{ "Type": "TopDownController", "Speed": 145, "Movement": "Free",
  "JoystickScreen": "Hud", "JoystickName": "Joystick" }
```

> `JoystickScreen` é o **id da tela** (nome do arquivo sem `.json`), e `JoystickName` é o **nome da
> entidade** dentro dela — não o nome do componente. É o mesmo par que o `UI.Find<T>(tela, nome)`
> usa.

**Rode agora:** barra de vida cheia no canto, barra de XP vazia, contadores zerados e o joystick
desenhado embaixo à esquerda (arrastando com o mouse ele funciona também). Vida e XP ainda não
mudam — falta quem os machuque e quem dê XP.

> **Armadilha clássica:** `Ui*` só funciona em arquivo de **tela** (`"UI": true`, carregado com
> `UI.Load`). Se você colar um `UiText` numa cena de gameplay, o console avisa
> `Componente 'UiText' … não registrado — ignorado` e nada aparece. Se um HUD sumiu, é o primeiro
> lugar pra olhar.

---

## Passo 6 — o inimigo

### 6.1 O prefab

Prefab é uma entidade salva sozinha num arquivo: `{ "Name": ..., "Components": [...] }`, sem
`"Objects"`. `Assets/prefabs/morcego.json`:

```json
{ "Name": "Morcego", "Components": [
  { "Type": "Transform", "X": 0, "Y": 0 },
  { "Type": "SpriteRenderer", "Texture": "sprites/morcego.png", "Layer": 10 },
  { "Type": "Collider", "Shape": "Circle", "Radius": 9, "Width": 18, "Height": 14, "IsSolid": true },
  { "Type": "Health", "Max": 16 },
  { "Type": "Tags", "Value": "inimigo" },
  { "Type": "ContactDamage", "Damage": 8, "Interval": 0.8, "TargetPrefix": "Player", "Knockback": 8 },
  { "Type": "EnemyChaser", "Speed": 58, "Xp": 1, "CoinChance": 0.07 }
] }
```

- **`Tags: "inimigo"` é obrigatório.** É por essa etiqueta que as armas miram e o spawner conta.
  Sem ela o bicho fica invencível e invisível pro resto do jogo — e não há erro nenhum avisando.
- **`ContactDamage` é nativo**: machuca quem encostar, repetindo a cada `Interval`. O
  `TargetPrefix: "Player"` impede que um morcego machuque o outro ao esbarrar.
- **Collider sólido** faz os inimigos se empurrarem e formarem massa em volta do jogador, que é
  metade da sensação do gênero.
- A `Health` fica baixa (16) porque o inimigo é descartável: o desafio vem da quantidade.

### 6.2 `Scripts/EnemyChaser.cs`

```csharp
using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Scenes;

namespace Survivors;

[SceneScript]
public sealed class EnemyChaser : Behavior
{
    public float Speed = 58f;
    public string TargetName = "Player";
    public float Xp = 1f;
    public float CoinChance = 0.07f;
    public string XpPrefab = "prefabs/gema.json";
    public string CoinPrefab = "prefabs/moeda.json";

    public override void Update(float deltaTime)
    {
        if (World is null || Get<Transform>() is not { } transform) return;
        if (!World.TryFind(TargetName, out var alvo)
            || alvo.Get<Transform>() is not { } destino) return;

        var direcao = destino.Position - transform.Position;
        if (direcao.LengthSquared() <= 1f) return;

        direcao = Vector2.Normalize(direcao);
        transform.Position += direcao * Speed * deltaTime;

        if (Get<SpriteRenderer>() is { } sprite)
            sprite.FlipX = direcao.X < 0f;
    }

    public override void OnDeath()      // roda ANTES da entidade ser destruída
    {
        if (World is null || Get<Transform>() is not { } transform) return;

        World.State?.AddVariable("Kills", 1f);

        if (World.Spawn(XpPrefab, transform.Position) is { } gema
            && gema.Get<Pickup>() is { } pickup)
            pickup.Value = Xp;

        if (CoinChance > 0f && Random.Shared.NextSingle() < CoinChance)
            World.Spawn(CoinPrefab, transform.Position);
    }
}
```

Duas decisões de projeto aqui:

- **Cada inimigo acha o alvo sozinho** (`World.TryFind` no próprio `Update`). Não existe um
  gerente passando a referência do jogador pra cada bicho — funciona igual com 1 ou 200 cópias.
- **Movimento reto, não `NavAgent`.** A arena não tem parede; pathfinding por bicho, vezes 150
  bichos, seria o custo mais caro do jogo à toa. Se um dia você puser paredes, troque as três
  linhas de movimento por `Get<NavAgent>()?.SetTarget(destino.Position)`.

E o motivo de o loot cair no `OnDeath` e não em outro lugar: o `World.Damage` dispara `OnDeath`
**com a entidade ainda de pé**, e só depois destrói. É onde a posição do drop ainda existe.

**Rode agora:** ponha um morcego direto na cena (copie os componentes do prefab pra uma entidade
`Morcego` em `arena.json`) e veja ele vir até você e tirar vida — a barra do HUD desce. Ainda não
dá pra revidar, então morrer é o resultado esperado. Tire o morcego da cena depois: no passo
seguinte quem faz nascer é o spawner.

---

## Passo 7 — spawner e curva de dificuldade

`Scripts/EnemySpawner.cs` — a dificuldade do jogo inteiro mora neste componente:

```csharp
[SceneScript]
public sealed class EnemySpawner : Behavior
{
    public string Prefab = "prefabs/morcego.json";
    public string TargetName = "Player";
    public float StartAfterSeconds;        // escalonar tipos de inimigo
    public float StartInterval = 1.1f;     // segundos entre nascimentos no começo
    public float MinInterval = 0.18f;      // piso
    public float IntervalHalfLife = 75f;   // a cada 75s o intervalo cai pela metade
    public float SpawnDistance = 780f;     // fora da tela (ver aviso)
    public int MaxAlive = 160;             // teto de inimigos vivos
    public float HealthPerMinute = 0.5f;   // +50% de vida por minuto
    public float SpeedPerMinute = 0.06f;
    public float HordeEvery = 45f;         // anel inteiro de uma vez
    public int HordeAmount = 18;

    public float Elapsed { get; private set; }
    private float _proximoSpawn;
    private int _hordasFeitas;

    public override void Update(float deltaTime)
    {
        if (World is null) return;
        Elapsed += deltaTime;
        if (Elapsed < StartAfterSeconds) return;

        if (HordeEvery > 0f && Elapsed >= (_hordasFeitas + 1) * HordeEvery)
        {
            _hordasFeitas++;
            for (int i = 0; i < HordeAmount; i++)
                Nascer(MathF.Tau * i / HordeAmount);
        }

        _proximoSpawn -= deltaTime;
        if (_proximoSpawn > 0f) return;

        _proximoSpawn = IntervaloAtual();
        Nascer(Random.Shared.NextSingle() * MathF.Tau);
    }

    private float IntervaloAtual()
    {
        float meiasVidas = Elapsed / MathF.Max(1f, IntervalHalfLife);
        return MathF.Max(MinInterval, StartInterval * MathF.Pow(0.5f, meiasVidas));
    }

    private void Nascer(float angulo)
    {
        if (World is null || !World.TryFind(TargetName, out var jogador)
            || jogador.Get<Transform>() is not { } centro) return;

        if (ContarVivos() >= MaxAlive) return;

        var posicao = centro.Position
            + new Vector2(MathF.Cos(angulo), MathF.Sin(angulo)) * SpawnDistance;

        if (World.Spawn(Prefab, posicao) is not { } inimigo) return;

        float minutos = Elapsed / 60f;
        if (inimigo.Get<Health>() is { } vida)
        {
            vida.Max *= 1f + HealthPerMinute * minutos;
            vida.Current = vida.Max;
        }
        if (inimigo.Get<EnemyChaser>() is { } perseguidor)
            perseguidor.Speed *= 1f + SpeedPerMinute * minutos;
    }

    private int ContarVivos()
    {
        int total = 0;
        foreach (var (entity, _) in World!.Query<Health>())
            if (Tags.Matches(entity, "#inimigo")) total++;
        return total;
    }
}
```

Três pontos que decidem se o jogo é jogável:

- **`SpawnDistance` tem que passar da meia-diagonal da tela.** Em 1280×720 com zoom 1 isso é
  `√(640² + 360²) ≈ 734px`. Com 520, o inimigo nasce **dentro** do campo de visão nos cantos e o
  jogador vê o bicho aparecer do nada. 780 resolve.
- **Meia-vida, não subtração.** `intervalo × 0,5^(t/75)` cai suave e nunca chega a zero (o
  `MinInterval` é o piso). Um `intervalo -= 0,01` por segundo vira "impossível de uma hora pra
  outra" e depois negativo.
- **`ContarVivos` só roda na hora de nascer**, nunca todo frame. Varrer a cena 60 vezes por
  segundo pra saber quantos morcegos existem seria o custo mais caro do jogo — e o `MaxAlive` é
  justamente o que segura o FPS na partida longa.

Na cena, uma entidade `Diretor` só com `Transform` + `EnemySpawner`.

**Rode agora:** morcegos começam a nascer em volta, um a cada ~1s, e vêm por todos os lados. Aos
45s vem o primeiro anel de 18. Você continua sem poder revidar — próximo passo.

> **Mais de um tipo de inimigo, depois:** ou você duplica o `Diretor` com outro `Prefab` e outro
> `StartAfterSeconds` (o morcego desde o início, o esqueleto só a partir de 2 minutos), ou cadastra
> uma **tabela de spawn** em `Assets/database/spawns.json` e põe o id dela no campo `Prefab` — onde
> se escreve um prefab também se pode escrever uma tabela, e a engine sorteia por peso e condição
> sozinha.

---

## Passo 8 — a arma automática

### 8.1 Quem é o alvo

`Game/Alvos.cs` — busca compartilhada por todas as armas, mirando **por etiqueta**, não por tipo
de script. É isso que faz uma arma já funcionar com qualquer inimigo novo:

```csharp
public static class Alvos
{
    public static bool MaisProximo(World world, Vector2 origem, float alcance, string etiqueta,
        out Entity alvo, out Vector2 posicao)
    {
        alvo = default; posicao = default;
        float melhor = alcance * alcance;
        bool achou = false;

        foreach (var (entity, health) in world.Query<Health>())
        {
            if (health.IsDead || !Tags.Matches(entity, etiqueta)) continue;
            if (entity.Get<Transform>() is not { } transform) continue;

            float distancia = Vector2.DistanceSquared(origem, transform.Position);
            if (distancia > melhor) continue;

            melhor = distancia; alvo = entity; posicao = transform.Position; achou = true;
        }
        return achou;
    }
}
```

`DistanceSquared` em vez de `Distance`: comparar quadrados dá a mesma ordem e evita uma raiz
quadrada por inimigo, por arma, por frame.

### 8.2 O projétil

`Assets/prefabs/tiro.json`:

```json
{ "Name": "Tiro", "Components": [
  { "Type": "Transform", "X": 0, "Y": 0 },
  { "Type": "SpriteRenderer", "Texture": "sprites/tiro.png", "Layer": 11 },
  { "Type": "Collider", "Shape": "Circle", "Radius": 5, "Width": 10, "Height": 10, "IsSolid": false },
  { "Type": "Projectile", "Life": 1.6, "Damage": 12, "TargetPrefix": "#inimigo" }
] }
```

`Projectile` é nativo e já traz a lógica pronta: anda na direção da `Velocity`, aplica dano em
quem tocar e se destrói (no toque ou quando a `Life` acaba). **`IsSolid: false` é obrigatório** —
com collider sólido ele empurraria o inimigo fisicamente em vez de disparar o `OnTriggerEnter` que
causa o dano.

### 8.3 `Scripts/WeaponAutoShoot.cs`

```csharp
[SceneScript]
public sealed class WeaponAutoShoot : Behavior
{
    public string Prefab = "prefabs/tiro.json";
    public float Interval = 0.75f;
    public float Damage = 12f;
    public float Range = 620f;
    public float SpreadDegrees = 14f;
    public string TargetTag = "#inimigo";
    public float MuzzleDistance = 16f;

    private float _cooldown;

    public override void Update(float deltaTime)
    {
        if (World is null || Get<Transform>() is not { } transform) return;

        var stats = Get<PlayerStats>();
        float cadencia = MathF.Max(0.05f, stats?.FireRateMultiplier ?? 1f);

        _cooldown -= deltaTime * cadencia;
        if (_cooldown > 0f) return;

        if (!Alvos.MaisProximo(World, transform.Position, Range, TargetTag, out _, out var alvo))
            return;

        var direcao = alvo - transform.Position;
        if (direcao.LengthSquared() <= 0.0001f) return;
        direcao = Vector2.Normalize(direcao);
        _cooldown = Interval;

        int quantidade = Math.Max(1, stats?.ProjectileCount ?? 1);
        float anguloBase = MathF.Atan2(direcao.Y, direcao.X);
        float passo = SpreadDegrees * MathF.PI / 180f;
        float inicio = anguloBase - passo * (quantidade - 1) / 2f;   // leque centrado na mira

        for (int i = 0; i < quantidade; i++)
        {
            float angulo = inicio + passo * i;
            Disparar(new Vector2(MathF.Cos(angulo), MathF.Sin(angulo)), transform.Position, stats);
        }
    }

    private void Disparar(Vector2 direcao, Vector2 origem, PlayerStats? stats)
    {
        if (World?.Spawn(Prefab, origem + direcao * MuzzleDistance) is not { } tiro) return;

        if (tiro.Get<Projectile>() is { } projectile)
        {
            projectile.Velocity = direcao * (stats?.ProjectileSpeed ?? 340f);
            projectile.Damage = Damage * (stats?.DamageMultiplier ?? 1f);
            projectile.Source = Entity;          // não acerta quem atirou
            projectile.TargetPrefix = TargetTag;
        }
        if (tiro.Get<Transform>() is { } transform)
            transform.Rotation = MathF.Atan2(direcao.Y, direcao.X);
    }
}
```

- **`Velocity`, `Source` e `Damage` são preenchidos no spawn**, não no arquivo: são justamente os
  três dados que não cabem num prefab estático (direção, dono e o dano já multiplicado pela ficha).
- **`Range` perto da meia-largura da tela (640px em 1280×720).** Alcance muito menor que isso
  deixa a arma calada com inimigo visível na tela — parece bug. Muito maior, o jogador atira em
  quem ele nem vê.
- O `cooldown` desce mais rápido conforme a cadência sobe. Foi por isso que a ficha guarda
  *multiplicador* e não *intervalo*: "+18% de cadência" soma bonito, "−0,1s de intervalo" vira
  disparo infinito lá pelo sexto upgrade.

Acrescente o componente na entidade `Player`.

**Rode agora:** o jogo virou jogo. Você anda, a arma mira sozinha no morcego mais próximo e os
bichos morrem. Ainda não há progressão — a cada minuto os inimigos engrossam e a sua arma não.

---

## Passo 9 — XP, moeda e subida de nível

### 9.1 O que cai no chão

`Scripts/Pickup.cs` — gema e moeda são o mesmo script, mudando o `Kind`:

```csharp
[SceneScript]
public sealed class Pickup : Behavior
{
    public string Kind = "Xp";          // "Xp" ou "Moeda"
    public float Value = 1f;
    public float Speed = 320f;
    public float CollectRadius = 14f;
    public string TargetName = "Player";

    public override void Update(float deltaTime)
    {
        if (World is null || Get<Transform>() is not { } transform) return;
        if (!World.TryFind(TargetName, out var jogador)
            || jogador.Get<Transform>() is not { } destino) return;

        var delta = destino.Position - transform.Position;
        float distancia = delta.Length();

        if (distancia <= CollectRadius) { Coletar(jogador); return; }

        float raio = jogador.Get<PlayerStats>()?.PickupRadius ?? 70f;
        if (distancia > raio) return;

        // acelera conforme chega perto: vira ímã, não arrasto linear
        float atracao = Speed * (1f + (1f - distancia / raio));
        transform.Position += delta / distancia * atracao * deltaTime;
    }

    private void Coletar(Entity jogador)
    {
        if (Kind.Equals("Moeda", StringComparison.OrdinalIgnoreCase))
            World?.Inventory?.Add("Moeda", Math.Max(1, (int)Value));
        else
            World?.State?.AddVariable("Xp",
                Value * (jogador.Get<PlayerStats>()?.XpMultiplier ?? 1f));

        Entity.Destroy();
    }
}
```

**Sem collider de propósito.** Coleta por distância é mais barata que colisão, não disputa camada
com nada e o "ímã" precisa da distância de qualquer jeito. Colisão aqui só traria dois problemas
novos (máscara e ordem de trigger) pra resolver o que uma subtração já resolve.

Os prefabs (`gema.json` e `moeda.json`) são `Transform` + `SpriteRenderer` + `Pickup` +
`Lifetime` (60s). O `Lifetime` existe pra gema não coletada não virar lixo eterno na partida de
20 minutos.

### 9.2 A curva de XP

`Game/RunManager.cs` guarda o estado de UMA partida. É C# comum: não conhece entidade nem cena, só
números e a ficha — então dá pra mexer e testar sem abrir o jogo.

```csharp
public sealed class RunManager
{
    private readonly Dictionary<string, int> _niveis = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Upgrade> _opcoes = [];

    public float Tempo { get; private set; }
    public int Nivel { get; private set; } = 1;
    public float XpProximo { get; private set; } = 6f;
    public int MoedasNoInicio { get; private set; }
    public IReadOnlyList<Upgrade> Opcoes => _opcoes;

    public void Iniciar(GameState state, InventoryManager inventario)
    {
        _niveis.Clear(); _opcoes.Clear();
        Tempo = 0f; Nivel = 1; XpProximo = XpParaNivel(1);
        MoedasNoInicio = inventario.GetCount("Moeda");

        state.SetVariable("Xp", 0f);   state.SetVariable("XpPct", 0f);
        state.SetVariable("Nivel", 1f); state.SetVariable("Tempo", 0f);
        state.SetVariable("Kills", 0f);
    }

    public void Update(float deltaTime, GameState state)
    {
        Tempo += deltaTime;
        state.SetVariable("Tempo", MathF.Floor(Tempo));
        float xp = state.GetVariable("Xp");
        state.SetVariable("XpPct", XpProximo > 0f
            ? Math.Clamp(xp / XpProximo * 100f, 0f, 100f) : 0f);
    }

    public bool PodeSubirDeNivel(GameState state) => state.GetVariable("Xp") >= XpProximo;

    public void AbrirNivel(GameState state, int quantidadeDeOpcoes = 3)
    {
        state.SetVariable("Xp", MathF.Max(0f, state.GetVariable("Xp") - XpProximo));
        Nivel++;
        XpProximo = XpParaNivel(Nivel);
        state.SetVariable("Nivel", Nivel);
        Sortear(quantidadeDeOpcoes);
    }

    public Upgrade? Escolher(int indice, PlayerStats stats)
    {
        if (indice < 0 || indice >= _opcoes.Count) return null;
        var upgrade = _opcoes[indice];
        upgrade.Aplicar(stats);
        _niveis[upgrade.Id] = NivelDe(upgrade.Id) + 1;
        _opcoes.Clear();
        return upgrade;
    }

    public int NivelDe(string id) => _niveis.TryGetValue(id, out int n) ? n : 0;

    private static float XpParaNivel(int nivel)
        => MathF.Round(6f + (nivel - 1) * 5f + nivel * nivel * 0.4f);

    private void Sortear(int quantidade)
    {
        _opcoes.Clear();
        _opcoes.AddRange(UpgradeCatalog.Todos
            .Where(u => NivelDe(u.Id) < u.MaxNivel)
            .OrderBy(_ => Random.Shared.Next())
            .Take(quantidade));
    }
}
```

O nível sobe em `AbrirNivel` (na hora de mostrar a tela), não em `Escolher`. Se subisse na escolha,
uma horda morrendo junto abriria a mesma tela várias vezes seguidas com o mesmo nível e o mesmo
XP — o sorteio ficaria travado.

### 9.3 O catálogo de melhorias

`Game/UpgradeCatalog.cs`. Uma melhoria é: id, nome, texto, teto e **uma linha que mexe na ficha**.

```csharp
public sealed class Upgrade
{
    public required string Id { get; init; }
    public required string Nome { get; init; }
    public required string Descricao { get; init; }
    public int MaxNivel { get; init; } = 5;
    public required Action<PlayerStats> Aplicar { get; init; }
}

public static class UpgradeCatalog
{
    public static readonly IReadOnlyList<Upgrade> Todos =
    [
        new() { Id = "dano", Nome = "Lâmina Afiada", Descricao = "+20% de dano em todas as armas",
                MaxNivel = 8, Aplicar = s => s.DamageMultiplier += 0.20f },
        new() { Id = "cadencia", Nome = "Gatilho Rápido", Descricao = "+18% de velocidade de ataque",
                MaxNivel = 8, Aplicar = s => s.FireRateMultiplier += 0.18f },
        new() { Id = "projetil", Nome = "Projétil Extra", Descricao = "+1 projétil por disparo",
                MaxNivel = 4, Aplicar = s => s.ProjectileCount += 1 },
        // …botas, vida, couraça, regeneração, ímã, sabedoria, balística, orbital
    ];
}
```

**Acrescentar uma habilidade nova é acrescentar um item nesta lista.** O sorteio, a tela e a
contagem de nível já funcionam. Se o efeito precisar de um atributo que não existe, crie o campo
em `PlayerStats` e leia ele onde importa.

**Rode agora:** matar morcego larga gema, a gema voa até você quando chega perto e a barra de XP
enche. Ao encher… nada acontece ainda: falta a tela. Próximo passo.

---

## Passo 10 — menu, pausa e derrota

### 10.1 A decisão de arquitetura mais importante do projeto

Subir de nível precisa **congelar o jogo**. A engine tem isso pronto: `World.Paused = true` para
Behaviors, colisão, partículas e vida, e a cena continua desenhada atrás.

Só que **mundo congelado não roda Behavior** — inclusive o seu. Um script de cena que abrisse a
tela de escolha nunca conseguiria ler o botão que descongela: ele estaria congelado junto. É por
isso que, neste projeto, **todo o fluxo de telas mora no `OnUpdate` do `Game`**, que roda sempre:

```
HandleUpdate (a cada frame, pela engine):
    SceneManager.Update  →  Dialogue.Update  →  OnUpdate (seu Game, sempre roda)
    →  World.Update (pula tudo se Paused)  →  Events.Update  →  UI.Update  →  câmera
```

### 10.2 A máquina de estados

```csharp
private enum Estado { Menu, Loja, Jogando, SubindoNivel, Pausa, Morto }

protected override void OnUpdate(float deltaTime)
{
    switch (_estado)
    {
        case Estado.Menu:        AtualizarMenu(); break;
        case Estado.Loja:        AtualizarLoja(); break;
        case Estado.Jogando:     AtualizarPartida(deltaTime); break;
        case Estado.SubindoNivel: AtualizarEscolha(); break;
        case Estado.Pausa:       AtualizarPausa(); break;
        case Estado.Morto:       AtualizarMorte(); break;
    }
}
```

E a partida:

```csharp
private void AtualizarPartida(float deltaTime)
{
    if (!World.TryFind("Player", out var jogador)) return;

    _run.Update(deltaTime, State);
    AtualizarHud();

    if (jogador.Get<Health>() is not { } vida || vida.IsDead) { Morrer(); return; }
    if (Input.WasKeyPressed(Key.Escape))                       { Pausar(); return; }
    if (_run.PodeSubirDeNivel(State))                            AbrirLevelUp();
}

private void AtualizarHud()
{
    int tempo = (int)_run.Tempo;
    Texto("Hud", "TextoTempo", $"{tempo / 60:00}:{tempo % 60:00}");
    Texto("Hud", "TextoNivel", $"Nível {_run.Nivel}");
    Texto("Hud", "TextoVida",
        $"{(int)State.GetVariable("Vida")} / {(int)State.GetVariable("VidaMax")}");
}
```

`Morrer()` funciona porque o jogador tem `DestroyOnDeath: false` (Passo 3): a entidade continua
existindo com `IsDead == true`, e dá pra ler o resultado da partida dela.

### 10.3 Botão que chama método seu

O `OnClick` do `UiButton` roda o vocabulário de ações da engine (`ChangeScene`, `ShowUI`,
`SetPause`…), que resolve o caso comum sem código. O que ele **não** faz é chamar um método
específico do seu script — e é disso que a escolha de melhoria precisa. Pra isso existe o estado
`Clicked`, lido a cada frame:

```csharp
private bool Clicou(string tela, string botao)
    => UI.Find<UiButton>(tela, botao) is { Clicked: true };

private void Texto(string tela, string elemento, string valor)
{
    if (UI.Find<UiText>(tela, elemento) is { } texto) texto.Text = valor;
}
```

`Find<T>(tela, elemento)` usa o **id da tela** (arquivo sem `.json`) e o **nome da entidade**
dentro dela.

A tela `LevelUp.json` tem três botões (`Opcao0..2`) e três textos (`Descricao0..2`) **em branco** —
quem escreve neles é o código, com os nomes sorteados:

```csharp
private void AbrirLevelUp()
{
    _run.AbrirNivel(State);
    if (_run.Opcoes.Count == 0) return;      // tudo no teto: sobe em silêncio

    for (int i = 0; i < 3; i++)
    {
        bool existe = i < _run.Opcoes.Count;
        if (UI.Find<UiButton>("LevelUp", $"Opcao{i}") is { } botao)
            botao.Text = existe ? _run.Opcoes[i].Nome : "—";
        Texto("LevelUp", $"Descricao{i}", existe ? _run.Opcoes[i].Descricao : "");
    }

    World.Paused = true;
    _estado = Estado.SubindoNivel;
    MostrarSomente("Hud", "LevelUp");
}

private void Escolher(int indice)
{
    if (World.TryFind("Player", out var jogador)
        && jogador.Get<PlayerStats>() is { } stats)
    {
        _run.Escolher(indice, stats);
        SincronizarVidaMaxima(jogador, stats);   // +vida máxima também cura o mesmo tanto
    }

    if (_run.PodeSubirDeNivel(State))            // dois níveis de uma vez? encadeia
    {
        AbrirLevelUp();
        if (_estado == Estado.SubindoNivel) return;
    }

    World.Paused = false;
    _estado = Estado.Jogando;
    MostrarSomente("Hud");
}
```

### 10.4 Tela de UI não é cena

`LoadScene` troca o `World`; as telas de UI continuam desenhadas por cima **até alguém
escondê-las**. O erro clássico do gênero — "cliquei em Jogar, a fase carregou atrás e o menu
continuou na tela" — é exatamente isso. Resolva num lugar só:

```csharp
private static readonly string[] Telas =
    ["MainMenu", "Loja", "Hud", "LevelUp", "GameOver", "Pausa"];

private void MostrarSomente(params string[] visiveis)
{
    foreach (string tela in Telas)
        if (visiveis.Contains(tela)) UI.Show(tela); else UI.Hide(tela);
}
```

Carregue todas no `OnLoad` e deixe o `MostrarSomente` decidir o resto:

```csharp
foreach (string tela in Telas)
    UI.Load($"scenes/{tela}.json", Assets);
```

As telas `MainMenu`, `Pausa` e `GameOver` são `UiPanel` + `UiText` + `UiButton` com âncora
`Center` — coordenada fixa só cai no meio numa tela do tamanho exato em que você autorou, e celular
real é bem mais largo que a referência.

**Rode agora:** o jogo tem começo, meio e fim. Menu → Jogar → arena → enche a barra → o mundo
congela e três melhorias aparecem → escolhe → volta a rodar mais forte. `ESC` pausa. Morrer mostra
o resumo da partida.

---

## Passo 11 — loja permanente e save

Moeda coletada durante a partida já entra no `InventoryManager`. Falta gastá-la entre partidas.

`Game/MetaShop.cs`:

```csharp
public sealed class MetaItem
{
    public required string Id { get; init; }      // nome da variável do GameState
    public required string Nome { get; init; }
    public required string Descricao { get; init; }
    public int MaxNivel { get; init; } = 5;
    public int PrecoBase { get; init; } = 25;
    public int PrecoPorNivel { get; init; } = 20;
}

public static class MetaShop
{
    public const string Moeda = "Moeda";

    public static readonly IReadOnlyList<MetaItem> Itens =
    [
        new() { Id = "MetaVida", Nome = "Vitalidade", Descricao = "+20 de vida máxima por nível",
                PrecoBase = 20, PrecoPorNivel = 15, MaxNivel = 5 },
        // …MetaDano, MetaVelocidade, MetaColeta
    ];

    public static int Nivel(GameState state, MetaItem item) => (int)state.GetVariable(item.Id);
    public static int Preco(GameState state, MetaItem item)
        => item.PrecoBase + item.PrecoPorNivel * Nivel(state, item);

    public static bool Comprar(GameState state, InventoryManager inv, MetaItem item, out string msg)
    {
        if (Nivel(state, item) >= item.MaxNivel) { msg = $"{item.Nome} já está no máximo."; return false; }

        int preco = Preco(state, item);
        if (inv.GetCount(Moeda) < preco) { msg = $"Faltam moedas para {item.Nome} ({preco})."; return false; }

        inv.Remove(Moeda, preco);
        state.AddVariable(item.Id, 1f);
        msg = $"{item.Nome} nível {Nivel(state, item)} comprado!";
        return true;
    }

    public static void AplicarEm(PlayerStats stats, GameState state)
    {
        stats.MaxHealth      += 20f  * state.GetVariable("MetaVida");
        stats.DamageMultiplier += 0.10f * state.GetVariable("MetaDano");
        stats.MoveSpeed      *= 1f + 0.06f * state.GetVariable("MetaVelocidade");
        stats.PickupRadius   += 20f  * state.GetVariable("MetaColeta");
    }
}
```

> **O truque que economiza o sistema de save inteiro:** o nível comprado é uma **variável do
> `GameState`**, e o `SaveManager` grava `GameState` + `Inventory` + quests + a cena atual. Ou
> seja: moedas e melhorias permanentes persistem **sem uma linha de serialização sua**.

No `Game`, três chamadas resolvem o resto:

```csharp
// boot
if (Save.HasSave(0)) Save.Load(0);
IrParaMenu();       // sempre: o save guarda a CENA, e ninguém quer voltar direto pra arena

// ao comprar
if (MetaShop.Comprar(State, Inventory, MetaShop.Itens[i], out _mensagemLoja)) Save.Save(0);

// ao morrer — fechar o jogo nesta tela não pode custar o prêmio da partida
Save.Save(0);
```

E a tela da loja se atualiza sozinha porque os textos são reescritos a cada frame:

```csharp
for (int i = 0; i < MetaShop.Itens.Count; i++)
{
    var item = MetaShop.Itens[i];
    Texto("Loja", $"Item{i}",
        $"{item.Nome}  [{MetaShop.Nivel(State, item)}/{item.MaxNivel}]  —  {item.Descricao}");

    if (UI.Find<UiButton>("Loja", $"BtnComprar{i}") is { } botao)
        botao.Text = MetaShop.NoMaximo(State, item)
            ? "Máximo" : $"Comprar ({MetaShop.Preco(State, item)})";
}
```

**Rode agora:** jogue, morra, volte ao menu, entre na loja e gaste as moedas. Feche o jogo, abra de
novo: as moedas e os níveis continuam lá, e a próxima partida começa mais forte (o
`MetaShop.AplicarEm` do `PlayerRunner.Start` — Passo 4).

---

## Passo 12 — uma arma que se desbloqueia

Melhoria que só soma número cansa. O padrão pra "essa melhoria **liga uma arma nova**" é: um campo
na ficha que começa em zero, e um script que não faz nada enquanto ele for zero.

`Scripts/OrbitBlade.cs` (resumido):

```csharp
[SceneScript]
public sealed class OrbitBlade : Behavior
{
    public string Prefab = "prefabs/lamina.json";
    public float Radius = 64f;
    public float RotationSpeed = 170f;      // graus/s

    private readonly List<Entity> _laminas = [];
    private float _angulo;

    public override void Update(float deltaTime)
    {
        var stats = Get<PlayerStats>();
        if (World is null || stats is null || Get<Transform>() is not { } dono) return;

        _laminas.RemoveAll(l => !l.IsAlive);        // lâmina destruída por fora sai da conta

        int desejadas = Math.Max(0, stats.OrbitBlades);
        while (_laminas.Count < desejadas)
        {
            if (World.Spawn(Prefab, dono.Position) is not { } nova) break;
            _laminas.Add(nova);
        }
        while (_laminas.Count > desejadas)
        {
            _laminas[^1].Destroy();
            _laminas.RemoveAt(_laminas.Count - 1);
        }
        if (_laminas.Count == 0) return;

        _angulo += RotationSpeed * MathF.PI / 180f * deltaTime;
        float dano = stats.OrbitDamage * stats.DamageMultiplier;

        for (int i = 0; i < _laminas.Count; i++)
        {
            float angulo = _angulo + MathF.Tau * i / _laminas.Count;
            var posicao = dono.Position + new Vector2(MathF.Cos(angulo), MathF.Sin(angulo)) * Radius;

            if (_laminas[i].Get<Transform>() is { } t) { t.Position = posicao; t.Rotation = angulo; }
            if (_laminas[i].Get<ContactDamage>() is { } c) c.Damage = dano;   // dano sempre em dia
        }
    }

    public override void OnDestroy()     // sem isto as lâminas giram em volta do vazio
    {
        foreach (var lamina in _laminas) if (lamina.IsAlive) lamina.Destroy();
        _laminas.Clear();
    }
}
```

A lâmina em si é um prefab com `Collider` **não sólido** + `ContactDamage` mirando `#inimigo` — o
dano é do componente nativo, o script só coloca cada lâmina no lugar.

No catálogo:

```csharp
new() { Id = "orbital", Nome = "Lâmina Orbital", Descricao = "+1 lâmina girando em volta de você",
        MaxNivel = 4, Aplicar = s => s.OrbitBlades += 1 },
```

**Rode agora:** jogue até tirar a Lâmina Orbital no sorteio e veja as lâminas nascerem já girando.
Tire de novo e são duas, opostas. Esse é o molde de qualquer arma futura.

---

## Passo 13 — balanceamento (onde mexer)

Tudo abaixo é editável no Inspector, sem recompilar:

| Quero… | Mexa em |
|---|---|
| Inimigo nascendo mais rápido | `EnemySpawner.StartInterval` ↓ ou `IntervalHalfLife` ↓ |
| Partida mais longa antes de apertar | `IntervalHalfLife` ↑ (ex.: 120) |
| Inimigo mais duro com o tempo | `HealthPerMinute` ↑ |
| Ondas mais dramáticas | `HordeEvery` ↓ / `HordeAmount` ↑ |
| FPS caindo na partida longa | `MaxAlive` ↓ (é o teto que segura tudo) |
| Subir de nível mais devagar | `RunManager.XpParaNivel` (o termo `nivel * nivel`) |
| Jogador mais forte de saída | os padrões do `PlayerStats` |
| Melhoria mais/menos poderosa | o `Aplicar` dela no `UpgradeCatalog` |
| Loja mais cara | `PrecoBase`/`PrecoPorNivel` do `MetaItem` |

Ordem prática de ajuste: primeiro o **ritmo de morte do inimigo** (dano da arma × vida do bicho),
depois o **ritmo de level up** (curva de XP), e só então a curva de spawn. Mexer nos três ao mesmo
tempo é como se afinasse um instrumento tocando outro.

---

## Passo 14 — testar sem abrir a janela

Um survivor é difícil de depurar olhando: são 100 entidades em movimento. O caminho rápido é rodar
a lógica **sem GL e sem janela**, num console — o `World`, o `SceneSerializer` e os seus scripts
não dependem de tela nenhuma.

O esqueleto (num projeto de console à parte que referencia o do jogo):

```csharp
var serializer = new SceneSerializer();
serializer.RegisterScripts(typeof(EnemyChaser).Assembly);   // registra seus [SceneScript]

var world = new World();
var state = new GameState();

// Sem AssetManager não dá pra resolver textura: remova o campo "Texture" do JSON antes de ler.
string Load(string rel) => Regex.Replace(File.ReadAllText(Caminho(rel)),
    "\"Texture\"\\s*:\\s*\"[^\"]*\"\\s*,?", "");

// World.State / World.Inventory / World.PrefabFactory têm setter internal (quem popula é o Game):
// num harness, reflection resolve.
typeof(World).GetProperty("State")!.SetValue(world, state);
Func<string, Vector2?, Entity?> fabrica = (path, pos) =>
    serializer.LoadEntity(Load(path), new SceneContext { World = world }, pos);
typeof(World).GetProperty("PrefabFactory", BindingFlags.NonPublic | BindingFlags.Instance)!
    .SetValue(world, fabrica);

serializer.Load(Load("scenes/arena.json"), new SceneContext { World = world });

for (int i = 0; i < 180; i++) world.Update(1f / 60f);       // 3 segundos de jogo
Console.WriteLine($"{world.Query<EnemyChaser>().Count()} inimigos vivos");
```

Com isso dá pra verificar em segundos coisas que levariam minutos de jogo: se o spawner nasce, se o
inimigo aproxima, se a arma dispara, se a morte larga gema, se a gema vira XP, se a curva de XP
abre nível e se a melhoria mexeu na ficha. E dá pra rodar **3 minutos de partida em ~1 segundo de
CPU** pra conferir que nada vaza — no projeto de exemplo, 3 minutos simulados terminam com
90–120 inimigos vivos (o teto do `MaxAlive` ainda longe) e pouco mais que isso de entidades no
total, o que mostra que gema, moeda e projétil estão mesmo sendo recolhidos em vez de acumular.

---

## Checklist — por que não funciona

| Sintoma | Causa quase sempre |
|---|---|
| Componente aparece como "não registrado" no console | falta `[SceneScript]`, ou a classe não é `sealed`/sem construtor vazio |
| Campo do script não aparece no Inspector | tipo diferente de `float`/`int`/`bool`/`string`, ou propriedade com `private set` |
| Entidade invisível | `SpriteRenderer` sem `Texture` (o runtime não desenha placeholder) ou `Layer` abaixo do chão |
| HUD não aparece | `Ui*` numa cena de gameplay em vez de tela `"UI": true`; ou faltou `UI.Load` |
| Menu fica grudado por cima do jogo | trocou de cena sem `Hide` na tela anterior (use `MostrarSomente`) |
| Botão do menu bate no lugar errado no celular | âncora `Left/Top` em vez de `Center`, ou `DesignResolution` ≠ `designWidth/Height` |
| Arma não acerta ninguém | prefab do inimigo sem `Tags: "inimigo"`, ou `TargetPrefix` diferente de `#inimigo` |
| Projétil empurra em vez de machucar | `Collider.IsSolid` ficou `true` no prefab do tiro |
| Inimigo aparece do nada na tela | `SpawnDistance` menor que a meia-diagonal (734px em 1280×720) |
| Tela de level up abre e trava o jogo | leu o botão num `Behavior` — mundo pausado não roda script; leia no `OnUpdate` do `Game` |
| Jogador some ao morrer | `Health.DestroyOnDeath` ficou `true` no Player |
| Loot não cai | tentou soltar depois da morte; solte no `OnDeath`, que roda antes da destruição |
| Progresso da loja não persiste | `Save.Save(0)` não foi chamado, ou o nível foi guardado num campo em vez de variável do `GameState` |
| Abre direto na arena ao ligar o jogo | `Save.Load` restaurou a cena salva — force o menu depois de carregar |
| FPS caindo com o tempo | `MaxAlive` alto demais, ou algo sem `Lifetime` acumulando (gema, efeito) |

---

## Próximos passos

Nada abaixo exige mudança estrutural — os ganchos já estão nos lugares certos:

- **Som**: `World?.Audio?.Play("audio/tiro.wav", pitch: 0.9f + Random.Shared.NextSingle() * 0.2f)`
  no disparo e no `OnDeath`. Música com `PlayMusic` (use `.ogg`, que toca em streaming).
- **Animação**: componente `Animator` com clipes de sprite sheet; o `TopDownController` já alimenta
  o parâmetro `Speed` sozinho.
- **Mais inimigos**: um prefab por tipo (sempre com `Tags: "inimigo"`), e um `Diretor` por tipo com
  `StartAfterSeconds` diferente — ou uma tabela de spawn em `database/spawns.json`.
- **Elite e chefe**: o mesmo prefab com `Health.Max` maior, `Transform.ScaleX/Y` maior e um drop
  garantido; o chefe pode ser um `Diretor` com `HordeEvery` gigante e `HordeAmount: 1`.
- **Evolução de arma**: um upgrade cujo `Aplicar` troca o `Prefab` do `WeaponAutoShoot` (guarde a
  referência ao componente na ficha ou busque com `TryFind`).
- **Personagens jogáveis**: cada um é um conjunto de padrões diferente do `PlayerStats` — nem
  precisa de cena nova.
- **Android**: [GUIA-ANDROID.md](GUIA-ANDROID.md). O joystick de toque e os botões de UI já estão
  prontos; o resto é o export.
