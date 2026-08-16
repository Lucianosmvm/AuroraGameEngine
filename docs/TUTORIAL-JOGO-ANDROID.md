# Tutorial: jogo base pra Android, do zero ao APK

Este tutorial monta um jogo jogável **no celular**: personagem que anda por joystick de toque,
um botão de ação, HUD, e o APK instalado no aparelho no fim.

É diferente dos outros dois documentos de Android:

| Documento | Pra quê |
|---|---|
| **Este tutorial** | Fazer um jogo mobile do zero, na ordem certa |
| [GUIA-JOGO-BASE.md](GUIA-JOGO-BASE.md) | O jogo completo (diálogo, inimigo, save, inventário) — desktop primeiro |
| [GUIA-ANDROID.md](GUIA-ANDROID.md) | Como o empacotamento funciona por dentro e as armadilhas já descobertas |

A diferença que importa: no desktop você pode decidir resolução e controle depois. **No celular
não** — orientação e resolução de referência definem onde cada elemento de UI cai, e mudar isso
no meio obriga a reposicionar a interface inteira. Por isso as duas primeiras seções são
decisões, não cliques.

---

## 0. Pré-requisitos

```bash
dotnet workload install android
```

Confira com `dotnet workload list` — precisa aparecer `android`. Sem isso o "Exportar Android"
falha na hora de gerar o APK. O SDK do Android e um JDK também precisam existir na máquina
(quem instala Visual Studio com carga de desenvolvimento mobile já tem os dois).

Pra instalar por cabo, o celular precisa de **Opções do Desenvolvedor → Depuração USB**. Sem
cabo dá pra copiar o `.apk` e abrir no aparelho, permitindo "fontes desconhecidas".

---

## 1. Decisão nº 1: orientação

Escolha antes de posicionar qualquer coisa.

| Opção | Quando usar |
|---|---|
| `Portrait` | Jogo vertical (a maioria dos casuais). **Fixo, nunca gira — é o caminho seguro.** |
| `Landscape` | Jogo horizontal. Fixo, nunca gira. Padrão histórico. |
| `SensorPortrait` / `SensorLandscape` / `Sensor` | Gira com o aparelho |

As opções `Sensor*` dependem de um bug antigo do Silk.NET/SDL no Android (crash ao rotacionar
durante o boot) que foi retestado sem reproduzir num Android 14 real — mas só num aparelho.
Se o seu crashar ao girar, volte pra fixo. Detalhes em [GUIA-ANDROID.md](GUIA-ANDROID.md).

Este tutorial usa **`Portrait`**.

## 2. Decisão nº 2: resolução de referência

É o tamanho de tela que o jogo finge ter, em qualquer aparelho. Sem isso, a tela do jogo é a
resolução física do celular — que varia de 720x1280 a 1440x3200 — e a sua UI muda de lugar em
cada modelo.

Pra retrato, use **720x1280**. Ela precisa estar escrita em **dois lugares que têm que
concordar**:

1. No código do jogo (`DesignResolution`)
2. No `aurora.project.json` (`designWidth`/`designHeight`), que é de onde o editor lê pra
   desenhar a moldura do viewport

Se divergirem, o menu que você montar no editor cai em outro lugar no celular.

---

## 3. Criar o projeto

**Arquivo → Novo Projeto…** (`Ctrl+Shift+N`). Escolha a pasta e o nome — digamos `MeuJogo`.

O scaffold já vem com `Program.cs`, `MeuJogoGame.cs`, a fonte `DejaVuSans.ttf`, uma cena
`main.json` e um menu `MainMenu.json`.

### 3.1 Trocar pra retrato no código

Abra `MeuJogoGame.cs` e ajuste o construtor (ele já vem com 1280x720 de paisagem):

```csharp
public MeuJogoGame()
{
    // Retrato. Precisa bater com designWidth/designHeight do aurora.project.json.
    DesignResolution = new Vector2D<int>(720, 1280);
}
```

