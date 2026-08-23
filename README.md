# chatp2p

Chat distribuído entre N participantes, em malha completa, sem servidor central — implementado em
C#/.NET 10 usando apenas `System.Net.Sockets.Socket` sobre TCP.

Trabalho prático da disciplina de Sistemas Distribuídos (FURB), evoluindo o `SocketChat` (chat
direto entre 2 pares) apresentado em aula. O framing por prefixo de tamanho e o padrão de
`Listen`/`Connect` com timeout do `SocketChat` foram reaproveitados quase literalmente
([Rede/Quadros.cs](Rede/Quadros.cs), [Rede/Conexao.cs](Rede/Conexao.cs)); do `P2PGossip` foi
aproveitado o conceito de um agregado de par com estado de liveness (heartbeat/última atividade) e
o padrão de laço de comandos no console — não o protocolo de gossip em si, já que o enunciado exige
malha completa formada a partir de configuração, não descoberta por fofoca em UDP.

## Arquitetura

Toda instância é, ao mesmo tempo, servidor (`Rede.Conexao.CriarOuvinte` + laço de aceitação) e
cliente (disca para os pares conhecidos). Não existe nó coordenador nem repositório central da
lista de participantes: cada nó mantém sua própria visão em `Pares.RegistroDePares`, construída só
a partir da configuração recebida e das conexões que ele mesmo estabelece ou aceita.

Quando dois nós têm um ao outro na lista de pares, cada um dispara uma conexão de saída para o
outro — o que resultaria em duas conexões TCP redundantes para o mesmo par. Isso é resolvido por
uma regra determinística (o apelido menor sempre disca, o maior sempre aceita), explicada em
detalhe em [EXPLICACOES.md](EXPLICACOES.md).

## Como compilar e executar

```bash
dotnet build
```

```bash
dotnet run --project . -- --porta 9001 --apelido alice --pares 127.0.0.1:9002,127.0.0.1:9003
```

Ou usando um arquivo de configuração (veja [pares.exemplo.json](pares.exemplo.json)):

```bash
dotnet run --project . -- --config pares.exemplo.json
```

Para testar localmente, abra 3 terminais e rode, por exemplo:

```bash
dotnet run --project . -- --porta 9001 --apelido alice --pares 127.0.0.1:9002,127.0.0.1:9003
dotnet run --project . -- --porta 9002 --apelido bob   --pares 127.0.0.1:9001,127.0.0.1:9003
dotnet run --project . -- --porta 9003 --apelido carol --pares 127.0.0.1:9001,127.0.0.1:9002
```

### Comandos

- Qualquer texto digitado é transmitido a todos os participantes conhecidos.
- `/list` — lista os participantes atualmente conhecidos.
- `/msg apelido texto` — envia uma mensagem privada diretamente ao destinatário.
- `/quit` — sai anunciando a saída aos demais participantes.

## Mapeamento dos requisitos do enunciado

| # | Requisito | Onde |
|---|---|---|
| 1 | Porta, apelido e pares conhecidos por argumento ou config | [Configuracao/AnalisadorDeArgumentos.cs](Configuracao/AnalisadorDeArgumentos.cs) aceita `--porta/--apelido/--pares` **ou** `--config arquivo.json` |
| 2 | Conecta aos pares conhecidos e aceita conexões dos demais, formando malha completa | `NoDeChat.Iniciar` inicia `LacoDeAceitacaoAsync` (aceita) e `DiscarParaParesConhecidosAsync` (disca) em paralelo |
| 3 | Mensagem de qualquer participante é entregue a todos os demais | `NoDeChat.Transmitir` escreve o envelope no canal de saída de cada `ConexaoComPar` registrada — entrega direta, sem retransmissão |
| 4 | Toda mensagem identifica o autor | `Protocolo.Envelope.Remetente`; exibido em `ProcessarEnvelopeAsync` |
| 5 | Framing correto, sem truncar/grudar mensagens | Prefixo de tamanho de 4 bytes em [Rede/Quadros.cs](Rede/Quadros.cs) (herdado do SocketChat); testado com rajada de 20 mensagens e mensagem de ~60 KB sem perda nem corrupção |
| 6 | Toda operação de rede com prazo definido | Constantes de timeout no topo de [NoDeChat.cs](NoDeChat.cs); detalhado em EXPLICACOES.md §3 |
| 7 | Queda de um par não derruba nem trava os demais | Cada `ConexaoComPar` roda em suas próprias tasks; exceções são capturadas por conexão e nunca propagam para os demais pares nem para o laço principal |
| 8 | Par que sai (limpo ou abrupto) é removido e a saída é anunciada | `TratarSaidaDoParAsync` (idempotente — EXPLICACOES.md §4), acionado tanto por `Saida` recebida quanto por falha de envio/recebimento/ociosidade |
| 9 | Participante lento não trava os demais; política documentada | Fila por par limitada (200) com descarte do mais antigo + `TryWrite` não bloqueante; escalonamento para desconexão via timeout de escrita. Justificativa completa em EXPLICACOES.md §2 |
| 10 | `/list` e `/quit` | `NoDeChat.ListarParticipantes` / `NoDeChat.SairAsync`, acionados em [Program.cs](Program.cs) |
| 11 | `/msg apelido texto` entregue direto ao destinatário | `NoDeChat.EnviarPrivada` escreve só no canal daquela `ConexaoComPar` — nunca passa por um terceiro |

## Estrutura do código

```
Program.cs                        parse de args/config, inicia o nó, laço de comandos do console
Configuracao/
  OpcoesDoNo.cs                   porta, apelido, lista de pares conhecidos
  AnalisadorDeArgumentos.cs       parsing de --porta/--apelido/--pares ou --config
Rede/
  Quadros.cs                      framing por prefixo de tamanho (herdado do SocketChat)
  Conexao.cs                      Listen/Connect com timeout (herdado do SocketChat)
Protocolo/
  TipoDeMensagem.cs               Ola, Mensagem, Privada, Ping, Pong, Saida
  Envelope.cs                     mensagem serializada em JSON sobre os quadros
Pares/
  ConexaoComPar.cs                estado de uma conexão ativa (socket, fila de saída, liveness)
  RegistroDePares.cs              registro thread-safe + regra de desempate da malha
NoDeChat.cs                       orquestração: aceitar, discar, handshake, broadcast, privada,
                                   /list, /quit, liveness (ping/pong + verificação de ociosidade)
```

Sem comentários no código-fonte — qualquer decisão não óbvia está documentada em
[EXPLICACOES.md](EXPLICACOES.md).
