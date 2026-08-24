# Explicação — Requisito 9

> Um participante que pare de consumir mensagens não pode travar a conversa dos outros. A política
> adotada (descartar, enfileirar com limite, ou desconectar) deve estar documentada e justificada.

## Política adotada: fila com limite por par, descartando a mensagem mais antiga

`ConexaoComPar` — [Pares/ConexaoComPar.cs](Pares/ConexaoComPar.cs)

```csharp
public Channel<byte[]> CanalDeSaida { get; } = Channel.CreateBounded<byte[]>(
    new BoundedChannelOptions(200)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false
    });
```

Cada par tem seu próprio canal de saída, e o envio (`Transmitir`, `EnviarPrivada`, ping/pong) usa
`Writer.TryWrite`, que não bloqueia quem chamou. Isso já resolve boa parte do problema: o `foreach`
que manda mensagem pra todo mundo em `NoDeChat.Transmitir` não fica esperando ninguém, não importa
se a fila de um par específico tá cheia ou não — um par lento nunca atrasa o envio pros outros.

Quando a fila de 200 mensagens enche, a política é jogar fora a mais antiga pra abrir espaço
(`DropOldest`) em vez de recusar a mensagem nova ou já cortar a conexão. Escolhemos assim porque
numa conversa, se um par ficou atrasado, uma mensagem antiga que ainda nem foi enviada já perdeu
bastante valor — melhor ele voltar e ver o fim da conversa do que travar a entrega pros outros ou
ser desconectado por um atraso passageiro.

## Rede de segurança: timeout de escrita desconecta quem realmente travou

Só descartar mensagem não basta: se o par nunca mais consumir nada, a fila fica sempre cheia e
descartando mensagens novas pra sempre, numa conversa que pra ele já não faz mais sentido. Quem
resolve isso é o timeout de escrita em `NoDeChat.LacoDeEnvioAsync`:

```csharp
using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(conexao.Cts.Token);
cts.CancelAfter(TimeoutEnvio); // 5s
await Quadros.EscreverAsync(conexao.Socket, payload, cts.Token);
```

Se o socket do par estiver com o buffer de TCP cheio (porque o processo do outro lado parou de ler
de verdade, não só ficou lento), o `SendAsync` fica pendurado e o timeout de 5s dispara,
encerrando a conexão com aquele par. Ou seja: a política de descarte cobre atrasos curtos sem
perder responsividade pros demais, e o timeout cobre o caso extremo (par travado ou processo morto)
desconectando de vez, em vez de deixar a fila e a memória crescendo pra sempre.
