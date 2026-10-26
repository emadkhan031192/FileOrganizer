using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FileOrganizer.App.Glass;
using FileOrganizer.Core;

namespace FileOrganizer.App;

/// <summary>
/// Phase 2 unified wheel. One fixed-size (420x420) transparent window, centred
/// on the cursor and clamped to the work area. Its Background is null, so
/// transparent pixels are click-through; only the centre lens and the petals
/// have a background. It is topmost and non-activating (WS_EX_NOACTIVATE,
/// ShowActivated=False) so it never steals focus or cancels an Explorer drag,
/// and it never uses ShowDialog or close-on-Deactivated.
/// Three wheels, switched with Shift (Move), Ctrl (Tools) or Z (Actions):
/// - Move: destinations, favourites first then recently used, with a glowing
///   "Suggested" petal from SuggestionService. Hovering or dwelling ~350 ms on
///   a parent destination (or pressing Space) fans out its sub-destinations
///   as a second arc. No 8-petal limit, no pager.
/// - Tools: Organize this folder, Rename, Find duplicates, "Copy instead of
///   move" toggle (persisted in Preferences.CopyInsteadOfMove).
/// - Actions: Copy path, Open in Terminal, Reveal in Explorer, Pin folder as
///   destination, Recycle Bin (neutral glass, red only on hover; never on
///   the Move wheel).
/// Keyboard (via the low-level hook, since the wheel never has focus):
/// arrows rotate the highlight, 1-8 pick a petal, typing letters jumps by
/// name, Space opens a sub-arc, Backspace goes back, Shift/Ctrl/Z switch
/// wheels, Enter applies, Esc cancels.
/// Motion (Phase 1 SpringAnimator): petals travel from the centre, staggered
/// 35 ms, scale 0.3 to 1 with overshoot; hover scales a petal to 1.12 and its
/// neighbours to 0.96; the glass ring glides between petals; the centre
/// label crossfades in 120 ms. Only transform and opacity animate.
/// </summary>
public partial class WheelWindow : Window
{
    public enum WheelKind
    {
        Move,
        Tools,
        Actions,
    }

    public static WheelWindow? Current { get; private set; }

    private sealed class PetalModel
    {
        public required string Name { get; init; }
        public required string IconKey { get; init; }
        public string Tooltip { get; init; } = "";
        public bool IsSuggested { get; init; }
        public bool IsRecycle { get; init; }
        public bool IsToggle { get; init; }
        public bool ToggleOn { get; set; }
        public Destination? Destination { get; init; }
        public List<PetalModel> Children { get; init; } = new();
        public required Func<PetalModel, List<string>, Task> Activate { get; init; }
    }

    private const double Centre = 210;
    private const double PetalRadius = 140;
    private const double PetalSize = 64;

