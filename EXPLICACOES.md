# Explicações de trechos não óbvios

Este projeto não tem comentários no código-fonte. Sempre que uma decisão de implementação não é
óbvia só de ler o código, o trecho é colado aqui, identificado pela classe/método a que pertence,
seguido da explicação.

## 1. Regra de desempate da malha (evita 2 conexões TCP para o mesmo par)

**Classe:** `RegistroDePares` — [Pares/RegistroDePares.cs](Pares/RegistroDePares.cs)

```csharp
public static bool DeveManterConexao(string meuApelido, string apelidoRemoto, Direcao direcao)
{
    bool euDevoDiscar = StringComparer.OrdinalIgnoreCase.Compare(meuApelido, apelidoRemoto) < 0;
    return direcao == Direcao.Saida ? euDevoDiscar : !euDevoDiscar;
}
```

Se A e B têm um ao outro na lista de pares conhecidos, os dois discam um para o outro ao mesmo
tempo: acabam existindo **duas** conexões TCP fisicamente distintas entre o mesmo par, quando só
uma é necessária. Não dá para simplesmente "manter a primeira que conectou", porque cada lado
observa a ordem de chegada de forma independente — isso pode levar A e B a manterem, cada um, a
conexão que o outro decidiu descartar (split-brain: a conversa morre para os dois lados).

A solução é uma regra **determinística e simétrica**, calculada só a partir dos dois apelidos (sem
nenhuma coordenação entre os processos): o apelido menor, em ordem ordinal, sempre disca; o maior
sempre aceita. Depois do handshake revelar o apelido remoto, cada lado aplica a mesma fórmula:

- Se eu disquei (`Direcao.Saida`): só mantenho se `meuApelido < apelidoRemoto`.
- Se eu aceitei (`Direcao.Entrada`): só mantenho se `meuApelido > apelidoRemoto`.

Como os dois lados calculam a mesma coisa a partir dos mesmos dois nomes, chegam sempre à mesma
conclusão sobre qual das duas conexões físicas é a "canônica" — sem trocar nenhuma mensagem extra
para negociar isso.

## 2. Fila de saída limitada com descarte do mais antigo (requisito 9)

**Classe:** `ConexaoComPar` — [Pares/ConexaoComPar.cs](Pares/ConexaoComPar.cs)

```csharp
public Channel<byte[]> CanalDeSaida { get; } = Channel.CreateBounded<byte[]>(
    new BoundedChannelOptions(200)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false
    });
```

Cada par tem seu próprio canal de saída, e todo envio (`Transmitir`, `EnviarPrivada`, ping/pong)
usa `Writer.TryWrite`, que nunca bloqueia quem chama. Isso já garante, por si só, que um par lento
não trava o envio para os demais: o laço que faz `foreach` sobre todos os pares em `Transmitir`
segue em frente imediatamente, não importa o estado de cada fila individual.

A política de descarte quando a fila de 200 mensagens enche é **jogar fora a mais antiga** para
abrir espaço para a nova (`DropOldest`), em vez de rejeitar a mensagem nova ou desconectar o par de
cara. Justificativa: numa conversa de chat, se um par está momentaneamente lento para consumir, o
valor de uma mensagem antiga ainda não enviada cai rápido — é melhor que ele, ao voltar a
acompanhar, veja o fim da conversa em vez de um backlog obsoleto. Se o problema for mais sério (o
par realmente travou, não só está atrasado), quem resolve é o timeout de escrita do item 3: a
conexão é derrubada de verdade, não fica só acumulando fila para sempre.

## 3. Timeouts de toda operação de rede (requisito 6)

**Classe:** `NoDeChat` — [NoDeChat.cs](NoDeChat.cs)

```csharp
private static readonly TimeSpan TimeoutConexao = TimeSpan.FromSeconds(10);
private static readonly TimeSpan TimeoutHandshake = TimeSpan.FromSeconds(5);
private static readonly TimeSpan TimeoutEnvio = TimeSpan.FromSeconds(5);
private static readonly TimeSpan TimeoutSaida = TimeSpan.FromSeconds(2);
private static readonly TimeSpan IntervaloPing = TimeSpan.FromSeconds(10);
private static readonly TimeSpan TimeoutOciosidade = TimeSpan.FromSeconds(25);
private static readonly TimeSpan IntervaloRediscagem = TimeSpan.FromSeconds(5);
```

