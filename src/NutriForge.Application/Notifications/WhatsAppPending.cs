using NutriForge.Domain.Common;

namespace NutriForge.Application.Notifications;

/// <summary>A single confirmed food item waiting for the user to say YES.</summary>
public sealed record WhatsAppPendingEntry(
    DateOnly Date,
    MealSlot MealSlot,
    Guid FoodId,
    Guid? PortionId,
    double Quantity,
    string FoodName,
    double Kcal);

/// <summary>Parse results cached in Redis while the user decides YES / NO.</summary>
public sealed record WhatsAppPending(IReadOnlyList<WhatsAppPendingEntry> Entries);
