using ThixOffice.Text.Drawing;
using ThixShow.Core.Drawing;
using ThixShow.Core.Model;

namespace ThixShow.Core.Tests.Model;

public class GeometryTests
{
    private static EvaluatedGeometry Evaluate(string preset, double width, double height, params (string Name, long Value)[] adjusts) =>
        GeometryEngine.Evaluate(new ShapeGeometry(preset, adjusts.Select(adjust => new GeometryAdjust(adjust.Name, adjust.Value)).ToList()), width, height);

    [Fact]
    public void Rectangle_is_its_box()
    {
        var geometry = Evaluate("rect", 100, 50);
        Assert.Equal("M 0 0 L 100 0 L 100 50 L 0 50 Z", geometry.Paths.Single().Path.ToString());
        Assert.Equal((0.0, 0.0, 100.0, 50.0), (geometry.TextLeft, geometry.TextTop, geometry.TextRight, geometry.TextBottom));
    }

    [Fact]
    public void Right_arrow_follows_its_guides()
    {
        // ss = 50: head length dx1 = ss · 50000 / 100000 = 25; shaft half height = h · 50000 / 200000 = 12.5.
        var geometry = Evaluate("rightArrow", 100, 50);
        Assert.Equal("M 0 12.5 L 75 12.5 L 75 0 L 100 25 L 75 50 L 75 37.5 L 0 37.5 Z", geometry.Paths.Single().Path.ToString());
        var thin = Evaluate("rightArrow", 100, 50, ("adj1", 20000), ("adj2", 80000));
        Assert.Equal("M 0 20 L 60 20 L 60 0 L 100 25 L 60 50 L 60 30 L 0 30 Z", thin.Paths.Single().Path.ToString());
    }

    [Fact]
    public void Round_rectangle_has_the_corner_radius_of_its_adjust_value()
    {
        var geometry = Evaluate("roundRect", 100, 50);
        var path = geometry.Paths.Single().Path;
        var radius = 50 * 16667 / 100000.0;
        Assert.Equal(0, path.Points[0], 6);
        Assert.Equal(radius, path.Points[1], 6);
        var bounds = path.Bounds();
        Assert.Equal((0.0, 0.0, 100.0, 50.0), (Math.Round(bounds.Left, 6), Math.Round(bounds.Top, 6), Math.Round(bounds.Right, 6), Math.Round(bounds.Bottom, 6)));
        // Text inset: il = radius · 0.29289.
        Assert.Equal(radius * 0.29289, geometry.TextLeft, 4);
    }

    [Fact]
    public void Ellipse_passes_through_the_midpoints_of_its_box()
    {
        var path = Evaluate("ellipse", 200, 100).Paths.Single().Path;
        var points = path.Points;
        Assert.Equal((0.0, 50.0), (points[0], points[1]));
        var ends = new List<(double, double)>();
        path.Visit((verb, values) =>
        {
            if (verb == PathVerb.CubicTo)
            {
                ends.Add((Math.Round(values[4], 6), Math.Round(values[5], 6)));
            }
        });
        Assert.Equal([(100.0, 0.0), (200.0, 50.0), (100.0, 100.0), (0.0, 50.0)], ends);
    }

    [Fact]
    public void Triangle_apex_moves_with_its_adjust_value()
    {
        Assert.Equal("M 0 80 L 50 0 L 100 80 Z", Evaluate("triangle", 100, 80).Paths.Single().Path.ToString());
        Assert.Equal("M 0 80 L 0 0 L 100 80 Z", Evaluate("triangle", 100, 80, ("adj", 0)).Paths.Single().Path.ToString());
    }

    [Fact]
    public void Stars_have_their_first_point_on_top_and_the_right_number_of_points()
    {
        foreach (var (name, count) in new[] { ("star4", 4), ("star5", 5), ("star6", 6), ("star8", 8), ("star12", 12), ("star32", 32) })
        {
            var path = Evaluate(name, 100, 100).Paths.Single().Path;
            Assert.Equal(50, path.Points[0], 6);
            Assert.Equal(count * 2, path.Verbs.Count(verb => verb is PathVerb.MoveTo or PathVerb.LineTo));
        }
    }

    [Fact]
    public void Pie_quarter_ends_at_the_bottom()
    {
        var path = Evaluate("pie", 100, 60, ("adj1", 0), ("adj2", 5400000)).Paths.Single().Path;
        Assert.Equal((100.0, 30.0), (Math.Round(path.Points[0], 6), Math.Round(path.Points[1], 6)));
        var points = path.Points;
        // Last command before close: the line to the centre; the arc ended at (50, 60).
        Assert.Equal((50.0, 30.0), (points[^2], points[^1]));
        Assert.Equal((50.0, 60.0), (Math.Round(points[^4], 6), Math.Round(points[^3], 6)));
    }

