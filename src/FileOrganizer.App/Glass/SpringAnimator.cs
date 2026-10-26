using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FileOrganizer.App.Glass;

/// <summary>
/// Phase 1 motion system. WPF has no spring animation, so this helper runs a
/// semi-implicit Euler spring on CompositionTarget.Rendering and animates ONLY
/// transform and opacity — never window size or hit-test area (except the edge
/// dock slide, which is a separate window-position animation in Phase 5).
/// Default spring: mass 1, stiffness 247, damping 23.6 — response about 0.4 s,
/// damping ratio about 0.75, overshoot about 6-8%.
/// When ReduceMotion is on, every animation becomes a 120 ms crossfade for
/// opacity, and transform changes snap instantly. The rendering loop is only
/// subscribed while at least one spring is active, so idle CPU stays near zero.
/// This helper never inspects keystrokes or any user content; it only moves
/// the visuals it is handed.
/// </summary>
public static class SpringAnimator
{
    public enum SpringPreset
    {
        /// <summary>Mass 1, stiffness 247, damping 23.6 (the default).</summary>
        Default,
        /// <summary>Softer, slower settle for large surfaces.</summary>
        Gentle,
        /// <summary>Faster, tighter settle for small controls.</summary>
        Snappy,
        /// <summary>More overshoot for playful entrances (petals).</summary>
        Bouncy,
    }

    /// <summary>Set from Preferences.ReduceMotion (and Windows "Animation effects").</summary>
    public static bool ReduceMotion { get; set; }

    private sealed class SpringState
    {
        public required UIElement Element;
        public required string Key;
        public required Func<double> Get;
        public required Action<double> Set;
        public double Target;
        public double Velocity;
        public double Stiffness;
        public double Damping;
    }

    private static readonly List<SpringState> _active = new();
    private static DateTime _lastTick = DateTime.MinValue;
    private static bool _subscribed;

    private static (double Stiffness, double Damping) Parameters(SpringPreset preset) => preset switch
    {
        SpringPreset.Gentle => (140, 20),
        SpringPreset.Snappy => (380, 30),
        SpringPreset.Bouncy => (220, 16),
        _ => (247, 23.6),
    };

    /// <summary>Animate an element's scale (around its centre) toward <paramref name="to"/>.</summary>
    public static void AnimateScale(UIElement element, double to, SpringPreset preset = SpringPreset.Default)
    {
        ArgumentNullException.ThrowIfNull(element);
        var scale = GetOrCreateScale(element);
        if (ReduceMotion)
        {
            scale.ScaleX = to;
            scale.ScaleY = to;
            return;
        }
        Start("scale", element, () => scale.ScaleX, v => { scale.ScaleX = v; scale.ScaleY = v; }, to, preset);
    }

    /// <summary>Animate an element's opacity toward <paramref name="to"/>.</summary>
    public static void AnimateOpacity(UIElement element, double to, SpringPreset preset = SpringPreset.Default)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (ReduceMotion)
        {
            var fade = new DoubleAnimation(element.Opacity, to, TimeSpan.FromMilliseconds(120));
            element.BeginAnimation(UIElement.OpacityProperty, fade);
            return;
        }
        Start("opacity", element, () => element.Opacity, v => element.Opacity = v, to, preset);
    }

    /// <summary>Animate an element's translation toward (<paramref name="toX"/>, <paramref name="toY"/>).</summary>
    public static void AnimateTranslate(UIElement element, double toX, double toY, SpringPreset preset = SpringPreset.Default)
    {
        ArgumentNullException.ThrowIfNull(element);
        var translate = GetOrCreateTranslate(element);
        if (ReduceMotion)
        {
            translate.X = toX;
            translate.Y = toY;
            return;
        }
        Start("translateX", element, () => translate.X, v => translate.X = v, toX, preset);
        Start("translateY", element, () => translate.Y, v => translate.Y = v, toY, preset);
    }

    /// <summary>Stop every spring on an element and leave it where it is.</summary>
    public static void Stop(UIElement element)
    {
        _active.RemoveAll(s => ReferenceEquals(s.Element, element));
        MaybeUnsubscribe();
    }

    private static ScaleTransform GetOrCreateScale(UIElement element)
    {
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        var group = EnsureTransformGroup(element);
        foreach (var child in group.Children)
            if (child is ScaleTransform scale)
                return scale;
        var created = new ScaleTransform(1, 1);
        group.Children.Add(created);
        return created;
    }

    private static TranslateTransform GetOrCreateTranslate(UIElement element)
    {
        var group = EnsureTransformGroup(element);
        foreach (var child in group.Children)
            if (child is TranslateTransform translate)
                return translate;
        var created = new TranslateTransform();
        group.Children.Add(created);
        return created;
    }

    private static TransformGroup EnsureTransformGroup(UIElement element)
    {
        if (element.RenderTransform is TransformGroup group)
            return group;
        group = new TransformGroup();
        if (element.RenderTransform is Transform existing && !(existing is MatrixTransform m && m.Matrix.IsIdentity))
            group.Children.Add(existing);
        element.RenderTransform = group;
        return group;
    }

    private static void Start(string key, UIElement element, Func<double> get, Action<double> set, double to, SpringPreset preset)
    {
        // One spring per (element, key): a new target replaces the old spring,
        // keeping its current velocity so motion never jumps.
        var existing = _active.FirstOrDefault(s => ReferenceEquals(s.Element, element) && s.Key == key);
        var (stiffness, damping) = Parameters(preset);
        if (existing is not null)
        {
            existing.Target = to;
            existing.Stiffness = stiffness;
            existing.Damping = damping;
            return;
        }
        _active.Add(new SpringState
        {
            Element = element,
            Key = key,
            Get = get,
            Set = set,
            Target = to,
            Velocity = 0,
            Stiffness = stiffness,
            Damping = damping,
        });
        MaybeSubscribe();
    }

    private static void MaybeSubscribe()
    {
        if (_subscribed || _active.Count == 0) return;
        _subscribed = true;
        _lastTick = DateTime.Now;
        CompositionTarget.Rendering += OnRendering;
    }

    private static void MaybeUnsubscribe()
    {
        if (!_subscribed || _active.Count > 0) return;
        _subscribed = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private static void OnRendering(object? sender, EventArgs e)
    {
        var now = DateTime.Now;
        var dt = (now - _lastTick).TotalSeconds;
        _lastTick = now;
        if (dt <= 0 || dt > 0.05) dt = 1.0 / 60.0;

        for (var i = _active.Count - 1; i >= 0; i--)
        {
            var state = _active[i];
            var x = state.Get();
            // Semi-implicit Euler: velocity first, then position. Stable for
            // these stiffness/damping values at frame-rate timesteps.
            state.Velocity += (-state.Stiffness * (x - state.Target) - state.Damping * state.Velocity) * dt;
            x += state.Velocity * dt;
            state.Set(x);
            if (Math.Abs(x - state.Target) < 0.001 && Math.Abs(state.Velocity) < 0.001)
            {
                state.Set(state.Target);
                _active.RemoveAt(i);
            }
        }
        MaybeUnsubscribe();
    }
}
