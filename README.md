# Aurora Engine

Game engine 2D em C# focada em jogos mobile, com editor visual (futuro), ECS próprio e exportação para Android (futuro).

## Estado atual — Fase 1 (Runtime básico)

- ✅ Janela e game loop (Silk.NET / GLFW)
- ✅ Renderização de sprites com batching automático (OpenGL 3.3)
- ✅ ECS mínimo: `World`, `Entity`, `Transform`, `SpriteRenderer`, `Behavior` (scripts)
- ✅ Câmera 2D com zoom e follow suave
- ✅ Letterbox/pillarbox opcional (`Game.DesignResolution`): trava a proporção de câmera/UI/toque numa resolução fixa, com barra centralizada em vez de esticar/cortar em telas com proporção diferente
- ✅ Input de teclado, mouse, toque e gamepad (analógicos + botões; `AxisX`/`AxisY` já combinam teclado+analógico esquerdo sem mudar script nenhum)
- ✅ Carregamento de texturas (PNG/JPG via StbImageSharp) com cache
- ✅ Assets abstraídos por `IAssetSource`: pasta no desktop, APK no Android
- ✅ Cenas em JSON (`scenes/*.json`) com registro extensível de componentes
- ✅ Tilemaps com culling e 1 draw call por mapa; pintura no editor
- ✅ Variáveis e switches globais (`GameState`) com save/load em JSON
- ✅ Eventos visuais (`EventTrigger`): gatilhos SceneStart/PlayerTouch/SwitchOn e
  ações SetVariable, SetSwitch, Teleport, Destroy, Wait, ShowMessage
- ✅ **Áudio** (OpenAL via Silk.NET): WAV/OGG, pool de SFX, canal de música, ações
  PlaySound/PlayMusic/StopMusic nos eventos visuais
- ✅ **Animação de sprites**: componente `Animator` com clipes de sprite sheet, troca de clipe em runtime
- ✅ **Quebra de linha automática**: caixa de diálogo quebra sozinha na largura da caixa;
  `UiText` ganha `MaxWidth` (0 = desligado). Palavra que não cabe sozinha é cortada em vez de vazar

## Editor

```bash
dotnet run --project src/Aurora.Editor -- samples/Aurora.Sandbox.Core/Assets/scenes/forest.json
```

- **Hierarquia** (esquerda): seleciona entidades
- **Cena** (centro): arrastar move a entidade; botão do meio/direito = pan; scroll = zoom
- **Inspector** (direita): edita Transform, SpriteRenderer e mostra componentes de script
- **Ctrl+S** salva de volta no JSON — componentes que o editor não conhece são preservados intactos

## Rodando a demo

```bash
dotnet run --project samples/Aurora.Sandbox
```

### Demo de plataforma (2D, duas fases)

```bash
dotnet run --project samples/Aurora.Platformer
```

Jogo de plataforma completo e comentado, feito para servir de referência: gravidade e pulo
(com coyote time e jump buffer), colisão do jogador com o tilemap, moedas, espinhos com
respawn e bandeira que leva para a fase 2. **A/D** ou setas movem, **Espaço/W/↑** pulam,
**R** reinicia a fase, **ESC** sai. `--smoke` roda o roteiro automatizado (chão, pulo,
moeda, espinho e troca de fase) e falha com exceção se algo quebrar.

As fases são cenas JSON comuns — abra no editor com
`dotnet run --project src/Aurora.Editor -- samples/Aurora.Platformer/Assets/scenes/level1.json`.
O passo a passo (física, colisão, montagem da cena, regras de level design) está em
[docs/GUIA-JOGO-PLATAFORMA.md](docs/GUIA-JOGO-PLATAFORMA.md).

### Jogo coop de teste (LAN, PC e Android)

```bash
dotnet run --project samples/Aurora.Coop
```

Caça às moedas cooperativa até 8 jogadores: um hospeda, os outros acham a partida numa lista.
Feito pra testar o multiplayer numa rede de verdade — liga busca de salas, entrada/saída de
jogadores, criação e destruição de entidades, host autoritativo com previsão, interpolação e
RPC ao mesmo tempo.