    [Fact]
    public void Wedge_callout_tail_reaches_its_tip()
    {
        // adj1 -20833, adj2 62500: tip at hc - 0.208 w, vc + 0.625 h – below the box, so the tail leaves the bottom side.
        var path = Evaluate("wedgeRectCallout", 120, 80).Paths.Single().Path;
        var tipX = 60 - (120 * 0.20833);
        var tipY = 40 + (80 * 0.625);
        var found = false;
        for (var index = 0; index + 1 < path.Points.Count; index += 2)
        {
            found |= Math.Abs(path.Points[index] - tipX) < 0.01 && Math.Abs(path.Points[index + 1] - tipY) < 0.01;
        }

        Assert.True(found);
    }

    [Fact]
    public void Every_preset_evaluates_to_finite_non_empty_paths()
    {
        Assert.True(PresetGeometries.Names.Count >= 120, $"{PresetGeometries.Names.Count} presets");
        foreach (var name in PresetGeometries.Names)
        {
            foreach (var (width, height) in new[] { (100.0, 60.0), (40.0, 120.0), (10.0, 10.0) })
            {
                var geometry = GeometryEngine.Evaluate(ShapeGeometry.Of(name), width, height);
                Assert.True(geometry.IsKnown, name);
                Assert.NotEmpty(geometry.Paths);
                foreach (var path in geometry.Paths)
                {
                    Assert.False(path.Path.IsEmpty, name);
                    Assert.All(path.Path.Points, value => Assert.True(double.IsFinite(value), $"{name}: {value}"));
                }

                Assert.True(double.IsFinite(geometry.TextLeft + geometry.TextTop + geometry.TextRight + geometry.TextBottom), name);
            }
        }
    }

    [Fact]
    public void Unknown_preset_falls_back_to_its_rectangle()
    {
        var geometry = GeometryEngine.Evaluate(ShapeGeometry.Of("gear9"), 100, 50);
        Assert.False(geometry.IsKnown);
        Assert.Equal("M 0 0 L 100 0 L 100 50 L 0 50 Z", geometry.Paths.Single().Path.ToString());
    }

    [Fact]
    public void Handle_sits_on_its_guide_and_dragging_it_solves_the_adjust_value()
    {
        var geometry = ShapeGeometry.Of("roundRect");
        var evaluated = GeometryEngine.Evaluate(geometry, 100, 50);
        var handle = evaluated.Handles.Single();
        Assert.Equal(50 * 0.16667, handle.X, 3);
        Assert.Equal(0, handle.Y);
        var adjusts = GeometryEngine.AdjustForHandle(geometry, 0, 100, 50, 20, 0);
        Assert.Equal("adj", adjusts.Single().Name);
        Assert.InRange(adjusts.Single().Value, 39990, 40010);
        var clamped = GeometryEngine.AdjustForHandle(geometry, 0, 100, 50, 90, 0);
        Assert.InRange(clamped.Single().Value, 49990, 50000);
    }

    [Fact]
    public void Custom_geometry_is_read_from_its_xml()
    {
        const string xml = """
            <a:custGeom xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <a:avLst/><a:gdLst><a:gd name="half" fmla="*/ w 1 2"/></a:gdLst><a:ahLst/><a:cxnLst/>
              <a:rect l="l" t="t" r="r" b="b"/>
              <a:pathLst>
                <a:path w="10" h="10"><a:moveTo><a:pt x="0" y="10"/></a:moveTo><a:lnTo><a:pt x="5" y="0"/></a:lnTo><a:lnTo><a:pt x="10" y="10"/></a:lnTo><a:close/></a:path>
                <a:path fill="none"><a:moveTo><a:pt x="half" y="0"/></a:moveTo><a:lnTo><a:pt x="half" y="h"/></a:lnTo></a:path>
              </a:pathLst>
            </a:custGeom>
            """;
        var geometry = GeometryEngine.Evaluate(new ShapeGeometry(null, Custom: new XmlFragment(xml)), 200, 100);
        Assert.Equal("M 0 100 L 100 0 L 200 100 Z", geometry.Paths[0].Path.ToString());
        Assert.Equal(PathFillMode.None, geometry.Paths[1].Fill);
        Assert.Equal("M 100 0 L 100 100", geometry.Paths[1].Path.ToString());
    }

    [Theory]
    [InlineData("*/ 10 3 2", 15)]
    [InlineData("+- 10 3 2", 11)]
    [InlineData("+/ 10 2 4", 3)]
    [InlineData("?: 1 5 6", 5)]
    [InlineData("?: 0 5 6", 6)]
    [InlineData("abs -4", 4)]
    [InlineData("max 3 7", 7)]
    [InlineData("min 3 7", 3)]
    [InlineData("mod 3 4 0", 5)]
    [InlineData("pin 0 120 100", 100)]
    [InlineData("sqrt 16", 4)]
    [InlineData("cos 10 5400000", 0)]
    [InlineData("sin 10 5400000", 10)]
    [InlineData("at2 1 1", 2700000)]
    [InlineData("cat2 10 1 0", 10)]
    [InlineData("sat2 10 0 1", 10)]
    public void Formulas_follow_the_specification(string formula, double expected)
    {
        Assert.Equal(expected, GeometryEngine.Formula(formula, new Dictionary<string, double>()), 6);
    }
}
