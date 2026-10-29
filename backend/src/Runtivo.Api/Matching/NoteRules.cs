using System.Text.RegularExpressions;

namespace Runtivo.Api.Matching;

/// <summary>
/// La frase libera dell'annuncio la leggono persone sconosciute: serve a presentarsi, non a lasciare contatti.
/// Si rifiutano email, indirizzi web, numeri di telefono e inviti a spostarsi su altre app.
/// </summary>
public static partial class NoteRules
{
    public const int MaxLength = 200;

    [GeneratedRegex(@"@|https?://|www\.|\b[\w-]+\.(com|it|net|org|me|io|eu|info)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Contact();

    [GeneratedRegex(@"\d(?:[\s.\-/]?\d){6,}")]
    private static partial Regex Phone();

    [GeneratedRegex(@"\b(whatsapp|telegram|instagram|facebook|tiktok|snapchat|signal)\b", RegexOptions.IgnoreCase)]
    private static partial Regex OtherApps();

    [GeneratedRegex(@"[\p{C}\s]+")]
    private static partial Regex Whitespace();

    /// <summary>Testo ripulito (null se vuoto) oppure il motivo per cui non è accettato.</summary>
    public static (string? Note, string? Error) Check(string? raw)
    {
        if (raw is null) return (null, null);
        var text = Whitespace().Replace(raw, " ").Trim();
        if (text.Length == 0) return (null, null);
        if (text.Length > MaxLength) return (null, $"La frase può essere lunga al massimo {MaxLength} caratteri.");
        if (Contact().IsMatch(text) || Phone().IsMatch(text) || OtherApps().IsMatch(text))
            return (null, "Per la tua sicurezza non scrivere email, numeri di telefono, link o nomi di altre app: i contatti ve li scambiate quando vi siete conosciuti.");
        return (text, null);
    }
}
