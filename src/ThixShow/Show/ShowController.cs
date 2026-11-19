using System.Diagnostics;
using Avalonia.Threading;
using ThixOffice.Desktop.Rendering;
using ThixShow.Core.Layout;
using ThixShow.Core.Model;
using ThixShow.Core.Show;

namespace ThixShow.Show;

/// <summary>What the audience sees instead of the slide (PowerPoint's B and W keys).</summary>
public enum BlankScreen
{
    None,
    Black,
    White,
}

/// <summary>
/// One running slide show for every surface that shows it (the full-screen window, the presenter view, the reading view): the
/// core's <see cref="SlideShow"/> on a monotonic clock, PowerPoint's navigation (next, previous, go to, typed slide numbers, black
/// and white screens), and a frame timer that runs at 60 fps only while something moves – otherwise nothing is drawn until input.
/// </summary>
public sealed class ShowController
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(1000.0 / 60) };
    private TimeSpan _pausedAt;
    private TimeSpan _pausedTotal;
    private bool _paused;
    private string _typed = "";
    private bool _ended;

    public ShowController(Presentation presentation, SlideShowOptions options)
    {
        Presentation = presentation;
        Show = new SlideShow(presentation, OfficeFonts.Skia, options, new RenderOptions(Now: DateTime.Now));
        _timer.Tick += (_, _) => Tick();
    }

    /// <summary>A new frame should be drawn (the slide, a step, the clock moved).</summary>
    public event EventHandler? Changed;

    /// <summary>The show ended (Esc, past the end screen).</summary>
    public event EventHandler? Ended;

    public Presentation Presentation { get; }

    public SlideShow Show { get; }

    public BlankScreen Blank { get; private set; }

    /// <summary>The time on the show's clock.</summary>
    public TimeSpan Now => _clock.Elapsed;

    /// <summary>The digits typed so far for "Nummer + Eingabe".</summary>
    public string TypedNumber => _typed;

    /// <summary>The presenter's timer (it can be paused and reset; the show's own clock runs on).</summary>
    public TimeSpan Elapsed => (_paused ? _pausedAt : _clock.Elapsed) - _pausedTotal;

    public bool IsTimerPaused => _paused;

    /// <summary>The frames per second the timer ran last (performance numbers).</summary>
    public int FramesDrawn { get; private set; }

    public void Start()
    {
        Show.Start(Now);
        Kick();
    }

    public void Next()
    {
        if (Blank != BlankScreen.None)
        {
            Blank = BlankScreen.None;
            Kick();
            return;
        }

        if (Show.IsFinished || Show.Phase == ShowPhase.End)
        {
            End();
            return;
        }

        Show.Next(Now);
        Kick();
    }

    public void Previous()
    {
        Blank = BlankScreen.None;
        Show.Previous(Now);
        Kick();
    }

    /// <summary>Goes to a position among the shown slides (0-based).</summary>
    public void GoTo(int position)
    {
        Blank = BlankScreen.None;
        Show.GoTo(position, Now);
        Kick();
    }

    /// <summary>Goes to a slide by its number in the presentation (1-based, as typed); a hidden slide is shown too.</summary>
    public void GoToSlideNumber(int number)
    {
        var position = Show.Shown.ToList().IndexOf(number - 1);
        if (position >= 0)
        {
            GoTo(position);
        }
    }

    /// <summary>A digit typed: collected until Eingabe goes to that slide.</summary>
    public void TypeDigit(char digit)
    {
        if (_typed.Length < 4)
        {
            _typed += digit;
        }
    }

    /// <summary>Eingabe after digits: goes there and returns true; without digits false (Eingabe is then "next").</summary>
    public bool CommitTyped()
    {
        if (_typed.Length == 0)
        {
            return false;
        }

        var number = int.Parse(_typed, System.Globalization.CultureInfo.InvariantCulture);
        _typed = "";
        GoToSlideNumber(number);
        return true;
    }

    public void ToggleBlank(BlankScreen screen)
    {
        Blank = Blank == screen ? BlankScreen.None : screen;
        Kick();
    }

    public void End()
    {
        if (_ended)
        {
            return;
        }

        _ended = true;
        _timer.Stop();
        Show.End();
        Ended?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleTimerPause()
    {
        if (_paused)
        {
            _pausedTotal += _clock.Elapsed - _pausedAt;
            _paused = false;
        }
        else
        {
            _pausedAt = _clock.Elapsed;
            _paused = true;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ResetTimer()
    {
        _pausedTotal = _clock.Elapsed;
        _pausedAt = _clock.Elapsed;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The frame to draw now.</summary>
    public ShowFrame Frame() => Show.Frame(Now);

    /// <summary>Something changed: draw, and keep the frame timer running while the show animates.</summary>
    private void Kick()
    {
        Changed?.Invoke(this, EventArgs.Empty);
        if (Show.IsAnimating(Now))
        {
            _timer.Start();
        }
    }

    private void Tick()
    {
        FramesDrawn++;
        Changed?.Invoke(this, EventArgs.Empty);
        if (Show.IsFinished)
        {
            _timer.Stop();
            return;
        }

        if (!Show.IsAnimating(Now))
        {
            _timer.Stop();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>True while the frame timer runs (tests: idle means no timer).</summary>
    public bool IsTicking => _timer.IsEnabled;
}
