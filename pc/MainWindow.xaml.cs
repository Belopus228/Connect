using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace ConnectPC;

public partial class MainWindow : Window
{
    private readonly BoardView _board = new();
    private readonly MusicPlayer _music = new();
    private FirmwareRunner _runner;

    private DataGrid _wireGrid;
    private DataGrid _measureGrid;
    private Slider _volSlider;
    private ComboBox _sideComboBox;

    public MainWindow()
    {
        InitializeComponent();
        _board.Data = ProjectData.CreateDemo();
        _board.AllowDrop = true;
        _board.LogMessage += Log;
        _board.StateChanged += RefreshState;
        DataContext = this;

        BoardHost.Content = _board;
        KeyDown += OnKeyDown;
        _runner = new FirmwareRunner(_board.Data);
        _runner.OnOutput = m => Dispatcher.Invoke(() => Log(m));
        _runner.StateChanged += () =>
        {
            _board.InvalidateVisual();
            if (EspPinsList != null) RefreshEspPins();
        };
        ShowPanel("tutorial");
        Log("✅ Connect v2.9 — нативная Windows-сборка открыта");
        Log("🎓 Открыт туториал: пройдите шаги слева, чтобы освоить интерфейс.");
        Log("Перетаскивайте детали из меню «＋ Добавить» на плату мышкой.");
        RefreshState();
    }

