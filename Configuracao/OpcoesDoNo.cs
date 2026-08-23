namespace ChatP2P.Configuracao;

public sealed record ParConhecido(string Host, int Porta);

public sealed record OpcoesDoNo(int Porta, string Apelido, IReadOnlyList<ParConhecido> Pares);
