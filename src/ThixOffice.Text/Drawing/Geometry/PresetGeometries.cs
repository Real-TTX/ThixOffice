using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace ThixOffice.Text.Drawing.Geometry;

/// <summary>
/// The preset shapes ThixShow draws, written from the guide formulas of ECMA-376 Part 1 Annex D (presetShapeDefinitions.xml) in
/// the compact form of <see cref="GeometryDefinition.Parse"/>. The regular stars (star4 … star32) and the line callouts are
/// generated from the same formulas. Presets not listed here are drawn as their rectangle and kept as they are.
/// </summary>
public static class PresetGeometries
{
    private static readonly ConcurrentDictionary<string, GeometryDefinition?> Cache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> Sources = Build();

    /// <summary>Every preset name with a definition, sorted.</summary>
    public static IReadOnlyList<string> Names { get; } = [.. Sources.Keys.Order(StringComparer.Ordinal)];

    /// <summary>The definition of a preset, or null when ThixShow does not know it.</summary>
    public static GeometryDefinition? Get(string preset) =>
        Cache.GetOrAdd(preset, name => Sources.TryGetValue(name, out var source) ? GeometryDefinition.Parse(source) : null);

    public static bool IsKnown(string preset) => Sources.ContainsKey(preset);

    private static Dictionary<string, string> Build()
    {
        var shapes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["rect"] = """
                p : M l t L r t L r b L l b Z
                """,
            ["roundRect"] = """
                av adj 16667
                gd a pin 0 adj 50000
                gd x1 */ ss a 100000
                gd x2 +- r 0 x1
                gd y2 +- b 0 x1
                gd il */ x1 29289 100000
                gd ir +- r 0 il
                gd ib +- b 0 il
                ah xy adj 0 50000 - - - x1 t
                tx il il ir ib
                p : M l x1 A x1 x1 cd2 cd4 L x2 t A x1 x1 3cd4 cd4 L r y2 A x1 x1 0 cd4 L x1 b A x1 x1 cd4 cd4 Z
                """,
            ["ellipse"] = """
                gd idx cos wd2 2700000
                gd idy sin hd2 2700000
                gd il +- hc 0 idx
                gd ir +- hc idx 0
                gd it +- vc 0 idy
                gd ib +- vc idy 0
                tx il it ir ib
                p : M l vc A wd2 hd2 cd2 cd4 A wd2 hd2 3cd4 cd4 A wd2 hd2 0 cd4 A wd2 hd2 cd4 cd4 Z
                """,
            ["triangle"] = """
                av adj 50000
                gd a pin 0 adj 100000
                gd x1 */ w a 200000
                gd x2 */ w a 100000
                gd x3 +- x1 wd2 0
                ah xy adj 0 100000 - - - x2 t
                tx x1 vc x3 b
                p : M l b L x2 t L r b Z
                """,
            ["rtTriangle"] = """
                gd it */ h 7 12
                gd ir */ w 7 12
                gd ib */ h 11 12
                tx l it ir ib
                p : M l b L l t L r b Z
                """,
            ["diamond"] = """
                gd ir */ w 3 4
                gd ib */ h 3 4
                tx wd4 hd4 ir ib
                p : M l vc L hc t L r vc L hc b Z
                """,
            ["parallelogram"] = """
                av adj 25000
                gd maxAdj */ 100000 w ss
                gd a pin 0 adj maxAdj
                gd x1 */ ss a 200000
                gd x2 */ ss a 100000
                gd x6 +- r 0 x1
                gd x5 +- r 0 x2
                gd il */ wd2 a maxAdj
                gd ir +- r 0 il
                gd it */ h a 400000
                gd ib +- b 0 it
                ah xy adj 0 maxAdj - - - x2 t
                tx il it ir ib
                p : M l b L x2 t L r t L x5 b Z
                """,
            ["trapezoid"] = """
                av adj 25000
                gd maxAdj */ 50000 w ss
                gd a pin 0 adj maxAdj
                gd x1 */ ss a 200000
                gd x2 */ ss a 100000
                gd x3 +- r 0 x2
                gd x4 +- r 0 x1
                gd il */ wd3 a maxAdj
                gd it */ hd3 a maxAdj
                gd ir +- r 0 il
                ah xy adj 0 maxAdj - - - x2 t
                tx il it ir b
                p : M l b L x2 t L x3 t L r b Z
                """,
            ["pentagon"] = """
                av hf 105146
                av vf 110557
                gd swd2 */ wd2 hf 100000
                gd shd2 */ hd2 vf 100000
                gd svc */ vc vf 100000
                gd dx1 cos swd2 1080000
                gd dx2 cos swd2 18360000
                gd dy1 sin shd2 1080000
                gd dy2 sin shd2 18360000
                gd x1 +- hc 0 dx1
                gd x2 +- hc 0 dx2
                gd x3 +- hc dx2 0
                gd x4 +- hc dx1 0
                gd y1 +- svc 0 dy1
                gd y2 +- svc 0 dy2
                gd it */ y1 dx2 dx1
                tx x2 it x3 y2
                p : M x1 y1 L hc t L x4 y1 L x3 y2 L x2 y2 Z
                """,
            ["hexagon"] = """
                av adj 25000
                av vf 115470
                gd maxAdj */ 50000 w ss
                gd a pin 0 adj maxAdj
                gd shd2 */ hd2 vf 100000
                gd x1 */ ss a 100000
                gd x2 +- r 0 x1
                gd dy1 sin shd2 3600000
                gd y1 +- vc 0 dy1
                gd y2 +- vc dy1 0
                gd q1 */ maxAdj -1 2
                gd q2 +- a q1 0
                gd q3 ?: q2 4 2
                gd q4 ?: q2 3 2
                gd q5 ?: q2 q1 0
                gd q6 +/ a q5 q1
                gd q7 */ q6 q4 -1
                gd q8 +- q3 q7 0
                gd il */ w q8 24
                gd it */ h q8 24
                gd ir +- r 0 il
                gd ib +- b 0 it
                ah xy adj 0 maxAdj - - - x1 t
                tx il it ir ib
                p : M l vc L x1 y1 L x2 y1 L r vc L x2 y2 L x1 y2 Z
                """,
            ["heptagon"] = """
                av hf 102572
                av vf 105210
                gd swd2 */ wd2 hf 100000
                gd shd2 */ hd2 vf 100000
                gd svc */ vc vf 100000
                gd dx1 */ swd2 97493 100000
                gd dx2 */ swd2 78183 100000
                gd dx3 */ swd2 43388 100000
                gd dy1 */ shd2 62349 100000
                gd dy2 */ shd2 22252 100000
                gd dy3 */ shd2 90097 100000
                gd x1 +- hc 0 dx1
                gd x2 +- hc 0 dx2
                gd x3 +- hc 0 dx3
                gd x4 +- hc dx3 0
                gd x5 +- hc dx2 0
                gd x6 +- hc dx1 0
                gd y1 +- svc 0 dy1
                gd y2 +- svc dy2 0
                gd y3 +- svc dy3 0
                tx x2 y1 x5 y3
                p : M x1 y2 L x2 y1 L hc t L x5 y1 L x6 y2 L x4 y3 L x3 y3 Z
                """,
            ["octagon"] = """
                av adj 29289
                gd a pin 0 adj 50000
                gd x1 */ ss a 100000
                gd x2 +- r 0 x1
                gd y2 +- b 0 x1
                gd il */ x1 1 2
                gd ir +- r 0 il
                gd ib +- b 0 il
                ah xy adj 0 50000 - - - x1 t
                tx il il ir ib
                p : M l x1 L x1 t L x2 t L r x1 L r y2 L x2 b L x1 b L l y2 Z
                """,
            ["decagon"] = """
                av vf 105146
                gd shd2 */ hd2 vf 100000
                gd dx1 cos wd2 2160000
                gd dx2 cos wd2 4320000
                gd x1 +- hc 0 dx1
                gd x2 +- hc 0 dx2
                gd x3 +- hc dx2 0
                gd x4 +- hc dx1 0
                gd dy1 sin shd2 4320000
                gd dy2 sin shd2 2160000
                gd y1 +- vc 0 dy1
                gd y2 +- vc 0 dy2
                gd y3 +- vc dy2 0
                gd y4 +- vc dy1 0
                tx x1 y2 x4 y3
                p : M l vc L x1 y2 L x2 y1 L x3 y1 L x4 y2 L r vc L x4 y3 L x3 y4 L x2 y4 L x1 y3 Z
                """,
            ["dodecagon"] = """
                gd x1 */ w 2894 21600
                gd x2 */ w 7906 21600
                gd x3 */ w 13694 21600
                gd x4 */ w 18706 21600
                gd y1 */ h 2894 21600
                gd y2 */ h 7906 21600
                gd y3 */ h 13694 21600
                gd y4 */ h 18706 21600
                tx x1 y1 x4 y4
                p : M l y2 L x1 y1 L x2 t L x3 t L x4 y1 L r y2 L r y3 L x4 y4 L x3 b L x2 b L x1 y4 L l y3 Z
                """,
            ["plus"] = """
                av adj 25000
                gd a pin 0 adj 50000
                gd x1 */ ss a 100000
                gd x2 +- r 0 x1
                gd y2 +- b 0 x1
                gd d +- w 0 h
                gd il ?: d l x1
                gd ir ?: d r x2
                gd it ?: d x1 t
                gd ib ?: d y2 b
                ah xy adj 0 50000 - - - x1 t
                tx il it ir ib
                p : M l x1 L x1 x1 L x1 t L x2 t L x2 x1 L r x1 L r y2 L x2 y2 L x2 b L x1 b L x1 y2 L l y2 Z
                """,
            ["donut"] = """
                av adj 25000
                gd a pin 0 adj 50000
                gd dr */ ss a 100000
                gd iwd2 +- wd2 0 dr
                gd ihd2 +- hd2 0 dr
                gd idx cos wd2 2700000
                gd idy sin hd2 2700000
                gd il +- hc 0 idx
                gd ir +- hc idx 0
                gd it +- vc 0 idy
                gd ib +- vc idy 0
                ah xy adj 0 50000 - - - dr vc
                tx il it ir ib
                p : M l vc A wd2 hd2 cd2 cd4 A wd2 hd2 3cd4 cd4 A wd2 hd2 0 cd4 A wd2 hd2 cd4 cd4 Z M dr vc A iwd2 ihd2 cd2 -5400000 A iwd2 ihd2 cd4 -5400000 A iwd2 ihd2 0 -5400000 A iwd2 ihd2 3cd4 -5400000 Z
                """,
            ["can"] = """
                av adj 25000
                gd maxAdj */ 50000 h ss
                gd a pin 0 adj maxAdj
                gd y1 */ ss a 200000
                gd y2 +- y1 y1 0
                gd y3 +- b 0 y1
                ah xy - - - adj 0 maxAdj hc y2
                tx l y2 r y3
                p stroke=false : M l y1 A wd2 y1 cd2 -10800000 L r y3 A wd2 y1 0 cd2 Z
                p fill=lighten stroke=false : M l y1 A wd2 y1 cd2 cd2 A wd2 y1 0 cd2 Z
                p fill=none : M r y1 A wd2 y1 0 cd2 A wd2 y1 cd2 cd2 L r y3 A wd2 y1 0 cd2 L l y1
                """,
            ["cube"] = """
                av adj 25000
                gd a pin 0 adj 100000
                gd y1 */ ss a 100000
                gd y4 +- b 0 y1
                gd x4 +- r 0 y1
                ah xy - - - adj 0 100000 l y1
                tx l y1 x4 b
                p stroke=false : M l y1 L x4 y1 L x4 b L l b Z
                p fill=darkenLess stroke=false : M x4 y1 L r t L r y4 L x4 b Z
                p fill=lightenLess stroke=false : M l y1 L y1 t L r t L x4 y1 Z
                p fill=none : M l y1 L y1 t L r t L r y4 L x4 b L l b Z M l y1 L x4 y1 L r t M x4 y1 L x4 b
                """,
            ["bevel"] = """
                av adj 12500
                gd a pin 0 adj 50000
                gd x1 */ ss a 100000
                gd x2 +- r 0 x1
                gd y2 +- b 0 x1
                ah xy adj 0 50000 - - - x1 vc
                tx x1 x1 x2 y2
                p stroke=false : M x1 x1 L x2 x1 L x2 y2 L x1 y2 Z
                p fill=lightenLess stroke=false : M l t L r t L x2 x1 L x1 x1 Z
                p fill=darkenLess stroke=false : M l b L x1 y2 L x2 y2 L r b Z
                p fill=lighten stroke=false : M l t L x1 x1 L x1 y2 L l b Z
                p fill=darken stroke=false : M r t L r b L x2 y2 L x2 x1 Z
                p fill=none : M l t L r t L r b L l b Z M x1 x1 L x2 x1 L x2 y2 L x1 y2 Z M l t L x1 x1 M l b L x1 y2 M r t L x2 x1 M r b L x2 y2
                """,
            ["foldedCorner"] = """
                av adj 16667
                gd a pin 0 adj 50000
                gd dy2 */ ss a 100000
                gd dy1 */ dy2 1 5
                gd x1 +- r 0 dy2
                gd x2 +- x1 dy1 0
                gd y2 +- b 0 dy2
                gd y1 +- y2 dy1 0
                ah xy adj 0 50000 - - - x1 b
                tx l t r y2
                p stroke=false : M l t L r t L r y2 L x1 b L l b Z
                p fill=darkenLess stroke=false : M x1 b L x2 y1 L r y2 Z
                p fill=none : M x1 b L x2 y1 L r y2 L x1 b L l b L l t L r t L r y2
                """,
            ["frame"] = """
                av adj1 12500
                gd a1 pin 0 adj1 50000
                gd x1 */ ss a1 100000
                gd x4 +- r 0 x1
                gd y4 +- b 0 x1
                ah xy adj1 0 50000 - - - x1 t
                tx x1 x1 x4 y4
                p : M l t L r t L r b L l b Z M x1 x1 L x1 y4 L x4 y4 L x4 x1 Z
                """,
            ["halfFrame"] = """
                av adj1 33333
                av adj2 33333
                gd maxAdj2 */ 100000 w ss
                gd a2 pin 0 adj2 maxAdj2
                gd x1 */ ss a2 100000
                gd g1 */ h x1 w
                gd g2 +- h 0 g1
                gd maxAdj1 */ 100000 g2 ss
                gd a1 pin 0 adj1 maxAdj1
                gd y1 */ ss a1 100000
                gd dx2 */ y1 w h
                gd x2 +- r 0 dx2
                gd dy2 */ x1 h w
                gd y2 +- b 0 dy2
                ah xy - - - adj1 0 maxAdj1 l y1
                ah xy adj2 0 maxAdj2 - - - x1 t
                p : M l t L r t L x2 y1 L x1 y1 L x1 y2 L l b Z
                """,
            ["heart"] = """
                gd dx1 */ w 49 48
                gd dx2 */ w 10 48
                gd x1 +- hc 0 dx1
                gd x2 +- hc 0 dx2
                gd x3 +- hc dx2 0
                gd x4 +- hc dx1 0
                gd y1 +- t 0 hd3
                gd il */ w 1 6
                gd ir */ w 5 6
                gd ib */ h 2 3
                tx il hd4 ir ib
                p : M hc hd4 C x3 y1 x4 hd4 hc b C x1 hd4 x2 y1 hc hd4 Z
                """,
            ["moon"] = """
                av adj 50000
                gd a pin 0 adj 87500
                gd g0 */ ss a 100000
                gd g0w */ g0 w ss
                gd g1 +- ss 0 g0
                gd g2 */ g0 g0 g1
                gd g3 */ ss ss g1
                gd g4 */ g3 2 1
                gd g5 +- g4 0 g2
                gd g6 +- g5 0 g0
                gd g6w */ g6 w ss
                gd g7 */ g5 1 2
                gd g8 +- g7 0 g0
                gd dy1 */ g8 hd2 ss
                gd g10h +- vc 0 dy1
                gd g11h +- vc dy1 0
                gd g12 */ g0 9598 32768
                gd g12w */ g12 w ss
                gd g13 +- ss 0 g12
                gd q1 */ ss ss 1
                gd q2 */ g13 g13 1
                gd q3 +- q1 0 q2
                gd q4 sqrt q3
                gd dy4 */ q4 hd2 ss
                gd g15h +- vc 0 dy4
                gd g16h +- vc dy4 0
                gd g17w +- g6w 0 g0w
                gd g18w */ g17w 1 2
                gd dx2p +- g0w g18w w
                gd dx2 */ dx2p -1 1
                gd dy2 */ hd2 -1 1
                gd stAng1 at2 dx2 dy2
                gd enAngp1 at2 dx2 hd2
                gd enAng1 +- enAngp1 0 21600000
                gd swAng1 +- enAng1 0 stAng1
                ah xy adj 0 87500 - - - g0w vc
                tx g12w g15h g0w g16h
                p : M r b A w hd2 cd4 cd2 A g18w dy1 stAng1 swAng1 Z
                """,
            ["smileyFace"] = """
                av adj 4653
                gd a pin -4653 adj 4653
                gd x1 */ w 4969 21699
                gd x2 */ w 6215 21600
                gd x3 */ w 13135 21600
                gd x4 */ w 16640 21600
                gd y1 */ h 7570 21600
                gd y3 */ h 16515 21600
                gd dy2 */ h a 100000
                gd y2 +- y3 0 dy2
                gd y4 +- y3 dy2 0
                gd dy3 */ h a 50000
                gd y5 +- y4 dy3 0
                gd idx cos wd2 2700000
                gd idy sin hd2 2700000
                gd il +- hc 0 idx
                gd ir +- hc idx 0
                gd it +- vc 0 idy
                gd ib +- vc idy 0
                gd wR */ w 1653 21600
                gd hR */ h 1653 21600
                ah xy - - - adj -4653 4653 hc y4
                tx il it ir ib
                p stroke=false : M l vc A wd2 hd2 cd2 21600000 Z
                p fill=darkenLess : M x2 y1 A wR hR cd2 21600000 M x3 y1 A wR hR cd2 21600000
                p fill=none : M x1 y2 Q hc y5 x4 y2
                p fill=none : M l vc A wd2 hd2 cd2 21600000 Z
                """,
            ["teardrop"] = """
                av adj 100000
                gd a pin 0 adj 200000
                gd r2 sqrt 2
                gd tw */ r2 wd2 1
                gd th */ r2 hd2 1
                gd sw */ tw a 100000
                gd sh */ th a 100000
                gd dx1 cos sw 2700000
                gd dy1 sin sh 2700000
                gd x1 +- hc dx1 0
                gd y1 +- vc 0 dy1
                gd x2 +/ hc x1 2
                gd y2 +/ vc y1 2
                gd idx cos wd2 2700000
                gd idy sin hd2 2700000
                gd il +- hc 0 idx
                gd ir +- hc idx 0
                gd it +- vc 0 idy
                gd ib +- vc idy 0
                ah xy adj 0 200000 - - - x1 t
                tx il it ir ib
                p : M l vc A wd2 hd2 cd2 cd4 Q x2 t x1 y1 Q r y2 r vc A wd2 hd2 0 cd4 A wd2 hd2 cd4 cd4 Z
                """,
            ["pie"] = Arc("p : M x1 y1 A wd2 hd2 stAng swAng L hc vc Z", 0, 16200000),
            ["chord"] = Arc("p : M x1 y1 A wd2 hd2 stAng swAng Z", 2700000, 16200000),
            ["arc"] = Arc("p stroke=false : M x1 y1 A wd2 hd2 stAng swAng L hc vc Z\np fill=none : M x1 y1 A wd2 hd2 stAng swAng", 16200000, 0),
            ["corner"] = """
                av adj1 50000
                av adj2 50000
                gd maxAdj1 */ 100000 h ss
                gd maxAdj2 */ 100000 w ss
                gd a1 pin 0 adj1 maxAdj1
                gd a2 pin 0 adj2 maxAdj2
                gd x1 */ ss a2 100000
                gd dy1 */ ss a1 100000
                gd y1 +- b 0 dy1
                ah xy - - - adj1 0 maxAdj1 l y1
                ah xy adj2 0 maxAdj2 - - - x1 t
                tx l y1 r b
                p : M l t L x1 t L x1 y1 L r y1 L r b L l b Z
                """,
            ["diagStripe"] = """
                av adj 50000
                gd a pin 0 adj 100000
                gd x2 */ w a 100000
                gd x1 */ x2 1 2
                gd x3 +/ x2 r 2
                gd y2 */ h a 100000
                gd y1 */ y2 1 2
                gd y3 +/ y2 b 2
                ah xy - - - adj 0 100000 l y2
                tx l t x3 y3
                p : M l y2 L x2 t L r t L l b Z
                """,
            ["plaque"] = """
                av adj 16667
                gd a pin 0 adj 50000
                gd x1 */ ss a 100000
                gd x2 +- r 0 x1
                gd y2 +- b 0 x1
                gd il */ x1 70711 100000
                gd ir +- r 0 il
                gd ib +- b 0 il
                ah xy adj 0 50000 - - - x1 t
                tx il il ir ib
                p : M l x1 A x1 x1 cd4 -5400000 L x2 t A x1 x1 cd2 -5400000 L r y2 A x1 x1 3cd4 -5400000 L x1 b A x1 x1 0 -5400000 Z
                """,
            ["snip1Rect"] = """
                av adj 16667
                gd a pin 0 adj 50000
                gd dx1 */ ss a 100000
                gd x1 +- r 0 dx1
                gd it */ dx1 1 2
                gd ir +/ x1 r 2
                ah xy adj 0 50000 - - - x1 t
                tx l it ir b
                p : M l t L x1 t L r dx1 L r b L l b Z
                """,
            ["snip2SameRect"] = """
                av adj1 16667
                av adj2 0
                gd a1 pin 0 adj1 50000
                gd a2 pin 0 adj2 50000
                gd tx1 */ ss a1 100000
                gd tx2 +- r 0 tx1
                gd bx1 */ ss a2 100000
                gd bx2 +- r 0 bx1
                gd by1 +- b 0 bx1
                gd d +- tx1 0 bx1
                gd dx ?: d tx1 bx1
                gd il */ dx 1 2
                gd ir +- r 0 il
                gd it */ tx1 1 2
                gd ib +/ by1 b 2
                ah xy adj1 0 50000 - - - tx2 t
                ah xy adj2 0 50000 - - - bx1 b
                tx il it ir ib
                p : M tx1 t L tx2 t L r tx1 L r by1 L bx2 b L bx1 b L l by1 L l tx1 Z
                """,
            ["snip2DiagRect"] = """
                av adj1 0
                av adj2 16667
                gd a1 pin 0 adj1 50000
                gd a2 pin 0 adj2 50000
                gd lx1 */ ss a1 100000
                gd lx2 +- r 0 lx1
                gd ly1 +- b 0 lx1
                gd rx1 */ ss a2 100000
                gd rx2 +- r 0 rx1
                gd ry1 +- b 0 rx1
                gd d +- lx1 0 rx1
                gd dx ?: d lx1 rx1
                gd il */ dx 1 2
                gd ir +- r 0 il
                gd ib +- b 0 il
                ah xy adj1 0 50000 - - - lx1 t
                ah xy adj2 0 50000 - - - rx2 t
                tx il il ir ib
                p : M lx1 t L rx2 t L r rx1 L r ly1 L lx2 b L rx1 b L l ry1 L l lx1 Z
                """,
            ["snipRoundRect"] = """
                av adj1 16667
                av adj2 16667
                gd a1 pin 0 adj1 50000
                gd a2 pin 0 adj2 50000
                gd x1 */ ss a1 100000
                gd dx2 */ ss a2 100000
                gd x2 +- r 0 dx2
                gd il */ x1 29289 100000
                gd ir +/ x2 r 2
                ah xy adj1 0 50000 - - - x1 t
                ah xy adj2 0 50000 - - - x2 t
                tx il il ir b
                p : M x1 t L x2 t L r dx2 L r b L l b L l x1 A x1 x1 cd2 cd4 Z
                """,
            ["round1Rect"] = """
                av adj 16667
                gd a pin 0 adj 50000
                gd dx1 */ ss a 100000
                gd x1 +- r 0 dx1
                gd idx */ dx1 29289 100000
                gd ir +- r 0 idx
                ah xy adj 0 50000 - - - x1 t
                tx l t ir b
                p : M l t L x1 t A dx1 dx1 3cd4 cd4 L r b L l b Z
                """,
            ["round2SameRect"] = """
                av adj1 16667
                av adj2 0
                gd a1 pin 0 adj1 50000
                gd a2 pin 0 adj2 50000
                gd tx1 */ ss a1 100000
                gd tx2 +- r 0 tx1
                gd bx1 */ ss a2 100000
                gd bx2 +- r 0 bx1
                gd by1 +- b 0 bx1
                gd d +- tx1 0 bx1
                gd tdx */ tx1 29289 100000
                gd bdx */ bx1 29289 100000
                gd il ?: d tdx bdx
                gd ir +- r 0 il
                gd ib +- b 0 bdx
                ah xy adj1 0 50000 - - - tx2 t
                ah xy adj2 0 50000 - - - bx1 b
                tx il tdx ir ib
                p : M tx1 t L tx2 t A tx1 tx1 3cd4 cd4 L r by1 A bx1 bx1 0 cd4 L bx1 b A bx1 bx1 cd4 cd4 L l tx1 A tx1 tx1 cd2 cd4 Z
                """,
            ["round2DiagRect"] = """
                av adj1 16667
                av adj2 0
                gd a1 pin 0 adj1 50000
                gd a2 pin 0 adj2 50000
                gd x1 */ ss a1 100000
                gd y1 +- b 0 x1
                gd a */ ss a2 100000
                gd x2 +- r 0 a
                gd y2 +- b 0 a
                gd dx1 */ x1 29289 100000
                gd dx2 */ a 29289 100000
                gd d +- dx1 0 dx2
                gd dx ?: d dx1 dx2
                gd ir +- r 0 dx
                gd ib +- b 0 dx
                ah xy adj1 0 50000 - - - x1 t
                ah xy adj2 0 50000 - - - x2 t
                tx dx dx ir ib
                p : M x1 t L x2 t A a a 3cd4 cd4 L r y1 A x1 x1 0 cd4 L a b A a a cd4 cd4 L l x1 A x1 x1 cd2 cd4 Z
                """,
            ["rightArrow"] = """
                av adj1 50000
                av adj2 50000
                gd maxAdj2 */ 100000 w ss
                gd a1 pin 0 adj1 100000
                gd a2 pin 0 adj2 maxAdj2
                gd dx1 */ ss a2 100000
                gd x1 +- r 0 dx1
                gd dy1 */ h a1 200000
                gd y1 +- vc 0 dy1
                gd y2 +- vc dy1 0
                gd dx2 */ y1 dx1 hd2
                gd x2 +- x1 dx2 0
                ah xy - - - adj1 0 100000 l y1
                ah xy adj2 0 maxAdj2 - - - x1 t
                tx l y1 x2 y2
                p : M l y1 L x1 y1 L x1 t L r vc L x1 b L x1 y2 L l y2 Z
                """,
            ["leftArrow"] = """
                av adj1 50000
                av adj2 50000
                gd maxAdj2 */ 100000 w ss
                gd a1 pin 0 adj1 100000
                gd a2 pin 0 adj2 maxAdj2
                gd dx2 */ ss a2 100000
                gd x2 +- l dx2 0
                gd dy1 */ h a1 200000
                gd y1 +- vc 0 dy1
                gd y2 +- vc dy1 0
                gd dx1 */ y1 dx2 hd2
                gd x1 +- x2 0 dx1
                ah xy - - - adj1 0 100000 r y1
                ah xy adj2 0 maxAdj2 - - - x2 t
                tx x1 y1 r y2
                p : M l vc L x2 t L x2 y1 L r y1 L r y2 L x2 y2 L x2 b Z
                """,
            ["upArrow"] = """
                av adj1 50000
                av adj2 50000
                gd maxAdj2 */ 100000 h ss
                gd a1 pin 0 adj1 100000
                gd a2 pin 0 adj2 maxAdj2
                gd dy2 */ ss a2 100000
                gd y2 +- t dy2 0
                gd dx1 */ w a1 200000
                gd x1 +- hc 0 dx1
                gd x2 +- hc dx1 0
                gd dy1 */ x1 dy2 wd2
                gd y1 +- y2 0 dy1
                ah xy adj1 0 100000 - - - x1 b
                ah xy - - - adj2 0 maxAdj2 l y2
                tx x1 y1 x2 b
                p : M l y2 L hc t L r y2 L x2 y2 L x2 b L x1 b L x1 y2 Z
                """,
            ["downArrow"] = """
                av adj1 50000
                av adj2 50000
                gd maxAdj2 */ 100000 h ss
                gd a1 pin 0 adj1 100000
                gd a2 pin 0 adj2 maxAdj2
                gd dy1 */ ss a2 100000
                gd y1 +- b 0 dy1
                gd dx1 */ w a1 200000
                gd x1 +- hc 0 dx1
                gd x2 +- hc dx1 0
                gd dy2 */ x1 dy1 wd2
                gd y2 +- y1 dy2 0
                ah xy adj1 0 100000 - - - x1 t
                ah xy - - - adj2 0 maxAdj2 r y1
                tx x1 t x2 y2
                p : M l y1 L x1 y1 L x1 t L x2 t L x2 y1 L r y1 L hc b Z
                """,
            ["leftRightArrow"] = """
                av adj1 50000
                av adj2 50000
                gd maxAdj2 */ 50000 w ss
                gd a1 pin 0 adj1 100000
                gd a2 pin 0 adj2 maxAdj2
                gd x2 */ ss a2 100000
                gd x3 +- r 0 x2
                gd dy */ h a1 200000
                gd y1 +- vc 0 dy
                gd y2 +- vc dy 0
                gd dx1 */ y1 x2 hd2
                gd x1 +- x2 0 dx1
                gd x4 +- x3 dx1 0
                ah xy - - - adj1 0 100000 x3 y1
                ah xy adj2 0 maxAdj2 - - - x2 t
                tx x1 y1 x4 y2
                p : M l vc L x2 t L x2 y1 L x3 y1 L x3 t L r vc L x3 b L x3 y2 L x2 y2 L x2 b Z
                """,
            ["upDownArrow"] = """
                av adj1 50000
                av adj2 50000
                gd maxAdj2 */ 50000 h ss
                gd a1 pin 0 adj1 100000
                gd a2 pin 0 adj2 maxAdj2
                gd y2 */ ss a2 100000
                gd y3 +- b 0 y2
                gd dx1 */ w a1 200000
                gd x1 +- hc 0 dx1
                gd x2 +- hc dx1 0
                gd dy1 */ x1 y2 wd2
                gd y1 +- y2 0 dy1
                gd y4 +- y3 dy1 0
                ah xy adj1 0 100000 - - - x1 y3
                ah xy - - - adj2 0 maxAdj2 l y2
                tx x1 y1 x2 y4
                p : M l y2 L hc t L r y2 L x2 y2 L x2 y3 L r y3 L hc b L l y3 L x1 y3 L x1 y2 Z
                """,
            ["notchedRightArrow"] = """
                av adj1 50000
                av adj2 50000
                gd maxAdj2 */ 100000 w ss
                gd a1 pin 0 adj1 100000
                gd a2 pin 0 adj2 maxAdj2
                gd dx2 */ ss a2 100000
                gd x2 +- r 0 dx2
                gd dy1 */ h a1 200000
                gd y1 +- vc 0 dy1
                gd y2 +- vc dy1 0
                gd x1 */ dy1 dx2 hd2
                gd x3 +- r 0 x1
                ah xy - - - adj1 0 100000 r y1
                ah xy adj2 0 maxAdj2 - - - x2 t
                tx x1 y1 x3 y2
                p : M l y1 L x2 y1 L x2 t L r vc L x2 b L x2 y2 L l y2 L x1 vc Z
                """,
            ["stripedRightArrow"] = """
                av adj1 50000
                av adj2 50000
                gd maxAdj2 */ 84375 w ss
                gd a1 pin 0 adj1 100000
                gd a2 pin 0 adj2 maxAdj2
                gd x4 */ ss 5 32
                gd dx5 */ ss a2 100000
                gd x5 +- r 0 dx5
                gd dy1 */ h a1 200000
                gd y1 +- vc 0 dy1
                gd y2 +- vc dy1 0
                gd dx6 */ dy1 dx5 hd2
                gd x6 +- r 0 dx6
                ah xy - - - adj1 0 100000 l y1
                ah xy adj2 0 maxAdj2 - - - x5 t
                tx x4 y1 x6 y2
                p : M l y1 L ssd32 y1 L ssd32 y2 L l y2 Z M ssd16 y1 L ssd8 y1 L ssd8 y2 L ssd16 y2 Z M x4 y1 L x5 y1 L x5 t L r vc L x5 b L x5 y2 L x4 y2 Z
                """,
            ["quadArrow"] = """
                av adj1 22500
                av adj2 22500
                av adj3 22500
                gd a2 pin 0 adj2 50000
                gd maxAdj1 */ a2 2 1
                gd a1 pin 0 adj1 maxAdj1
                gd q1 +- 100000 0 maxAdj1
                gd maxAdj3 */ q1 1 2
                gd a3 pin 0 adj3 maxAdj3
                gd x1 */ ss a3 100000
                gd dx2 */ ss a2 100000
                gd x2 +- hc 0 dx2
                gd x5 +- hc dx2 0
                gd dx3 */ ss a1 200000
                gd x3 +- hc 0 dx3
                gd x4 +- hc dx3 0
                gd x6 +- r 0 x1
                gd y2 +- vc 0 dx2
                gd y5 +- vc dx2 0
                gd y3 +- vc 0 dx3
                gd y4 +- vc dx3 0
                gd y6 +- b 0 x1
                gd il */ dx3 x1 dx2
                gd ir +- r 0 il
                tx il y3 ir y4
                p : M l vc L x1 y2 L x1 y3 L x3 y3 L x3 x1 L x2 x1 L hc t L x5 x1 L x4 x1 L x4 y3 L x6 y3 L x6 y2 L r vc L x6 y5 L x6 y4 L x4 y4 L x4 y6 L x5 y6 L hc b L x2 y6 L x3 y6 L x3 y4 L x1 y4 L x1 y5 Z
                """,
            ["homePlate"] = """
                av adj 50000
                gd maxAdj */ 100000 w ss
                gd a pin 0 adj maxAdj
                gd dx1 */ ss a 100000
                gd x1 +- r 0 dx1
                gd ir +/ x1 r 2
                ah xy adj 0 maxAdj - - - x1 t
                tx l t ir b
                p : M l t L x1 t L r vc L x1 b L l b Z
                """,
            ["chevron"] = """
                av adj 50000
                gd maxAdj */ 100000 w ss
                gd a pin 0 adj maxAdj
                gd x1 */ ss a 100000
                gd x2 +- r 0 x1
                gd dx +- x2 0 x1
                gd il ?: dx x1 l
                gd ir ?: dx x2 r
                ah xy adj 0 maxAdj - - - x2 t
                tx il t ir b
                p : M l t L x2 t L r vc L x2 b L l b L x1 vc Z
                """,
            ["wedgeRectCallout"] = Callout(rounded: false),
            ["wedgeRoundRectCallout"] = Callout(rounded: true),
            ["wedgeEllipseCallout"] = """
                av adj1 -20833
                av adj2 62500
                gd dxPos */ w adj1 100000
                gd dyPos */ h adj2 100000
                gd xPos +- hc dxPos 0
                gd yPos +- vc dyPos 0
                gd sdx */ dxPos h 1
                gd sdy */ dyPos w 1
                gd pang at2 sdx sdy
                gd stAng +- pang 660000 0
                gd enAng +- pang 0 660000
                gd dx1 cos wd2 stAng
                gd dy1 sin hd2 stAng
                gd x1 +- hc dx1 0
                gd y1 +- vc dy1 0
                gd dx2 cos wd2 enAng
                gd dy2 sin hd2 enAng
                gd stAng1 at2 dx1 dy1
                gd enAng1 at2 dx2 dy2
                gd swAng1 +- enAng1 0 stAng1
                gd swAng2 +- swAng1 21600000 0
                gd swAng ?: swAng1 swAng1 swAng2
                gd idx cos wd2 2700000
                gd idy sin hd2 2700000
                gd il +- hc 0 idx
                gd ir +- hc idx 0
                gd it +- vc 0 idy
                gd ib +- vc idy 0
                ah xy adj1 -2147483647 2147483647 adj2 -2147483647 2147483647 xPos yPos
                tx il it ir ib
                p : M xPos yPos L x1 y1 A wd2 hd2 stAng1 swAng Z
                """,
            ["line"] = """
                p fill=none : M l t L r b
                """,
            ["lineInv"] = """
                p fill=none : M l b L r t
                """,
            ["straightConnector1"] = """
                p fill=none : M l t L r b
                """,
            ["bentConnector2"] = """
                p fill=none : M l t L r t L r b
                """,
            ["bentConnector3"] = """
                av adj1 50000
                gd x1 */ w adj1 100000
                ah xy adj1 -2147483647 2147483647 - - - x1 vc
                p fill=none : M l t L x1 t L x1 b L r b
                """,
            ["bentConnector4"] = """
                av adj1 50000
                av adj2 50000
                gd x1 */ w adj1 100000
                gd y2 */ h adj2 100000
                ah xy adj1 -2147483647 2147483647 - - - x1 hd4
                ah xy - - - adj2 -2147483647 2147483647 wd2 y2
                p fill=none : M l t L x1 t L x1 y2 L r y2 L r b
                """,
            ["bentConnector5"] = """
                av adj1 50000
                av adj2 50000
                av adj3 50000
                gd x1 */ w adj1 100000
                gd x3 */ w adj3 100000
                gd y2 */ h adj2 100000
                p fill=none : M l t L x1 t L x1 y2 L x3 y2 L x3 b L r b
                """,
            ["curvedConnector2"] = """
                p fill=none : M l t C wd2 t r hd2 r b
                """,
            ["curvedConnector3"] = """
                av adj1 50000
                gd x2 */ w adj1 100000
                gd x1 +/ l x2 2
                gd x3 +/ r x2 2
                gd y3 */ h 3 4
                ah xy adj1 -2147483647 2147483647 - - - x2 vc
                p fill=none : M l t C x1 t x2 hd4 x2 vc C x2 y3 x3 b r b
                """,
            ["curvedConnector4"] = """
                av adj1 50000
                av adj2 50000
                gd x2 */ w adj1 100000
                gd x1 +/ l x2 2
                gd x3 +/ r x2 2
                gd x4 +/ x2 x3 2
                gd x5 +/ x3 r 2
                gd y4 */ h adj2 100000
                gd y1 +/ t y4 2
                gd y2 +/ t y1 2
                gd y3 +/ y1 y4 2
                gd y5 +/ b y4 2
                p fill=none : M l t C x1 t x2 y2 x2 y1 C x2 y3 x4 y4 x3 y4 C x5 y4 r y5 r b
                """,
            ["leftBracket"] = """
                av adj 8333
                gd maxAdj */ 50000 h ss
                gd a pin 0 adj maxAdj
                gd y1 */ ss a 100000
                gd y2 +- b 0 y1
                gd dx1 cos w 2700000
                gd dy1 sin y1 2700000
                gd il +- r 0 dx1
                gd it +- y1 0 dy1
                gd ib +- b dy1 y1
                ah xy - - - adj 0 maxAdj l y1
                tx il it r ib
                p stroke=false : M r b A w y1 cd4 cd4 L l y1 A w y1 cd2 cd4 Z
                p fill=none : M r b A w y1 cd4 cd4 L l y1 A w y1 cd2 cd4
                """,
            ["rightBracket"] = """
                av adj 8333
                gd maxAdj */ 50000 h ss
                gd a pin 0 adj maxAdj
                gd y1 */ ss a 100000
                gd y2 +- b 0 y1
                gd dx1 cos w 2700000
                gd dy1 sin y1 2700000
                gd ir +- l dx1 0
                gd it +- y1 0 dy1
                gd ib +- b dy1 y1
                ah xy - - - adj 0 maxAdj r y1
                tx l it ir ib
                p stroke=false : M l t A w y1 3cd4 cd4 L r y2 A w y1 0 cd4 Z
                p fill=none : M l t A w y1 3cd4 cd4 L r y2 A w y1 0 cd4
                """,
            ["leftBrace"] = """
                av adj1 8333
                av adj2 50000
                gd a2 pin 0 adj2 100000
                gd q1 +- 100000 0 a2
                gd q2 min q1 a2
                gd q3 */ q2 1 2
                gd maxAdj1 */ q3 h ss
                gd a1 pin 0 adj1 maxAdj1
                gd y1 */ ss a1 100000
                gd y3 */ h a2 100000
                gd y4 +- y3 y1 0
                gd dx1 cos wd2 2700000
                gd dy1 sin y1 2700000
                gd il +- r 0 dx1
                gd it +- y1 0 dy1
                gd ib +- b dy1 y1
                ah xy - - - adj1 0 maxAdj1 hc y1
                ah xy - - - adj2 0 100000 l y3
                tx il it r ib
                p stroke=false : M r b A wd2 y1 cd4 cd4 L hc y4 A wd2 y1 0 -5400000 A wd2 y1 cd4 -5400000 L hc y1 A wd2 y1 cd2 cd4 Z
                p fill=none : M r b A wd2 y1 cd4 cd4 L hc y4 A wd2 y1 0 -5400000 A wd2 y1 cd4 -5400000 L hc y1 A wd2 y1 cd2 cd4
                """,
            ["rightBrace"] = """
                av adj1 8333
                av adj2 50000
                gd a2 pin 0 adj2 100000
                gd q1 +- 100000 0 a2
                gd q2 min q1 a2
                gd q3 */ q2 1 2
                gd maxAdj1 */ q3 h ss
                gd a1 pin 0 adj1 maxAdj1
                gd y1 */ ss a1 100000
                gd y3 */ h a2 100000
                gd y2 +- y3 0 y1
                gd y4 +- b 0 y1
                gd dx1 cos wd2 2700000
                gd dy1 sin y1 2700000
                gd ir +- l dx1 0
                gd it +- y1 0 dy1
                gd ib +- b dy1 y1
                ah xy - - - adj1 0 maxAdj1 hc y1
                ah xy - - - adj2 0 100000 r y3
                tx l it ir ib
                p stroke=false : M l t A wd2 y1 3cd4 cd4 L hc y2 A wd2 y1 cd2 -5400000 A wd2 y1 3cd4 -5400000 L hc y4 A wd2 y1 0 cd4 Z
                p fill=none : M l t A wd2 y1 3cd4 cd4 L hc y2 A wd2 y1 cd2 -5400000 A wd2 y1 3cd4 -5400000 L hc y4 A wd2 y1 0 cd4
                """,
            ["bracketPair"] = """
                av adj 16667
                gd a pin 0 adj 50000
                gd x1 */ ss a 100000
                gd x2 +- r 0 x1
                gd y2 +- b 0 x1
                gd il */ x1 29289 100000
                gd ir +- r 0 il
                gd ib +- b 0 il
                ah xy - - - adj 0 50000 l x1
                tx il il ir ib
                p stroke=false : M l x1 A x1 x1 cd2 cd4 L x2 t A x1 x1 3cd4 cd4 L r y2 A x1 x1 0 cd4 L x1 b A x1 x1 cd4 cd4 Z
                p fill=none : M x1 b A x1 x1 cd4 cd4 L l x1 A x1 x1 cd2 cd4 M x2 t A x1 x1 3cd4 cd4 L r y2 A x1 x1 0 cd4
                """,
            ["bracePair"] = """
                av adj 8333
                gd a pin 0 adj 25000
                gd x1 */ ss a 100000
                gd x2 */ ss a 50000
                gd x3 +- r 0 x2
                gd x4 +- r 0 x1
                gd y2 +- vc 0 x1
                gd y3 +- vc x1 0
                gd y4 +- b 0 x1
                gd it */ x1 29289 100000
                gd il +- x1 it 0
                gd ir +- r 0 il
                gd ib +- b 0 it
                ah xy - - - adj 0 25000 l x1
                tx il il ir ib
                p stroke=false : M x2 b A x1 x1 cd4 cd4 L x1 y3 A x1 x1 0 -5400000 A x1 x1 cd4 -5400000 L x1 x1 A x1 x1 cd2 cd4 L x3 t A x1 x1 3cd4 cd4 L x4 y2 A x1 x1 cd2 -5400000 A x1 x1 3cd4 -5400000 L x4 y4 A x1 x1 0 cd4 Z
                p fill=none : M x2 b A x1 x1 cd4 cd4 L x1 y3 A x1 x1 0 -5400000 A x1 x1 cd4 -5400000 L x1 x1 A x1 x1 cd2 cd4 M x3 t A x1 x1 3cd4 cd4 L x4 y2 A x1 x1 cd2 -5400000 A x1 x1 3cd4 -5400000 L x4 y4 A x1 x1 0 cd4
                """,
            ["mathPlus"] = """
                av adj1 23520
                gd a1 pin 0 adj1 73490
                gd dx1 */ w 73490 200000
                gd dy1 */ h 73490 200000
                gd dx2 */ ss a1 200000
                gd x1 +- hc 0 dx1
                gd x2 +- hc 0 dx2
                gd x3 +- hc dx2 0
                gd x4 +- hc dx1 0
                gd y1 +- vc 0 dy1
                gd y2 +- vc 0 dx2
                gd y3 +- vc dx2 0
                gd y4 +- vc dy1 0
                tx x1 y2 x4 y3
                p : M x1 y2 L x2 y2 L x2 y1 L x3 y1 L x3 y2 L x4 y2 L x4 y3 L x3 y3 L x3 y4 L x2 y4 L x2 y3 L x1 y3 Z
                """,
            ["mathMinus"] = """
                av adj1 23520
                gd a1 pin 0 adj1 100000
                gd dy1 */ h a1 200000
                gd dx1 */ w 73490 200000
                gd y1 +- vc 0 dy1
                gd y2 +- vc dy1 0
                gd x1 +- hc 0 dx1
                gd x2 +- hc dx1 0
                tx x1 y1 x2 y2
                p : M x1 y1 L x2 y1 L x2 y2 L x1 y2 Z
                """,
            ["mathEqual"] = """
                av adj1 23520
                av adj2 11760
                gd a1 pin 0 adj1 36745
                gd 2a1 */ a1 2 1
                gd mAdj2 +- 100000 0 2a1
                gd a2 pin 0 adj2 mAdj2
                gd dy1 */ h a1 100000
                gd dy2 */ h a2 200000
                gd dx1 */ w 73490 200000
                gd y2 +- vc 0 dy2
                gd y3 +- vc dy2 0
                gd y1 +- y2 0 dy1
                gd y4 +- y3 dy1 0
                gd x1 +- hc 0 dx1
                gd x2 +- hc dx1 0
                tx x1 y1 x2 y4
                p : M x1 y1 L x2 y1 L x2 y2 L x1 y2 Z M x1 y3 L x2 y3 L x2 y4 L x1 y4 Z
                """,
            ["mathMultiply"] = """
                av adj1 23520
                gd a1 pin 0 adj1 51965
                gd th */ ss a1 100000
                gd a at2 w h
                gd sa sin 1 a
                gd ca cos 1 a
                gd ta tan 1 a
                gd dl mod w h 0
                gd rw */ dl 51965 100000
                gd lM +- dl 0 rw
                gd xM */ ca lM 2
                gd yM */ sa lM 2
                gd dxAM */ sa th 2
                gd dyAM */ ca th 2
                gd xA +- xM 0 dxAM
                gd yA +- yM dyAM 0
                gd xB +- xM dxAM 0
                gd yB +- yM 0 dyAM
                gd xBC +- hc 0 xB
                gd yBC */ xBC ta 1
                gd yC +- yBC yB 0
                gd xD +- r 0 xB
                gd xE +- r 0 xA
                gd yFE +- vc 0 yA
                gd xFE */ yFE 1 ta
                gd xF +- xE 0 xFE
                gd xL +- xA xFE 0
                gd yG +- b 0 yA
                gd yH +- b 0 yB
                gd yI +- b 0 yC
                tx xA yA xE yG
                p : M xA yA L xB yB L hc yC L xD yB L xE yA L xF vc L xE yG L xD yH L hc yI L xB yH L xA yG L xL vc Z
                """,
            ["mathDivide"] = """
                av adj1 23520
                av adj2 5880
                av adj3 11760
                gd a1 pin 1000 adj1 36745
                gd ma1 +- 36745 0 a1
                gd ma3h +/ 73490 ma1 4
                gd ma3w */ 36745 w h
                gd maxAdj3 min ma3h ma3w
                gd a3 pin 1000 adj3 maxAdj3
                gd m4a3 */ -4 a3 1
                gd maxAdj2 +- 73490 m4a3 a1
                gd a2 pin 0 adj2 maxAdj2
                gd dy1 */ h a1 200000
                gd yg */ h a2 100000
                gd rad */ h a3 100000
                gd dx1 */ w 73490 200000
                gd y3 +- vc 0 dy1
                gd y4 +- vc dy1 0
                gd a +- yg rad 0
                gd y2 +- y3 0 a
                gd y1 +- y2 0 rad
                gd y5 +- b 0 y1
                gd x1 +- hc 0 dx1
                gd x3 +- hc dx1 0
                tx x1 y3 x3 y4
                p : M hc y1 A rad rad 3cd4 21600000 Z M hc y5 A rad rad cd4 21600000 Z M x1 y3 L x3 y3 L x3 y4 L x1 y4 Z
                """,
            ["wave"] = """
                av adj1 12500
                av adj2 0
                gd a1 pin 0 adj1 20000
                gd a2 pin -10000 adj2 10000
                gd y1 */ h a1 100000
                gd dy2 */ y1 10 3
                gd y2 +- y1 0 dy2
                gd y3 +- y1 dy2 0
                gd y4 +- b 0 y1
                gd y5 +- y4 0 dy2
                gd y6 +- y4 dy2 0
                gd dx1 */ w a2 100000
                gd of2 */ w a2 50000
                gd x1 abs dx1
                gd dx2 ?: of2 0 of2
                gd x2 +- l 0 dx2
                gd dx5 ?: of2 of2 0
                gd x5 +- r 0 dx5
                gd dx3 +/ dx2 x5 3
                gd x3 +- x2 dx3 0
                gd x4 +/ x3 x5 2
                gd x6 +- l dx5 0
                gd x10 +- r dx2 0
                gd x7 +- x6 dx3 0
                gd x8 +/ x7 x10 2
                gd il max x2 x6
                gd ir min x5 x10
                gd it */ h a1 50000
                gd ib +- b 0 it
                ah xy - - - adj1 0 20000 l y1
                tx il it ir ib
                p : M x2 y1 C x3 y2 x4 y3 x5 y1 L x10 y4 C x8 y6 x7 y5 x6 y4 Z
                """,
            ["flowChartProcess"] = """
                p w=1 h=1 : M 0 0 L 1 0 L 1 1 L 0 1 Z
                """,
            ["flowChartAlternateProcess"] = """
                gd x2 +- r 0 ssd6
                gd y2 +- b 0 ssd6
                gd il */ ssd6 29289 100000
                gd ir +- r 0 il
                gd ib +- b 0 il
                tx il il ir ib
                p : M l ssd6 A ssd6 ssd6 cd2 cd4 L x2 t A ssd6 ssd6 3cd4 cd4 L r y2 A ssd6 ssd6 0 cd4 L ssd6 b A ssd6 ssd6 cd4 cd4 Z
                """,
            ["flowChartDecision"] = """
                gd ir */ w 3 4
                gd ib */ h 3 4
                tx wd4 hd4 ir ib
                p w=2 h=2 : M 0 1 L 1 0 L 2 1 L 1 2 Z
                """,
            ["flowChartInputOutput"] = """
                gd x4 */ w 4 5
                tx wd5 t x4 b
                p w=5 h=5 : M 0 5 L 1 0 L 5 0 L 4 5 Z
                """,
            ["flowChartPredefinedProcess"] = """
                gd x2 */ w 7 8
                tx wd8 t x2 b
                p w=1 h=1 stroke=false : M 0 0 L 1 0 L 1 1 L 0 1 Z
                p w=8 h=8 fill=none : M 1 0 L 1 8 M 7 0 L 7 8
                p w=1 h=1 fill=none : M 0 0 L 1 0 L 1 1 L 0 1 Z
                """,
            ["flowChartInternalStorage"] = """
                tx wd4 hd4 r b
                p w=1 h=1 stroke=false : M 0 0 L 1 0 L 1 1 L 0 1 Z
                p w=4 h=4 fill=none : M 1 0 L 1 4 M 0 1 L 4 1
                p w=1 h=1 fill=none : M 0 0 L 1 0 L 1 1 L 0 1 Z
                """,
            ["flowChartDocument"] = """
                gd y2 */ h 17322 21600
                tx l t r y2
                p w=21600 h=21600 : M 0 0 L 21600 0 L 21600 17322 C 10800 17322 10800 23922 0 20172 Z
                """,
            ["flowChartTerminator"] = """
                gd il */ w 1018 21600
                gd ir */ w 20582 21600
                gd it */ h 3163 21600
                gd ib */ h 18437 21600
                tx il it ir ib
                p w=21600 h=21600 : M 3475 0 L 18125 0 A 3475 10800 3cd4 cd2 L 3475 21600 A 3475 10800 cd4 cd2 Z
                """,
            ["flowChartPreparation"] = """
                gd x2 */ w 4 5
                tx wd5 t x2 b
                p w=10 h=10 : M 0 5 L 2 0 L 8 0 L 10 5 L 8 10 L 2 10 Z
                """,
            ["flowChartManualInput"] = """
                gd y1 */ h 1 5
                tx l y1 r b
                p w=5 h=5 : M 0 1 L 5 0 L 5 5 L 0 5 Z
                """,
            ["flowChartManualOperation"] = """
                gd x4 */ w 4 5
                tx wd5 t x4 b
                p w=5 h=5 : M 0 0 L 5 0 L 4 5 L 1 5 Z
                """,
            ["flowChartConnector"] = """
                gd idx cos wd2 2700000
                gd idy sin hd2 2700000
                gd il +- hc 0 idx
                gd ir +- hc idx 0
                gd it +- vc 0 idy
                gd ib +- vc idy 0
                tx il it ir ib
                p : M l vc A wd2 hd2 cd2 cd4 A wd2 hd2 3cd4 cd4 A wd2 hd2 0 cd4 A wd2 hd2 cd4 cd4 Z
                """,
            ["flowChartOffpageConnector"] = """
                gd y1 */ h 4 5
                tx l t r y1
                p w=10 h=10 : M 0 0 L 10 0 L 10 8 L 5 10 L 0 8 Z
                """,
            ["flowChartPunchedCard"] = """
                tx l hd5 r b
                p w=5 h=5 : M 0 1 L 1 0 L 5 0 L 5 5 L 0 5 Z
                """,
            ["flowChartPunchedTape"] = """
                gd ib */ h 4 5
                tx l hd5 r ib
                p w=20 h=20 : M 0 2 A 5 2 cd2 -10800000 A 5 2 cd2 cd2 L 20 18 A 5 2 0 -10800000 A 5 2 0 cd2 Z
                """,
            ["flowChartOr"] = """
                gd idx cos wd2 2700000
                gd idy sin hd2 2700000
                gd il +- hc 0 idx
                gd ir +- hc idx 0
                gd it +- vc 0 idy
                gd ib +- vc idy 0
                tx il it ir ib
                p : M l vc A wd2 hd2 cd2 cd4 A wd2 hd2 3cd4 cd4 A wd2 hd2 0 cd4 A wd2 hd2 cd4 cd4 Z
                p fill=none : M hc t L hc b M l vc L r vc
                """,
            ["flowChartSummingJunction"] = """
                gd idx cos wd2 2700000
                gd idy sin hd2 2700000
                gd il +- hc 0 idx
                gd ir +- hc idx 0
                gd it +- vc 0 idy
                gd ib +- vc idy 0
                tx il it ir ib
                p : M l vc A wd2 hd2 cd2 cd4 A wd2 hd2 3cd4 cd4 A wd2 hd2 0 cd4 A wd2 hd2 cd4 cd4 Z
                p fill=none : M il it L ir ib M ir it L il ib
                """,
            ["flowChartCollate"] = """
                gd ir */ w 3 4
                gd ib */ h 3 4
                tx wd4 hd4 ir ib
                p w=2 h=2 : M 0 0 L 2 0 L 1 1 L 2 2 L 0 2 L 1 1 Z
                """,
            ["flowChartSort"] = """
                gd ir */ w 3 4
                gd ib */ h 3 4
                tx wd4 hd4 ir ib
                p w=2 h=2 stroke=false : M 0 1 L 1 0 L 2 1 L 1 2 Z
                p w=2 h=2 fill=none : M 0 1 L 2 1
                p w=2 h=2 fill=none : M 0 1 L 1 0 L 2 1 L 1 2 Z
                """,
            ["flowChartExtract"] = """
                gd x2 */ w 3 4
                tx wd4 vc x2 b
                p w=2 h=2 : M 0 2 L 1 0 L 2 2 Z
                """,
            ["flowChartMerge"] = """
                gd x2 */ w 3 4
                tx wd4 t x2 vc
                p w=2 h=2 : M 0 0 L 2 0 L 1 2 Z
                """,
            ["flowChartOnlineStorage"] = """
                gd x2 */ w 5 6
                tx wd6 t x2 b
                p w=6 h=6 : M 1 0 L 6 0 A 1 3 3cd4 -10800000 L 1 6 A 1 3 cd4 cd2 Z
                """,
            ["flowChartDelay"] = """
                gd idx cos wd2 2700000
                gd idy sin hd2 2700000
                gd ir +- hc idx 0
                gd it +- vc 0 idy
                gd ib +- vc idy 0
                tx l it ir ib
                p : M l t L hc t A wd2 hd2 3cd4 cd2 L l b Z
                """,
            ["flowChartDisplay"] = """
                gd x2 */ w 5 6
                tx wd6 t x2 b
                p w=6 h=6 : M 0 3 L 1 0 L 5 0 A 1 3 3cd4 cd2 L 1 6 Z
                """,
            ["flowChartMagneticDisk"] = """
                gd y3 */ h 1 3
                gd y2 */ h 5 6
                tx l y3 r y2
                p w=6 h=6 stroke=false : M 0 1 A 3 1 cd2 cd2 L 6 5 A 3 1 0 cd2 Z
                p w=6 h=6 fill=none : M 6 1 A 3 1 0 cd2
                p w=6 h=6 fill=none : M 0 1 A 3 1 cd2 cd2 L 6 5 A 3 1 0 cd2 Z
                """,
            ["chartX"] = """
                p fill=none : M l t L r b M r t L l b
                """,
            ["chartPlus"] = """
                p fill=none : M hc t L hc b M l vc L r vc
                """,
            ["chartStar"] = """
                p fill=none : M l t L r b M r t L l b M hc t L hc b
                """,
        };

