using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace BattleplanADC;

public sealed class NumberAssignment
{
    public required Unit Unit { get; init; }
    public required string Tier { get; init; }
    public int Number { get; init; }
    public bool Preserved { get; init; }
    public bool NewlyAllocated { get; init; }
}

public static class ScatteredNumberService
{
    // Only explicit player names can claim an observed number. A game-generated "1st
    // Regiment" is not evidence that the player intended Regiment 1.
    public static int? NumberInUserName(Unit unit)
    {
        var match = Regex.Match(unit.UserTypedName, @"^\s*(\d+)(?:st|nd|rd|th)?(?=\D|$)", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out var n) && n > 0 ? n : null;
    }
    public static int Maximum(string tier) => tier switch { "Division" => 90, "Regiment" => 300, _ => 0 };
    public static List<NumberAssignment> Assign(Campaign campaign, NamingCatalog catalog, IEnumerable<Unit> scope, string tier, Func<int, int>? randomIndex = null)
    {
        // Classify across the entire campaign before allocating inside the requested scope.
        // This prevents a selected branch from borrowing another branch's number.
        var maximum = Maximum(tier);
        if (maximum == 0) throw new ArgumentException("Scattered numbers are supported for Divisions and Regiments.");
        randomIndex ??= RandomNumberGenerator.GetInt32;
        var classified = campaign.EditableUnits.Where(u => catalog.Units.TryGetValue(u.Id, out var c) && c.Tier == tier).ToList();
        var reservations = catalog.ReservedNumbers;
        bool ReservedByOther(int number, Unit unit) => reservations.Any(r => r.Tier == tier && r.Number == number && r.UnitId != unit.Id);
        void Reserve(Unit unit, int number)
        {
            if (!reservations.Any(r => r.Tier == tier && r.Number == number && r.UnitId == unit.Id))
                reservations.Add(new NumberReservation { Tier = tier, Number = number, UnitId = unit.Id });
        }
        // Existing editor assignments claim their numbers first. Historical reservations are never released.
        foreach (var unit in classified)
        {
            var choice = catalog.Choice(unit);
            if (choice.Number is > 0 && !ReservedByOther(choice.Number.Value, unit)) Reserve(unit, choice.Number.Value);
        }
        // Only player-entered names count as visible existing numbers. OriginalName is ignored.
        foreach (var unit in classified)
        {
            var current = NumberInUserName(unit);
            if (current is > 0 && !ReservedByOther(current.Value, unit))
            {
                Reserve(unit, current.Value);
                var choice = catalog.Choice(unit);
                if (!choice.NumberOverride || choice.Number is null) choice.Number = current.Value;
            }
        }
        var scopeIds = scope.Select(u => u.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<NumberAssignment>();
        foreach (var unit in classified.Where(u => scopeIds.Contains(u.Id)))
        {
            var choice = catalog.Choice(unit);
            if (choice.Policy == "Exclude") continue;
            var existing = choice.Number ?? reservations.LastOrDefault(r => r.Tier == tier && r.UnitId == unit.Id)?.Number;
            if (existing is > 0 && ReservedByOther(existing.Value, unit)) existing = null;
            var newlyAllocated = existing is null;
            if (newlyAllocated)
            {
                // Randomness chooses among available values; reservations make later previews
                // deterministic for each unit and keep retired numbers unavailable.
                var available = Enumerable.Range(1, maximum).Where(n => !reservations.Any(r => r.Tier == tier && r.Number == n)).ToList();
                if (available.Count == 0) throw new InvalidOperationException($"No unused {tier.ToLowerInvariant()} numbers remain in 1–{maximum}.");
                var index = randomIndex(available.Count);
                if (index < 0 || index >= available.Count) throw new ArgumentOutOfRangeException(nameof(randomIndex));
                existing = available[index];
                choice.Number = existing;
                Reserve(unit, existing.Value);
            }
            choice.Number = existing;
            if (existing is null) throw new InvalidOperationException("Unable to assign a number.");
            var preserved = NumberInUserName(unit) == existing && !string.IsNullOrWhiteSpace(unit.UserTypedName);
            result.Add(new NumberAssignment { Unit = unit, Tier = tier, Number = existing.Value, Preserved = preserved, NewlyAllocated = newlyAllocated });
        }
        return result;
    }
}
