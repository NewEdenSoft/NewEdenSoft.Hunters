using NewEdenSoft.Hunters.Desktop.Map;

namespace NewEdenSoft.Hunters.Desktop.Tests.Map;

public sealed class GridIndexTests
{
    private readonly GridIndex _grid = new([0, 0, 2.5f, 0, -3, -3], cellSize: 1);

    [Fact]
    public void Finds_the_nearest_point_within_reach()
    {
        Assert.Equal(1, _grid.Nearest(2, 0.2f, maxDistance: 1));
        Assert.Equal(2, _grid.Nearest(-3.4f, -2.6f, maxDistance: 1));
    }

    [Fact]
    public void Finds_nothing_out_of_reach()
    {
        Assert.Equal(-1, _grid.Nearest(1.2f, 1.2f, maxDistance: 1));
    }

    [Fact]
    public void Skips_excluded_points()
    {
        Assert.Equal(0, _grid.Nearest(1.5f, 0, maxDistance: 2, include: i => i != 1));
    }
}