    private List<string> _items;
    private WheelKind _kind;
    private readonly bool _fromDrag;
    private readonly Point? _anchor;
    private List<PetalModel> _petals = new();
    private List<Button> _petalButtons = new();
    private List<PetalModel> _subArc = new();
    private List<Button> _subArcButtons = new();
    private PetalModel? _subArcParent;
    private int _highlight;
    private string _typeBuffer = "";
    private DispatcherTimer? _typeTimer;
    private DispatcherTimer? _dwellTimer;
    private DispatcherTimer? _dragAwayTimer;
    private bool _closing;

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out CursorPoint point);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint { public int X, Y; }

    private WheelWindow(WheelKind kind, List<string> items, bool fromDrag, Point? anchor)
    {
        InitializeComponent();
        _kind = kind;
        _items = items;
        _fromDrag = fromDrag;
        _anchor = anchor;
        Loaded += (_, _) =>
        {
            PositionAtAnchorOrCursor();
            BuildPetals(animateEntrance: true);
            UpdateCentre();
        };
        Closed += (_, _) =>
        {
            if (ReferenceEquals(Current, this)) Current = null;
            _dwellTimer?.Stop();
            _dragAwayTimer?.Stop();
            _typeTimer?.Stop();
        };
        CentreLens.DragOver += (_, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effects = DragDropEffects.Move; };
        CentreLens.Drop += Centre_Drop;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Non-activating + tool window: never steals focus, never cancels an Explorer drag.
        var hwnd = new WindowInteropHelper(this).Handle;
        const int GwlExstyle = -20;
        const int WsExNoactivate = 0x08000000;
        const int WsExToolwindow = 0x00000080;
        var style = GetWindowLong(hwnd, GwlExstyle);
        SetWindowLong(hwnd, GwlExstyle, style | WsExNoactivate | WsExToolwindow);
    }

    public static void Show(WheelKind kind, List<string>? items = null, bool fromDrag = false, Point? anchor = null)
    {
        if (Current is WheelWindow existing)
        {
            existing._items = items ?? existing._items;
            existing.SwitchTo(kind);
            return;
        }
        var window = new WheelWindow(kind, items ?? ExplorerSelection.GetSelectedFiles(), fromDrag, anchor);
        Current = window;
        window.Show();
    }

    public static void ShowMove(List<string>? items = null) => Show(WheelKind.Move, items, fromDrag: false);

    private void PositionAtAnchorOrCursor()
    {
        var point = _anchor;
        if (point is null && GetCursorPos(out var cursor))
            point = new Point(cursor.X, cursor.Y);
        if (point is null) point = new Point(SystemParameters.WorkArea.Left + 400, SystemParameters.WorkArea.Top + 300);
        var wa = SystemParameters.WorkArea;
        Left = Math.Clamp(point.Value.X - Centre, wa.Left, wa.Right - Width);
        Top = Math.Clamp(point.Value.Y - Centre, wa.Top, wa.Bottom - Height);
    }

    // ----- Petal construction -----

    private List<PetalModel> BuildModels(WheelKind kind)
    {
        var prefs = App.State.Config.Preferences;
        if (kind == WheelKind.Tools)
        {
            return new List<PetalModel>
            {
                new() { Name = "Organize this folder", IconKey = "IconOrganize", Activate = OrganizeAsync },
                new() { Name = "Rename", IconKey = "IconRename", Activate = RenameAsync },
                new() { Name = "Find duplicates", IconKey = "IconDuplicates", Activate = DuplicatesAsync },
                new() { Name = "Copy instead of move", IconKey = "IconCopy", IsToggle = true, ToggleOn = prefs.CopyInsteadOfMove, Activate = ToggleCopyAsync },
            };
        }
        if (kind == WheelKind.Actions)
        {
            return new List<PetalModel>
            {
                new() { Name = "Copy path", IconKey = "IconCopy", Activate = CopyPathAsync },
                new() { Name = "Open in Terminal", IconKey = "IconTerminal", Activate = OpenTerminalAsync },
                new() { Name = "Reveal in Explorer", IconKey = "IconReveal", Activate = RevealAsync },
                new() { Name = "Pin folder as destination", IconKey = "IconPin", Activate = PinFolderAsync },
                new() { Name = "Recycle Bin", IconKey = "IconRecycle", IsRecycle = true, Activate = RecycleAsync },
            };
        }

        // Move wheel: top-level destinations, favourites first, then recently used.
        var all = App.State.Config.Destinations;
        var ordered = all
            .OrderByDescending(d => d.IsFavorite)
            .ThenByDescending(d => d.LastUsedUtc ?? DateTime.MinValue)
            .ThenBy(d => d.SortOrder)
            .ToList();
        var suggestedId = SuggestionService.Suggest(all, _items, BuildHistorySamples())
            .FirstOrDefault()?.Destination.Id;
        var models = new List<PetalModel>();
        foreach (var dest in ordered.Where(d => d.ParentId is null))
        {
            var children = ordered.Where(d => d.ParentId == dest.Id)
                .Select(child => new PetalModel
                {
                    Name = child.Name,
                    IconKey = "IconFolder",
                    Tooltip = $"{child.Name} — {child.ExpandedPath}",
                    Destination = child,
                    Activate = MoveAsync,
                })
                .ToList();
            models.Add(new PetalModel
            {
                Name = dest.Name,
                IconKey = "IconFolder",
                Tooltip = $"{(dest.IsFavorite ? "Favourite · " : "")}{dest.Name} — {dest.ExpandedPath}",
                IsSuggested = dest.Id == suggestedId,
                Destination = dest,
                Children = children,
                Activate = MoveAsync,
            });
        }
        // A suggested child destination is still reachable: it appears as its
        // own petal only when it is also a top-level destination; otherwise
        // its parent petal's sub-arc contains it.
        return models;
    }

    private List<SuggestionService.HistorySample> BuildHistorySamples()
    {
        var samples = new List<SuggestionService.HistorySample>();
        var destinations = App.State.Config.Destinations;
        foreach (var entry in App.State.History.Recent(200))
        {
            var dest = destinations.FirstOrDefault(d =>
                string.Equals(d.ExpandedPath, entry.DestinationPath, StringComparison.OrdinalIgnoreCase)
                || entry.DestinationPath.StartsWith(d.ExpandedPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            if (dest is null) continue;
            samples.Add(new SuggestionService.HistorySample(Path.GetFileName(entry.SourcePath) ?? "", dest.Id));
        }
        return samples;
    }

    private void BuildPetals(bool animateEntrance)
    {
        CloseSubArc(removeButtons: true);
        foreach (var button in _petalButtons)
            RootCanvas.Children.Remove(button);
        _petalButtons.Clear();
        _petals = BuildModels(_kind);
        KeycapText.Text = _kind switch
        {
            WheelKind.Move => App.State.Config.Preferences.WheelMoveKey,
            WheelKind.Tools => App.State.Config.Preferences.WheelToolsKey,
            _ => App.State.Config.Preferences.WheelActionsKey,
        };

        for (var i = 0; i < _petals.Count; i++)
        {
            var model = _petals[i];
            var button = CreatePetalButton(model, PetalSize);
            var angle = -Math.PI / 2 + i * 2 * Math.PI / Math.Max(_petals.Count, 1);
            var finalX = Centre + PetalRadius * Math.Cos(angle) - PetalSize / 2;
            var finalY = Centre + PetalRadius * Math.Sin(angle) - PetalSize / 2;
            Canvas.SetLeft(button, finalX);
            Canvas.SetTop(button, finalY);
            RootCanvas.Children.Add(button);
            _petalButtons.Add(button);
            if (animateEntrance && !SpringAnimator.ReduceMotion)
            {
                // Petals travel from the centre: start translated to the centre, scaled down, invisible.
                var group = EnsureTransformGroup(button);
                var translate = group.Children.OfType<TranslateTransform>().First();
                translate.X = Centre - (finalX + PetalSize / 2);
                translate.Y = Centre - (finalY + PetalSize / 2);
                var scale = group.Children.OfType<ScaleTransform>().First();
                scale.ScaleX = 0.3;
                scale.ScaleY = 0.3;
                button.Opacity = 0;
                var captured = button;
                var delay = i * 35;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delay + 1) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    SpringAnimator.AnimateTranslate(captured, 0, 0, SpringAnimator.SpringPreset.Bouncy);
                    SpringAnimator.AnimateScale(captured, 1.0, SpringAnimator.SpringPreset.Bouncy);
                    SpringAnimator.AnimateOpacity(captured, 1.0, SpringAnimator.SpringPreset.Bouncy);
                };
                timer.Start();
            }
            else
            {
                button.Opacity = 1;
            }
        }
        _highlight = 0;
        HighlightPetal(0, animateRing: false);
        UpdateCentre();
    }

    private Button CreatePetalButton(PetalModel model, double size)
    {
        var button = new Button
        {
            Style = (Style)FindResource(model.IsRecycle ? "RecyclePetalButton" : "GlassPetalButton"),
            Width = size,
            Height = size,
            ToolTip = string.IsNullOrWhiteSpace(model.Tooltip) ? model.Name : model.Tooltip,
            Tag = model,
            AllowDrop = true,
        };
        button.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, model.Name);
        if (FindResource(model.IconKey) is Geometry geometry)
        {
            var path = new System.Windows.Shapes.Path
            {
                Data = geometry,
                Width = 24,
                Height = 24,
                Stretch = Stretch.Uniform,
            };
            path.SetBinding(System.Windows.Shapes.Path.FillProperty, new Binding("Foreground")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1),
            });
            button.Content = path;
        }
        else
        {
            button.Content = model.Name;
        }
        if (model.Destination is Destination dest && !string.IsNullOrWhiteSpace(dest.ColorHex)
            && ColorConverter.ConvertFromString(dest.ColorHex) is Color color)
        {
            button.Background = new SolidColorBrush(color);
        }
        if (model.IsSuggested)
        {
            // Glowing Suggested petal: an accent halo around the glass petal.
            button.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 22, ShadowDepth = 0, Opacity = 0.7, Color = Colors.DodgerBlue };
            button.ToolTip = $"Suggested — {model.Name}";
        }
        if (model.IsToggle && model.ToggleOn)
            button.Background = (Brush)FindResource("GlassAccentSoft");

        button.MouseEnter += (_, _) => { HighlightPetal(IndexOf(button)); StartDwellTimer(model); };
        button.MouseLeave += (_, _) => _dwellTimer?.Stop();
        button.Click += async (_, _) => await PressThenActivateAsync(model, null);
        button.DragOver += (_, e) =>
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                // The app sets the drop effect itself: Explorer's Shift/Ctrl/Alt
                // move/copy semantics must not interfere.
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
                HighlightPetal(IndexOf(button));
            }
        };
        button.Drop += async (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] dropped)
            {
                var items = dropped.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
                if (items.Count == 0) return;
                await DropSuccessAsync(button, e.GetPosition(RootCanvas), model, items);
            }
        };
        button.DragLeave += (_, _) => StartDragAwayTimer();
        button.DragEnter += (_, _) => _dragAwayTimer?.Stop();
        EnsureTransformGroup(button);
        return button;
    }

    private static TransformGroup EnsureTransformGroup(Button button)
    {
        if (button.RenderTransform is TransformGroup group
            && group.Children.OfType<ScaleTransform>().Any()
            && group.Children.OfType<TranslateTransform>().Any()
            && group.Children.OfType<RotateTransform>().Any())
            return group;
        group = new TransformGroup();
        group.Children.Add(new ScaleTransform(1, 1));
        group.Children.Add(new TranslateTransform());
        group.Children.Add(new RotateTransform());
        button.RenderTransformOrigin = new Point(0.5, 0.5);
        button.RenderTransform = group;
        return group;
    }

    private int IndexOf(Button button)
    {
        var index = _subArcButtons.IndexOf(button);
        if (index >= 0) return index;
        return _petalButtons.IndexOf(button);
    }

    private List<PetalModel> CurrentModels => _subArc.Count > 0 ? _subArc : _petals;

    private List<Button> CurrentButtons => _subArc.Count > 0 ? _subArcButtons : _petalButtons;

    // ----- Highlight, sub-arcs, switching -----

    private void HighlightPetal(int index, bool animateRing = true)
    {
        var buttons = CurrentButtons;
        if (buttons.Count == 0) return;
        _highlight = Math.Clamp(index, 0, buttons.Count - 1);
        var model = CurrentModels[_highlight];
        // Dock-style magnification: hovered petal 1.12, neighbours 0.96, rest 1.0.
        for (var i = 0; i < buttons.Count; i++)
        {
            var target = i == _highlight ? 1.12 : (Math.Abs(i - _highlight) == 1 ? 0.96 : 1.0);
            SpringAnimator.AnimateScale(buttons[i], target, SpringAnimator.SpringPreset.Snappy);
        }
        // Glide the glass ring to the highlighted petal (no jumping).
        var button = buttons[_highlight];
        var left = Canvas.GetLeft(button) + button.Width / 2 - HighlightRing.Width / 2;
        var top = Canvas.GetTop(button) + button.Height / 2 - HighlightRing.Height / 2;
        var ringX = left - Canvas.GetLeft(HighlightRing);
        var ringY = top - Canvas.GetTop(HighlightRing);
        if (animateRing && !SpringAnimator.ReduceMotion)
            SpringAnimator.AnimateTranslate(HighlightRing, ringX, ringY, SpringAnimator.SpringPreset.Snappy);
        else
        {
            SpringAnimator.Stop(HighlightRing);
            var group = HighlightRing.RenderTransform as TransformGroup;
            if (group is null)
            {
                group = new TransformGroup();
                group.Children.Add(new TranslateTransform());
                HighlightRing.RenderTransform = group;
            }
            var translate = group.Children.OfType<TranslateTransform>().First();
            translate.X = ringX;
            translate.Y = ringY;
        }
        HoverText.Opacity = 0;
        HoverText.Text = model.IsSuggested ? $"Suggested · {model.Name}" : model.Name;
        SpringAnimator.AnimateOpacity(HoverText, 1.0, SpringAnimator.SpringPreset.Snappy);
    }

    private void StartDwellTimer(PetalModel model)
    {
        _dwellTimer?.Stop();
        if (model.Children.Count == 0) return;
        _dwellTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _dwellTimer.Tick += (_, _) =>
        {
            _dwellTimer?.Stop();
            OpenSubArc(model);
        };
        _dwellTimer.Start();
    }

    private void OpenSubArc(PetalModel parent)
    {
        if (parent.Children.Count == 0) return;
        if (ReferenceEquals(_subArcParent, parent)) { CloseSubArc(removeButtons: true); return; }
        CloseSubArc(removeButtons: true);
        _subArcParent = parent;
        _subArc = parent.Children;
        var parentButton = _petalButtons.FirstOrDefault(b => ReferenceEquals(b.Tag, parent));
        var parentAngle = -Math.PI / 2;
        if (parentButton is not null)
        {
            var cx = Canvas.GetLeft(parentButton) + parentButton.Width / 2;
            var cy = Canvas.GetTop(parentButton) + parentButton.Height / 2;
            parentAngle = Math.Atan2(cy - Centre, cx - Centre);
        }
        const double childSize = 56;
        const double childRadius = 170;
        for (var i = 0; i < _subArc.Count; i++)
        {
            var child = _subArc[i];
            var offset = (i - (_subArc.Count - 1) / 2.0) * (Math.PI / 12); // 15 degrees apart
            var angle = parentAngle + offset;
            var button = CreatePetalButton(child, childSize);
            Canvas.SetLeft(button, Centre + childRadius * Math.Cos(angle) - childSize / 2);
            Canvas.SetTop(button, Centre + childRadius * Math.Sin(angle) - childSize / 2);
            RootCanvas.Children.Add(button);
            _subArcButtons.Add(button);
            if (!SpringAnimator.ReduceMotion)
            {
                button.Opacity = 0;
                var captured = button;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(i * 25 + 1) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    SpringAnimator.AnimateOpacity(captured, 1.0, SpringAnimator.SpringPreset.Bouncy);
                    SpringAnimator.AnimateScale(captured, 1.0, SpringAnimator.SpringPreset.Bouncy);
                };
                timer.Start();
            }
        }
        HighlightPetal(0);
    }

    private void CloseSubArc(bool removeButtons)
    {
        if (removeButtons)
        {
            foreach (var button in _subArcButtons)
                RootCanvas.Children.Remove(button);
        }
        _subArcButtons.Clear();
        _subArc.Clear();
        _subArcParent = null;
    }

    private async void SwitchTo(WheelKind kind)
    {
        if (kind == _kind && _petalButtons.Count > 0) return;
        _kind = kind;
        CloseSubArc(removeButtons: true);
        // Old petals rotate out 30 degrees and fade in 200 ms.
        foreach (var button in _petalButtons)
        {
            var group = EnsureTransformGroup(button);
            var rotate = group.Children.OfType<RotateTransform>().First();
            var fade = new DoubleAnimation(button.Opacity, 0, TimeSpan.FromMilliseconds(200));
            button.BeginAnimation(UIElement.OpacityProperty, fade);
            var turn = new DoubleAnimation(0, 30, TimeSpan.FromMilliseconds(200));
            rotate.BeginAnimation(RotateTransform.AngleProperty, turn);
        }
        await Task.Delay(200);
        BuildPetals(animateEntrance: true);
    }

    // ----- Keyboard hook handling -----

    /// <summary>Returns true when the wheel consumed the key (the hook then swallows it).</summary>
    public bool HandleHookKey(Key key, bool keyDown)
    {
        if (!keyDown || _closing) return false;
        if (key == Key.Escape)
        {
            CloseWheel();
            return true;
        }
        if (CurrentModels.Count == 0) return false;
        switch (key)
        {
            case Key.Enter:
                if (CurrentModels.Count > 0)
                    _ = PressThenActivateAsync(CurrentModels[_highlight], null);
                return true;
            case Key.Left:
            case Key.Up:
                HighlightPetal(_highlight - 1 < 0 ? CurrentButtons.Count - 1 : _highlight - 1);
                return true;
            case Key.Right:
            case Key.Down:
                HighlightPetal((_highlight + 1) % Math.Max(CurrentButtons.Count, 1));
                return true;
            case Key.Space:
                if (CurrentModels[_highlight].Children.Count > 0)
                {
                    OpenSubArc(CurrentModels[_highlight]);
                    return true;
                }
                return true; // swallow: the wheel owns Space while open
            case Key.Back:
                if (_subArc.Count > 0)
                {
                    var parent = _subArcParent;
                    CloseSubArc(removeButtons: true);
                    var parentButton = parent is null ? null : _petalButtons.FirstOrDefault(b => ReferenceEquals(b.Tag, parent));
                    HighlightPetal(parentButton is null ? 0 : _petalButtons.IndexOf(parentButton));
                    return true;
                }
                return true;
            case Key.LeftShift:
            case Key.RightShift:
                SwitchTo(WheelKind.Move);
                return true;
            case Key.LeftCtrl:
            case Key.RightCtrl:
                SwitchTo(WheelKind.Tools);
                return true;
            case Key.Z:
                SwitchTo(WheelKind.Actions);
                return true;
        }
        if (key is >= Key.D1 and <= Key.D8)
        {
            var index = (int)key - (int)Key.D1;
            if (index < CurrentModels.Count)
                _ = PressThenActivateAsync(CurrentModels[index], null);
            return true;
        }
        if (key is >= Key.A and <= Key.Y)
        {
            // Typing letters jumps to a destination by name; the buffer resets after 1.5 s.
            _typeBuffer += key.ToString().ToLowerInvariant();
            _typeTimer?.Stop();
            _typeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            _typeTimer.Tick += (_, _) => { _typeTimer?.Stop(); _typeBuffer = ""; };
            _typeTimer.Start();
            var match = CurrentModels.FindIndex(m => m.Name.StartsWith(_typeBuffer, StringComparison.OrdinalIgnoreCase));
            if (match >= 0) HighlightPetal(match);
            return true;
        }
        return false;
    }

    // ----- Activation -----

    private async Task PressThenActivateAsync(PetalModel model, List<string>? itemsOverride)
    {
        var button = CurrentButtons.FirstOrDefault(b => ReferenceEquals(b.Tag, model));
        if (button is not null)
        {
            SpringAnimator.AnimateScale(button, 0.94, SpringAnimator.SpringPreset.Snappy);
            await Task.Delay(SpringAnimator.ReduceMotion ? 0 : 90);
            SpringAnimator.AnimateScale(button, 1.0, SpringAnimator.SpringPreset.Bouncy);
        }
        await model.Activate(model, itemsOverride ?? _items);
    }

    private async Task DropSuccessAsync(Button petalButton, Point dropPoint, PetalModel model, List<string> items)
    {
        // A file chip flies along a curve into the petal in 280 ms, the petal
        // pulses once, a check draws in the centre in 300 ms, then the wheel
        // collapses in 260 ms with no overshoot. The live pill (Phase 3) then
        // reports the result; until then the existing toast carries it.
        var chip = new Border
        {
            Style = (Style)FindResource("GlassChip"),
            Child = new TextBlock { Text = $"{items.Count} file(s)", FontSize = 11, Foreground = (Brush)FindResource("GlassTextPrimary") },
        };
        Canvas.SetLeft(chip, dropPoint.X);
        Canvas.SetTop(chip, dropPoint.Y);
        RootCanvas.Children.Add(chip);
        var petalX = Canvas.GetLeft(petalButton) + petalButton.Width / 2;
        var petalY = Canvas.GetTop(petalButton) + petalButton.Height / 2;
        var translate = new TranslateTransform();
        chip.RenderTransform = translate;
        var animX = new DoubleAnimation(0, petalX - dropPoint.X, TimeSpan.FromMilliseconds(280)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
        var animY = new DoubleAnimation(0, petalY - dropPoint.Y, TimeSpan.FromMilliseconds(280)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
        translate.BeginAnimation(TranslateTransform.XProperty, animX);
        translate.BeginAnimation(TranslateTransform.YProperty, animY);
        await Task.Delay(280);
        RootCanvas.Children.Remove(chip);
        SpringAnimator.AnimateScale(petalButton, 1.25, SpringAnimator.SpringPreset.Snappy);
        await Task.Delay(120);
        SpringAnimator.AnimateScale(petalButton, 1.0, SpringAnimator.SpringPreset.Bouncy);
        CheckMark.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
        await Task.Delay(300);
        await model.Activate(model, items);
    }

    private void CloseWheel()
    {
        if (_closing) return;
        _closing = true;
        Close();
    }

    private async Task CloseWithFadeAsync()
    {
        if (_closing) return;
        _closing = true;
        // Collapse in 260 ms, no overshoot (plain opacity fade).
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(260));
        BeginAnimation(UIElement.OpacityProperty, fade);
        await Task.Delay(260);
        Close();
    }

    private void Centre_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] dropped)
        {
            _items = dropped.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
            UpdateCentre();
        }
    }

    private void StartDragAwayTimer()
    {
        if (!_fromDrag) return;
        _dragAwayTimer?.Stop();
        _dragAwayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _dragAwayTimer.Tick += (_, _) =>
        {
            _dragAwayTimer?.Stop();
            // Cancel only on a real exit: the cursor must be outside the window.
            if (GetCursorPos(out var p)
                && (p.X < Left - 10 || p.X > Left + Width + 10 || p.Y < Top - 10 || p.Y > Top + Height + 10))
                CloseWheel();
        };
        _dragAwayTimer.Start();
    }

    private void UpdateCentre()
    {
        if (_items.Count == 0)
        {
            CountText.Text = "Nothing selected";
            SizeText.Text = "";
            CentreIcon.Data = (Geometry)FindResource("IconFile");
            return;
        }
        CountText.Text = $"{_items.Count} item(s) ready";
        long bytes = 0;
        var hasFolder = false;
        foreach (var item in _items)
        {
            if (Directory.Exists(item)) hasFolder = true;
            else if (File.Exists(item))
            {
                try { bytes += new FileInfo(item).Length; } catch (IOException) { }
            }
        }
        SizeText.Text = PathHelpers.FormatSize(bytes) + (hasFolder ? " · folders included" : "");
        CentreIcon.Data = (Geometry)FindResource(hasFolder ? "IconFolder" : "IconFile");
    }

    private List<string> EffectiveItems(List<string> items) =>
        items.Count > 0 ? items : ExplorerSelection.GetSelectedFiles();

    private void RefreshMainActivity()
    {
        if (Application.Current.MainWindow is MainWindow main)
            main.RefreshActivity();
    }

    private void ShowResultToast(BatchResult result, string destinationName)
    {
        var firstFail = result.Files.FirstOrDefault(f => !f.Success && !f.Skipped);
        var message = $"✓ {result.Succeeded} item(s) → {destinationName}" +
            (result.Failed > 0 ? $" · {result.Failed} failed: {firstFail?.Error}" : "");
        if (Application.Current.MainWindow is MainWindow main)
            ToastWindow.Show(message, onUndo: () => _ = main.UndoLast());
        else
            ToastWindow.Show(message);
    }

    // ----- Move petal -----

    private async Task MoveAsync(PetalModel model, List<string> items)
    {
        if (model.Destination is not Destination dest) return;
        var effective = EffectiveItems(items);
        if (effective.Count == 0) { UpdateCentre(); return; }
        var prefs = App.State.Config.Preferences;
        var mode = prefs.CopyInsteadOfMove ? TransferMode.Copy : prefs.DefaultTransferMode;
        var conflict = prefs.DefaultConflictPolicy;
        if (!await TransferHelper.ConfirmIfNeededAsync(this, effective, dest.Name, mode)) return;
        var result = effective.Count >= 10 || effective.Any(Directory.Exists)
            ? await TransferHelper.RunTransferWithProgressAsync(this, effective, dest, mode, conflict)
            : await Task.Run(() => App.State.Ops.TransferFiles(effective, dest.ExpandedPath, mode, conflict));
        if (result.Succeeded > 0)
            App.State.RecordDestinationUsed(dest);
        await CloseWithFadeAsync();
        ShowResultToast(result, dest.Name);
        RefreshMainActivity();
    }

    // ----- Tools petals -----

    private async Task OrganizeAsync(PetalModel model, List<string> items)
    {
        var folder = (items.Count == 1 && Directory.Exists(items[0]) ? items[0] : null)
            ?? ExplorerSelection.GetExplorerFolderPath()
            ?? Dialogs.PickFolder("Pick the folder to organize");
        if (folder is null) return;
        List<PreviewItem> preview;
        try
        {
            preview = await Task.Run(() => QuickBarWindow.CreateEngine().BuildPreview(folder, App.State.Config.Preferences.IncludeSubfolders));
        }
        catch (Exception ex)
        {
            Dialogs.Error(ex.Message);
            return;
        }
        if (preview.Count == 0)
        {
            Dialogs.Info($"Nothing to organize in:\n{folder}");
            return;
        }
        var window = new PreviewWindow(preview);
        if (window.ShowDialog() != true) return;
        var confirmed = window.ConfirmedItems;
        if (confirmed.Count == 0) return;
        var pairs = confirmed.Select(p => (p.SourcePath, p.DestinationPath)).ToList();
        var progressOwner = Application.Current.MainWindow ?? this;
        CloseWheel();
        var result = await TransferHelper.RunApplyWithProgressAsync(progressOwner, pairs,
            App.State.Config.Preferences.DefaultTransferMode, App.State.Config.Preferences.DefaultConflictPolicy);
        Dialogs.Info($"Organized {result.Succeeded} file(s) in {folder}." +
            (result.Failed > 0 ? $"\nFailed: {result.Failed}" : "") +
            "\n(↶ Undo in the main window reverses this.)");
        RefreshMainActivity();
    }

    private Task RenameAsync(PetalModel model, List<string> items)
    {
        var effective = EffectiveItems(items);
        if (effective.Count == 0) return Task.CompletedTask;
        CloseWheel();
        new RenameWizardWindow(effective).ShowDialog();
        RefreshMainActivity();
        return Task.CompletedTask;
    }

    private Task DuplicatesAsync(PetalModel model, List<string> items)
    {
        var folder = (items.Count == 1 && Directory.Exists(items[0]) ? items[0] : null)
            ?? ExplorerSelection.GetExplorerFolderPath()
            ?? Dialogs.PickFolder("Pick the folder to check for duplicates");
        if (folder is null) return Task.CompletedTask;
        CloseWheel();
        new DuplicatesWindow(folder).ShowDialog();
        return Task.CompletedTask;
    }

    private Task ToggleCopyAsync(PetalModel model, List<string> items)
    {
        var prefs = App.State.Config.Preferences;
        prefs.CopyInsteadOfMove = !prefs.CopyInsteadOfMove;
        App.State.Save();
        model.ToggleOn = prefs.CopyInsteadOfMove;
        var button = _petalButtons.FirstOrDefault(b => ReferenceEquals(b.Tag, model));
        if (button is not null)
            button.Background = prefs.CopyInsteadOfMove
                ? (Brush)FindResource("GlassAccentSoft")
                : (Brush)FindResource("GlassSurface");
        HoverText.Text = prefs.CopyInsteadOfMove ? "Copy mode on — moves will copy" : "Move mode — moves will move";
        return Task.CompletedTask;
    }

    // ----- Actions petals -----

    private Task CopyPathAsync(PetalModel model, List<string> items)
    {
        var effective = EffectiveItems(items);
        if (effective.Count == 0) return Task.CompletedTask;
        Clipboard.SetText(string.Join(Environment.NewLine, effective));
        CloseWheel();
        ToastWindow.Show($"✓ Copied {effective.Count} path(s)");
        return Task.CompletedTask;
    }

    private Task OpenTerminalAsync(PetalModel model, List<string> items)
    {
        var folder = FirstFolder(items);
        if (folder is null) return Task.CompletedTask;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = "wt.exe", Arguments = $"-d \"{folder}\"", UseShellExecute = true });
        }
        catch
        {
            try { Process.Start(new ProcessStartInfo { FileName = "cmd.exe", Arguments = $"/k cd /d \"{folder}\"", UseShellExecute = true }); }
            catch (Exception ex) { Dialogs.Error($"Could not open a terminal: {ex.Message}"); return Task.CompletedTask; }
        }
        CloseWheel();
        return Task.CompletedTask;
    }

    private Task RevealAsync(PetalModel model, List<string> items)
    {
        var effective = EffectiveItems(items);
        if (effective.Count == 0) return Task.CompletedTask;
        var first = effective[0];
        try
        {
            if (Directory.Exists(first))
                Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{first}\"", UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"/select,\"{first}\"", UseShellExecute = true });
        }
        catch (Exception ex) { Dialogs.Error($"Could not reveal in Explorer: {ex.Message}"); return Task.CompletedTask; }
        CloseWheel();
        return Task.CompletedTask;
    }

    private Task PinFolderAsync(PetalModel model, List<string> items)
    {
        var folder = FirstFolder(items);
        if (folder is null)
        {
            Dialogs.Info("Pin folder needs a folder: select a folder in Explorer, or drop one on this petal.");
            return Task.CompletedTask;
        }
        var config = App.State.Config;
        if (config.Destinations.Any(d => string.Equals(d.ExpandedPath, folder, StringComparison.OrdinalIgnoreCase)))
        {
            Dialogs.Info($"\"{Path.GetFileName(folder)}\" is already a destination.");
            return Task.CompletedTask;
        }
        var dest = new Destination
        {
            Name = Path.GetFileName(folder) ?? folder,
            Path = folder,
            Icon = "📁",
            SortOrder = config.Destinations.Count == 0 ? 0 : config.Destinations.Max(d => d.SortOrder) + 1,
        };
        config.Destinations.Add(dest);
        App.State.Save();
        CloseWheel();
        ToastWindow.Show($"✓ Pinned \"{dest.Name}\" as a destination");
        RefreshMainActivity();
        return Task.CompletedTask;
    }

    private async Task RecycleAsync(PetalModel model, List<string> items)
    {
        var effective = EffectiveItems(items);
        if (effective.Count == 0) return;
        var message = await RecycleBinService.RecycleAsync(effective);
        await CloseWithFadeAsync();
        ToastWindow.Show(message);
        RefreshMainActivity();
    }

    private string? FirstFolder(List<string> items)
    {
        var effective = EffectiveItems(items);
        var folder = effective.FirstOrDefault(Directory.Exists);
        if (folder is not null) return folder;
        if (effective.Count > 0)
        {
            var parent = Path.GetDirectoryName(effective[0]);
            if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent)) return parent;
        }
        return ExplorerSelection.GetExplorerFolderPath();
    }
}
