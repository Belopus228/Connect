using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ConnectPC;

/// <summary>
/// 3D (изометрическая) плата. Режимы: Camera, MoveBoard, Move, Wire, Solder,
/// Delete, Sim, Measure. Плата поворачивается (BoardRotDeg), двигается (Pan),
/// смотрится сверху и снизу (ViewSide), провода ведутся по обеим сторонам.
/// Подписи — специально мелкие и только там, где нужны.
/// </summary>
public sealed class BoardView : FrameworkElement
{
    public ProjectData Data { get; set; } = ProjectData.CreateDemo();
    public string Mode { get; set; } = "Camera";
    public string ViewSide { get; set; } = "Top";   // Top | Bottom
    public string WireSide { get; set; } = "Top";   // куда кладём новые провода
    public int SolderTempC { get; set; } = 350;
    public bool UseFlux { get; set; } = true;
    public double CameraAngle { get; set; } = -28;
    public double Tilt { get; set; } = 0.52;
    public double Zoom { get; set; } = 1.0;
    public int BoardRotDeg { get; set; } = 0;
    public PlacedComponent Selected { get; set; }

    public Point Pan = new(0, 0);

    public event Action<string> LogMessage;
    public event Action StateChanged;

    private const double Cell = 11.0;
    private (PlacedComponent, string)? _pendingPin;
    private (string, string)? _hoverPin; // (compId, pinName)
    private PlacedComponent _dragComp;
    private (double, double) _dragOffset;
    private Point _lastMouse;
    private bool _panning, _rotatingCam;
    private (double, double)? _measureStart;
    private (double, double)? _hoverBoard;

    private static readonly string[] WireColors = { "#d32f2f", "#ff6f00", "#f9a825", "#2e7d32", "#1565c0", "#6a1b9a", "#00838f" };

    public string SolderQuality() =>
        SolderTempC > 430 ? "hot" : (SolderTempC < 280 || (SolderTempC < 315 && !UseFlux)) ? "cold" : "good";

    public void ResetView()
    {
        CameraAngle = -28; Tilt = 0.52; Zoom = 1.0; Pan = new Point(0, 0);
        BoardRotDeg = 0; ViewSide = "Top";
        InvalidateVisual();
    }

    public void RotateBoard(int dir)
    {
        BoardRotDeg = ((BoardRotDeg + dir * 90) % 360 + 360) % 360;
        LogMessage?.Invoke($"Плата повёрнута: {BoardRotDeg}°");
        InvalidateVisual();
    }

    // ---------------- проекция ----------------
    private (double, double) RotateBoardPoint(double wx, double wy)
    {
        double cx = Data.Cols / 2.0, cy = Data.Rows / 2.0;
        double a = BoardRotDeg * Math.PI / 180.0;
        double dx = wx - cx, dy = wy - cy;
        double rx = dx * Math.Cos(a) - dy * Math.Sin(a);
        double ry = dx * Math.Sin(a) + dy * Math.Cos(a);
        double x = cx + rx, y = cy + ry;
        if (ViewSide == "Bottom") x = Data.Cols - x;
        return (x, y);
    }

    public Point Project(double wx, double wy, double wz = 0)
    {
        var (ux, uy) = RotateBoardPoint(wx, wy);
        double cx = Data.Cols / 2.0, cy = Data.Rows / 2.0;
        double a = CameraAngle * Math.PI / 180.0;
        double dx = ux - cx, dy = uy - cy;
        double xr = dx * Math.Cos(a) - dy * Math.Sin(a);
        double yr = dx * Math.Sin(a) + dy * Math.Cos(a);
        double sx = ActualWidth / 2 + Pan.X + (xr - yr) * Cell * Zoom;
        double sy = ActualHeight / 2 + Pan.Y + (xr + yr) * Cell * Tilt * Zoom - wz * Cell * Zoom;
        return new Point(sx, sy);
    }

    public (double, double) Unproject(Point s)
    {
        double a = CameraAngle * Math.PI / 180.0;
        double aa = (s.X - ActualWidth / 2 - Pan.X) / (Cell * Zoom);
        double bb = (s.Y - ActualHeight / 2 - Pan.Y) / (Cell * Tilt * Zoom);
        double xr = (aa + bb) / 2, yr = (bb - aa) / 2;
        double dx = xr * Math.Cos(a) + yr * Math.Sin(a);
        double dy = -xr * Math.Sin(a) + yr * Math.Cos(a);
        double ux = dx + Data.Cols / 2.0, uy = dy + Data.Rows / 2.0;
        if (ViewSide == "Bottom") ux = Data.Cols - ux;
        // обратный поворот платы
        double cx = Data.Cols / 2.0, cy = Data.Rows / 2.0;
        double ra = -BoardRotDeg * Math.PI / 180.0;
        double ddx = ux - cx, ddy = uy - cy;
        double wx = cx + ddx * Math.Cos(ra) - ddy * Math.Sin(ra);
        double wy = cy + ddx * Math.Sin(ra) + ddy * Math.Cos(ra);
        return (wx, wy);
    }

