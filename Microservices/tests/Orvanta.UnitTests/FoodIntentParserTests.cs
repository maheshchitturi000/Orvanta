using Orvanta.FoodService.Services;

namespace Orvanta.UnitTests;

public class FoodIntentParserTests
{
    [Theory]
    [InlineData("I want chicken biryani", "chicken biryani")]
    [InlineData("order 2 paneer butter masala please", "paneer butter masala")]
    [InlineData("Can you get me a margherita pizza from Swiggy?", "margherita pizza")]
    [InlineData("I'm craving masala dosa", "masala dosa")]
    [InlineData("hungry, order me some momos", "momos")]
    public void Extracts_dish_query(string prompt, string expected)
    {
        var intent = FoodIntentParser.Parse(prompt);

        Assert.True(intent.IsFoodIntent);
        Assert.Equal(expected, intent.Query);
    }

    [Theory]
    [InlineData("What is the capital of France?")]
    [InlineData("Help me order my thoughts for an essay")]
    [InlineData("Write a poem about the sea")]
    public void Ignores_non_food_prompts(string prompt) =>
        Assert.False(FoodIntentParser.Parse(prompt).IsFoodIntent);

    [Fact]
    public void Detects_provider_veg_and_auto_pick()
    {
        var intent = FoodIntentParser.Parse("Order the top one veg biryani from zomato");

        Assert.Equal("zomato", intent.PreferredProvider);
        Assert.True(intent.VegOnly);
        Assert.True(intent.AutoPickTop);
        Assert.Equal("veg biryani", intent.Query);
    }

    [Fact]
    public void Non_veg_is_not_treated_as_veg() =>
        Assert.False(FoodIntentParser.Parse("non veg biryani").VegOnly);
}