APK: `dotnet build samples/Aurora.Coop.Android -c Release`. Passo a passo, controles e o que
fazer quando a partida não aparece na lista: [docs/GUIA-JOGO-COOP.md](docs/GUIA-JOGO-COOP.md).

### Android (APK)

```bash
dotnet publish samples/Aurora.Sandbox.Android -c Release -f net10.0-android
```

APK sai em `samples/Aurora.Sandbox.Android/bin/Release/net10.0-android/publish/com.auroraengine.sandbox-Signed.apk`.
Copie para o celular e instale (permitir "fontes desconhecidas"). Controle por toque: segure o dedo e o jogador segue.

Pra fazer o **seu** jogo mobile (orientação, resolução de referência, joystick de toque e o APK
pelo editor), o passo a passo está em [docs/TUTORIAL-JOGO-ANDROID.md](docs/TUTORIAL-JOGO-ANDROID.md).

Controles: **WASD/setas** movem o jogador, câmera segue, **ESC** sai.
O título da janela mostra FPS, contagem de entidades e draw calls.

`--smoke` fecha a janela sozinha após 1,5 s (usado para teste automatizado).

## Testes

```bash
dotnet test tests/Aurora.Runtime.Tests
```

Cobre a lógica pura do runtime — colisão e resolução (caixa/círculo/tilemap, camadas, triggers),
A* e `NavGrid`, roundtrip de cena no `SceneSerializer` (incluindo scripts `[SceneScript]`),
`GameState`/inventário/quests, vida e i-frames, e o ciclo de vida de entidades e behaviors.
Nada aqui precisa de GPU, janela ou dispositivo de áudio.

Fora de cobertura por dependerem de contexto OpenGL: `SpriteBatch`, `Font`, `Texture2D`,
`AudioManager` e o loop do `Game`. A demo `--smoke` continua sendo o teste de ponta a ponta
desses.

O CI (`.github/workflows/ci.yml`) roda a suíte em cada push/PR e compila editor e demo desktop.
`Aurora.Sandbox.Android` fica fora do CI: exige a workload `android`.

## Estrutura

```
src/Aurora.Runtime      Núcleo da engine (sem dependência de editor/UI desktop)
  Game.cs               Classe base: janela, loop, inicialização GL
  Graphics/             SpriteBatch, Shader, Texture2D, Camera2D, Color
  Ecs/                  World, Entity, Behavior, componentes
  Input/                InputManager (teclado/mouse)
  Assets/               AssetManager (cache de texturas)
  Net/                  Multiplayer LAN: NetSession, NetHost, NetClient, NetSyncSystem,
                        NetRpcSystem, NetBrowser/NetLobby, canal confiável, protocolo UDP
samples/Aurora.Sandbox  Demo jogável da Fase 1
samples/Aurora.Platformer   Demo de plataforma 2D com duas fases (referência)
samples/Aurora.Coop.Core    Jogo coop de teste do multiplayer (desktop + Android)
tests/Aurora.Runtime.Tests  Testes da lógica pura do runtime (xUnit)
```

## Formato de cena

```json
{
  "Scene": "Forest",
  "Objects": [
    {
      "Name": "Player",
      "Components": [
        { "Type": "Transform", "X": 0, "Y": 0 },
        { "Type": "SpriteRenderer", "Texture": "sprites/player.png", "Layer": 10 },
        { "Type": "PlayerController" }
      ]
    }
  ]
}
```

Componentes próprios entram no mesmo registro dos nativos:

```csharp
Scenes.Register<BobBehavior>("Bob",
    (json, ctx) => new BobBehavior(SceneSerializer.GetFloat(json, "Amplitude", 4f)),
    (json, c, ctx) => json.WriteNumber("Amplitude", ((BobBehavior)c).Amplitude));
LoadScene("scenes/forest.json");
```

## Exemplo de uso

