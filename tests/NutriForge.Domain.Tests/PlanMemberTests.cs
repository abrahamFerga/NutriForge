using NutriForge.Domain.Planning;

namespace NutriForge.Domain.Tests;

public class PlanMemberTests
{
    [Theory]
    [InlineData(2000, 2000, 1.0)]   // "same as me"
    [InlineData(1000, 2000, 0.5)]   // half the target ⇒ half the portions
    [InlineData(3000, 2000, 1.5)]
    [InlineData(100, 2000, 0.25)]   // clamps UP from 0.05 (safety floor)
    [InlineData(20000, 2000, 3.0)]  // clamps DOWN from 10 (safety ceiling)
    public void FactorOf_scales_to_target_within_safety_bounds(double memberKcal, double planKcal, double expected) =>
        Assert.Equal(expected, MealPlan.FactorOf(memberKcal, planKcal), 3);

    [Fact]
    public void PortionMultiplier_with_no_members_falls_back_to_Eaters()
    {
        var plan = new MealPlan { TargetKcal = 2000, Eaters = 3 };
        Assert.Equal(3, plan.PortionMultiplier);
    }

    [Fact]
    public void PortionMultiplier_is_the_sum_of_member_portion_factors()
    {
        var plan = new MealPlan { TargetKcal = 2000 };
        plan.AddMember(new PlanMember { Name = "You", TargetKcal = 2000 });  // 1.0
        plan.AddMember(new PlanMember { Name = "GF", TargetKcal = 1000 });   // 0.5
        Assert.Equal(1.5, plan.PortionMultiplier, 3);
    }
}
