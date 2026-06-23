using FluentValidation;
using NutriForge.Application.Food;
using NutriForge.Application.Tracking;

namespace NutriForge.Application.Validation;

public sealed class SubmitFoodRequestValidator : AbstractValidator<SubmitFoodRequest>
{
    public SubmitFoodRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Brand).MaximumLength(200);
        RuleFor(x => x.Gtin).Matches(@"^\d{8,14}$").When(x => !string.IsNullOrWhiteSpace(x.Gtin))
            .WithMessage("GTIN must be 8-14 digits.");
        RuleFor(x => x.KcalPer100g).InclusiveBetween(0, 900);
        RuleFor(x => x.ProteinPer100g).InclusiveBetween(0, 100);
        RuleFor(x => x.FatPer100g).InclusiveBetween(0, 100);
        RuleFor(x => x.CarbPer100g).InclusiveBetween(0, 100);
    }
}

public sealed class UpsertProfileRequestValidator : AbstractValidator<UpsertProfileRequest>
{
    public UpsertProfileRequestValidator()
    {
        RuleFor(x => x.HeightCm).InclusiveBetween(50, 272);
        RuleFor(x => x.WeightKg).InclusiveBetween(20, 640);
        RuleFor(x => x.BodyFatPct).InclusiveBetween(1, 75).When(x => x.BodyFatPct.HasValue);
        RuleFor(x => x.BirthDate)
            .LessThan(DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-13)))
            .WithMessage("Must be at least 13 years old.")
            .GreaterThan(DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-120)));
        RuleFor(x => x.Sex).IsInEnum();
        RuleFor(x => x.Activity).IsInEnum();
        RuleFor(x => x.Goal).IsInEnum();
        RuleFor(x => x.MacroStrategy).IsInEnum();
    }
}

public sealed class AddDiaryEntryRequestValidator : AbstractValidator<AddDiaryEntryRequest>
{
    public AddDiaryEntryRequestValidator()
    {
        RuleFor(x => x.FoodId).NotEmpty();
        RuleFor(x => x.MealSlot).IsInEnum();
        RuleFor(x => x.Quantity).GreaterThan(0).LessThanOrEqualTo(10000);
    }
}