```csharp
public class MyGame : Game
{
    protected override void OnLoad()
    {
        var player = World.CreateEntity("Player");
        player.Add(new Transform(0, 0));
        player.Add(new SpriteRenderer(Assets.LoadTexture("player.png")));
        player.Add(new PlayerController(Input)); // Behavior customizado
    }
}

new MyGame().Run("Meu Jogo", 1280, 720);
```

## Animação de sprites

Coloque o sprite sheet como qualquer textura e adicione um `Animator`:

```csharp
var hero = World.CreateEntity("Hero");
hero.Add(new Transform(0, 0));
hero.Add(new SpriteRenderer(Assets.LoadTexture("sprites/hero.png")));
var anim = hero.Add(new Animator
{
    FrameWidth = 32, FrameHeight = 48, SheetColumns = 4,
    Clips =
    [
        new AnimationClip { Name = "idle", Frames = [0, 1, 2, 3], FrameDuration = 0.2f },
        new AnimationClip { Name = "walk", Frames = [4, 5, 6, 7], FrameDuration = 0.1f },
        new AnimationClip { Name = "attack", Frames = [8, 9, 10], FrameDuration = 0.08f, Loop = false },
    ],
});
```

Dentro de um `Behavior`:

```csharp
var anim = Get<Animator>()!;
if (moving) anim.Play("walk");
else        anim.Play("idle");

if (attacking && anim.IsFinished) anim.Play("idle");
```

O frame ativo é calculado do índice na grade: `col = frame % SheetColumns`, `row = frame / SheetColumns`.
O `Animator` atualiza o `SourceRect` do `SpriteRenderer` automaticamente a cada frame.

### Cena JSON

```json
{
  "Type": "Animator",
  "FrameWidth": 32, "FrameHeight": 48, "SheetColumns": 4,
  "Clips": [
    { "Name": "idle", "Frames": [0,1,2,3], "Duration": 0.2 },
    { "Name": "walk", "Frames": [4,5,6,7], "Duration": 0.1 }
  ]
}
```

## Áudio

Coloque os arquivos em `Assets/sounds/`. Formatos suportados: **WAV** (PCM 8/16 bits) e **OGG Vorbis**.

```csharp
// Em OnLoad():
Audio.Preload("sounds/bgm.ogg");          // opcional: pré-carrega sem tocar

// Em OnUpdate() ou Behavior:
Audio.Play("sounds/coin.wav");            // SFX (one-shot, pool de 16 fontes)
Audio.Play("sounds/hit.wav", volume: 0.6f, pitch: 1.2f);
Audio.PlayMusic("sounds/bgm.ogg");        // canal de música com loop
Audio.StopMusic();
```

### Volume

Três controles independentes, todos em 0..1 — o suficiente para a tela de opções padrão:

```csharp
Audio.MasterVolume = 0.8f;   // geral (ganho do listener); multiplica os dois abaixo
Audio.MusicVolume  = 0.5f;   // aplica na hora, inclusive na faixa que já está tocando
Audio.SfxVolume    = 1.0f;   // vale do próximo Play em diante
```

O `volume` passado em `Play`/`PlayMusic` continua valendo: é multiplicado pelo barramento.

### Música em streaming

`PlayMusic` com arquivo `.ogg` decodifica aos poucos — só ~0,7 s ficam em PCM por vez, em vez
da faixa inteira (uma trilha de 3 min em memória passaria de 30 MB, o bastante para derrubar o
app no Android). Os bytes **comprimidos** ficam em memória porque asset dentro do APK não é
seekável e o loop precisa rebobinar.

`Game` chama `Audio.Update()` a cada frame para realimentar a fila; quem roda a engine fora do
`Game` precisa chamar também. WAV continua carregando inteiro — o formato não comprime, então
música longa em WAV é cara de qualquer jeito.

### Nos eventos visuais (JSON de cena)

```json
{ "Action": "PlaySound", "Name": "sounds/coin.wav", "Value": 1.0 }
{ "Action": "PlayMusic", "Name": "sounds/bgm.ogg", "On": true, "Value": 0.7 }
{ "Action": "StopMusic" }
```

