# Requisito 9

Foi adotada uma fila limitada para cada participante. Cada fila comporta até 200 mensagens e, quando
fica cheia, a mensagem mais antiga é descartada (`DropOldest`).

```csharp
public Channel<byte[]> CanalDeSaida { get; } = Channel.CreateBounded<byte[]>(
	new BoundedChannelOptions(200)
	{
		FullMode = BoundedChannelFullMode.DropOldest,
		SingleReader = true,
		SingleWriter = false
	});
```

Essa política evita que um participante lento bloqueie o envio para os demais ou faça a memória
crescer sem limite. Além disso, as operações de escrita possuem timeout de 5 segundos. Se o
participante continuar sem responder, sua conexão é encerrada.
