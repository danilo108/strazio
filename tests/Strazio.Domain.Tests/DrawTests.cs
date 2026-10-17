using Strazio.Domain;

namespace Strazio.Domain.Tests;

public class DrawTests
{
    private static Draw Example(DateOnly? date = null, int competition = 1, int oro = 30, int second = 25, IEnumerable<int>? main = null, IEnumerable<int>? extra = null) =>
        new(date ?? new(2026, 10, 1), competition, main ?? [10, 12, 20, 25, 27, 30, 31, 39, 42, 43, 46, 51, 53, 66, 73, 74, 75, 76, 83, 85],
            oro, second, extra ?? [2, 4, 11, 24, 28, 40, 44, 55, 59, 65, 70, 71, 77, 79, 88]);

    [Fact]
    public void Mapping_preserves_source_order_and_removes_both_specials()
    {
        var row = DrawValidation.Normalize(Example());
        Assert.Equal(new[] { 10, 12, 20, 27, 31, 39, 42, 43, 46, 51, 53, 66, 73, 74, 75, 76, 83, 85 }, row.MainNumbers18);
        Assert.Equal(30, row.Oro);
        Assert.Equal(25, row.DoppioOroSecondNumber);
        Assert.Equal(new[] { 2, 4, 11, 24, 28, 40, 44, 55, 59, 65, 70, 71, 77, 79, 88 }, row.ExtraNumbers15);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(96)]
    [InlineData(287)]
    [InlineData(288)]
    public void Partial_days_are_valid(int count) => DrawValidation.Validate(new DrawDay(new(2026, 10, 1), Enumerable.Range(1, count).Select(i => Example(competition: i)), "test", true));

    [Fact] public void Non_contiguous_competitions_are_valid() => DrawValidation.Validate(new DrawDay(new(2026, 10, 1), [Example(competition: 1), Example(competition: 288)], "test", false));
    [Theory][InlineData(0)][InlineData(289)] public void Competition_out_of_range_fails(int number) => Assert.Throws<InvalidDrawException>(() => DrawValidation.Validate(Example(competition: number)));
    [Theory][InlineData(30, 30)][InlineData(1, 25)] public void Special_numbers_must_be_distinct_and_in_main(int oro, int second) => Assert.Throws<InvalidDrawException>(() => DrawValidation.Validate(Example(oro: oro, second: second)));
    [Fact] public void Duplicate_competitions_fail() => Assert.Throws<InvalidDrawException>(() => DrawValidation.Validate(new DrawDay(new(2026, 10, 1), [Example(), Example()], "test", false)));
    [Fact] public void Duplicate_main_numbers_fail() => Assert.Throws<InvalidDrawException>(() => DrawValidation.Validate(Example(main: Enumerable.Repeat(10, 20))));
    [Fact] public void Invalid_extra_fails() => Assert.Throws<InvalidDrawException>(() => DrawValidation.Validate(Example(extra: Enumerable.Range(80, 15))));
    [Fact] public void Historical_extra_is_optional() => DrawValidation.Validate(new Draw(new(2020, 9, 9), 1, Enumerable.Range(1, 20), 1, 2, null));
    [Fact] public void Modern_extra_is_required() => Assert.Throws<InvalidDrawException>(() => DrawValidation.Validate(new Draw(new(2020, 9, 10), 1, Enumerable.Range(1, 20), 1, 2, null)));
    [Fact]
    public void Draw_snapshots_mutable_input()
    {
        var numbers = Enumerable.Range(1, 20).ToArray();
        var draw = new Draw(new(2020, 9, 9), 1, numbers, 1, 2, null);
        numbers[0] = 90;
        Assert.Equal(1, draw.MainNumbers20[0]);
    }
}