`Value` = volume (0..1, padrão 1.0). `On` no PlayMusic = loop (padrão true).
Se não houver dispositivo de áudio, `Audio.IsAvailable` é false e todas as chamadas são no-op.

## Multiplayer local (LAN) — em construção

Um jogador hospeda, os outros entram digitando o **IP** dele. Até **8 jogadores** por sala,
host incluído. UDP puro, sem dependência externa. Funciona em Wi-Fi, então desktop e Android
entram na mesma partida.

Na maioria das vezes nem precisa do IP: as partidas da rede local **aparecem numa lista**.
O campo de IP fica como saída pra rede que bloqueia broadcast.

Tudo vive em `Game.Net`, que fica offline até você pedir — jogo single player não abre porta
nenhuma.

### Hospedar

```csharp
protected override void OnLoad()
{
    Net.StartHost("Ana", maxPlayers: 8);

    // O que mostrar na tela pros outros digitarem:
    Console.WriteLine($"IP: {UdpNetTransport.GetLocalAddress()}  porta: {Net.Host!.Port}");

    Net.PlayerJoined += p => Console.WriteLine($"{p.Name} entrou (#{p.Id})");
    Net.PlayerLeft   += p => Console.WriteLine($"{p.Name} saiu");
}
```

### Entrar

```csharp
Net.Join("192.168.0.15", playerName: "Bruno");

Net.JoinedRoom += id => Console.WriteLine($"entrei como jogador #{id}");
Net.LeftRoom   += motivo => Console.WriteLine($"saí: {motivo}");
```

`Join` não trava o jogo esperando: o resultado chega em `JoinedRoom`/`LeftRoom` nos frames
seguintes. Se a sala estiver cheia, `LeftRoom` traz `Rejected` e
`Net.Client.LastRejectReason` traz `Full`.

### Durante o jogo

```csharp
if (Net.IsReady)
{
    foreach (var jogador in Net.Peers)
        Console.WriteLine($"#{jogador.Id} {jogador.Name}");
}
```

`Net.SelfId` é o id deste jogador (0 = host). `Net.IsHost` diz quem manda — na fase 2 é ele
que roda a simulação de verdade.

`Game` já chama `Net.Update()` no começo de cada frame e avisa o outro lado ao fechar o jogo.

### Sincronizar bonecos

Entidade que precisa existir nas outras telas leva um `NetworkIdentity`, e o jogo registra uma
**receita** pra recriá-la do outro lado:

```csharp
const byte PlayerPrefab = 1;

protected override void OnLoad()
{
    Net.Sync!.Prefabs.Register(PlayerPrefab, (world, identity) =>
    {
        var e = world.CreateEntity($"Player{identity.OwnerId}");
        e.Add(new Transform());
        e.Add(new SpriteRenderer { Texture = Assets.Load("player.png") });

        // Só o boneco DESTE jogador leva script de controle: os outros têm a posição
        // entregue pela rede, e um script de movimento local só brigaria com ela.
        if (identity.IsMine)
            e.Add(new PlayerController());

        return e;
    });

    // O host cria um boneco pra cada um que entra.
    Net.PlayerJoined += p => Net.Sync!.Spawn(PlayerPrefab, p.Id);
}
```

O host distribui os números (`NetId`) e transmite o estado da sala 20 vezes por segundo. Nos
clientes o boneco aparece sozinho, e some sozinho quando deixa de existir no host.

### Quem manda em quê

Dois modos, em `Net.Sync.Authority`. Escolha **o mesmo em todas as máquinas**.

| | `NetAuthority.Owner` (padrão) | `NetAuthority.Host` |
|---|---|---|
| O que o cliente manda | posição pronta | o que está apertando |
| Quem calcula | cada dono | o host |
| Código a mais | nenhum | uma função de movimento |
| Cliente modificado | consegue se teletransportar | não consegue |
| Resposta do seu boneco | imediata | imediata (predição local) |

