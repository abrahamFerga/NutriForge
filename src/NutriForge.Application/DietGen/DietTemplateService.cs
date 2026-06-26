using Microsoft.EntityFrameworkCore;
using NutriForge.Application.Abstractions;
using NutriForge.Domain.Planning;

namespace NutriForge.Application.DietGen;

/// <summary>
/// CRUD for the user's saved diet-plan presets (#102): named sets of generation parameters they re-run in
/// one tap, since they tend to create the same kind of plan every time. Owner-scoped. The endpoint pairs
/// <see cref="ToCreateRequestAsync"/> with <see cref="DietPlanService.CreateAsync"/> for one-tap regenerate;
/// the people to cook for still come from the saved household (#100).
/// </summary>
public sealed class DietTemplateService(IAppDbContext db)
{
    private const int MaxTemplates = 24;
    private const int MaxNameLength = 120;

    public async Task<IReadOnlyList<DietTemplateDto>> ListAsync(Guid userId, CancellationToken ct = default)
    {
        var templates = await db.DietTemplates.AsNoTracking()
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.UpdatedAt)
            .ToListAsync(ct).ConfigureAwait(false);
        return [.. templates.Select(ToDto)];
    }

    /// <summary>Save a new preset. Returns null when the user is already at the cap.</summary>
    public async Task<DietTemplateDto?> AddAsync(Guid userId, UpsertDietTemplateRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);

        var count = await db.DietTemplates.CountAsync(t => t.UserId == userId, ct).ConfigureAwait(false);
        if (count >= MaxTemplates)
        {
            return null;
        }

        var template = new DietTemplate { UserId = userId };
        Apply(template, req);
        db.DietTemplates.Add(template);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(template);
    }

    /// <summary>Update an existing preset. Returns null when it doesn't exist for this user.</summary>
    public async Task<DietTemplateDto?> UpdateAsync(Guid userId, Guid id, UpsertDietTemplateRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);

        var template = await db.DietTemplates.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, ct).ConfigureAwait(false);
        if (template is null)
        {
            return null;
        }

        Apply(template, req);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(template);
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var template = await db.DietTemplates.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, ct).ConfigureAwait(false);
        if (template is null)
        {
            return false;
        }

        db.DietTemplates.Remove(template);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Build the plan-create request a saved preset regenerates, or null when it doesn't exist for this
    /// user. Members are left null so <see cref="DietPlanService"/> attaches the saved household (#100).
    /// </summary>
    public async Task<CreateDietPlanRequest?> ToCreateRequestAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var t = await db.DietTemplates.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct).ConfigureAwait(false);
        return t is null
            ? null
            : new CreateDietPlanRequest(
                Desire: t.Desire,
                KcalTarget: t.KcalTarget,
                DietSlug: t.DietSlug,
                ExcludeAllergens: null,
                Dislikes: null,
                MaxPrepMinutes: t.MaxPrepMinutes,
                MealsPerDay: t.MealsPerDay,
                HorizonDays: t.HorizonDays,
                Eaters: null,
                BlockSize: t.BlockSize,
                Members: null);
    }

    private static void Apply(DietTemplate t, UpsertDietTemplateRequest req)
    {
        var name = string.IsNullOrWhiteSpace(req.Name) ? "My diet" : req.Name.Trim();
        t.Name = name.Length > MaxNameLength ? name[..MaxNameLength] : name;
        t.DietSlug = string.IsNullOrWhiteSpace(req.DietSlug) ? null : req.DietSlug.Trim();
        t.KcalTarget = req.KcalTarget is > 0 ? req.KcalTarget : null;
        t.MaxPrepMinutes = req.MaxPrepMinutes is > 0 ? req.MaxPrepMinutes : null;
        t.MealsPerDay = req.MealsPerDay is > 0 ? req.MealsPerDay : null;
        t.HorizonDays = Math.Clamp(req.HorizonDays ?? 7, 1, 30);
        t.BlockSize = Math.Clamp(req.BlockSize ?? 1, 1, t.HorizonDays);
        t.Desire = string.IsNullOrWhiteSpace(req.Desire) ? null : req.Desire.Trim();
    }

    private static DietTemplateDto ToDto(DietTemplate t) => new(
        t.Id, t.Name, t.DietSlug, t.KcalTarget, t.MaxPrepMinutes, t.MealsPerDay, t.HorizonDays, t.BlockSize, t.Desire);
}
