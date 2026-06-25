namespace NutriForge.Application.DietGen;

/// <summary>
/// Closes the central allergen-safety gap (#60): the exclusion gate matches keywords as substrings of
/// ingredient names, so a bare declaration of <c>milk</c> would never catch <c>butter</c>, <c>cheese</c>,
/// or <c>whey</c>. This ontology expands a declared allergen into the common derivative/synonym terms that
/// signal its presence, so the gate excludes them too.
///
/// Safety bias: the expansion only ever ADDS exclusion terms (and always keeps the literal declaration),
/// so it can over-exclude (a "coconut" declaration also drops tree nuts) but can never let a covered
/// derivative slip through. Coverage is bounded by this table and by free-text ingredient names — see
/// docs/allergen-safety.md for the threat model and the residual risk (structured allergen tags are the
/// long-term fix).
/// </summary>
public static class AllergenOntology
{
    private sealed record Group(string[] Triggers, string[] Members);

    // Triggers: terms a user might DECLARE (matched if the declaration contains the trigger). Members:
    // ingredient terms to exclude when the group fires (each still matched as a substring downstream).
    private static readonly Group[] Groups =
    [
        new(["milk", "dairy", "lactose", "casein"],
            ["milk", "butter", "buttermilk", "cheese", "cheddar", "parmesan", "mozzarella", "cream", "yogurt", "yoghurt", "whey", "casein", "ghee", "custard", "paneer"]),
        new(["egg"],
            ["egg", "mayonnaise", "mayo", "albumin", "meringue", "aioli"]),
        new(["peanut", "groundnut", "arachis"],
            ["peanut", "groundnut", "arachis"]),
        new(["tree nut", "treenut", "nut", "nuts"],
            ["almond", "cashew", "walnut", "pecan", "pistachio", "hazelnut", "macadamia", "brazil nut", "pine nut", "praline", "marzipan", "nutella"]),
        new(["soy", "soya", "soybean"],
            ["soy", "soya", "soybean", "tofu", "edamame", "miso", "tempeh", "tamari"]),
        new(["wheat", "gluten"],
            ["wheat", "gluten", "flour", "bread", "pasta", "noodle", "semolina", "spelt", "couscous", "bulgur", "farro", "seitan", "barley", "rye", "cracker", "breadcrumb"]),
        new(["fish"],
            ["fish", "anchovy", "salmon", "tuna", "cod", "haddock", "sardine", "mackerel", "trout", "tilapia"]),
        new(["shellfish", "crustacean"],
            ["shellfish", "shrimp", "prawn", "crab", "lobster", "crayfish", "scampi", "langoustine"]),
        new(["sesame"],
            ["sesame", "tahini", "hummus"]),
    ];

    /// <summary>
    /// Expand declared allergens into the full set of exclusion keywords (declared literals + every
    /// triggered group's members). Trims input, drops blanks, and de-duplicates case-insensitively.
    /// </summary>
    public static IReadOnlyList<string> Expand(IEnumerable<string> declared)
    {
        ArgumentNullException.ThrowIfNull(declared);
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in declared)
        {
            var term = raw?.Trim();
            if (string.IsNullOrEmpty(term))
            {
                continue;
            }

            result.Add(term); // always keep the literal declaration — expansion only adds safety
            foreach (var group in Groups)
            {
                if (group.Triggers.Any(t => term.Contains(t, StringComparison.OrdinalIgnoreCase)))
                {
                    foreach (var member in group.Members)
                    {
                        result.Add(member);
                    }
                }
            }
        }

        return [.. result];
    }
}