**`Owner`** é o mais simples e basta para jogo cooperativo com amigos na mesma rede. O host já
recusa cliente tentando mexer em boneco que não é dele — o que ele não impede é o jogador
inventar a própria posição.

**`Host`** é para quando isso importa. O cliente manda só o input; o host simula e devolve.

### Modo autoritativo (`NetAuthority.Host`)

Precisa de duas coisas: uma função de movimento registrada junto do prefab, e um leitor de
input.

```csharp
Net.Sync!.Authority = NetAuthority.Host;

// Roda no host pra valer e no cliente pra prever — então só pode depender da entidade e do
// input. Nada de ler teclado, relógio ou aleatório aqui dentro.
Net.Sync.Prefabs.Register(PlayerPrefab, Criar, static (entity, in input) =>
{
    var t = entity.Get<Transform>()!;
    t.Position += new Vector2(input.AxisX, input.AxisY) * 200f * input.DeltaTime;
});

const int BotaoPular = 0;

Net.Sync.SampleInput = () => new NetInputState(
    Input.AxisX, Input.AxisY, 0u).With(BotaoPular, Input.IsKeyDown(Key.Space));
```

**Por que o boneco não fica lento.** O cliente aplica o próprio input na hora e guarda uma
cópia. Quando o snapshot chega, ele volta pra posição que o host confirmou e refaz por cima os
frames que ainda não tinham sido processados. Previsão certa não muda nada na tela.

Medido em socket real: o boneco anda **no primeiro frame**, antes do host sequer rodar, e fica
3,3 px (um frame) à frente do host o tempo todo. Quando o host discorda — uma parede que o
cliente não conhecia — o cliente é puxado exatamente para a posição do host.

Diferença menor que `ReconcileThreshold` (0,5 px) é tratada como ruído de ponto flutuante e a
previsão é mantida: aceitar essa diferença 20 vezes por segundo faria o boneco vibrar parado.

**Contra cliente modificado:** o host limita a duração do frame (`MaxInputDelta`) e os eixos a
-1..1, recusa mensagem de posição crua, ignora input numerado que já processou e limita o
tamanho da fila por jogador. Sem isso, um cliente conseguia pedir um frame de 10 segundos e
atravessar o mapa num pacote.

**Perda de pacote:** cada pacote de input carrega também os frames anteriores
(`InputRedundancy`, padrão 3). Com 50% dos pacotes caindo e 8 frames repetidos, o host recebeu
100% dos frames de input no teste.

### Por que o boneco do outro não treme

Pacote chega a 20 Hz, o jogo desenha a 60. Aplicar a posição crua deixaria o boneco três
frames parado e um pulo. Então as entidades dos outros são mostradas ~100 ms no passado
(`Net.Sync.InterpolationDelay`) e interpoladas entre as duas amostras que cercam esse instante
— sempre existe um "próximo" conhecido pra onde caminhar.

Medido em socket real, boneco a 200 px/s: 3,33 px por frame, constante, sem um único engasgo.

Não se extrapola de propósito: adivinhar além da última amostra acerta enquanto o outro anda
reto e erra feio quando ele vira, e o conserto aparece como teleporte na tela.

### Eventos (RPC)

Posição é "onde está agora" e pode se perder — o próximo snapshot conserta. Evento é
"aconteceu isso", e perder um não tem conserto: um `-30 de vida` que some deixa as duas
máquinas contando histórias diferentes pelo resto da partida. Por isso os RPCs vão por um
canal com entrega garantida **e em ordem** ("nasceu" chegando depois de "morreu" deixaria um
cadáver andando).

```csharp
// Registre nas duas pontas.
Net.Rpc.On("Dano", args =>
{
    Console.WriteLine($"jogador {args.SenderId} causou {args.GetFloat(1)} em {args.GetInt(0)}");
});

Net.Rpc.Send("Dano", netId, 12.5f);                          // todo mundo, inclusive eu
Net.Rpc.Send(NetRpcTarget.Others, "Explosao", "torre");      // todo mundo menos eu
Net.Rpc.Send(NetRpcTarget.Host, "Comprar", "espada");        // só o host
Net.Rpc.SendToPlayer(2, "Sussurro", "só pra você");          // um jogador
```

