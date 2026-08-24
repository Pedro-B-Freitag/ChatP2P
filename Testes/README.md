# Testes do ChatP2P

Os testes cobrem as partes principais do projeto sem precisar subir dois chats completos. Cada teste
indica no código, logo acima do método, qual requisito está sendo verificado.

O objetivo principal também tem um teste de ciclo básico: um nó inicia sua escuta e é encerrado
sem depender de servidor central. A malha completa entre N processos continua sendo demonstrada
com o roteiro do README principal.

## Cobertura dos requisitos

- Requisito 1: coberto pela leitura dos argumentos e pela validação de erro.
- Requisito 2: coberto pela regra de conexão e pela simulação de conexões TCP; a formação da malha
  completa precisa de vários processos rodando.
- Requisito 3: coberto pela simulação de envio para dois pares.
- Requisito 4: coberto pelo envelope que guarda o remetente.
- Requisito 5: coberto pela rajada de quadros, incluindo mensagem longa.
- Requisito 6: coberto pelo cancelamento de uma operação de escuta.
- Requisito 7: coberto pela simulação de queda de um ouvinte enquanto outro continua aceitando
  conexões.
- Requisito 8: coberto pela remoção de um par do registro; a mensagem exibida na saída pode ser
  conferida manualmente.
- Requisito 9: coberto pelo teste de flood da fila limitada.
- Requisito 10: coberto pela chamada de listagem e pela saída limpa do nó; a digitação interativa
  pode ser conferida manualmente.
- Requisito 11: coberto pela criação e leitura de um envelope privado.

Para demonstrar o comportamento completo, ainda é recomendado usar o roteiro de três instâncias no
README principal, com `/list`, mensagens normais, `/msg`, `/quit` e `docker kill`.

## Executar

```powershell
dotnet test .\Testes\ChatP2P.Testes.csproj
```
