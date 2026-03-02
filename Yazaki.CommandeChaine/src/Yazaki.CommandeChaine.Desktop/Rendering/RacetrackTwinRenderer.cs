using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Shapes;
using Yazaki.CommandeChaine.Desktop.Services;

namespace Yazaki.CommandeChaine.Desktop.Rendering;

public static class RacetrackTwinRenderer
{
    public static void Render(
        Canvas canvas,
        ChainDto chain,
        IReadOnlyDictionary<Guid, string> tableToBarcode,
        double progress01,
        Action<ChainTableDto>? onTableClick = null,
        IReadOnlyDictionary<Guid, double>? creditRatios = null)
    {
        var width = Math.Max(100, canvas.ActualWidth);
        var height = Math.Max(100, canvas.ActualHeight);

        canvas.Children.Clear();

        // Track area (horizontal loop / conveyor style)
        var margin = 18.0;
        var trackWidth = Math.Max(320.0, width - (margin * 2));
        var trackHeight = Math.Max(180.0, height - (margin * 2));

        var x0 = (width - trackWidth) / 2;
        var y0 = (height - trackHeight) / 2;

        // Soft background card (keeps UI readable) + single continuous rail on top.
        var bg = new Rectangle
        {
            Width = trackWidth,
            Height = trackHeight,
            RadiusX = 22,
            RadiusY = 22,
            Stroke = new SolidColorBrush(Color.FromRgb(235, 235, 235)),
            Fill = new SolidColorBrush(Color.FromRgb(250, 250, 250)),
            StrokeThickness = 1
        };
        Canvas.SetLeft(bg, x0);
        Canvas.SetTop(bg, y0);
        canvas.Children.Add(bg);

        // Centerline inset so tables never clip outside the rail stroke.
        var centerlineInset = 26.0;
        var cx0 = x0 + centerlineInset;
        var cy0 = y0 + centerlineInset;
        var cw = Math.Max(80.0, trackWidth - (2.0 * centerlineInset));
        var ch = Math.Max(80.0, trackHeight - (2.0 * centerlineInset));

        // Racetrack radius: true semicircle ends => loop height is 2r.
        // Keep it bounded by available height, and also by width so we keep a visible straight section.
        var cr = Math.Min(ch / 2.0, 56.0);
        cr = Math.Min(cr, (cw - 40.0) / 2.0);
        cr = Math.Max(16.0, cr);

        var centerY = cy0 + (ch / 2.0);
        var topY = centerY - cr;
        var bottomY = centerY + cr;

        var straight = Math.Max(1.0, cw - 2.0 * cr);
        var arcLen = Math.PI * cr;
        var perimeter = (2.0 * straight) + (2.0 * arcLen);

        // Draw the rail as a single closed loop (PathGeometry).
        var railGeometry = CreateHorizontalLoopGeometry(cx0, topY, cw, cr);

        var railThickness = Math.Clamp(cr * 0.28, 10.0, 14.0);

        var rail = new Path
        {
            Data = railGeometry,
            Stroke = new SolidColorBrush(Color.FromRgb(210, 210, 210)),
            StrokeThickness = railThickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = Brushes.Transparent,
            Opacity = 0.95
        };
        canvas.Children.Add(rail);

        var railInner = new Path
        {
            Data = railGeometry,
            Stroke = new SolidColorBrush(Color.FromRgb(160, 160, 160)),
            StrokeThickness = 1.8,
            StrokeDashArray = new DoubleCollection { 3, 6 },
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = Brushes.Transparent,
            Opacity = 0.55
        };
        canvas.Children.Add(railInner);

        if (chain.Tables.Count == 0)
        {
            var hint = new TextBlock
            {
                Text = "Aucun tableau configuré.\nAllez dans Chaînes pour créer / gérer.",
                Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128)),
                FontSize = 14,
                TextAlignment = System.Windows.TextAlignment.Center
            };
            Canvas.SetLeft(hint, width / 2 - 170);
            Canvas.SetTop(hint, height / 2 - 18);
            canvas.Children.Add(hint);
            return;
        }

        // Tables move along the full loop perimeter; they rotate with the rail (turns included).
        var ordered = chain.Tables.OrderBy(t => t.Index).ToList();
        var n = ordered.Count;
        var spacing = perimeter / Math.Max(1, n);

        // Visual table size: smaller + closer to a pallet.
        var tableW = 56.0;
        var tableH = 32.0;
        if (spacing < 80)
        {
            // If the chain is very crowded, shrink a bit.
            tableW = Math.Max(36.0, spacing * 0.55);
            tableH = Math.Max(22.0, tableW * 0.55);
        }

        for (var i = 0; i < n; i++)
        {
            var table = ordered[i];
            var dist = ((progress01 % 1.0) * perimeter) + (i * spacing);
            dist %= perimeter;

            var (pos, tangent) = PointAndTangentOnHorizontalLoop(cx0, topY, cw, cr, straight, arcLen, dist);
            if (tangent.LengthSquared < 1e-9)
            {
                tangent = new Vector(1, 0);
            }
            tangent.Normalize();
            var angleDeg = Math.Atan2(tangent.Y, tangent.X) * 180.0 / Math.PI;

            var creditRatio = 0.0;
            if (creditRatios is not null && creditRatios.TryGetValue(table.Id, out var value))
            {
                creditRatio = value;
            }

            var node = new Border
            {
                Width = tableW,
                Height = tableH,
                CornerRadius = new System.Windows.CornerRadius(10),
                Background = new SolidColorBrush(GetCreditColor(creditRatio)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(120, 120, 120)),
                BorderThickness = new System.Windows.Thickness(2),
                Child = new TextBlock
                {
                    Text = table.Index.ToString(CultureInfo.InvariantCulture),
                    FontWeight = System.Windows.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center
                }
            };

            if (onTableClick is not null)
            {
                node.Cursor = Cursors.Hand;
                node.MouseLeftButtonDown += (_, _) => onTableClick(table);
            }

            node.RenderTransform = new RotateTransform(angleDeg, node.Width / 2.0, node.Height / 2.0);

            Canvas.SetLeft(node, pos.X - (node.Width / 2));
            Canvas.SetTop(node, pos.Y - (node.Height / 2));
            canvas.Children.Add(node);

            if (tableToBarcode.TryGetValue(table.Id, out var barcode))
            {
                var badge = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
                    CornerRadius = new System.Windows.CornerRadius(8),
                    Padding = new System.Windows.Thickness(8, 4, 8, 4),
                    Child = new TextBlock { Text = barcode, Foreground = Brushes.White, FontSize = 11 }
                };

                // Place badge slightly outward from the rail, perpendicular to motion.
                var normal = new Vector(-tangent.Y, tangent.X);
                normal.Normalize();
                var bx = pos.X + (normal.X * 26);
                var by = pos.Y + (normal.Y * 26);

                Canvas.SetLeft(badge, bx - 46);
                Canvas.SetTop(badge, by - 14);
                canvas.Children.Add(badge);
            }
        }
    }

    private static Color GetCreditColor(double creditRatio)
    {
        var t = Math.Clamp(creditRatio, -1, 1);
        var red = Color.FromRgb(220, 53, 69);
        var yellow = Color.FromRgb(255, 193, 7);
        var green = Color.FromRgb(40, 167, 69);

        if (t >= 0)
        {
            return LerpColor(yellow, green, t);
        }

        return LerpColor(red, yellow, t + 1);
    }

    private static Color LerpColor(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        var r = (byte)(a.R + (b.R - a.R) * t);
        var g = (byte)(a.G + (b.G - a.G) * t);
        var bl = (byte)(a.B + (b.B - a.B) * t);
        return Color.FromRgb(r, g, bl);
    }

    private static PathGeometry CreateHorizontalLoopGeometry(double x0, double topY, double w, double r)
    {
        // Horizontal racetrack: top straight -> right arc -> bottom straight -> left arc.
        var bottomY = topY + (2.0 * r);
        var start = new Point(x0 + r, topY);
        var fig = new PathFigure { StartPoint = start, IsClosed = true, IsFilled = false };

        // Top straight
        fig.Segments.Add(new LineSegment(new Point(x0 + w - r, topY), true));
        // Right semicircle (top -> bottom)
        fig.Segments.Add(new ArcSegment(
            new Point(x0 + w - r, bottomY),
            new Size(r, r),
            rotationAngle: 0,
            isLargeArc: false,
            sweepDirection: SweepDirection.Clockwise,
            isStroked: true));
        // Bottom straight
        fig.Segments.Add(new LineSegment(new Point(x0 + r, bottomY), true));
        // Left semicircle (bottom -> top)
        fig.Segments.Add(new ArcSegment(
            new Point(x0 + r, topY),
            new Size(r, r),
            rotationAngle: 0,
            isLargeArc: false,
            sweepDirection: SweepDirection.Clockwise,
            isStroked: true));

        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        return geo;
    }

    private static (Point pos, Vector tangent) PointAndTangentOnHorizontalLoop(
        double x0,
        double topY,
        double w,
        double r,
        double straight,
        double arcLen,
        double dist)
    {
        var centerY = topY + r;
        var bottomY = topY + (2.0 * r);

        // Segment A: top straight (left -> right)
        if (dist <= straight)
        {
            return (new Point(x0 + r + dist, topY), new Vector(1, 0));
        }
        dist -= straight;

        // Segment B: right arc (top -> bottom), center at (x0 + w - r, centerY)
        // Using param angle from -90° to +90°.
        if (dist <= arcLen)
        {
            var t = dist / arcLen;
            var ang = (-Math.PI / 2.0) + (t * Math.PI);
            var cx = x0 + w - r;
            var cy = centerY;
            var pos = new Point(cx + (r * Math.Cos(ang)), cy + (r * Math.Sin(ang)));
            var tan = new Vector(-Math.Sin(ang), Math.Cos(ang));
            return (pos, tan);
        }
        dist -= arcLen;

        // Segment C: bottom straight (right -> left)
        if (dist <= straight)
        {
            return (new Point(x0 + (w - r) - dist, bottomY), new Vector(-1, 0));
        }
        dist -= straight;

        // Segment D: left arc (bottom -> top), center at (x0 + r, centerY)
        // Angle from +90° to +270°.
        var t2 = (arcLen <= 0.0001) ? 0.0 : (dist / arcLen);
        var ang2 = (Math.PI / 2.0) + (t2 * Math.PI);
        var cx2 = x0 + r;
        var cy2 = centerY;
        var pos2 = new Point(cx2 + (r * Math.Cos(ang2)), cy2 + (r * Math.Sin(ang2)));
        var tan2 = new Vector(-Math.Sin(ang2), Math.Cos(ang2));
        return (pos2, tan2);
    }
}