Argumentos: até 8, entre `int`, `float`, `bool`, `string` e `enum`. Para mandar uma entidade,
mande o `NetId` dela. Os getters convertem entre tipos e devolvem fallback em índice
inexistente — pacote vem da rede, e handler de jogo não é lugar pra tratar exceção de índice.

**`args.SenderId` é confiável.** O host reescreve o remetente com o id real do peer que mandou
o pacote, então um cliente não consegue se passar por outro nem pelo host mudando um byte.

**Offline funciona:** sem sala, a chamada é entregue localmente e nada vai pro fio. O mesmo
código roda em single player sem nenhum `if`.

`Net.Rpc.AllowClientBroadcast = false` tira dos clientes a capacidade de falar com a sala
inteira — sobra `NetRpcTarget.Host`, e o host decide o que retransmitir.

### Animação

O clipe atual do `Animator` viaja no snapshot (1 byte: o índice na lista `Clips`). Sem isso o
boneco dos outros atravessa o mapa deslizando na pose de descanso.

Quem decide a animação é a máquina dona do boneco, igual à posição — o clipe do seu próprio
boneco nunca é imposto de fora, senão ele piscaria 20 vezes por segundo.

Como o índice viaja no lugar do nome, as duas máquinas precisam da **mesma lista de clipes na
mesma ordem** — o que já acontece naturalmente, já que ela vem do mesmo prefab ou do mesmo
JSON de cena. Índice que não existe do outro lado é ignorado, não derruba a partida.

Nas entidades dos outros jogadores, não pendure `Transitions` no `Animator`: os parâmetros
locais não são alimentados por ninguém e só brigariam com o clipe que chega pela rede. Use
`identity.IsMine` na receita do prefab pra decidir.

### Limites

- 64 entidades sincronizadas por snapshot (`NetProtocol.MaxSyncedEntities`) — o snapshot
  inteiro tem que caber num datagrama, senão a regra "quem sumiu da lista foi destruído"
  quebraria e meia cena piscaria a cada frame.
- Só posição e rotação trafegam. Animação, vida e inventário ainda não.
- Entidade fixa da cena (`PrefabId` 0) só sincroniza se já existir nas duas máquinas.
- No modo `Host`, o input de um jogador move todas as entidades dele que tenham função de
  movimento — pensado para um boneco por jogador.

### Achar partidas sem digitar IP

```csharp
Net.StartBrowsing();          // abre a busca (tela de partidas)

foreach (var sala in Net.Rooms)
    Console.WriteLine($"{sala.RoomName} — {sala.PlayerCount}/{sala.MaxPlayers}");

Net.Join(Net.Rooms[0], "Bruno");   // entra sem IP nenhum
```

Funciona por pergunta e resposta: quem procura manda um broadcast, e todo host daquele jogo
que ouvir responde com nome da sala e lotação. O contrário (host anunciando sozinho o tempo
todo) gastaria rede de graça enquanto ninguém está procurando. A pergunta vai na **mesma porta
do jogo** — um segundo socket seria mais uma porta pro jogador liberar no firewall sem ganhar
nada.

`Net.GameId` separa os jogos: só aparecem hosts que declararam o mesmo identificador. `Game`
preenche com o `GameName`, então dois jogos Aurora diferentes na mesma rede não se misturam.

Sala cheia **continua aparecendo**, marcada como `IsFull`. Sumir da lista faria o jogador achar
que digitou algo errado; "3/3" explica sozinho.

`Net.RoomName` define o nome mostrado; vazio usa o nome de quem hospeda.
`Net.Host.Discoverable = false` esconde a partida da busca sem impedir quem sabe o IP de entrar.