### 3.2 Trocar no editor

No **Inspector**, campo **TELA DE REFERÊNCIA (UI)**: `720` × `1280`, fonte `22`.
Logo abaixo, **ORIENTAÇÃO ANDROID**: `Portrait`.

O viewport passa a desenhar a moldura vertical com o rótulo `720x1280`. **Tudo que você
posicionar dentro dessa moldura é exatamente onde vai aparecer no celular.**

---

## 4. A regra de ouro dos anchors no celular

Esta é a parte que mais confunde, e no mobile ela custa caro.

Cada elemento de UI tem `AnchorX`/`AnchorY`, que decidem de onde o `X`/`Y` é medido:

| Anchor | O `X`/`Y` significa | Use pra |
|---|---|---|
| `Left` / `Top` (padrão) | Pixel absoluto a partir da borda esquerda/superior | Quase nada, no mobile |
| `Center` | Deslocamento a partir do centro da tela | Botões de menu, títulos |
| `Right` / `Bottom` | Distância a partir da borda direita/inferior | HUD de canto, joystick, botões de ação |

**Deixar tudo em `Left`/`Top` é o erro clássico.** Funciona na sua moldura e sai do lugar em
aparelho de proporção diferente. A regra prática: se o elemento é "do canto", ancore naquele
canto; se é "do meio", ancore no centro. Só use `Left`/`Top` quando o elemento realmente
pertence ao canto superior esquerdo.

### Zona segura

Não encoste nada nas bordas. O Android reserva a faixa de baixo pro gesto de navegação
(voltar/home por swipe) e ele **engole o toque antes do jogo**, mesmo em tela cheia. Muitos
aparelhos ainda têm notch ou câmera furada em cima.

Deixe pelo menos **60px de margem** (na escala 720x1280) em cima e embaixo. É por isso que o
joystick abaixo fica em `Y: 90` com `AnchorY: Bottom`, e não colado na borda.

---

## 5. O player

Na cena `main.json`, **+ Nova** entidade, nome `Player`. Depois **+Add Componente**:

- **SpriteRenderer** — arraste um sprite do painel ASSETS.
- **Collider** — `Shape: Box`, `Width`/`Height` do tamanho do corpo, `IsSolid: true`.

Ligue o checkbox **Colisores** na toolbar pra ver a hitbox desenhada — ela **não** acompanha
`ScaleX`/`ScaleY` do Transform, então esticar o sprite não estica a colisão. Ver a caixa verde
evita descobrir isso só quando o personagem trava no lugar errado.

---

## 6. Os controles de toque

Aqui está a diferença central em relação ao desktop: **não existe teclado**. O script de
movimento que vem no template lê `Input.AxisX`/`AxisY`, que combina teclado e gamepad — no
celular isso fica sempre zero. Você precisa de controles na tela.

### 6.1 Criar a tela de HUD

No painel **TELAS UI**, botão **+**. Nome: `Hud`. Isso cria `Assets/scenes/Hud.json` marcado
com `"UI": true`.

> Numa tela de UI, o `UIManager` lê **só** componentes `Ui*`. Se você puser `Transform` ou
> `SpriteRenderer` numa entidade dessa tela, eles não existem no jogo — o editor desenha esses
> casos apagados com contorno vermelho tracejado justamente pra avisar.

### 6.2 Joystick

**+ Nova** entidade, nome `MoveStick` → **+Add Componente → UiJoystick**:

```
UiJoystick
  X  110           Y  190
  AnchorX [ Left  ▾ ]   AnchorY [ Bottom ▾ ]
  Radius  90
```

Polegar em tela de celular precisa de alvo grande: `Radius 90` na escala 720x1280 dá um
joystick confortável. O `Y: 190` afasta do gesto de navegação.

### 6.3 Botão de ação

**+ Nova** entidade, nome `BotaoAcao` → **+Add Componente → UiButton**:

```
UiButton
  X  110           Y  210
  AnchorX [ Right ▾ ]   AnchorY [ Bottom ▾ ]
  Width  150       Height  150
  Text   Atacar
```

`AnchorX: Right` põe ele no canto oposto ao joystick — um polegar em cada lado. Botão de toque
quer no mínimo ~120px de lado; `150` é folgado sem exagero.

### 6.4 Carregar a HUD no jogo

Em `MeuJogoGame.cs`, no `OnLoad`:

```csharp
protected override void OnLoad()
{
    _font = Assets.LoadFont("fonts/DejaVuSans.ttf", 22f);
    UI.Load("scenes/MainMenu.json", Assets);
    UI.Load("scenes/Hud.json", Assets);   // ← a HUD nova
    UI.Hide("Hud");                        // escondida até o jogo começar
    LoadScene(BootScene ?? "scenes/main.json");
}
```

> **O id da tela é o nome do arquivo sem extensão.** `scenes/Hud.json` vira `"Hud"` — é esse
> nome que você usa em `UI.Show`, `UI.Hide` e `UI.Find`.

No botão "Jogar" do `MainMenu`, na lista `OnClick`, deixe as três ações: `HideUI(MainMenu)`,
`ShowUI(Hud)`, `ChangeScene(scenes/main.json)`.

---

## 7. O script de movimento por toque

**Painel SCRIPTS → +** , template **Vazio**, nome `TouchMovement`. Substitua o conteúdo:

```csharp
using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Scenes;
using Aurora.Runtime.UI;

namespace MeuJogo;

// Movimento por joystick de toque, com teclado/gamepad como reserva — assim o mesmo script
// funciona no Play do editor (mouse arrasta o joystick) e no celular.
[SceneScript]
public sealed class TouchMovement : Behavior
{
    public float Speed = 220f;

    /// <summary>Nome do arquivo da tela de UI, sem extensão.</summary>
    public string ScreenId = "Hud";
    public string StickName = "MoveStick";

    public override void Update(float deltaTime)
    {
        var transform = Get<Transform>();
        if (World is null || transform is null)
            return;

        // O joystick manda quando está sendo tocado; sem toque, Value é vetor zero e o
        // teclado/gamepad assume.
        var stick = World.UI?.Find<UiJoystick>(ScreenId, StickName);
        var move = stick?.Value ?? Vector2.Zero;

        if (move.LengthSquared() <= 0.0001f && World.Input is { } input)
            move = new Vector2(input.AxisX, input.AxisY);

        if (move.LengthSquared() > 0.0001f)
        {
            // O Value do joystick já vem com intensidade 0..1 (quanto mais longe do centro,
            // mais rápido). Normalizar aqui jogaria isso fora, então só limita o máximo.
            if (move.LengthSquared() > 1f)
                move = Vector2.Normalize(move);

            transform.Position += move * Speed * deltaTime;

            var sprite = Get<SpriteRenderer>();
            if (sprite is not null && move.X != 0f)
                sprite.FlipX = move.X < 0f;
        }

        Get<Animator>()?.SetFloat("Speed", move.Length() * Speed);
    }
}
```

Depois de salvar, clique no **↻** do painel SCRIPTS (o editor recompila o jogo pra descobrir
scripts novos) e adicione `TouchMovement` na entidade `Player` pelo **+Add Componente**.

### Botão: `Clicked` ou `Pressed`?

São coisas diferentes, e trocar um pelo outro é bug garantido:

| Propriedade | Vale quando | Use pra |
|---|---|---|
| `Clicked` | **Só no frame do toque** | Atacar, pular, abrir menu |
| `Pressed` | **Enquanto o dedo continua em cima** | Segurar pra acelerar, atirar contínuo |

```csharp
var botao = World.UI?.Find<UiButton>("Hud", "BotaoAcao");
if (botao?.Clicked == true)
    Atacar();
```

