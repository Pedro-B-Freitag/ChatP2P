using System.Collections.Concurrent;

namespace ChatP2P.Pares;

public sealed class RegistroDePares
{
    private readonly ConcurrentDictionary<string, ConexaoComPar> _pares = new(StringComparer.OrdinalIgnoreCase);

    public static bool DeveManterConexao(string meuApelido, string apelidoRemoto, Direcao direcao)
    {
        bool euDevoDiscar = StringComparer.OrdinalIgnoreCase.Compare(meuApelido, apelidoRemoto) < 0;
        return direcao == Direcao.Saida ? euDevoDiscar : !euDevoDiscar;
    }

    public ConexaoComPar? SubstituirERetornarAntiga(ConexaoComPar conexao)
    {
        ConexaoComPar antiga = _pares.GetOrAdd(conexao.ApelidoRemoto, conexao);
        if (ReferenceEquals(antiga, conexao))
            return null;

        _pares[conexao.ApelidoRemoto] = conexao;
        return antiga;
    }

    public bool RemoverSe(string apelido, ConexaoComPar esperada) =>
        _pares.TryRemove(new KeyValuePair<string, ConexaoComPar>(apelido, esperada));

    public ConexaoComPar? BuscarPorApelido(string apelido) =>
        _pares.TryGetValue(apelido, out ConexaoComPar? conexao) ? conexao : null;

    public IReadOnlyCollection<ConexaoComPar> Todas() => _pares.Values.ToList();
}