**Rede que bloqueia broadcast** (Wi-Fi de empresa, roteador com isolamento de cliente): o
jogador digita o IP e `Net.Browser.Probe(ip)` pergunta direto — a sala aparece na lista igual
às outras, já com nome e lotação, o que confirma o IP antes de tentar conectar.

**PC com várias placas de rede** (Wi-Fi + cabo, VPN, WSL, Docker) responde à busca por cada uma
delas, com IP de origem diferente em cada resposta. As respostas trazem um identificador de
sala sorteado por sessão, então a partida aparece **uma vez só** — sem isso ela apareceria
duplicada na lista.

### Tela de partidas

`NetLobby` é a tela sem a parte visual: lista, seleção, endereço digitado e os botões como
métodos. Não desenha nada de propósito — cada jogo tem a própria arte, e uma tela pronta da
engine seria trocada no primeiro dia. O que ninguém quer reescrever é o que está ali: o vaivém
de estado, o índice que precisa continuar válido enquanto salas entram e saem da lista, e os
motivos de falha traduzidos em frase de tela.

```csharp
var lobby = new NetLobby(Net) { PlayerName = "Ana", RoomName = "Sala da Ana" };

lobby.Browse();                       // botão "procurar"
lobby.Host();                         // botão "hospedar"
lobby.MoveSelection(+1);              // seta pra baixo
lobby.JoinSelected();                 // botão "entrar"

lobby.Address = campoDeTexto.Text;    // UiTextInput
lobby.JoinTyped();                    // botão "entrar por IP"

// Na tela: lobby.State, lobby.Rooms, lobby.Selected, lobby.Message, lobby.LocalAddress
```

`lobby.Message` já vem em português pronto pra desenhar: "Sala cheia.", "O host encerrou a
partida.", "Versão do jogo diferente da do host.", "Não foi possível conectar. Confira o IP e se
o host está com o jogo aberto."

### Campo de texto na UI

Pra montar o "entrar por IP", a UI ganhou `UiTextInput` — clique pra focar, digitação, backspace
e Enter:

```json
{ "Type": "UiTextInput", "X": 40, "Y": 200, "Width": 220,
  "Placeholder": "192.168.0.10", "Allowed": "0123456789.", "MaxLength": 15 }
```

`Allowed` barra caractere inválido na entrada, o que evita a tela de erro depois. Leia
`campo.Text` e `campo.Submitted` (Enter neste frame) do mesmo jeito que `UiButton.Clicked`.

O texto vem de `Input.TypedText`, alimentado pelo evento de caractere do teclado — e não pela
varredura de teclas, que daria "a" onde o jogador digitou "á" e "1" onde ele digitou "!".

### Porta e firewall

Porta padrão **7777/UDP** — a mesma serve pro jogo e pra busca de partidas. Na primeira vez que hospedar no Windows, o firewall pergunta se
libera — precisa liberar em **rede privada**. No Android é só garantir `INTERNET` no manifest.

Fora da LAN (internet) ainda não funciona: precisaria de redirecionamento de porta no roteador
ou de um relay.

1. **Fase 1 — Runtime básico** ✅
2. **Fase 1.5 — Prova de conceito Android** (sprite rodando em APK) — próximo
3. **Fase 2 — Editor** (Avalonia): hierarquia, inspector, scene view, asset browser ✅ (parcial)
4. **Fase 3 — Ferramentas RPG**: tiles ✅, eventos visuais ✅, diálogos ✅, **áudio** ✅, inventário, quests, save
5. **Fase 4 — Avançado**: animação de sprites, partículas, luzes 2D, física 2D, A*
6. **Fase 5 — Exportação**: Android (APK/AAB), Windows, Linux, Web, plugins
7. **Fase 6 — Multiplayer LAN**: handshake e presença ✅, sincronização de estado com
   interpolação ✅, host autoritativo com predição de input ✅, RPCs com entrega garantida ✅,
   sincronização de animação ✅, descoberta na rede local e tela de partidas ✅

## Requisitos

- .NET 10 SDK
- GPU com OpenGL 3.3+
