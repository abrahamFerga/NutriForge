namespace NutriForge.Domain.Common;

/// <summary>Biological sex, used by the BMR equation (Mifflin-St Jeor constant).</summary>
public enum Sex
{
    Male = 1,
    Female = 2,
}

/// <summary>Activity multiplier applied to BMR to derive TDEE. See docs/algorithms/tdee-and-macros.md.</summary>
public enum ActivityLevel
{
    Sedentary = 1,
    LightlyActive = 2,
    ModeratelyActive = 3,
    Active = 4,
    VeryActive = 5,
}

/// <summary>Body-composition goal, applied as a percentage delta to TDEE.</summary>
public enum Goal
{
    AggressiveCut = 1,
    ModerateCut = 2,
    Maintain = 3,
    LeanBulk = 4,
    Bulk = 5,
}

/// <summary>How the calorie target is split into protein/fat/carbs.</summary>
public enum MacroStrategy
{
    /// <summary>Protein from bodyweight, fat floor, carbs fill the remainder (recommended).</summary>
    ProteinAnchored = 1,

    /// <summary>AMDR percentage split.</summary>
    Percentage = 2,
}

/// <summary>The meal a diary entry belongs to.</summary>
public enum MealSlot
{
    Breakfast = 1,
    Lunch = 2,
    Dinner = 3,
    Snack = 4,
}

/// <summary>
/// Food catalog verification tier. Higher tiers are more trustworthy and are ranked first;
/// tiers are never silently blended (SPEC: "curated &gt; authoritative &gt; community &gt; user-submitted").
/// </summary>
public enum VerificationStatus
{
    Curated = 1,
    Authoritative = 2,
    Community = 3,
    UserSubmitted = 4,
}
