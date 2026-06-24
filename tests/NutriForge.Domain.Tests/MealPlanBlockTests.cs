using NutriForge.Domain.Planning;

namespace NutriForge.Domain.Tests;

/// <summary>
/// Day-block rotation math on <see cref="MealPlan"/>: a plan is cooked as ⌈HorizonDays/BlockSize⌉
/// blocks, each covering BlockSize days (the last possibly shorter), and the block day-counts always
/// sum back to the horizon (no day lost or double-counted).
/// </summary>
public class MealPlanBlockTests
{
    [Theory]
    [InlineData(6, 3, 2)]   // two 3-day blocks
    [InlineData(7, 3, 3)]   // 3,3,1
    [InlineData(6, 1, 6)]   // default — a fresh meal-set every day
    [InlineData(5, 5, 1)]   // one block covering the whole horizon
    [InlineData(5, 10, 1)]  // blockSize clamps to the horizon
    [InlineData(6, 0, 6)]   // blockSize clamps up to 1
    public void NumBlocks_is_ceil_horizon_over_blocksize(int horizon, int blockSize, int expected)
    {
        var plan = new MealPlan { HorizonDays = horizon, BlockSize = blockSize };
        Assert.Equal(expected, plan.NumBlocks);
    }

    [Fact]
    public void BlockDaysFor_handles_an_uneven_final_block()
    {
        var plan = new MealPlan { HorizonDays = 7, BlockSize = 3 };
        Assert.Equal(3, plan.BlockDaysFor(1));
        Assert.Equal(3, plan.BlockDaysFor(2));
        Assert.Equal(1, plan.BlockDaysFor(3)); // final block is short
    }

    [Fact]
    public void BlockDaysFor_is_zero_out_of_range()
    {
        var plan = new MealPlan { HorizonDays = 6, BlockSize = 3 };
        Assert.Equal(0, plan.BlockDaysFor(0));
        Assert.Equal(0, plan.BlockDaysFor(3)); // only 2 blocks exist
    }

    [Theory]
    [InlineData(6, 3)]
    [InlineData(7, 3)]
    [InlineData(6, 1)]
    [InlineData(10, 4)]
    [InlineData(5, 7)]
    public void Block_day_counts_sum_to_the_horizon(int horizon, int blockSize)
    {
        var plan = new MealPlan { HorizonDays = horizon, BlockSize = blockSize };
        var total = Enumerable.Range(1, plan.NumBlocks).Sum(plan.BlockDaysFor);
        Assert.Equal(horizon, total); // every calendar day belongs to exactly one block
    }

    [Fact]
    public void Default_blocksize_is_one_distinct_set_per_day()
    {
        var plan = new MealPlan { HorizonDays = 4 }; // BlockSize defaults to 1
        Assert.Equal(4, plan.NumBlocks);
        Assert.All(Enumerable.Range(1, 4), b => Assert.Equal(1, plan.BlockDaysFor(b)));
    }
}
