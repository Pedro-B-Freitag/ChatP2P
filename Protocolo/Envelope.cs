using System.Text;
using System.Text.Json;

namespace ChatP2P.Protocolo;

public sealed record Envelope(
    TipoDeMensagem Tipo,
    string Remetente,
    string? Destinatario,
    string? Texto,
    int? PortaDeEscuta,
    DateTimeOffset CarimboDeTempo)
{
    private static readonly JsonSerializerOptions Opcoes = new(JsonSerializerDefaults.General);

    public static Envelope Ola(string apelido, int portaDeEscuta) =>
        new(TipoDeMensagem.Ola, apelido, null, null, portaDeEscuta, DateTimeOffset.UtcNow);

    public static Envelope Mensagem(string apelido, string texto) =>
        new(TipoDeMensagem.Mensagem, apelido, null, texto, null, DateTimeOffset.UtcNow);

    public static Envelope Privada(string apelido, string destinatario, string texto) =>
        new(TipoDeMensagem.Privada, apelido, destinatario, texto, null, DateTimeOffset.UtcNow);

    public static Envelope PingNovo(string apelido) =>
        new(TipoDeMensagem.Ping, apelido, null, null, null, DateTimeOffset.UtcNow);

    public static Envelope PongNovo(string apelido) =>
        new(TipoDeMensagem.Pong, apelido, null, null, null, DateTimeOffset.UtcNow);

    public static Envelope SaidaNova(string apelido) =>
        new(TipoDeMensagem.Saida, apelido, null, null, null, DateTimeOffset.UtcNow);

    public byte[] ParaBytes() => JsonSerializer.SerializeToUtf8Bytes(this, Opcoes);

    public static Envelope DeBytes(byte[] bytes) =>
        JsonSerializer.Deserialize<Envelope>(Encoding.UTF8.GetString(bytes), Opcoes)
        ?? throw new InvalidDataException("Envelope inválido recebido.");
}