- `TimeoutConexao`: prazo para `Socket.ConnectAsync` ao discar para um par conhecido.
- `TimeoutHandshake`: prazo para trocar os envelopes `Ola` logo após conectar/aceitar.
- `TimeoutEnvio`: prazo para cada `Quadros.EscreverAsync` individual no laço de envio de um par —
  se estourar, o par é tratado como não responsivo e a conexão é encerrada (ver item 2).
- `TimeoutSaida`: prazo curto para tentar entregar o aviso de `/quit` a cada par antes de fechar
  tudo (não vale a pena esperar muito por um par lento na hora de sair).
- `IntervaloPing` / `TimeoutOciosidade`: a leitura de um par (`LacoDeRecebimentoAsync`) não tem
  prazo fixo por si só, porque é normal não haver mensagens por um tempo numa conversa parada. Em
  vez disso, `VigiarOciosidadeAsync` manda um `Ping` a cada 10s e fecha a conexão se **nenhum**
  tráfego (mensagem, privada, ping ou pong) chegar desse par em 25s — isso cobre o caso de queda
  abrupta em que o SO não avisa imediatamente (sem FIN/RST na hora).
- `IntervaloRediscagem`: a cada 5s, `DiscarParaParesConhecidosAsync` tenta de novo qualquer par
  configurado que ainda não esteja conectado. Isso resolve a ordem de início dos processos sem
  precisar de coordenação: se o par ainda não subiu, a tentativa de agora falha rápido (respeitando
  `TimeoutConexao`) e é só tentar de novo no próximo ciclo — sem travar o laço principal do nó.

## 4. Saída idempotente (requisito 8)

**Classe:** `NoDeChat` — [NoDeChat.cs](NoDeChat.cs)

```csharp
private async Task TratarSaidaDoParAsync(ConexaoComPar conexao, string motivo)
{
    if (!conexao.MarcarComoEncerrada())
        return;

    _registro.RemoverSe(conexao.ApelidoRemoto, conexao);
    Console.WriteLine($"[-] {conexao.ApelidoRemoto} saiu ({motivo}). Participantes: {_registro.Todas().Count}");
    await conexao.DisposeAsync();
}
```

Uma conexão pode terminar de duas formas concorrentes: (a) o par manda um envelope `Saida`
(`/quit` limpo) e o laço de recebimento processa isso, ou (b) qualquer um dos três laços da conexão
(`LacoDeEnvioAsync`, `LacoDeRecebimentoAsync`, `VigiarOciosidadeAsync`) simplesmente termina por
erro/timeout, e o `Task.WhenAny` em `TratarNovaConexaoAsync` cai no fallback genérico
`"conexão perdida"`. As duas vias chamam `TratarSaidaDoParAsync`. `ConexaoComPar.MarcarComoEncerrada`
usa `Interlocked.Exchange` para garantir que só a **primeira** chamada de fato remove do registro e
imprime o anúncio — a segunda vira um no-op. Sem isso, uma queda abrupta bem no meio de um `/quit`
alheio poderia gerar dois anúncios de saída para o mesmo par.

## 5. BOM (byte-order mark) na primeira linha de entrada

**Classe:** `Program.cs`

```csharp
if (linha.Length > 0 && linha[0] == (char)0xFEFF)
    linha = linha[1..];
```

Se a entrada padrão for redirecionada de um arquivo salvo como "UTF-8 com BOM" (comum em editores
no Windows, e é exatamente o que acontece quando `Process.StandardInput` grava a primeira linha),
o primeiro caractere lido não é `/` mas sim U+FEFF. Sem esse tratamento, o primeiro comando digitado
pelo usuário (`/list`, por exemplo) falha silenciosamente a bater com `"/list"` e acaba sendo
transmitido como se fosse uma mensagem de chat comum. A remoção do BOM só é feita no primeiro
caractere da linha, e só se ele realmente for o BOM — não afeta entrada digitada normalmente num
terminal.
