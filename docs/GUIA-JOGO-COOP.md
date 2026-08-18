# Aurora Coop — jogo de teste do multiplayer

Caça às moedas cooperativa. Um jogador hospeda, os outros acham a partida numa lista (ou
digitam o IP). Todo mundo corre pelo mesmo mapa pegando moedas e o placar é do time.

Existe pra **testar o multiplayer da engine numa rede de verdade**, inclusive entre celulares —
não é um jogo pronto, é um alvo de teste com todas as peças de rede ligadas ao mesmo tempo.

## O que ele exercita

| Peça | Onde aparece |
|---|---|
| Busca de salas na LAN | menu "PROCURAR PARTIDAS" |
| Entrada e saída de jogadores | boneco nasce e some sozinho |
| Criar/destruir entidades em rede | moedas repostas pelo host |
| Host autoritativo + previsão local | movimento do boneco |
| Interpolação | boneco dos outros jogadores |
| RPC com entrega garantida | placar e fim de rodada |

## Rodar no PC

```bash
dotnet run --project samples/Aurora.Coop
```

Abre no menu. Duas janelas na mesma máquina já servem pra testar: numa clique **HOSPEDAR**, na
outra **PROCURAR PARTIDAS**.

Sem clicar em nada:

```bash
dotnet run --project samples/Aurora.Coop -- --host
```

```bash
dotnet run --project samples/Aurora.Coop -- --join 127.0.0.1
```

Argumentos: `--host`, `--join <ip>`, `--bot` (o boneco anda sozinho atrás da moeda mais perto),
`--seconds <n>` (fecha sozinho). O `--bot` é o que prova o laço inteiro — pegar, destruir,
avisar por RPC, somar nas duas máquinas — sem ninguém segurando o controle.

Uma linha por segundo sai no console com papel, jogadores, placar, entidades sincronizadas e
input pendente. Input pendente subindo é sinal de rede ruim.

## Gerar o APK

```bash
dotnet build samples/Aurora.Coop.Android -c Release
```

Sai em `samples/Aurora.Coop.Android/bin/Release/net10.0-android/com.auroraengine.coop-Signed.apk`,
já assinado com a keystore de debug — serve pra sideload, não pra Play Store.

Instale nos dois celulares (o mesmo APK), abra, e num deles toque **HOSPEDAR**; no outro,
**PROCURAR PARTIDAS**.

## Controles

- **Celular**: encoste na metade esquerda da tela e arraste — o analógico nasce onde o dedo
  encostou. Botões ficam na direita e no topo.
- **PC**: WASD ou setas.

## Se a partida não aparecer na lista

1. **Mesmo Wi-Fi.** Celular em 4G não acha ninguém. Repare que "mesma rede" também falha se um
   estiver na rede de visitantes do roteador.
2. **Isolamento de cliente.** Muito roteador (e quase todo Wi-Fi de empresa) bloqueia um
   aparelho de falar com o outro. Sintoma: a busca não acha nada mas o jogo funciona se você
   digitar o IP. Nesse caso, desligue "AP isolation"/"isolamento de clientes" no roteador.
3. **Firewall do Windows**, se o host for o PC: na primeira vez ele pergunta — libere em
   **rede privada**. Recusado, o PC some da busca e ninguém consegue entrar.
4. **Digite o IP.** Quem hospeda vê o endereço no canto inferior esquerdo da tela de jogo.

## Detalhes que valem saber

**Por que o `MulticastLock` no Android.** O Wi-Fi do Android descarta pacote de broadcast que
não seja endereçado ao aparelho, pra economizar bateria. A busca de salas é justamente um
broadcast, então sem o lock o celular que hospeda nunca recebe a pergunta e não aparece na
lista de ninguém — enquanto entrar digitando o IP continua funcionando, o que torna a falha
bem confusa de diagnosticar. Ver `MainActivity.OnCreate`.

**Por que os bonecos são conferidos todo frame** em vez de criados/destruídos nos eventos de
entrada e saída: o host não recebe evento de "eu entrei", e um evento perdido no meio de uma
troca de cena deixaria um boneco fantasma andando pra sempre. Conferir o estado se conserta
sozinho.

**Por que o placar viaja no RPC** em vez de cada máquina somar 1: quem entra no meio da partida
acerta o número no primeiro evento que receber, em vez de contar a partir do zero pra sempre.

**Por que as árvores não são entidades de rede**: são iguais em toda máquina porque a semente do
sorteio é fixa. Mandá-las pela rede seria desperdício puro.

## Estrutura

```
samples/Aurora.Coop.Core      Jogo (CoopGame, TouchStick) — sem nada de plataforma
samples/Aurora.Coop           Executável desktop
samples/Aurora.Coop.Android   Activity, permissões, empacotamento do APK
```

Os sprites e a fonte são os mesmos do Sandbox (`samples/Aurora.Sandbox.Core/Assets`), apontados
por link em vez de copiados.
