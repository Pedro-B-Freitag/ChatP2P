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

## Rodando com Docker

Não precisa ter o .NET 10 instalado pra rodar — só Docker. Tem um `docker-compose.yml` que já sobe
3 participantes (`alice`, `bob`, `carol`), cada um no seu próprio container, numa rede Docker só
deles. Aqui a malha é "de verdade": cada container tem seu próprio IP, e um participante acha o
outro pelo nome do container (`alice`, `bob`, `carol`) em vez de `127.0.0.1:porta` — o Docker resolve
esses nomes automaticamente.

Builda a imagem uma vez (só precisa repetir se mudar o código):

```bash
docker compose build
```

Suba os três:

```bash
docker compose up -d
```

Confira que a malha se formou (cada um deve ter reconhecido os outros dois):

```bash
docker compose logs
```

Pra digitar mensagens em algum deles, conecte no console dele (graças ao TTY e o stdin_open pra digitar):

```bash
docker attach chatp2p-alice
```

Digite mensagens, `/list`, `/msg bob oi` etc. normalmente. Pra sair do `attach` **sem** matar o
container, use `Ctrl+P` seguido de `Ctrl+Q` (desconecta o terminal, mas o processo continua rodando
lá dentro). Se apertar `Ctrl+C`, você mata o processo do container de verdade — bom até pra testar o
requisito de queda abrupta (os outros dois devem detectar e remover o `alice` da lista sozinhos).

Repita o `docker attach` em outro terminal pra cada participante que quiser controlar
(`chatp2p-bob`, `chatp2p-carol`).

Pra derrubar tudo no final:

```bash
docker compose down
```

Quer testar queda abrupta sem precisar de terminal aberto? Dá pra matar um participante de fora,
simulando uma queda real de processo:

```bash
docker kill chatp2p-carol
```

Os outros dois devem detectar a queda e imprimir `[-] carol saiu (conexão perdida)` em poucos
segundos.