    private Point ProjectLocal(PlacedComponent c, double lx, double ly, double wz = 0)
    {
        var d = c.Def;
        double rx, ry;
        switch (((c.Rot % 360) + 360) % 360)
        {
            case 90: rx = d.H - ly; ry = lx; break;
            case 180: rx = d.W - lx; ry = d.H - ly; break;
            case 270: rx = ly; ry = d.W - lx; break;
            default: rx = lx; ry = ly; break;
        }
        return Project(c.X + rx, c.Y + ry, wz);
    }

    private (double, double) PinWorld(PlacedComponent c, PinDef pin)
    {
        var (px, py) = c.PinPos(pin);
        return (c.X + px + 0.5, c.Y + py + 0.5);
    }

    private static double PinZ(PlacedComponent c) => c.Type switch
    {
        "esp32" => 2.1,
        "oled" or "tft" or "tft18" or "lcd1602" or "nrf24" => 1.9,
        "led" or "resistor" or "cap" => 0.5,
        _ => 1.2
    };

    // ---------------- утилиты рисования ----------------
    private static Color Hex(string hex, byte alpha = 255)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        c.A = alpha;
        return c;
    }
    private static SolidColorBrush Brush(string hex, byte alpha = 255) => new(Hex(hex, alpha));
    private static SolidColorBrush Shade(string hex, double f, byte alpha = 255)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        return new SolidColorBrush(Color.FromArgb(alpha,
            (byte)Math.Clamp(c.R * f, 0, 255), (byte)Math.Clamp(c.G * f, 0, 255), (byte)Math.Clamp(c.B * f, 0, 255)));
    }
    private static Pen PenOf(string hex, double w, byte alpha = 255) => new(Brush(hex, alpha), w);

    private void Text(DrawingContext dc, string text, Point at, double size, string color, bool bold = false, bool center = true)
    {
        if (string.IsNullOrEmpty(text)) return;
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
            size, Brush(color), VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var p = center ? new Point(at.X - ft.Width / 2, at.Y - ft.Height / 2) : at;
        dc.DrawText(ft, p);
    }

    private void Poly(DrawingContext dc, Point[] pts, Brush fill, Pen pen = null)
    {
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(pts[0], fill != null, true);
            g.PolyLineTo(pts.Skip(1).ToArray(), true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(fill, pen, geo);
    }

    private void DrawBox(DrawingContext dc, PlacedComponent c, double lx1, double ly1, double lx2, double ly2,
        double z1, double z2, string color)
    {
        var top = new[]
        {
            ProjectLocal(c, lx1, ly1, z2), ProjectLocal(c, lx2, ly1, z2),
            ProjectLocal(c, lx2, ly2, z2), ProjectLocal(c, lx1, ly2, z2)
        };
        var sides = new (Point[], double)[]
        {
            (new[] { ProjectLocal(c, lx1, ly2, z1), ProjectLocal(c, lx2, ly2, z1), ProjectLocal(c, lx2, ly2, z2), ProjectLocal(c, lx1, ly2, z2) }, 0.62),
            (new[] { ProjectLocal(c, lx2, ly1, z1), ProjectLocal(c, lx2, ly2, z1), ProjectLocal(c, lx2, ly2, z2), ProjectLocal(c, lx2, ly1, z2) }, 0.78),
            (new[] { ProjectLocal(c, lx1, ly1, z1), ProjectLocal(c, lx2, ly1, z1), ProjectLocal(c, lx2, ly1, z2), ProjectLocal(c, lx1, ly1, z2) }, 0.5),
            (new[] { ProjectLocal(c, lx1, ly1, z1), ProjectLocal(c, lx1, ly2, z1), ProjectLocal(c, lx1, ly2, z2), ProjectLocal(c, lx1, ly1, z2) }, 0.7),
        };
        foreach (var (pts, f) in sides) Poly(dc, pts, Shade(color, f), PenOf("#000000", 0.5, 60));
        Poly(dc, top, Brush(color), PenOf("#000000", 0.6, 80));
    }

    // ---------------- рендер ----------------
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(Brush("#0f1b24"), null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (Data == null) return;
        var state = Circuit.Compute(Data);
        bool bottom = ViewSide == "Bottom";

        // плата
        var bc = new[] { Project(0, 0), Project(Data.Cols, 0), Project(Data.Cols, Data.Rows), Project(0, Data.Rows) };
        var bcLow = bc.Select(p => new Point(p.X, p.Y + 7 * Zoom)).ToArray();
        Poly(dc, new[] { bcLow[3], bcLow[2], bc[2], bc[3] }, Brush("#0a3520"));
        Poly(dc, new[] { bcLow[1], bcLow[2], bc[2], bc[1] }, Brush("#0d4426"));
        Poly(dc, bc, Brush(bottom ? "#6d4c2f" : "#14532d"), PenOf("#0b3d20", 2));
        dc.DrawRectangle(Brush("#ffffff", 22), null, new Rect(bc[0].X - 2, bc[0].Y - 14, 1, 1)); // noop-сохранитель

        // отверстия
        var occupied = new HashSet<(int, int)>();
        foreach (var c in Data.Components)
        {
            var (ew, eh) = c.EffSize;
            for (int yy = c.Y; yy < c.Y + eh; yy++)
                for (int xx = c.X; xx < c.X + ew; xx++) occupied.Add((xx, yy));
        }
        for (int gy = 0; gy < Data.Rows; gy++)
            for (int gx = 0; gx < Data.Cols; gx++)
            {
                if (occupied.Contains((gx, gy))) continue;
                var p = Project(gx + 0.5, gy + 0.5, 0.02);
                dc.DrawEllipse(Brush(bottom ? "#4e342e" : "#0b3d20"), null, p, 1.7 * Zoom, 1.3 * Zoom);
            }
        var silk = Project(1, Data.Rows - 0.6, 0.05);
        Text(dc, bottom ? "СОЛДЕРНАЯ СТОРОНА (BOTTOM)" : "CONNECT PCB • REV 2.0 • TOP", silk, 7, "#cfe8d4", center: false);

        // паяные дорожки активной стороны + чужие пунктиром
        foreach (var w in Data.Wires.Where(w => w.Style == "solder"))
            DrawWire(dc, w, state, bottom);
        // компоненты
        foreach (var c in Data.Components.OrderBy(c =>
                 {
                     var (ew, eh) = c.EffSize;
                     return Project(c.X + ew / 2.0, c.Y + eh / 2.0).Y;
                 }))
            DrawComponent(dc, c, state, bottom);
        // провода-дуги
        foreach (var w in Data.Wires.Where(w => w.Style != "solder"))
            DrawWire(dc, w, state, bottom);
        // штыри
        DrawPins(dc, bottom);
        // подписи: маленькие, только выбранное и экраны
        DrawLabels(dc, state, bottom);
        // замеры
        DrawMeasurements(dc);
    }

    private void DrawComponent(DrawingContext dc, PlacedComponent c, CircuitResult state, bool bottom)
    {
        var d = c.Def;
        var (w, h) = (d.W, d.H);
        double ztop = d.HeightMm / 2.54;
        if (bottom)
        {
            // снизу видны только контур платы модуля и контактные площадки
            var (ew, eh) = c.EffSize;
            var poly = new[]
            {
                Project(c.X, c.Y, 0.1), Project(c.X + ew, c.Y, 0.1),
                Project(c.X + ew, c.Y + eh, 0.1), Project(c.X, c.Y + eh, 0.1)
            };
            Poly(dc, poly, Brush("#90a4ae", 45), PenOf("#cfd8dc", 1, 120));
            return;
        }
        switch (c.Type)
        {
            case "esp32":
                DrawBox(dc, c, 0, 0, w, h, 0, 0.65, "#1a237e");
                DrawBox(dc, c, w * 0.22, h * 0.30, w * 0.78, h * 0.72, 0.65, 2.0, "#c0c6ce");
                DrawBox(dc, c, w * 0.22, h * 0.05, w * 0.78, h * 0.25, 0.65, 0.95, "#3949ab");
                DrawBox(dc, c, w * 0.30, -0.3, w * 0.70, 1.8, 0.65, 1.9, "#9aa0a6");
                DrawBox(dc, c, 0.2, 0.5, 1.5, 1.8, 0.65, 1.5, "#212121");
                DrawBox(dc, c, w - 1.5, 0.5, w - 0.2, 1.8, 0.65, 1.5, "#212121");
                bool powered = state.EspPowered.Contains(c.Id);
                var ledP = ProjectLocal(c, w - 0.8, 2.6, 1.6);
                dc.DrawEllipse(Brush(powered && Data.Running ? "#69f0ae" : "#546e7a"), null, ledP, 3.2, 3.2);
                break;
            case "oled":
            case "tft":
            case "tft18":
            case "lcd1602":
                DrawBox(dc, c, 0, 0, w, h, 0, 0.65, d.Color);
                var scr = new[]
                {
                    ProjectLocal(c, w * 0.07, h * 0.08, 0.75), ProjectLocal(c, w * 0.93, h * 0.08, 0.75),
                    ProjectLocal(c, w * 0.93, h * 0.92, 0.75), ProjectLocal(c, w * 0.07, h * 0.92, 0.75)
                };
                Poly(dc, scr, Brush(c.Type == "lcd1602" ? "#9ccc65" : "#05131a"), PenOf("#90a4ae", 1));
                break;
            case "nrf24":
                DrawBox(dc, c, 0, 0, w, h, 0, 0.65, "#0d47a1");
                DrawBox(dc, c, w * 0.3, h * 0.38, w * 0.7, h * 0.68, 0.65, 1.3, "#111111");
                for (int i = 0; i < 3; i++)
                {
                    double yy = h * (0.06 + i * 0.11);
                    dc.DrawLine(PenOf("#ffd54f", 2), ProjectLocal(c, w * 0.08, yy, 0.8), ProjectLocal(c, w * 0.92, yy, 0.8));
                }
                break;
            case "led":
            {
                var colors = new Dictionary<string, string>
                { ["Красный"] = "#ff2222", ["Зелёный"] = "#22ff66", ["Синий"] = "#2266ff", ["Жёлтый"] = "#ffee22", ["Белый"] = "#ffffff" };
                string baseCol = colors.TryGetValue(c.Prop("colorName", "Красный"), out var bc2) ? bc2 : "#ff2222";
                var ctr = ProjectLocal(c, w / 2.0, h / 2.0, 3.4);
                state.Leds.TryGetValue(c.Id, out var st);
                if (st is { Burnt: true })
                {
                    dc.DrawEllipse(Brush("#333333"), PenOf("#ff2222", 2), ctr, 10, 10);
                    Text(dc, "✕", ctr, 10, "#ff2222", bold: true);
                }
                else
                {
                    if (st is { Brightness: > 0.02 } && Data.Running)
                    {
                        double r = 13 + 15 * st.Brightness;
                        dc.DrawEllipse(null, new Pen(Brush(baseCol, 170), 3), ctr, r, r);
                    }
                    dc.DrawEllipse(Brush(baseCol, 220), PenOf("#000000", 0.7, 90), ctr, 8.5, 11);
                    dc.DrawEllipse(Brush("#ffffff", 200), null, new Point(ctr.X - 3, ctr.Y - 4), 2.2, 3);
                }
                break;
            }
            case "resistor":
            {
                var p1 = ProjectLocal(c, 0.3, h / 2.0, 0.8);
                var p2 = ProjectLocal(c, w * 0.28, h / 2.0, 0.8);
                var p3 = ProjectLocal(c, w * 0.72, h / 2.0, 0.8);
                var p4 = ProjectLocal(c, w - 0.3, h / 2.0, 0.8);
                dc.DrawLine(PenOf("#c0c6ce", 2), p1, p2);
                dc.DrawLine(PenOf("#c0c6ce", 2), p3, p4);
                dc.DrawLine(PenOf("#d7a86e", 9), p2, p3);
                var stripe = new[] { "#ff2222", "#ff2222", "#795548", "#ffd700" };
                for (int i = 0; i < stripe.Length; i++)
                {
                    double tt = 0.36 + i * 0.10;
                    dc.DrawLine(PenOf(stripe[i], 3), ProjectLocal(c, w * tt, h / 2.0 - 0.5, 0.9), ProjectLocal(c, w * tt, h / 2.0 + 0.5, 0.9));
                }
                break;
            }
            case "button":
                DrawBox(dc, c, 0, 0, w, h, 0, 0.9, "#263238");
                var bp = ProjectLocal(c, w / 2.0, h / 2.0, 1.8);
                dc.DrawEllipse(Brush(Data.Pressed.Contains(c.Id) ? "#69f0ae" : "#ef5350"), PenOf("#ffffff", 1), bp, 8, 7);
                break;
            case "bat18650":
            {
                DrawBox(dc, c, 0, 0, w, h, 0, 1.2, "#1b5e20");
                var ctr = ProjectLocal(c, w / 2.0, h / 2.0, 2.6);
                dc.DrawEllipse(Brush("#2e7d32"), PenOf("#a5d6a7", 1.5), ctr, 25 * Zoom, 9 * Zoom);
                break;
            }
            case "pot":
                DrawBox(dc, c, 0, 0, w, h, 0, 1.8, "#263238");
                var pp = ProjectLocal(c, w / 2.0, h / 2.0, 3.6);
                dc.DrawEllipse(Brush("#b0bec5"), PenOf("#ffffff", 1), pp, 7.5, 7.5);
                break;
            case "servo":
                DrawBox(dc, c, 0, 0, w, h, 0, ztop * 0.6, "#1565c0");
                var sp = ProjectLocal(c, w / 2.0, h / 2.0, ztop * 0.75);
                dc.DrawLine(PenOf("#ffffff", 4), new Point(sp.X - 13, sp.Y), new Point(sp.X + 13, sp.Y));
                break;
            case "cap":
            case "buzzer":
                DrawBox(dc, c, 0, 0, w, h, 0, 0.5, d.Color);
                var cp = ProjectLocal(c, w / 2.0, h / 2.0, ztop * 0.8);
                dc.DrawEllipse(Brush(d.Color), PenOf("#c0c6ce", 1.5), cp, 8.5, 8.5);
                break;
            default:
                DrawBox(dc, c, 0, 0, w, h, 0, ztop * 0.7, d.Color);
                break;
        }
    }

    private void DrawWire(DrawingContext dc, WireConn w, CircuitResult state, bool bottom)
    {
        var a = Data.ById(w.AId);
        var b = Data.ById(w.BId);
        if (a == null || b == null) return;
        var pa = a.Def.Pins.FirstOrDefault(p => p.Name == w.APin);
        var pb = b.Def.Pins.FirstOrDefault(p => p.Name == w.BPin);
        if (pa == null || pb == null) return;
        var (ax, ay) = PinWorld(a, pa);
        var (bx, by) = PinWorld(b, pb);
        bool activeSide = (w.Side == "Top") != bottom;
        byte alpha = activeSide ? (byte)255 : (byte)70;

        if (w.Style == "solder")
        {
            var pts = new[] { Project(ax, ay, 0.12), Project(bx, ay, 0.12), Project(bx, by, 0.12) };
            if (w.Quality == "hot")
                DrawPolyline(dc, pts, PenOf("#3e2723", 6.5, (byte)(alpha * 0.55)));
            string col = w.Quality switch { "cold" => "#7d838a", "hot" => "#5f564e", _ => "#b9c2c9" };
            var pen = PenOf(col, 3.6, alpha);
            if (!activeSide) pen.DashStyle = DashStyles.Dash;
            DrawPolyline(dc, pts, pen);
            if (w.Quality == "good" && activeSide)
                DrawPolyline(dc, pts, PenOf("#f4f8fa", 1.0, alpha));
        }
        else
        {
            double topz = Math.Max(PinZ(a), PinZ(b)) + 2.6;
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                var start = Project(ax, ay, PinZ(a));
                g.BeginFigure(start, false, false);
                var pts = new List<Point>();
                for (int i = 1; i <= 16; i++)
                {
                    double t = i / 16.0, mt = 1 - t;
                    double x = mt * mt * mt * ax + 3 * mt * mt * t * ax + 3 * mt * t * t * bx + t * t * t * bx;
                    double y = mt * mt * mt * ay + 3 * mt * mt * t * ay + 3 * mt * t * t * by + t * t * t * by;
                    double z = mt * mt * mt * PinZ(a) + 3 * mt * mt * t * topz + 3 * mt * t * t * topz + t * t * t * PinZ(b);
                    pts.Add(Project(x, y, z));
                }
                g.PolyLineTo(pts, true, false);
            }
            geo.Freeze();
            var pen = PenOf(w.Color, 2.6, alpha);
            if (!activeSide) pen.DashStyle = DashStyles.Dash;
            dc.DrawGeometry(null, pen, geo);
        }
        // наплывы припоя по концам
        foreach (var (wx, wy) in new[] { (ax, ay), (bx, by) })
        {
            var s = Project(wx, wy, 0.35);
            if (w.Flux) dc.DrawEllipse(Brush("#d9a441", (byte)(activeSide ? 55 : 22)), null, s, 8.5, 6);
            if (w.Quality == "hot") dc.DrawEllipse(Brush("#3e2723", (byte)(activeSide ? 120 : 45)), null, s, 7, 5);
            string col = w.Quality switch { "cold" => "#878e96", "hot" => "#6e6259", _ => "#dde5ea" };
            dc.DrawEllipse(Brush(col, alpha), PenOf("#5f6b73", 0.8, alpha), s, 5.6, 4.6);
            if (w.Quality == "cold" && activeSide)
            {
                dc.DrawLine(PenOf("#37474f", 1.2), new Point(s.X - 3.6, s.Y - 1.8), new Point(s.X + 2.8, s.Y + 2.8));
                dc.DrawLine(PenOf("#37474f", 1.2), new Point(s.X + 3.6, s.Y - 2.6), new Point(s.X - 2.6, s.Y + 0.8));
            }
            else if (w.Quality == "good" && activeSide)
            {
                dc.DrawEllipse(Brush("#ffffff", 230), null, new Point(s.X - 1.8, s.Y - 2.0), 1.7, 1.4);
            }
        }
    }

    private void DrawPolyline(DrawingContext dc, Point[] pts, Pen pen)
    {
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(pts[0], false, false);
            g.PolyLineTo(pts.Skip(1).ToArray(), true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }

    private void DrawPins(DrawingContext dc, bool bottom)
    {
        foreach (var c in Data.Components)
        {
            foreach (var pin in c.Def.Pins)
            {
                var (wx, wy) = PinWorld(c, pin);
                var s = Project(wx, wy, bottom ? 0.25 : PinZ(c));
                string key = c.Id + ":" + pin.Name;
                bool isOut = c.Type == "esp32" && Data.PinModes.TryGetValue(key, out var m) && m == "OUTPUT";
                bool high = isOut && Data.PinOutputs.TryGetValue(key, out var o) && o == 1 && Data.Running;
                var brush = high ? Brush("#22c55e") : isOut ? Brush("#475569") : Brush("#ffd54f");
                if (high) dc.DrawEllipse(null, PenOf("#22c55e", 1.6), s, 8, 6.4);
                dc.DrawEllipse(brush, PenOf("#4e342e", 0.8), s, 3.6, 2.9);
                if (_pendingPin is { } pp && pp.Item1.Id == c.Id && pp.Item2 == pin.Name)
                    dc.DrawEllipse(null, PenOf("#ffff00", 2), s, 8.5, 7);
            }
        }
    }

    private void DrawLabels(DrawingContext dc, CircuitResult state, bool bottom)
    {
        // экраны — текст нужен всегда, но мелкий
        foreach (var c in Data.Components.Where(c => c.Type is "oled" or "tft" or "tft18" or "lcd1602"))
        {
            if (bottom) continue;
            string txt = c.Prop("displayText");
            if (txt.Length == 0) continue;
            var (ew, eh) = c.EffSize;
            var ctr = Project(c.X + ew / 2.0, c.Y + eh / 2.0, 1.0);
            Text(dc, txt.Length > 18 ? txt[..18] + "…" : txt, ctr, 7.5, c.Type == "lcd1602" ? "#1b5e20" : "#80deea", bold: true);
        }
        // подпись только у выбранной детали (маленькая)
        if (Selected != null && !bottom)
        {
            var c = Selected;
            var (ew, eh) = c.EffSize;
            double z = c.Def.HeightMm / 2.54 + 0.8;
            var at = Project(c.X + ew / 2.0, c.Y + eh / 2.0, z);
            string label = c.Type == "resistor" ? $"R {c.Prop("ohms", "220")}Ω" : c.Def.Short;
            Text(dc, label, at, 7.5, "#e2e8f0");
            var poly = new[]
            {
                Project(c.X, c.Y, z - 0.3), Project(c.X + ew, c.Y, z - 0.3),
                Project(c.X + ew, c.Y + eh, z - 0.3), Project(c.X, c.Y + eh, z - 0.3)
            };
            Poly(dc, poly, null, PenOf("#ffff00", 1.6));
        }
        // имена пинов: только в режимах проводки, только у ESP и выбранной, шрифт 6.5
        if (Mode is "Wire" or "Solder")
        {
            foreach (var c in Data.Components.Where(c => c.Type == "esp32" || c.Id == Selected?.Id))
            {
                foreach (var pin in c.Def.Pins)
                {
                    var (wx, wy) = PinWorld(c, pin);
                    var s = Project(wx, wy, PinZ(c));
                    bool hover = _hoverPin is { } hp && hp.Item1 == c.Id && hp.Item2 == pin.Name;
                    Text(dc, pin.Name, new Point(s.X, s.Y - 8), hover ? 8 : 6.5, hover ? "#ffff00" : "#e2e8f0", bold: hover);
                }
            }
        }
        else if (_hoverPin is { } hp2 && Data.ById(hp2.Item1) is { } hc)
        {
            var pin = hc.Def.Pins.FirstOrDefault(p => p.Name == hp2.Item2);
            if (pin != null)
            {
                var (wx, wy) = PinWorld(hc, pin);
                var s = Project(wx, wy, PinZ(hc));
                Text(dc, pin.Name, new Point(s.X, s.Y - 9), 8, "#ffff00", bold: true);
            }
        }
    }

    private void DrawMeasurements(DrawingContext dc)
    {
        foreach (var m in Data.Measurements)
        {
            var a = Project(m.AX, m.AY, 0.25);
            var b = Project(m.BX, m.BY, 0.25);
            dc.DrawLine(PenOf("#facc15", 1.8), a, b);
            // засечки по отверстиям вдоль линии
            double lenHoles = Math.Sqrt((m.BX - m.AX) * (m.BX - m.AX) + (m.BY - m.AY) * (m.BY - m.AY));
            int steps = (int)Math.Floor(lenHoles);
            for (int i = 1; i < steps; i++)
            {
                double t = i / lenHoles;
                var p = Project(m.AX + (m.BX - m.AX) * t, m.AY + (m.BY - m.AY) * t, 0.25);
                dc.DrawLine(PenOf("#facc15", 1.2), new Point(p.X, p.Y - 3.5), new Point(p.X, p.Y + 3.5));
            }
            foreach (var p in new[] { a, b })
            {
                dc.DrawLine(PenOf("#facc15", 1.8), new Point(p.X - 5, p.Y), new Point(p.X + 5, p.Y));
                dc.DrawLine(PenOf("#facc15", 1.8), new Point(p.X, p.Y - 5), new Point(p.X, p.Y + 5));
            }
            var mid = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2 - 10);
            string label = $"{m.DistanceMm:F1} мм";
            var ft = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 8, Brush("#111827"), VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawRoundedRectangle(Brush("#facc15"), null,
                new Rect(mid.X - ft.Width / 2 - 5, mid.Y - ft.Height / 2 - 2.5, ft.Width + 10, ft.Height + 5), 3, 3);
            dc.DrawText(ft, new Point(mid.X - ft.Width / 2, mid.Y - ft.Height / 2));
        }
        if (_measureStart is { } ms && _hoverBoard is { } hb)
        {
            var a = Project(ms.Item1, ms.Item2, 0.25);
            var b = Project(hb.Item1, hb.Item2, 0.25);
            var dashPen = PenOf("#facc15", 1.4);
            dashPen.DashStyle = DashStyles.Dash;
            dc.DrawLine(dashPen, a, b);
            var tmp = new Measurement { AX = ms.Item1, AY = ms.Item2, BX = hb.Item1, BY = hb.Item2 };
            Text(dc, $"{tmp.DistanceMm:F1} мм", new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2 - 10), 8, "#facc15", bold: true);
            dc.DrawEllipse(null, PenOf("#facc15", 2), a, 5, 5);
        }
    }

    // ---------------- hit-testing ----------------
    private (PlacedComponent, string)? HitPin(Point s)
    {
        (PlacedComponent, string)? best = null;
        double bestD = 18;
        foreach (var c in Data.Components)
            foreach (var pin in c.Def.Pins)
            {
                var (wx, wy) = PinWorld(c, pin);
                var p = Project(wx, wy, PinZ(c));
                double d = Math.Sqrt((s.X - p.X) * (s.X - p.X) + (s.Y - p.Y) * (s.Y - p.Y));
                if (d < bestD) { bestD = d; best = (c, pin.Name); }
            }
        return best;
    }

    private PlacedComponent HitComponent(Point s)
    {
        foreach (var c in Data.Components.AsEnumerable().Reverse())
        {
            var (ew, eh) = c.EffSize;
            double z = c.Def.HeightMm / 2.54;
            var poly = new[]
            {
                Project(c.X, c.Y, z), Project(c.X + ew, c.Y, z),
                Project(c.X + ew, c.Y + eh, z), Project(c.X, c.Y + eh, z)
            };
            if (PointInPoly(s, poly)) return c;
        }
        return null;
    }

    private static bool PointInPoly(Point p, Point[] poly)
    {
        bool inside = false;
        int j = poly.Length - 1;
        for (int i = 0; i < poly.Length; i++)
        {
            var (xi, yi) = (poly[i].X, poly[i].Y);
            var (xj, yj) = (poly[j].X, poly[j].Y);
            if ((yi > p.Y) != (yj > p.Y) && p.X < (xj - xi) * (p.Y - yi) / (yj - yi) + xi) inside = !inside;
            j = i;
        }
        return inside;
    }

    private (double, double) SnapToHole(Point s)
    {
        var (wx, wy) = Unproject(s);
        double sx = Math.Clamp(Math.Floor(wx) + 0.5, 0.5, Data.Cols - 0.5);
        double sy = Math.Clamp(Math.Floor(wy) + 0.5, 0.5, Data.Rows - 0.5);
        return (sx, sy);
    }

    // ---------------- drag&drop из меню ----------------
    protected override void OnDragOver(DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.Text) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.Text) is string typeId && Catalog.All.ContainsKey(typeId))
        {
            var pos = e.GetPosition(this);
            var (wx, wy) = Unproject(pos);
            var def = Catalog.ById(typeId);
            var c = Data.AddAt(typeId, (int)Math.Round(wx - def.W / 2.0), (int)Math.Round(wy - def.H / 2.0));
            Selected = c;
            Mode = "Move";
            LogMessage?.Invoke($"🧩 Перетащено из меню: {def.Title} → плата ({c.X},{c.Y})");
            StateChanged?.Invoke();
            InvalidateVisual();
        }
        e.Handled = true;
    }

    // ---------------- мышь ----------------
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var pos = e.GetPosition(this);
        _lastMouse = pos;
        CaptureMouse();

        if (Mode == "Measure")
        {
            var snap = SnapToHole(pos);
            if (_measureStart == null)
            {
                _measureStart = snap;
                LogMessage?.Invoke("📏 Замер: первая точка поставлена — кликните вторую.");
            }
            else
            {
                var m = new Measurement { AX = _measureStart.Value.Item1, AY = _measureStart.Value.Item2, BX = snap.Item1, BY = snap.Item2 };
                Data.Measurements.Add(m);
                LogMessage?.Invoke($"📏 Замер: {m}");
                _measureStart = null;
                StateChanged?.Invoke();
            }
            InvalidateVisual();
            return;
        }
        if (Mode is "Wire" or "Solder")
        {
            var hp = HitPin(pos);
            if (hp is { } hit)
            {
                if (_pendingPin == null)
                {
                    _pendingPin = hit;
                }
                else if (_pendingPin.Value.Item1.Id == hit.Item1.Id && _pendingPin.Value.Item2 == hit.Item2)
                {
                    _pendingPin = null;
                }
                else
                {
                    var aComp = _pendingPin.Value.Item1;
                    string quality = Mode == "Solder" ? SolderQuality() : "good";
                    Data.Wires.Add(new WireConn
                    {
                        AId = aComp.Id,
                        APin = _pendingPin.Value.Item2,
                        BId = hit.Item1.Id,
                        BPin = hit.Item2,
                        Style = Mode == "Solder" ? "solder" : "wire",
                        Color = Mode == "Solder" ? "#90a4ae" : WireColors[Data.Wires.Count % WireColors.Length],
                        Quality = quality,
                        TempC = SolderTempC,
                        Flux = UseFlux,
                        Side = WireSide
                    });
                    if (Mode == "Solder")
                    {
                        string verdict = quality switch
                        {
                            "cold" => "🥶 ХОЛОДНАЯ пайка — перепаяйте при 320–380°C с флюсом",
                            "hot" => "🔥 ПЕРЕГРЕВ — поджог платы, перепаяйте при 320–380°C",
                            _ => "✨ блестящий наплыв припоя"
                        };
                        LogMessage?.Invoke($"Припаяно при {SolderTempC}°C [{WireSide}]: {aComp.Def.Title}.{_pendingPin.Value.Item2} — {hit.Item1.Def.Title}.{hit.Item2}. {verdict}");
                    }
                    else
                    {
                        LogMessage?.Invoke($"Провод [{WireSide}]: {aComp.Def.Title}.{_pendingPin.Value.Item2} — {hit.Item1.Def.Title}.{hit.Item2}");
                    }
                    _pendingPin = null;
                    StateChanged?.Invoke();
                }
                InvalidateVisual();
                return;
            }
        }
        var hc = HitComponent(pos);
        switch (Mode)
        {
            case "Camera":
                _rotatingCam = true;
                break;
            case "MoveBoard":
                _panning = true;
                break;
            case "Delete":
                if (hc != null)
                {
                    Data.Components.Remove(hc);
                    Data.Wires.RemoveAll(w => w.AId == hc.Id || w.BId == hc.Id);
                    if (Selected == hc) Selected = null;
                    StateChanged?.Invoke();
                }
                break;
            case "Sim":
                if (hc != null)
                {
                    if (hc.Type == "button")
                    {
                        if (!Data.Pressed.Add(hc.Id)) Data.Pressed.Remove(hc.Id);
                        LogMessage?.Invoke(Data.Pressed.Contains(hc.Id) ? "🔘 Кнопка НАЖАТА" : "🔘 Кнопка отпущена");
                    }
                    else if (hc.Type == "pot")
                    {
                        int v = (int.Parse(hc.Prop("value", "512")) + 128) % 1024;
                        hc.Props["value"] = v.ToString();
                        LogMessage?.Invoke($"🎛 Потенциометр: {v}/1023");
                    }
                    else Selected = hc;
                    StateChanged?.Invoke();
                }
                break;
            case "Move":
                if (hc != null)
                {
                    Selected = hc;
                    var (wx, wy) = Unproject(pos);
                    _dragComp = hc;
                    _dragOffset = (wx - hc.X, wy - hc.Y);
                    StateChanged?.Invoke();
                }
                else
                {
                    Selected = null;
                    StateChanged?.Invoke();
                }
                break;
        }
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var pos = e.GetPosition(this);
        // наведение на пин (для подписи и курсора)
        var hp = HitPin(pos);
        var newHover = hp is { } h ? (h.Item1.Id, h.Item2) : ((string, string)?)null;
        if (newHover != _hoverPin) { _hoverPin = newHover; InvalidateVisual(); }
        _hoverBoard = Mode == "Measure" ? SnapToHole(pos) : null;

        if (_rotatingCam && e.LeftButton == MouseButtonState.Pressed)
        {
            CameraAngle = Math.Clamp(CameraAngle + (pos.X - _lastMouse.X) * 0.25, -85, 85);
            Tilt = Math.Clamp(Tilt + (pos.Y - _lastMouse.Y) * 0.002, 0.30, 0.85);
            InvalidateVisual();
        }
        else if (_panning && e.LeftButton == MouseButtonState.Pressed)
        {
            Pan = new Point(Pan.X + pos.X - _lastMouse.X, Pan.Y + pos.Y - _lastMouse.Y);
            InvalidateVisual();
        }
        else if (_dragComp != null && e.LeftButton == MouseButtonState.Pressed)
        {
            var (wx, wy) = Unproject(pos);
            var (ew, eh) = _dragComp.EffSize;
            _dragComp.X = Math.Clamp((int)Math.Round(wx - _dragOffset.Item1), 0, Data.Cols - ew);
            _dragComp.Y = Math.Clamp((int)Math.Round(wy - _dragOffset.Item2), 0, Data.Rows - eh);
            InvalidateVisual();
        }
        else if (Mode == "Measure" && _measureStart != null)
        {
            InvalidateVisual();
        }
        _lastMouse = pos;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        bool wasDragging = _dragComp != null;
        _dragComp = null;
        _panning = false;
        _rotatingCam = false;
        ReleaseMouseCapture();
        if (wasDragging) StateChanged?.Invoke();
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        // правый клик: отменить текущее действие
        if (_measureStart != null) { _measureStart = null; LogMessage?.Invoke("📏 Замер отменён"); }
        if (_pendingPin != null) { _pendingPin = null; LogMessage?.Invoke("Соединение отменено"); }
        InvalidateVisual();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        Zoom = Math.Clamp(Zoom + (e.Delta > 0 ? 0.08 : -0.08), 0.55, 2.4);
        InvalidateVisual();
    }
}