        AddStar(shapes, "star4", 4, 12500, 100000, 100000);
        AddStar(shapes, "star5", 5, 19098, 105146, 110557);
        AddStar(shapes, "star6", 6, 28868, 115470, 100000);
        AddStar(shapes, "star7", 7, 34601, 102572, 105210);
        AddStar(shapes, "star8", 8, 38250, 100000, 100000);
        AddStar(shapes, "star10", 10, 42533, 105146, 100000);
        AddStar(shapes, "star12", 12, 37500, 100000, 100000);
        AddStar(shapes, "star16", 16, 37500, 100000, 100000);
        AddStar(shapes, "star24", 24, 37500, 100000, 100000);
        AddStar(shapes, "star32", 32, 37500, 100000, 100000);
        AddLineCallouts(shapes);
        return shapes;
    }

    /// <summary>pie / chord / arc: the shared angle guides (adj1 start, adj2 end) and the points on the ellipse.</summary>
    private static string Arc(string paths, int start, int end) => $"""
        av adj1 {start}
        av adj2 {end}
        gd stAng pin 0 adj1 21599999
        gd enAng pin 0 adj2 21599999
        gd sw1 +- enAng 0 stAng
        gd sw2 +- sw1 21600000 0
        gd swAng ?: sw1 sw1 sw2
        gd wt1 sin wd2 stAng
        gd ht1 cos hd2 stAng
        gd dx1 cat2 wd2 ht1 wt1
        gd dy1 sat2 hd2 ht1 wt1
        gd x1 +- hc dx1 0
        gd y1 +- vc dy1 0
        gd wt2 sin wd2 enAng
        gd ht2 cos hd2 enAng
        gd dx2 cat2 wd2 ht2 wt2
        gd dy2 sat2 hd2 ht2 wt2
        gd x2 +- hc dx2 0
        gd y2 +- vc dy2 0
        gd idx cos wd2 2700000
        gd idy sin hd2 2700000
        gd il +- hc 0 idx
        gd ir +- hc idx 0
        gd it +- vc 0 idy
        gd ib +- vc idy 0
        ah xy - - - - - - x1 y1
        tx il it ir ib
        {paths}
        """;

    /// <summary>wedgeRectCallout / wedgeRoundRectCallout: the tail goes from the side the tip is beyond (adj1/adj2: tip offset from the centre).</summary>
    private static string Callout(bool rounded)
    {
        var builder = new StringBuilder();
        builder.AppendLine("av adj1 -20833");
        builder.AppendLine("av adj2 62500");
        if (rounded)
        {
            builder.AppendLine("av adj3 16667");
        }

        builder.AppendLine("""
            gd dxPos */ w adj1 100000
            gd dyPos */ h adj2 100000
            gd xPos +- hc dxPos 0
            gd yPos +- vc dyPos 0
            gd dx */ dxPos h 1
            gd dy */ dyPos w 1
            gd adx abs dx
            gd ady abs dy
            gd dq +- adx 0 ady
            gd xg1 ?: dx 7 2
            gd xg2 ?: dx 10 5
            gd x1 */ w xg1 12
            gd x2 */ w xg2 12
            gd yg1 ?: dy 7 2
            gd yg2 ?: dy 10 5
            gd y1 */ h yg1 12
            gd y2 */ h yg2 12
            gd t1 ?: dx l xPos
            gd xl ?: dq t1 l
            gd t5 ?: dx y1 yPos
            gd yl ?: dq t5 y1
            gd t2 ?: dy x1 xPos
            gd xt ?: dq x1 t2
            gd t6 ?: dy t yPos
            gd yt ?: dq t t6
            gd t3 ?: dx xPos r
            gd xr ?: dq t3 r
            gd t7 ?: dx yPos y1
            gd yr ?: dq t7 y1
            gd t4 ?: dy xPos x1
            gd xb ?: dq x1 t4
            gd t8 ?: dy yPos b
            gd yb ?: dq b t8
            ah xy adj1 -2147483647 2147483647 adj2 -2147483647 2147483647 xPos yPos
            """);
        if (!rounded)
        {
            builder.AppendLine("tx l t r b");
            builder.AppendLine("p : M l t L x1 t L xt yt L x2 t L r t L r y1 L xr yr L r y2 L r b L x2 b L xb yb L x1 b L l b L l y2 L xl yl L l y1 Z");
            return builder.ToString();
        }

        builder.AppendLine("""
            gd u1 */ ss adj3 100000
            gd u2 +- r 0 u1
            gd v2 +- b 0 u1
            gd il */ u1 29289 100000
            gd ir +- r 0 il
            gd ib +- b 0 il
            tx il il ir ib
            p : M l u1 A u1 u1 cd2 cd4 L x1 t L xt yt L x2 t L u2 t A u1 u1 3cd4 cd4 L r y1 L xr yr L r y2 L r v2 A u1 u1 0 cd4 L x2 b L xb yb L x1 b L u1 b A u1 u1 cd4 cd4 L l y2 L xl yl L l y1 Z
            """);
        return builder.ToString();
    }

    /// <summary>
    /// A regular star of <paramref name="points"/> points: outer points on the ellipse scaled by hf/vf, inner points on the ellipse
    /// scaled by adj/50000 (the presets' "iwd2 = swd2 · a / 50000"), the first point straight up.
    /// </summary>
    private static void AddStar(Dictionary<string, string> shapes, string name, int points, int adjust, int horizontal, int vertical)
    {
        var builder = new StringBuilder();
        builder.AppendLine(CultureInfo.InvariantCulture, $"av adj {adjust}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"av hf {horizontal}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"av vf {vertical}");
        builder.AppendLine("gd a pin 0 adj 50000");
        builder.AppendLine("gd swd2 */ wd2 hf 100000");
        builder.AppendLine("gd shd2 */ hd2 vf 100000");
        builder.AppendLine("gd svc */ vc vf 100000");
        builder.AppendLine("gd iwd2 */ swd2 a 50000");
        builder.AppendLine("gd ihd2 */ shd2 a 50000");
        builder.AppendLine("gd yAdj +- svc 0 ihd2");
        var commands = new List<string>();
        for (var index = 0; index < points * 2; index++)
        {
            var angle = (int)Math.Round(16200000 + (index * 10800000.0 / points)) % 21600000;
            var (rx, ry) = index % 2 == 0 ? ("swd2", "shd2") : ("iwd2", "ihd2");
            builder.AppendLine(CultureInfo.InvariantCulture, $"gd dx{index} cos {rx} {angle}");
            builder.AppendLine(CultureInfo.InvariantCulture, $"gd dy{index} sin {ry} {angle}");
            builder.AppendLine(CultureInfo.InvariantCulture, $"gd x{index} +- hc dx{index} 0");
            builder.AppendLine(CultureInfo.InvariantCulture, $"gd y{index} +- svc dy{index} 0");
            commands.Add($"{(index == 0 ? "M" : "L")} x{index} y{index}");
        }

        // The text sits in the inner polygon's box at 45°.
        builder.AppendLine("gd tdx cos iwd2 2700000");
        builder.AppendLine("gd tdy sin ihd2 2700000");
        builder.AppendLine("gd il +- hc 0 tdx");
        builder.AppendLine("gd ir +- hc tdx 0");
        builder.AppendLine("gd it +- svc 0 tdy");
        builder.AppendLine("gd ib +- svc tdy 0");
        builder.AppendLine("ah xy - - - adj 0 50000 hc yAdj");
        builder.AppendLine("tx il it ir ib");
        builder.AppendLine("p : " + string.Join(' ', commands) + " Z");
        shapes[name] = builder.ToString();
    }

    /// <summary>
    /// callout1–3, borderCallout1–3, accentCallout1–3, accentBorderCallout1–3: a box with a leader line of one, two or three segments
    /// (adj pairs: y then x, fractions of the height/width), with or without border and accent bar.
    /// </summary>
    private static void AddLineCallouts(Dictionary<string, string> shapes)
    {
        int[][] defaults =
        [
            [18750, -8333, 112500, -38333],
            [18750, -8333, 18750, -16667, 112500, -46667],
            [18750, -8333, 18750, -16667, 100000, -16667, 112963, -8333],
        ];
        foreach (var (prefix, border, accent) in new[] { ("callout", false, false), ("borderCallout", true, false), ("accentCallout", false, true), ("accentBorderCallout", true, true) })
        {
            for (var segments = 1; segments <= 3; segments++)
            {
                var builder = new StringBuilder();
                var values = defaults[segments - 1];
                for (var index = 0; index < values.Length; index++)
                {
                    builder.AppendLine(CultureInfo.InvariantCulture, $"av adj{index + 1} {values[index]}");
                }

                var line = new List<string>();
                for (var point = 0; point <= segments; point++)
                {
                    builder.AppendLine(CultureInfo.InvariantCulture, $"gd y{point + 1} */ h adj{(point * 2) + 1} 100000");
                    builder.AppendLine(CultureInfo.InvariantCulture, $"gd x{point + 1} */ w adj{(point * 2) + 2} 100000");
                    builder.AppendLine(CultureInfo.InvariantCulture, $"ah xy adj{(point * 2) + 2} -2147483647 2147483647 adj{(point * 2) + 1} -2147483647 2147483647 x{point + 1} y{point + 1}");
                    line.Add($"{(point == 0 ? "M" : "L")} x{point + 1} y{point + 1}");
                }

                builder.AppendLine(border ? "p : M l t L r t L r b L l b Z" : "p stroke=false : M l t L r t L r b L l b Z");
                if (accent)
                {
                    builder.AppendLine("p fill=none : M x1 t L x1 b");
                }

                builder.AppendLine("p fill=none : " + string.Join(' ', line));
                shapes[$"{prefix}{segments}"] = builder.ToString();
            }
        }
    }
}