O `UIManager` dá dono por id de toque, então dá pra segurar o joystick com um dedo e apertar o
botão com o outro ao mesmo tempo — multi-toque de verdade, sem código extra.

---

## 8. Testar no PC antes de gerar APK

Clique **▶ Play**. O `UiJoystick` responde a clique-e-arraste do mouse, então dá pra validar
movimento, botão e HUD sem passar pelo ciclo lento de gerar e instalar APK.

O que o Play **não** testa: multi-toque (dois dedos), tamanho real dos alvos no polegar, e o
gesto de navegação do Android comendo o toque de baixo. Essas três só no aparelho.

---

## 9. Gerar o APK

**Arquivo → Exportar Android (APK)…**

Preencha:

- **Application ID** — identificador único, formato `com.suaempresa.seujogo`. É a identidade do
  app no aparelho: dois APKs com o mesmo id se sobrescrevem.
- **Nome de exibição** — o que aparece embaixo do ícone.

O editor gera um projeto `net10.0-android` ao lado do seu (com `MainActivity` e o
encaminhamento de toque já escritos) e builda em Release. O `.apk` sai assinado com a debug
keystore: serve pra sideload e teste, **não pra Play Store**.

Demora alguns minutos na primeira vez.

---

## 10. Instalar no celular

Com cabo e depuração USB ligada:

```bash
adb install -r caminho\para\com.suaempresa.seujogo-Signed.apk
```

O `-r` reinstala por cima da versão anterior mantendo o id. Sem cabo: copie o `.apk` pro
aparelho e abra — na primeira vez ele pede permissão de "fontes desconhecidas".

---

## 11. Checklist

- [ ] Workload `android` instalada (`dotnet workload list`)
- [ ] Orientação escolhida de propósito, no Inspector (`Portrait` pra vertical)
- [ ] `DesignResolution` no código **igual** a `designWidth`/`designHeight` do `aurora.project.json`
- [ ] `UI.Draw` recebendo `ScreenSize.X/Y` — nunca `View.FramebufferSize`
- [ ] Nenhum elemento de UI importante em `Left`/`Top`: canto ancora no canto, meio no centro
- [ ] Margem de ~60px das bordas (gesto de navegação e notch)
- [ ] `UiJoystick` com raio confortável (~90) e botões de no mínimo ~120px
- [ ] HUD carregada com `UI.Load` e escondida no boot; botão "Jogar" faz `ShowUI(Hud)`
- [ ] Movimento lendo o joystick — `AxisX`/`AxisY` sozinho não anda no celular
- [ ] `Clicked` pra ação instantânea, `Pressed` pra segurar
- [ ] Testado no Play do editor antes de gerar APK
- [ ] Testado no aparelho: multi-toque, alvos no polegar, toque perto da borda de baixo

---

## 12. Quando der errado

| Sintoma | Causa provável |
|---|---|
| UI no lugar certo no editor, errado no celular | `DesignResolution` ≠ `designWidth`/`designHeight`, ou `UI.Draw` recebendo `FramebufferSize` |
| Jogo abre, HUD aparece, **nada anda e nenhum toque responde** | Movimento lendo só `AxisX`/`AxisY` (teclado/gamepad) |
| Botão de baixo não responde no aparelho, mas responde no Play | Colado na borda inferior — o gesto de navegação do Android consome antes |
| Botão desenha num lugar e clica em outro | `UI.Update` e `UI.Draw` recebendo tamanhos diferentes |
| Texto some no celular | `.ttf` fora de `Assets/fonts/`, não empacotado no APK |
| Erro de compilação só no alvo Android, em `GameState` | `Android.App.GameState` colide — qualifique pra `Aurora.Runtime.GameState` nesse arquivo |
| Crash ao girar a tela | Bug Silk.NET/SDL — volte pra orientação fixa |

As três últimas, e outras já mapeadas em device real, estão detalhadas em
[GUIA-ANDROID.md](GUIA-ANDROID.md).
