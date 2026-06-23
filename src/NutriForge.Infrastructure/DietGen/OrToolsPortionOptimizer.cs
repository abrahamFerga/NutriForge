using Google.OrTools.LinearSolver;
using NutriForge.Application.DietGen;

namespace NutriForge.Infrastructure.DietGen;

/// <summary>
/// REPAIR step 5a — portion tuning as a linear program (OR-Tools GLOP). Holds the recipe selection
/// fixed and solves for per-slot servings in [min,max] that minimize the daily deviation from the
/// calorie (and optional protein) target. Fast, deterministic, optimal — the LLM is never involved.
/// </summary>
public sealed class OrToolsPortionOptimizer : IPortionOptimizer
{
    private const double ProteinWeight = 0.5;

    public IReadOnlyList<double> Optimize(
        IReadOnlyList<OptimizerSlot> slots, double dailyKcalTarget, double? dailyProteinTarget,
        double minServings, double maxServings)
    {
        ArgumentNullException.ThrowIfNull(slots);
        if (slots.Count == 0)
        {
            return [];
        }

        using var solver = Solver.CreateSolver("GLOP");
        if (solver is null)
        {
            return [.. slots.Select(_ => 1.0)];
        }

        var s = new Variable[slots.Count];
        for (var i = 0; i < slots.Count; i++)
        {
            s[i] = solver.MakeNumVar(minServings, maxServings, $"s{i}");
        }

        var objective = solver.Objective();
        objective.SetMinimization();

        foreach (var day in slots.Select((slot, i) => (slot, i)).GroupBy(x => x.slot.Day))
        {
            var members = day.ToList();
            AddDeviationTerm(solver, objective, s, members, dailyKcalTarget, x => x.KcalPerServing, weight: 1.0);
            if (dailyProteinTarget is { } proteinTarget and > 0)
            {
                AddDeviationTerm(solver, objective, s, members, proteinTarget, x => x.ProteinPerServing, ProteinWeight);
            }
        }

        var status = solver.Solve();
        if (status is not (Solver.ResultStatus.OPTIMAL or Solver.ResultStatus.FEASIBLE))
        {
            return [.. slots.Select(_ => Math.Clamp(1.0, minServings, maxServings))];
        }

        return [.. s.Select(v => v.SolutionValue())];
    }

    // Adds an L1 deviation term |Σ coeff_i·s_i − target| for one day, via an auxiliary dev var.
    private static void AddDeviationTerm(
        Solver solver, Objective objective, Variable[] s,
        List<(OptimizerSlot slot, int i)> members, double target,
        Func<OptimizerSlot, double> coeff, double weight)
    {
        var dev = solver.MakeNumVar(0, double.PositiveInfinity, $"dev{target}{members[0].i}");
        objective.SetCoefficient(dev, weight);

        // dev ≥ Σ coeff·s − target  ⇒  dev − Σ coeff·s ≥ −target
        var upper = solver.MakeConstraint(-target, double.PositiveInfinity);
        upper.SetCoefficient(dev, 1);
        // dev ≥ target − Σ coeff·s  ⇒  dev + Σ coeff·s ≥ target
        var lower = solver.MakeConstraint(target, double.PositiveInfinity);
        lower.SetCoefficient(dev, 1);

        foreach (var (slot, i) in members)
        {
            var c = coeff(slot);
            upper.SetCoefficient(s[i], -c);
            lower.SetCoefficient(s[i], c);
        }
    }
}
