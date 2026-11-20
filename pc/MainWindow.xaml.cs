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
        _runner = new FirmwareRunner(_board.Data);
        _runner.OnOutput = m => Dispatcher.Invoke(() => Log(m));
        _runner.StateChanged += () =>
        {
            _board.InvalidateVisual();
            if (EspPinsList != null) RefreshEspPins();
        };
        ShowPanel("tutorial");
        Log("✅ Connect v2.3 — нативная Windows-сборка открыта");
        Log("🎓 Открыт туториал: пройдите шаги слева, чтобы освоить интерфейс.");
        Log("Перетаскивайте детали из меню «＋ Добавить» на плату мышкой.");
        RefreshState();
    }

    private ListBox EspPinsList;

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
            Text = "Режимы переключаются на левой панели инструментов:\n📷 камера • ✋ плата • ↔ деталь • ✒ провод • 🔥 пайка • 🖱 симуляция • 📏 замер • 🗑 удалить.\n\nАктивный режим подсвечен голубым.",
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

        SideContent.Children.Add(SectionTitle("ПАЯЛЬНИК И ПРИПОЙ"));
        var tempLabel = new TextBlock { Text = $"Жало: {_board.SolderTempC}°C" };
        var tempSlider = new Slider { Minimum = 200, Maximum = 480, Value = _board.SolderTempC, TickFrequency = 10 };
        tempSlider.ValueChanged += (_, ev) =>
        {
            _board.SolderTempC = (int)ev.NewValue;
            tempLabel.Text = $"Жало: {_board.SolderTempC}°C" + QualitySuffix();
        };
        SideContent.Children.Add(tempLabel);
        SideContent.Children.Add(tempSlider);
        var fluxBox = new CheckBox { Content = "Флюс", IsChecked = _board.UseFlux };
        fluxBox.Checked += (_, _) => _board.UseFlux = true;
        fluxBox.Unchecked += (_, _) => _board.UseFlux = false;
        SideContent.Children.Add(fluxBox);
        var solderHint = new TextBlock
        {
            Text = "Хорошая пайка: 320–380°C + флюс. <280°C — холодная,\n>430°C — перегрев и поджог платы.",
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0)
        };
        SideContent.Children.Add(solderHint);

        SideContent.Children.Add(SectionTitle("ЗВУК И ВИД"));
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
         "Кнопка «＋ Добавить» сверху: деталь перетаскивается мышкой на плату. Режим «↔ Деталь» (слева) двигает детали по плате, «✋ Плата» — саму плату.",
         "Добавьте светодиод и резистор и подвиньте их режимом «Деталь».", "Move"),
        ("Провода по звеньям",
         "ЛКМ ставит звено, колесо мыши меняет длину следующего звена (подпись у курсора), ПКМ завершает провод. Линия идёт только по вашим точкам — без привязки к пинам, а подписи контактов напечатаны прямо на текстолите.",
         "Проведите провод от батареи к ESP32 тремя-четырьмя звеньями.", "Wire"),
        ("Пайка — это температура",
         "В «Инструментах» выставьте жало 320–380°C и включите флюс — пайка будет блестящей. Холодная пайка (<280°C) трескается, перегрев (>430°C) жжёт плату. Сторона линий — Top/Bottom.",
         "Поставьте режим «Пайка» слева и припаяйте резистор к светодиоду.", "Solder"),
        ("Корпус, низ платы, замер",
         "«Инструменты → Корпус платы»: акрил с прозрачной крышкой, чёрный ABS или металл. «Смотреть НИЗ» — обратная сторона. Режим «Замер»: два клика — расстояние в миллиметрах.",
         "Добавьте акриловый корпус и замерьте диагональ платы.", "Measure"),
        ("Прошивка и запуск",
         "«⌨ Прошивка» сверху: примеры кода или свой, кнопка «▶ Запуск». Панель ESP показывает живые HIGH/LOW, в режиме «Симуляция» кликаются кнопка и потенциометр на плате. Проект — «💾 Проект».",
         "Запустите демо-прошивку и посмотрите, как мигает D13.", "Sim"),
        ("Готово!",
         "Вы знаете всё основное: камера, детали, провода звеньями, пайка, корпус, замеры и прошивка. Туториал всегда доступен кнопкой «🎓 Туториал» сверху.",
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
            Text = $"Деталей: {_board.Data.Components.Count}\nСоединений: {_board.Data.Wires.Count + _board.Data.FreeWires.Count} (свободных линий: {_board.Data.FreeWires.Count})\nЗамеров: {_board.Data.Measurements.Count}\nКорпус: {_board.Data.CaseType}",
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
            : $"Выбрано: {_board.Selected.Def.Title} @({_board.Selected.X},{_board.Selected.Y})";
        Hint.Text = _board.Mode switch
        {
            "Camera" => "Камера: тащите левой кнопкой • колесо — масштаб",
            "MoveBoard" => "Перемещение платы: тащите левой кнопкой",
            "Move" => "Перемещение детали: перетащите её мышкой",
            "Wire" => $"Провод звеньями: ЛКМ — звено • колесо — длина звена ({_board.LinkLengthCells:F1} кл.) • ПКМ — завершить",
            "Solder" => $"Пайка звеньями: ЛКМ — звено • колесо — длина ({_board.LinkLengthCells:F1} кл.) • ПКМ — готово. Качество от температуры",
            "Sim" => "Симуляция: клик по кнопке/потенциометру на плате",
            "Measure" => "Замер: клик точка → клик точка (по отверстиям 2.54 мм)",
            "Delete" => "Удаление: клик по детали",
            _ => ""
        };
        var rail = new Dictionary<string, Button>
        {
            ["Camera"] = RailCamera, ["MoveBoard"] = RailMoveBoard, ["Move"] = RailMove,
            ["Wire"] = RailWire, ["Solder"] = RailSolder, ["Sim"] = RailSim,
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