    private ListBox EspPinsList;

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox) return;
        switch (e.Key)
        {
            case Key.D1: SetMode("Camera"); break;
            case Key.D2: SetMode("MoveBoard"); break;
            case Key.D3: SetMode("Move"); break;
            case Key.D4: SetMode("Wire"); break;
            case Key.D5: SetMode("Sim"); break;
            case Key.D6: SetMode("Measure"); break;
            case Key.D7: SetMode("Delete"); break;
            case Key.R:
                if (_board.Selected != null) { _board.RotateComponent(_board.Selected); RefreshState(); }
                break;
            case Key.Q:
                if (_board.Selected != null) { _board.RotateComponentZ(_board.Selected, -15); RefreshState(); }
                break;
            case Key.E:
                if (_board.Selected != null) { _board.RotateComponentZ(_board.Selected, 15); RefreshState(); }
                break;
            case Key.PageUp:
                if (_board.Selected != null) { _board.Selected.Z = Math.Clamp(_board.Selected.Z + 0.5, 0, 12); _board.InvalidateVisual(); RefreshState(); }
                break;
            case Key.PageDown:
                if (_board.Selected != null) { _board.Selected.Z = Math.Clamp(_board.Selected.Z - 0.5, 0, 12); _board.InvalidateVisual(); RefreshState(); }
                break;
            case Key.Delete:
                if (_board.Selected != null)
                {
                    var c = _board.Selected;
                    _board.Data.Components.Remove(c);
                    _board.Data.Wires.RemoveAll(w => w.AId == c.Id || w.BId == c.Id);
                    _board.Selected = null;
                    RefreshState();
                    Log($"🗑 Удалено: {c.Def.Title}");
                }
                break;
            case Key.F: _board.FitView(); break;
        }
    }

    // -------------------- панель --------------------
    private void ShowPanel(string kind)
    {
        SideTitle.Text = kind switch
        {
            "add" => "＋ ДОБАВЛЕНИЕ ДЕТАЛЕЙ",
            "tools" => "🛠 ИНСТРУМЕНТЫ",
            "tutorial" => "🎓 ТУТОРИАЛ",
            "esp" => "◼ ПАНЕЛЬ ESP32",
            "code" => "⌨ ПРОШИВКА",
            "project" => "💾 ПРОЕКТ",
            _ => "ПАНЕЛЬ"
        };
        SideContent.Children.Clear();
        SidePanel.Visibility = Visibility.Visible;
        switch (kind)
        {
            case "add": BuildAddPanel(); break;
            case "tools": BuildToolsPanel(); break;
            case "tutorial": BuildTutorialPanel(); break;
            case "esp": BuildEspPanel(); break;
            case "code": BuildCodePanel(); break;
            case "project": BuildProjectPanel(); break;
        }
        if (_runner != null && EspPinsList != null) RefreshEspPins();
    }

    private void TutorialBtn_Click(object sender, RoutedEventArgs e) => ShowPanel("tutorial");

    private void ModeRail_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string mode) SetMode(mode);
    }
    private void AddBtn_Click(object sender, RoutedEventArgs e) => ShowPanel("add");
    private void ToolsBtn_Click(object sender, RoutedEventArgs e) => ShowPanel("tools");
    private void EspPanelBtn_Click(object sender, RoutedEventArgs e) => ShowPanel("esp");
    private void CodeBtn_Click(object sender, RoutedEventArgs e) => ShowPanel("code");
    private void ProjectBtn_Click(object sender, RoutedEventArgs e) => ShowPanel("project");
    private void CloseSide_Click(object sender, RoutedEventArgs e) => SidePanel.Visibility = Visibility.Collapsed;
    private void DemoBtn_Click(object sender, RoutedEventArgs e)
    {
        _runner?.Stop();
        _board.Data = ProjectData.CreateDemo();
        _board.Selected = null;
        _runner = new FirmwareRunner(_board.Data);
        _runner.OnOutput = m => Dispatcher.Invoke(() => Log(m));
        _runner.StateChanged += () => _board.InvalidateVisual();
        RefreshState();
        Log("🔄 Демо-схема восстановлена");
    }

    private void MusicBtn_Click(object sender, RoutedEventArgs e)
    {
        _music.Toggle();
        MusicBtn.Content = _music.IsPlaying ? "🎵 вкл" : "🎵 выкл";
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender == VolumeSlider) _music.Volume = e.NewValue;
    }

    private void ResetView_Click(object sender, RoutedEventArgs e) => _board.ResetView();

    // -------------------- ADD --------------------
    private void BuildAddPanel()
    {
        var hint = new TextBlock
        {
            Text = "Перетащите деталь мышкой на плату.\nКлик — поставить в свободное место.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            Margin = new Thickness(0, 0, 0, 8)
        };
        SideContent.Children.Add(hint);
        foreach (var d in Catalog.All.Values.OrderBy(d => d.Title))
        {
            var b = new Button
            {
                Content = $"{d.Title}   •   {d.Short}",
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Tag = d.Id
            };
            b.Click += (_, _) =>
            {
                var c = _board.Data.AddFree((string)b.Tag);
                if (c != null)
                {
                    _board.Selected = c;
                    Log($"🧩 {d.Title} → плата ({c.X},{c.Y})");
                    RefreshState();
                }
            };
            b.PreviewMouseMove += (_, ev) =>
            {
                if (ev.LeftButton == MouseButtonState.Pressed)
                    DragDrop.DoDragDrop(b, new DataObject(DataFormats.Text, (string)b.Tag), DragDropEffects.Copy);
            };
            SideContent.Children.Add(b);
        }
    }

    // -------------------- TOOLS --------------------
    private void BuildToolsPanel()
    {
        SideContent.Children.Add(SectionTitle("РЕЖИМ РАБОТЫ"));
        SideContent.Children.Add(new TextBlock
        {
            Text = "Режимы переключаются на левой панели инструментов:\n📷 камера • ✋ плата • ↔ деталь • ✒ провод • 🖱 симуляция • 📏 замер • 🗑 удалить.\n\nАктивный режим подсвечен голубым.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            Margin = new Thickness(0, 0, 0, 4)
        });

        SideContent.Children.Add(SectionTitle("ПЛАТА: ПОВОРОТ И СТОРОНА"));
        var rotRow = new StackPanel { Orientation = Orientation.Horizontal };
        var rotL = new Button { Content = "⟲ −90°" };
        rotL.Click += (_, _) => { _board.RotateBoard(-1); RefreshState(); };
        var rotR = new Button { Content = "⟳ +90°" };
        rotR.Click += (_, _) => { _board.RotateBoard(1); RefreshState(); };
        rotRow.Children.Add(rotL);
        rotRow.Children.Add(rotR);
        SideContent.Children.Add(rotRow);
        SideContent.Children.Add(new TextBlock
        {
            Text = "Поворот — вместе со схемой и замерами.",
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            TextWrapping = TextWrapping.Wrap
        });

        var viewRow = new StackPanel();
        var topBtn = new Button { Content = _board.ViewSide == "Top" ? "● Смотреть ВЕРХ" : "Смотреть ВЕРХ" };
        topBtn.Click += (_, _) => { _board.ViewSide = "Top"; RefreshState(); };
        var botBtn = new Button { Content = _board.ViewSide == "Bottom" ? "● Смотреть НИЗ (пайка)" : "Смотреть НИЗ (пайка)" };
        botBtn.Click += (_, _) => { _board.ViewSide = "Bottom"; RefreshState(); };
        viewRow.Children.Add(topBtn);
        viewRow.Children.Add(botBtn);
        SideContent.Children.Add(viewRow);

        SideContent.Children.Add(new TextBlock { Text = "Новые провода класть на:", Margin = new Thickness(0, 4, 0, 2) });
        _sideComboBox = new ComboBox { Width = 150, HorizontalAlignment = HorizontalAlignment.Left };
        _sideComboBox.Items.Add("Top");
        _sideComboBox.Items.Add("Bottom");
        _sideComboBox.SelectedItem = _board.WireSide;
        _sideComboBox.SelectionChanged += (_, _) =>
        {
            _board.WireSide = _sideComboBox.SelectedItem?.ToString() ?? "Top";
            RefreshState();
        };
        SideContent.Children.Add(_sideComboBox);
        SideContent.Children.Add(new TextBlock
        {
            Text = "Пунктирные провода — на другой стороне платы.",
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            TextWrapping = TextWrapping.Wrap
        });

        SideContent.Children.Add(SectionTitle("КОРПУС ПЛАТЫ"));
        var caseBox = new ComboBox { Width = 190, HorizontalAlignment = HorizontalAlignment.Left };
        caseBox.Items.Add("Без корпуса");
        caseBox.Items.Add("Прозрачный акрил");
        caseBox.Items.Add("Чёрный ABS");
        caseBox.Items.Add("Синий металл");
        caseBox.SelectedIndex = _board.Data.CaseType switch
        {
            "Acrylic" => 1,
            "Black" => 2,
            "BlueMetal" => 3,
            _ => 0
        };
        caseBox.SelectionChanged += (_, _) =>
        {
            _board.Data.CaseType = caseBox.SelectedIndex switch
            {
                1 => "Acrylic",
                2 => "Black",
                3 => "BlueMetal",
                _ => "None"
            };
            RefreshState();
            Log(_board.Data.CaseType == "None" ? "Корпус убран" : $"🧰 Корпус добавлен: {caseBox.SelectedItem}");
        };
        SideContent.Children.Add(caseBox);
        SideContent.Children.Add(new TextBlock
        {
            Text = "Корпус ставится вокруг платы: стенки, винты,\nу акрила — прозрачная крышка. Сохраняется в проекте.",
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            TextWrapping = TextWrapping.Wrap
        });

        var caseMarginLabel = new TextBlock { Text = $"Отступ корпуса: {_board.Data.CaseMargin:F1} кл." };
        var caseMarginSlider = new Slider { Minimum = 0.5, Maximum = 4, Value = _board.Data.CaseMargin, TickFrequency = 0.5 };
        caseMarginSlider.ValueChanged += (_, ev) =>
        {
            _board.Data.CaseMargin = ev.NewValue;
            caseMarginLabel.Text = $"Отступ корпуса: {ev.NewValue:F1} кл.";
            _board.InvalidateVisual();
        };
        var caseHeightLabel = new TextBlock { Text = $"Высота корпуса: {_board.Data.CaseHeight:F1} кл." };
        var caseHeightSlider = new Slider { Minimum = 2, Maximum = 9, Value = _board.Data.CaseHeight, TickFrequency = 0.5 };
        caseHeightSlider.ValueChanged += (_, ev) =>
        {
            _board.Data.CaseHeight = ev.NewValue;
            caseHeightLabel.Text = $"Высота корпуса: {ev.NewValue:F1} кл.";
            _board.InvalidateVisual();
        };
        SideContent.Children.Add(caseMarginLabel);
        SideContent.Children.Add(caseMarginSlider);
        SideContent.Children.Add(caseHeightLabel);
        SideContent.Children.Add(caseHeightSlider);

        SideContent.Children.Add(SectionTitle("STL-КОРПУС (ИМПОРТ МОДЕЛИ)"));
        var stlStatus = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(_board.Data.CaseStlPath)
                ? "STL-модель не импортирована. Поддерживаются бинарные и ASCII .stl, единицы — миллиметры."
                : $"Модель: {_board.Data.CaseStlName}\n{_board.Data.CaseStlPath}",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184))
        };
        SideContent.Children.Add(stlStatus);
        var importStlBtn = new Button { Content = "📥 Импорт STL-корпуса…" };
        importStlBtn.Click += (_, _) =>
        {
            var dlg = new OpenFileDialog { Filter = "STL модель (*.stl)|*.stl|Все файлы (*.*)|*.*" };
            if (dlg.ShowDialog() != true) return;
            try
            {
                var mesh = StlMesh.Load(dlg.FileName);
                _board.Data.CaseStlPath = dlg.FileName;
                _board.Data.CaseStlName = Path.GetFileName(dlg.FileName);
                _board.Data.CaseType = "None";
                Log($"🧰 STL-корпус импортирован: {_board.Data.CaseStlName} — {mesh.Triangles.Count} треугольников, " +
                    $"{mesh.SizeX:F0}×{mesh.SizeY:F0}×{mesh.SizeZ:F0} мм. Модель по центру платы; масштаб и сдвиги — ползунками ниже.");
                RefreshState();
                ShowPanel("tools");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось прочитать STL: " + ex.Message, "Импорт STL");
            }
        };
        SideContent.Children.Add(importStlBtn);
        var removeStlBtn = new Button { Content = "Убрать STL-корпус" };
        removeStlBtn.Click += (_, _) =>
        {
            _board.Data.CaseStlPath = "";
            _board.Data.CaseStlName = "";
            Log("STL-корпус убран");
            RefreshState();
            ShowPanel("tools");
        };
        SideContent.Children.Add(removeStlBtn);
        var stlScaleLabel = new TextBlock { Text = $"Масштаб STL: ×{_board.Data.CaseStlScale:F1}" };
        var stlScaleSlider = new Slider { Minimum = 0.2, Maximum = 3, Value = Math.Clamp(_board.Data.CaseStlScale, 0.2, 3), TickFrequency = 0.1 };
        stlScaleSlider.ValueChanged += (_, ev) =>
        {
            _board.Data.CaseStlScale = ev.NewValue;
            stlScaleLabel.Text = $"Масштаб STL: ×{ev.NewValue:F1}";
            _board.InvalidateVisual();
        };
        SideContent.Children.Add(stlScaleLabel);
        SideContent.Children.Add(stlScaleSlider);
        var stlXLabel = new TextBlock { Text = $"Сдвиг STL по X: {_board.Data.CaseStlX:F1} кл." };
        var stlXSlider = new Slider { Minimum = -15, Maximum = 15, Value = _board.Data.CaseStlX, TickFrequency = 0.5 };
        stlXSlider.ValueChanged += (_, ev) =>
        {
            _board.Data.CaseStlX = ev.NewValue;
            stlXLabel.Text = $"Сдвиг STL по X: {ev.NewValue:F1} кл.";
            _board.InvalidateVisual();
        };
        SideContent.Children.Add(stlXLabel);
        SideContent.Children.Add(stlXSlider);
        var stlYLabel = new TextBlock { Text = $"Сдвиг STL по Y: {_board.Data.CaseStlY:F1} кл." };
        var stlYSlider = new Slider { Minimum = -15, Maximum = 15, Value = _board.Data.CaseStlY, TickFrequency = 0.5 };
        stlYSlider.ValueChanged += (_, ev) =>
        {
            _board.Data.CaseStlY = ev.NewValue;
            stlYLabel.Text = $"Сдвиг STL по Y: {ev.NewValue:F1} кл.";
            _board.InvalidateVisual();
        };
        SideContent.Children.Add(stlYLabel);
        SideContent.Children.Add(stlYSlider);
        var stlZLabel = new TextBlock { Text = $"Сдвиг STL по Z (высота): {_board.Data.CaseStlZ:F1} кл." };
        var stlZSlider = new Slider { Minimum = -3, Maximum = 12, Value = _board.Data.CaseStlZ, TickFrequency = 0.5 };
        stlZSlider.ValueChanged += (_, ev) =>
        {
            _board.Data.CaseStlZ = ev.NewValue;
            stlZLabel.Text = $"Сдвиг STL по Z (высота): {ev.NewValue:F1} кл.";
            _board.InvalidateVisual();
        };
        SideContent.Children.Add(stlZLabel);
        SideContent.Children.Add(stlZSlider);
        SideContent.Children.Add(new TextBlock
        {
            Text = "Проект хранит путь к STL-файлу, а не саму модель: не перемещайте файл после импорта.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184))
        });

        SideContent.Children.Add(SectionTitle("ВЫБРАННАЯ ДЕТАЛЬ"));
        var sel = _board.Selected;
        if (sel == null)
        {
            SideContent.Children.Add(new TextBlock
            {
                Text = "Ничего не выбрано. В режиме «Деталь» кликните компонент:\nпоявятся стрелки X (красная), Y (зелёная), Z (синяя, вверх) и ручки поворотов ⟳/X/Y по всем осям.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184))
            });
        }
        else
        {
            SideContent.Children.Add(new TextBlock
            {
                Text = $"{sel.Def.Title} @({sel.X},{sel.Y}) высота Z {sel.Z:F1}\nповорот Z {sel.Rot}°, наклон X {sel.RotX:F0}° / Y {sel.RotY:F0}°",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4)
            });
            var selRow = new StackPanel { Orientation = Orientation.Horizontal };
            var rotBtn = new Button { Content = "⟳ Повернуть" };
            rotBtn.Click += (_, _) => { if (_board.Selected != null) { _board.RotateComponent(_board.Selected); ShowPanel("tools"); } };
            var delBtn = new Button { Content = "🗑 Удалить" };
            delBtn.Click += (_, _) =>
            {
                var c = _board.Selected;
                if (c == null) return;
                _board.Data.Components.Remove(c);
                _board.Data.Wires.RemoveAll(w => w.AId == c.Id || w.BId == c.Id);
                _board.Selected = null;
                RefreshState();
                ShowPanel("tools");
            };
            selRow.Children.Add(rotBtn);
            selRow.Children.Add(delBtn);
            SideContent.Children.Add(selRow);

            // ---- движение и вращение по ВСЕМ осям ----
            SideContent.Children.Add(new TextBlock
            {
                Text = "Оси: стрелки гизмо X/Y/Z на плате, Shift+тащить — высота,\nQ/E — поворот Z, PgUp/PgDn — высота. Точно — ползунками:",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184))
            });
            var zLabel = new TextBlock { Text = $"Высота Z над платой: {sel.Z:F1} кл." };
            var zSlider = new Slider { Minimum = 0, Maximum = 12, Value = sel.Z, TickFrequency = 0.5 };
            zSlider.ValueChanged += (_, ev) =>
            {
                if (_board.Selected == null) return;
                _board.Selected.Z = ev.NewValue;
                zLabel.Text = $"Высота Z над платой: {ev.NewValue:F1} кл.";
                _board.InvalidateVisual();
            };
            SideContent.Children.Add(zLabel);
            SideContent.Children.Add(zSlider);
            double yawShown = sel.Rot > 180 ? sel.Rot - 360 : sel.Rot;
            var yawLabel = new TextBlock { Text = $"Поворот вокруг Z: {sel.Rot}°" };
            var yawSlider = new Slider { Minimum = -180, Maximum = 180, Value = yawShown, TickFrequency = 15 };
            yawSlider.ValueChanged += (_, ev) =>
            {
                if (_board.Selected == null) return;
                _board.Selected.Rot = ((int)Math.Round(ev.NewValue) % 360 + 360) % 360;
                _board.ClampComponentPos(_board.Selected);
                yawLabel.Text = $"Поворот вокруг Z: {_board.Selected.Rot}°";
                _board.InvalidateVisual();
            };
            SideContent.Children.Add(yawLabel);
            SideContent.Children.Add(yawSlider);
            var tiltXLabel = new TextBlock { Text = $"Наклон вокруг X: {sel.RotX:F0}°" };
            var tiltXSlider = new Slider { Minimum = -180, Maximum = 180, Value = sel.RotX, TickFrequency = 5 };
            tiltXSlider.ValueChanged += (_, ev) =>
            {
                if (_board.Selected == null) return;
                _board.Selected.RotX = ev.NewValue;
                tiltXLabel.Text = $"Наклон вокруг X: {ev.NewValue:F0}°";
                _board.InvalidateVisual();
            };
            SideContent.Children.Add(tiltXLabel);
            SideContent.Children.Add(tiltXSlider);
            var tiltYLabel = new TextBlock { Text = $"Наклон вокруг Y: {sel.RotY:F0}°" };
            var tiltYSlider = new Slider { Minimum = -180, Maximum = 180, Value = sel.RotY, TickFrequency = 5 };
            tiltYSlider.ValueChanged += (_, ev) =>
            {
                if (_board.Selected == null) return;
                _board.Selected.RotY = ev.NewValue;
                tiltYLabel.Text = $"Наклон вокруг Y: {ev.NewValue:F0}°";
                _board.InvalidateVisual();
            };
            SideContent.Children.Add(tiltYLabel);
            SideContent.Children.Add(tiltYSlider);
            var poseRow = new StackPanel { Orientation = Orientation.Horizontal };
            var tiltXBtn = new Button { Content = "Наклон X +45°" };
            tiltXBtn.Click += (_, _) => { if (_board.Selected != null) { _board.TiltComponent(_board.Selected, "X"); ShowPanel("tools"); } };
            var tiltYBtn = new Button { Content = "Наклон Y +45°" };
            tiltYBtn.Click += (_, _) => { if (_board.Selected != null) { _board.TiltComponent(_board.Selected, "Y"); ShowPanel("tools"); } };
            var resetPoseBtn = new Button { Content = "Сброс высоты/наклонов" };
            resetPoseBtn.Click += (_, _) =>
            {
                var c = _board.Selected;
                if (c == null) return;
                c.Z = 0; c.RotX = 0; c.RotY = 0;
                Log($"↺ {c.Def.Title}: высота и наклоны сброшены");
                RefreshState();
                ShowPanel("tools");
            };
            poseRow.Children.Add(tiltXBtn);
            poseRow.Children.Add(tiltYBtn);
            poseRow.Children.Add(resetPoseBtn);
            SideContent.Children.Add(poseRow);
            if (sel.Type == "resistor")
            {
                var ohmsBox = new ComboBox { Width = 150, HorizontalAlignment = HorizontalAlignment.Left };
                foreach (var o in new[] { "100", "220", "470", "1000", "4700", "10000" }) ohmsBox.Items.Add(o + " Ω");
                ohmsBox.SelectedItem = sel.Prop("ohms", "220") + " Ω";
                ohmsBox.SelectionChanged += (_, _) =>
                {
                    if (_board.Selected != null && ohmsBox.SelectedItem is string t)
                        _board.Selected.Props["ohms"] = t.Replace(" Ω", "");
                    _board.InvalidateVisual();
                };
                SideContent.Children.Add(new TextBlock { Text = "Сопротивление:" });
                SideContent.Children.Add(ohmsBox);
            }
            if (sel.Type == "led")
            {
                var colBox = new ComboBox { Width = 150, HorizontalAlignment = HorizontalAlignment.Left };
                foreach (var cn in new[] { "Красный", "Зелёный", "Синий", "Жёлтый", "Белый" }) colBox.Items.Add(cn);
                colBox.SelectedItem = sel.Prop("colorName", "Красный");
                colBox.SelectionChanged += (_, _) =>
                {
                    if (_board.Selected != null && colBox.SelectedItem is string t)
                        _board.Selected.Props["colorName"] = t;
                    _board.InvalidateVisual();
                };
                SideContent.Children.Add(new TextBlock { Text = "Цвет светодиода:" });
                SideContent.Children.Add(colBox);
            }
        }

        SideContent.Children.Add(SectionTitle("ПРОВОД"));
        var thickLabel = new TextBlock { Text = $"Толщина новых проводов: ×{_board.WireThickness:F1}" };
        var thickSlider = new Slider { Minimum = 0.6, Maximum = 2.4, Value = _board.WireThickness, TickFrequency = 0.2 };
        thickSlider.ValueChanged += (_, ev) =>
        {
            _board.WireThickness = ev.NewValue;
            thickLabel.Text = $"Толщина новых проводов: ×{ev.NewValue:F1}";
            _board.InvalidateVisual();
        };
        SideContent.Children.Add(thickLabel);
        SideContent.Children.Add(thickSlider);
        SideContent.Children.Add(new TextBlock
        {
            Text = "В режиме провода колесо мыши — высота звена над платой.\nГорячие клавиши: 1–7 режимы, R — повернуть деталь, Q/E — поворот Z ±15°, PgUp/PgDn — высота детали, Del — удалить, F — вписать плату.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184))
        });

        SideContent.Children.Add(SectionTitle("ЗВУК И ВИД"));
        var perspBox = new CheckBox { Content = "Перспективная камера (настоящий 3D)", IsChecked = _board.UsePerspective };
        perspBox.Checked += (_, _) => { _board.UsePerspective = true; _board.InvalidateVisual(); };
        perspBox.Unchecked += (_, _) => { _board.UsePerspective = false; _board.InvalidateVisual(); };
        SideContent.Children.Add(perspBox);
        var boardBox = new CheckBox { Content = "Макетная плата (можно выключить)", IsChecked = _board.Data.ShowBoard };
        boardBox.Checked += (_, _) => { _board.Data.ShowBoard = true; _board.InvalidateVisual(); Log("Макетная плата показана"); };
        boardBox.Unchecked += (_, _) => { _board.Data.ShowBoard = false; _board.InvalidateVisual(); Log("Макетная плата выключена — схема строится прямо в пространстве"); };
        SideContent.Children.Add(boardBox);
        var musicRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var mBtn = new Button { Content = _music.IsPlaying ? "🎵 Музыка: вкл" : "🎵 Музыка: выкл" };
        mBtn.Click += (_, _) => { _music.Toggle(); mBtn.Content = _music.IsPlaying ? "🎵 Музыка: вкл" : "🎵 Музыка: выкл"; };
        _volSlider = new Slider { Minimum = 0, Maximum = 1, Value = _music.Volume, Width = 90, VerticalAlignment = VerticalAlignment.Center };
        _volSlider.ValueChanged += (_, ev) => _music.Volume = ev.NewValue;
        musicRow.Children.Add(mBtn);
        musicRow.Children.Add(_volSlider);
        SideContent.Children.Add(musicRow);
        var resetBtn = new Button { Content = "Сброс камеры и поворота" };
        resetBtn.Click += (_, _) => _board.ResetView();
        SideContent.Children.Add(resetBtn);
        var fitBtn = new Button { Content = "Вписать плату (F)" };
        fitBtn.Click += (_, _) => _board.FitView();
        SideContent.Children.Add(fitBtn);

        SideContent.Children.Add(SectionTitle("ЗАМЕРЫ (шаг отверстий 2.54 мм)"));
        var clearMeas = new Button { Content = "Очистить замеры" };
        clearMeas.Click += (_, _) =>
        {
            _board.Data.Measurements.Clear();
            RefreshState();
            Log("📏 Все замеры удалены");
        };
        SideContent.Children.Add(clearMeas);
        _measureGrid = BuildMeasureGrid();
        SideContent.Children.Add(_measureGrid);

        SideContent.Children.Add(SectionTitle("СОЕДИНЕНИЯ"));
        var clearWires = new Button { Content = "Удалить все соединения" };
        clearWires.Click += (_, _) =>
        {
            _board.Data.Wires.Clear();
            _board.Data.FreeWires.Clear();
            RefreshState();
            Log("Удалены все соединения и свободные линии");
        };
        SideContent.Children.Add(clearWires);
        _wireGrid = BuildWireGrid();
        SideContent.Children.Add(_wireGrid);
    }

    private string QualitySuffix() => _board.SolderQuality() switch
    {
        "cold" => "  •  холодная пайка 🥶",
        "hot" => "  •  перегрев 🔥",
        _ => "  •  хорошая пайка ✨"
    };

    // -------------------- TUTORIAL --------------------
    private int _tutorialIndex;

    private static readonly (string Title, string Body, string Try, string Mode)[] TutorialSteps =
    {
        ("Добро пожаловать в Connect!",
         "Это 3D-симулятор электроники: слева — панель режимов, по центру — плата, справа — инструменты, сверху — проекты и запуск.",
         "Нажмите «Далее» — каждый шаг сам включит нужный режим, а вы сразу попробуете на плате.", "Camera"),
        ("Камера без инверсии",
         "Зажмите левую кнопку и ведите мышь: плата поворачивается за рукой — вправо, влево, вверх и вниз. То же вращение — зажатым колёсиком мыши в любом режиме.",
         "Повращайте плату левой и средней кнопкой. Колесо без зажатия — масштаб.", "Camera"),
        ("Детали на плату",
         "Кнопка «＋ Добавить» сверху: деталь перетаскивается мышкой на плату. Режим «↔ Деталь» (слева): стрелки X/Y/Z двигают деталь по всем осям (Z — вверх), ручки ⟳/X/Y крутят вокруг всех осей, Shift+тащить — высота, точные углы — ползунками в Инструментах.",
         "Добавьте светодиод и резистор и подвиньте их режимом «Деталь».", "Move"),
        ("Провода по звеньям",
         "ЛКМ ставит звено, колесо мыши меняет ВЫСОТУ звена над платой (как близко оно к камере), ПКМ завершает провод. Узлы готовых проводов можно перетаскивать мышкой.",
         "Проведите провод от батареи к ESP32 тремя-четырьмя звеньями разной высоты.", "Wire"),
        ("Провода сами находят контакты",
         "Отдельной пайки нет: подведите конец провода к контакту детали — он сам прилипнет (голубое кольцо «контакт!») и соединится. Все контакты подписаны на плате, у ESP32 их 30. Толщина проводов — ползунком в Инструментах.",
         "Соедините «+» батареи с VIN, а «−» — с GND на ESP32.", "Wire"),
        ("Корпус, низ платы, замер",
         "«Инструменты → Корпус платы»: акрил с прозрачной крышкой, чёрный ABS или металл. Там же «STL-корпус»: кнопка импорта .stl, масштаб и сдвиги ползунками (проект хранит путь к файлу). «Смотреть НИЗ» — обратная сторона. Режим «Замер»: два клика — расстояние в миллиметрах.",
         "Добавьте акриловый корпус или импортируйте свой STL и замерьте диагональ платы.", "Measure"),
        ("Прошивка и запуск",
         "«⌨ Прошивка» сверху: примеры кода или свой, кнопка «▶ Запуск». Панель ESP показывает живые HIGH/LOW, в режиме «Симуляция» кликаются кнопка и потенциометр на плате. Проект — «💾 Проект».",
         "Запустите демо-прошивку и посмотрите, как мигает D13.", "Sim"),
        ("Готово!",
         "Вы знаете всё основное: камера, детали, провода, контакты, корпус, замеры и прошивка. Туториал всегда доступен кнопкой «🎓 Туториал» сверху.",
         "Удачных схем! Дальше — «Инструменты» и кнопка «Демо» для готовой схемы.", "Camera"),
    };

    private void BuildTutorialPanel()
    {
        _tutorialIndex = Math.Clamp(_tutorialIndex, 0, TutorialSteps.Length - 1);
        var step = TutorialSteps[_tutorialIndex];
        int n = TutorialSteps.Length;

        SideContent.Children.Add(SectionTitle($"ТУТОРИАЛ • ШАГ {_tutorialIndex + 1} ИЗ {n}"));
        // полоса прогресса
        var track = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(47, 77, 109)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Height = 14,
            Margin = new Thickness(0, 0, 0, 4)
        };
        var fillHost = new Grid();
        var fill = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(34, 197, 94)),
            CornerRadius = new CornerRadius(6),
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = 248.0 * (_tutorialIndex + 1) / n
        };
        fillHost.Children.Add(fill);
        track.Child = fillHost;
        SideContent.Children.Add(track);
        // точки шагов
        var dots = new TextBlock
        {
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 6),
            Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
            Text = string.Concat(Enumerable.Range(0, n).Select(i => i == _tutorialIndex ? "● " : i < _tutorialIndex ? "● " : "○ "))
        };
        // пройденные точки — зелёным показать нельзя в одном TextBlock; используем два цвета через Runs
        dots.Inlines.Clear();
        for (int i = 0; i < n; i++)
        {
            dots.Inlines.Add(new System.Windows.Documents.Run(i == _tutorialIndex ? "● " : "● ")
            {
                Foreground = new SolidColorBrush(i < _tutorialIndex
                    ? Color.FromRgb(34, 197, 94)
                    : i == _tutorialIndex ? Color.FromRgb(56, 189, 248) : Color.FromRgb(71, 85, 105))
            });
        }
        SideContent.Children.Add(dots);

        SideContent.Children.Add(new TextBlock
        {
            Text = step.Title,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 6)
        });
        SideContent.Children.Add(new TextBlock
        {
            Text = step.Body,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
            Margin = new Thickness(0, 0, 0, 8)
        });
        var tryCard = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(20, 35, 30)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(34, 197, 94)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(9, 7, 9, 7),
            Margin = new Thickness(0, 0, 0, 10),
            Child = new TextBlock
            {
                Text = "✋ Попробуйте: " + step.Try,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(187, 247, 208))
            }
        };
        SideContent.Children.Add(tryCard);

        var nav = new StackPanel { Orientation = Orientation.Horizontal };
        var prev = new Button { Content = "← Назад", IsEnabled = _tutorialIndex > 0 };
        prev.Click += (_, _) => { _tutorialIndex--; BuildTutorialRefresh(); };
        var next = new Button
        {
            Content = _tutorialIndex == n - 1 ? "✓ Завершить" : "Далее →",
            Background = new SolidColorBrush(Color.FromRgb(22, 163, 74)),
            Foreground = Brushes.White
        };
        next.Click += (_, _) =>
        {
            if (_tutorialIndex == n - 1) { ShowPanel("tools"); return; }
            _tutorialIndex++;
            BuildTutorialRefresh();
        };
        nav.Children.Add(prev);
        nav.Children.Add(next);
        SideContent.Children.Add(nav);

        var skip = new Button { Content = "Пропустить туториал", HorizontalAlignment = HorizontalAlignment.Left };
        skip.Click += (_, _) => ShowPanel("tools");
        SideContent.Children.Add(skip);

        if (step.Mode != null) SetMode(step.Mode);
    }

    private void BuildTutorialRefresh()
    {
        SideContent.Children.Clear();
        BuildTutorialPanel();
    }

    // -------------------- ESP --------------------
    private void BuildEspPanel()
    {
        SideContent.Children.Add(SectionTitle("СОСТОЯНИЕ ВЫВОДОВ ESP32"));
        EspPinsList = new ListBox { Height = 210 };
        RefreshEspPins();
        SideContent.Children.Add(EspPinsList);
        var hint = new TextBlock
        {
            Text = "Клик по строке: если вывод OUTPUT — переключает HIGH/LOW.\nHIGH подсвечен зелёным на плате и в списке.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            Margin = new Thickness(0, 4, 0, 0)
        };
        SideContent.Children.Add(hint);
    }

    private void RefreshEspPins()
    {
        if (EspPinsList == null) return;
        var esp = _board.Data.Components.FirstOrDefault(c => c.Type == "esp32");
        EspPinsList.Items.Clear();
        if (esp == null)
        {
            EspPinsList.Items.Add("ESP32 не установлена");
            return;
        }
        foreach (var pin in esp.Def.Pins)
        {
            string mode = _board.Data.PinMode(esp, pin.Name);
            int val = _board.Data.PinOutput(esp, pin.Name);
            string text = $"{pin.Name,-8}  {mode,-7}  {(val == 1 ? "HIGH" : "LOW")}{(mode == "INPUT" ? "  ← клик кнопкой на плате" : "")}";
            var item = new ListBoxItem
            {
                Content = text,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                Background = new SolidColorBrush(val == 1 ? Color.FromRgb(34, 197, 94) : (mode == "OUTPUT" ? Color.FromRgb(71, 85, 105) : Color.FromRgb(30, 41, 59))),
                Foreground = new SolidColorBrush(Color.FromRgb(229, 231, 235)),
                Tag = pin.Name
            };
            EspPinsList.Items.Add(item);
        }
        PowerStatus.Text = "Питание: " + (Circuit.Compute(_board.Data).EspPowered.Contains(esp.Id) ? "ESP запитан ✓" : "ESP без питания");
    }

    // -------------------- CODE --------------------
    private void BuildCodePanel()
    {
        SideContent.Children.Add(SectionTitle("ПРОШИВКА"));
        var exampleBox = new ComboBox();
        foreach (var ex in FirmwareExamples.All) exampleBox.Items.Add(ex.Name);
        exampleBox.SelectedIndex = 0;
        var code = new TextBox
        {
            Text = _board.Data.Code,
            AcceptsReturn = true,
            AcceptsTab = true,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11.5,
            Height = 260,
            TextWrapping = TextWrapping.NoWrap,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
            Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
        };
        exampleBox.SelectionChanged += (_, _) =>
        {
            if (exampleBox.SelectedIndex >= 0 && exampleBox.SelectedIndex < FirmwareExamples.All.Count)
                code.Text = FirmwareExamples.All[exampleBox.SelectedIndex].Code;
        };
        SideContent.Children.Add(exampleBox);
        SideContent.Children.Add(code);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var startBtn = new Button { Content = "▶ Запуск", Background = new SolidColorBrush(Color.FromRgb(34, 197, 94)), Foreground = Brushes.White, FontWeight = FontWeights.Bold };
        var stopBtn = new Button { Content = "■ Стоп", Background = new SolidColorBrush(Color.FromRgb(239, 68, 68)), Foreground = Brushes.White };
        startBtn.Click += (_, _) => StartSim(code.Text);
        stopBtn.Click += (_, _) => { _runner.Stop(); _board.Data.Running = false; RefreshState(); };
        row.Children.Add(startBtn);
        row.Children.Add(stopBtn);
        SideContent.Children.Add(row);
        var note = new TextBlock
        {
            Text = "Это эмулятор уровня выводов, не прошивка по USB.\ndigitalRead кнопок и аналоговых входов работает.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            Margin = new Thickness(0, 4, 0, 0)
        };
        SideContent.Children.Add(note);

        var logTitle = new TextBlock { Text = "ЖУРНАЛ", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 12, 0, 4) };
        SideContent.Children.Add(logTitle);
        _logBox = new TextBox
        {
            IsReadOnly = true,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10.5,
            Height = 170,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = new SolidColorBrush(Color.FromRgb(7, 20, 15)),
            Foreground = new SolidColorBrush(Color.FromRgb(134, 239, 172))
        };
        SideContent.Children.Add(_logBox);
    }

    private TextBox _logBox;

    private void Log(string msg)
    {
        string line = $"{DateTime.Now:HH:mm:ss}  {msg}";
        if (_logBox != null)
        {
            _logBox.AppendText(line + "\r\n");
            _logBox.ScrollToEnd();
        }
        else
        {
            System.Diagnostics.Debug.WriteLine(line);
        }
    }

    private void StartStop_Click(object sender, RoutedEventArgs e)
    {
        if (_board.Data.Running) { _runner.Stop(); _board.Data.Running = false; RefreshState(); }
        else
        {
            ShowPanel("code");
            StartSim(_board.Data.Code);
        }
    }

    private void StartSim(string codeText)
    {
        _board.Data.Code = codeText;
        foreach (var w in _board.Data.Wires.Where(w => w.Quality != "good"))
        {
            var a = _board.Data.ById(w.AId);
            var b = _board.Data.ById(w.BId);
            Log($"⚠ {w.Quality switch { "cold" => "🥶 холодная", _ => "🔥 перегретая" }} пайка: {a?.Def.Title}.{w.APin} — {b?.Def.Title}.{w.BPin} ({w.TempC}°C) — возможен плохой контакт!");
        }
        _runner.Start(codeText);
        _board.Data.Running = true;
        RefreshState();
    }

    // -------------------- PROJECT --------------------
    private void BuildProjectPanel()
    {
        SideContent.Children.Add(SectionTitle("ФАЙЛ ПРОЕКТА"));
        var saveBtn = new Button { Content = "💾 Сохранить проект…" };
        saveBtn.Click += (_, _) =>
        {
            var dlg = new SaveFileDialog { Filter = "Проект Connect (*.json)|*.json", FileName = "Мой проект.json" };
            if (dlg.ShowDialog() == true)
            {
                _board.Data.Save(dlg.FileName);
                Log($"💾 Проект сохранён: {dlg.FileName}");
            }
        };
        var openBtn = new Button { Content = "📂 Открыть проект…" };
        openBtn.Click += (_, _) =>
        {
            var dlg = new OpenFileDialog { Filter = "Проект Connect (*.json)|*.json" };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    _runner.Stop();
                    _board.Data = ProjectData.Load(dlg.FileName);
                    _board.Selected = null;
                    _runner = new FirmwareRunner(_board.Data);
                    _runner.OnOutput = m => Dispatcher.Invoke(() => Log(m));
                    _runner.StateChanged += () => _board.InvalidateVisual();
                    RefreshState();
                    Log($"📂 Проект загружен: {Path.GetFileName(dlg.FileName)}");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Ошибка чтения проекта");
                }
            }
        };
        var resetBtn = new Button { Content = "Пустая плата" };
        resetBtn.Click += (_, _) =>
        {
            _runner.Stop();
            _board.Data = new ProjectData();
            _runner = new FirmwareRunner(_board.Data);
            RefreshState();
            Log("Плата очищена");
        };
        SideContent.Children.Add(saveBtn);
        SideContent.Children.Add(openBtn);
        SideContent.Children.Add(resetBtn);

        SideContent.Children.Add(SectionTitle("СТАТИСТИКА"));
        var stats = new TextBlock
        {
            Text = $"Деталей: {_board.Data.Components.Count}\nСоединений: {_board.Data.Wires.Count + _board.Data.FreeWires.Count} (свободных линий: {_board.Data.FreeWires.Count})\nЗамеров: {_board.Data.Measurements.Count}\nКорпус: {_board.Data.CaseType}\nSTL-корпус: {(string.IsNullOrWhiteSpace(_board.Data.CaseStlName) ? "нет" : _board.Data.CaseStlName)}",
            TextWrapping = TextWrapping.Wrap
        };
        SideContent.Children.Add(stats);
        var loopInfo = new TextBlock
        {
            Text = "Конструктор: добавьте детали, двигайте их, соединяйте\nпроводами и пайкой, поворачивайте плату и измеряйте.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            Margin = new Thickness(0, 6, 0, 0)
        };
        SideContent.Children.Add(loopInfo);
    }

    private static Border SectionTitle(string text) => new()
    {
        Background = new SolidColorBrush(Color.FromRgb(22, 40, 58)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(47, 77, 109)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(5),
        Padding = new Thickness(8, 5, 8, 5),
        Margin = new Thickness(0, 12, 0, 6),
        Child = new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(147, 197, 253))
        }
    };

    private void SetMode(string mode)
    {
        _board.Mode = mode;
        RefreshState();
        Log($"Режим: {mode}");
    }

    private void RefreshState()
    {
        ModeStatus.Text = "Режим: " + (_board.Mode switch
        {
            "Camera" => "КАМЕРА 📷",
            "MoveBoard" => "ПЛАТУ ✋ (перемещение)",
            "Move" => "ДЕТАЛЬ ↔ (перемещение)",
            "Wire" => "ПРОВОД 〰",
            "Solder" => "ПАЙКА ▤",
            "Sim" => "СИМУЛЯЦИЯ 🖱",
            "Measure" => "ЗАМЕР 📏",
            "Delete" => "УДАЛЕНИЕ 🗑",
            _ => _board.Mode
        });
        SideStatus.Text = $"Плата: {_board.ViewSide} ({(_board.ViewSide == "Top" ? "верх" : "низ/пайка")}) • " +
                          $"провода кладу на: {_board.WireSide} • повёрнута на {_board.BoardRotDeg}°";
        SelectionStatus.Text = _board.Selected == null
            ? "Ничего не выбрано"
            : $"Выбрано: {_board.Selected.Def.Title} @({_board.Selected.X},{_board.Selected.Y}) Z={_board.Selected.Z:F1} ⟳Z={_board.Selected.Rot}° X={_board.Selected.RotX:F0}° Y={_board.Selected.RotY:F0}°";
        Hint.Text = _board.Mode switch
        {
            "Camera" => "Камера: тащите левой кнопкой • колесо — масштаб",
            "MoveBoard" => "Перемещение платы: тащите левой кнопкой",
            "Move" => "Деталь по всем осям: тащить — X/Y • Shift+тащить — высота Z • стрелки X/Y/Z • ручки ⟳/X/Y — повороты • Q/E, PgUp/PgDn",
            "Wire" => $"Провод: ЛКМ — звено (у контакта прилипнет) • колесо — высота звена ({_board.LinkHeightCells:F1} кл.) • ПКМ — завершить • узлы таскаются",
            "Solder" => "Пайка убрана: просто ведите провода к контактам",
            "Sim" => "Симуляция: клик по кнопке/потенциометру на плате",
            "Measure" => "Замер: клик точка → клик точка (по отверстиям 2.54 мм)",
            "Delete" => "Удаление: клик по детали",
            _ => ""
        };
        var rail = new Dictionary<string, Button>
        {
            ["Camera"] = RailCamera, ["MoveBoard"] = RailMoveBoard, ["Move"] = RailMove,
            ["Wire"] = RailWire, ["Sim"] = RailSim,
            ["Measure"] = RailMeasure, ["Delete"] = RailDelete
        };
        foreach (var kv in rail)
        {
            if (_board.Mode == kv.Key)
            {
                kv.Value.Background = new SolidColorBrush(Color.FromRgb(14, 116, 144));
                kv.Value.BorderBrush = new SolidColorBrush(Color.FromRgb(34, 211, 238));
            }
            else
            {
                kv.Value.ClearValue(BackgroundProperty);
                kv.Value.ClearValue(BorderBrushProperty);
            }
        }
        if (_wireGrid != null) _wireGrid.ItemsSource = _board.Data.WireRows();
        if (_measureGrid != null) _measureGrid.ItemsSource = _board.Data.MeasurementRows();
        RefreshEspPins();
        _board.InvalidateVisual();
    }

    private DataGrid BuildWireGrid()
    {
        var g = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            Height = 180,
            FontSize = 11
        };
        g.Columns.Add(new DataGridTextColumn { Header = "Откуда", Binding = new System.Windows.Data.Binding("A") });
        g.Columns.Add(new DataGridTextColumn { Header = "Куда", Binding = new System.Windows.Data.Binding("B") });
        g.Columns.Add(new DataGridTextColumn { Header = "Стиль", Binding = new System.Windows.Data.Binding("Style") });
        g.Columns.Add(new DataGridTextColumn { Header = "Сторона", Binding = new System.Windows.Data.Binding("Side") });
        g.Columns.Add(new DataGridTextColumn { Header = "Пайка", Binding = new System.Windows.Data.Binding("Quality") });
        return g;
    }

    private DataGrid BuildMeasureGrid()
    {
        var g = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            Height = 140,
            FontSize = 11
        };
        g.Columns.Add(new DataGridTextColumn { Header = "Расстояние, мм", Binding = new System.Windows.Data.Binding("DistanceMm") });
        g.Columns.Add(new DataGridTextColumn { Header = "ΔX, мм", Binding = new System.Windows.Data.Binding("DXmm") });
        g.Columns.Add(new DataGridTextColumn { Header = "ΔY, мм", Binding = new System.Windows.Data.Binding("DYmm") });
        g.Columns.Add(new DataGridTextColumn { Header = "Угол, °", Binding = new System.Windows.Data.Binding("AngleDeg") });
        return g;
    }
}
