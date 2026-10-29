using System.Numerics;

namespace Runtivo.Api.Matching;

public record IntentProfile(int PaceMin, int PaceMax, int DistMin, int DistMax, RunType Type, int Mask, int RadiusKm);

public record MatchScore(int Total, IReadOnlyList<string> Reasons, int SharedMask);

/// <summary>
/// Compatibilità tra due annunci: formula leggibile, simmetrica e spiegabile (niente modelli opachi).
/// Pesi: passo 35, distanza 20, orari 25, vicinanza 15, tipo 5.
/// </summary>
public static class MatchScoring
{
    public const int MinTotal = 50;
    private const int MaxPaceGapSeconds = 60;

    private static readonly string[] DayNames = ["domenica", "lunedì", "martedì", "mercoledì", "giovedì", "venerdì", "sabato"];
    private static readonly string[] BandNames = ["mattina", "pausa pranzo", "sera"];

    public static MatchScore? Score(IntentProfile a, IntentProfile b, double distanceKm)
    {
        // Filtri obbligatori: ciascuno si sposta al massimo per il proprio raggio, quindi vale il minore.
        if (distanceKm > Math.Min(a.RadiusKm, b.RadiusKm)) return null;
        var shared = a.Mask & b.Mask;
        if (shared == 0) return null;
        if (Gap(a.PaceMin, a.PaceMax, b.PaceMin, b.PaceMax) > MaxPaceGapSeconds) return null;

        var pace = Fit(a.PaceMin, a.PaceMax, b.PaceMin, b.PaceMax, minWidth: 30, maxGap: 30);
        var distance = Fit(a.DistMin, a.DistMax, b.DistMin, b.DistMax, minWidth: 1000, maxGap: 3000);
        var time = (double)BitOperations.PopCount((uint)shared) / Math.Min(BitOperations.PopCount((uint)a.Mask), BitOperations.PopCount((uint)b.Mask));
        var radius = Math.Min(a.RadiusKm, b.RadiusKm);
        var proximity = Math.Clamp(1 - distanceKm / radius, 0, 1);
        var type = a.Type == b.Type ? 1.0 : 0.4;

        var total = (int)Math.Round(35 * pace + 20 * distance + 25 * time + 15 * proximity + 5 * type);
        if (total < MinTotal) return null;

        return new MatchScore(total, Reasons(pace, distance, shared, distanceKm, a.Type == b.Type, a.Type), shared);
    }

    /// <summary>Divario tra due intervalli (0 se si toccano o si sovrappongono).</summary>
    private static int Gap(int minA, int maxA, int minB, int maxB) => Math.Max(0, Math.Max(minA, minB) - Math.Min(maxA, maxB));

    /// <summary>
    /// Quanto due intervalli combaciano, da 0 a 1. Gli intervalli molto stretti (o a un solo valore) vengono
    /// allargati a una larghezza minima per non dividere per zero; senza sovrapposizione c'è un credito parziale
    /// se il divario è piccolo.
    /// </summary>
    private static double Fit(int minA, int maxA, int minB, int maxB, int minWidth, int maxGap)
    {
        var pad = minWidth / 2.0;
        var (loA, hiA, loB, hiB) = (minA - pad, maxA + pad, minB - pad, maxB + pad);
        var overlap = Math.Min(hiA, hiB) - Math.Max(loA, loB);
        if (overlap > 0) return Math.Min(1, overlap / Math.Min(hiA - loA, hiB - loB));

        var gap = Gap(minA, maxA, minB, maxB);
        return gap <= maxGap ? 0.3 * (1 - (double)gap / maxGap) : 0;
    }

    private static List<string> Reasons(double pace, double distance, int shared, double distanceKm, bool sameType, RunType type)
    {
        var reasons = new List<string>();
        if (pace >= 0.6) reasons.Add("Passo compatibile");
        else if (pace > 0) reasons.Add("Passo vicino al tuo");
        if (distance >= 0.6) reasons.Add("Distanze di corsa simili");

        var slots = Availability.Slots(shared).Select(s => $"{DayNames[(int)s.Day]} {BandNames[(int)s.Band]}").ToList();
        reasons.Add("Liberi insieme: " + (slots.Count <= 3 ? string.Join(", ", slots) : string.Join(", ", slots.Take(3)) + $" e altri {slots.Count - 3}"));

        reasons.Add(distanceKm < 1 ? "Nella stessa zona" : $"A circa {Math.Max(1, (int)Math.Round(distanceKm))} km da te");
        if (sameType) reasons.Add("Stessa voglia: corsa " + TypeLabel(type));
        return reasons;
    }

    public static string TypeLabel(RunType type) => type switch
    {
        RunType.Easy => "tranquilla",
        RunType.Training => "da allenamento",
        _ => "in compagnia",
    };
}
