using ThixOffice.Text.Drawing.Geometry;
using ThixShow.Core.Model;

namespace ThixShow.Core.Drawing;

/// <summary>
/// ThixShow's side of the shared geometry engine (<see cref="GeometryEvaluator"/> in ThixOffice.Text): a <see cref="ShapeGeometry"/>
/// of the model (preset or custom XML, adjust values) evaluated for a size.
/// </summary>
public static class GeometryEngine
{
    /// <summary>The definition of a geometry (the rectangle for null; null for an unknown preset).</summary>
    public static GeometryDefinition? DefinitionOf(ShapeGeometry? geometry) =>
        geometry is null ? PresetGeometries.Get("rect") : GeometryEvaluator.DefinitionOf(geometry.Preset, geometry.Custom?.Xml);

    /// <summary>Evaluates <paramref name="geometry"/> for a box of <paramref name="width"/> × <paramref name="height"/>.</summary>
    public static EvaluatedGeometry Evaluate(ShapeGeometry? geometry, double width, double height)
    {
        var definition = DefinitionOf(geometry);
        var known = definition is not null;
        definition ??= PresetGeometries.Get("rect")!;
        return GeometryEvaluator.Evaluate(definition, Adjustments(geometry), width, height, known);
    }

    /// <summary>The adjust values that put handle <paramref name="handleIndex"/> nearest to (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public static IReadOnlyList<GeometryAdjust> AdjustForHandle(ShapeGeometry geometry, int handleIndex, double width, double height, double x, double y) =>
        [.. GeometryEvaluator.AdjustForHandle(DefinitionOf(geometry), Adjustments(geometry), handleIndex, width, height, x, y).Select(adjust => new GeometryAdjust(adjust.Name, adjust.Value))];

    /// <summary>Evaluates one guide formula (ECMA-376 20.1.9.11).</summary>
    public static double Formula(string formula, IReadOnlyDictionary<string, double> variables) => GeometryEvaluator.Formula(formula, variables);

    private static IReadOnlyList<GeometryAdjustment> Adjustments(ShapeGeometry? geometry) =>
        geometry is null ? [] : [.. geometry.Adjustments.Select(adjust => new GeometryAdjustment(adjust.Name, adjust.Value))];
}
