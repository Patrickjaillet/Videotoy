namespace Videotoy.Rendering;

/// <summary>
/// Pilote la position de lecture du viewport en temps réel : play/pause, scrub manuel,
/// et bouclage automatique sur une durée donnée. Ne fait aucun rendu elle-même — elle
/// ne fait qu'avancer <see cref="CurrentTimeSeconds"/> à partir des deltas de temps réel
/// qui lui sont fournis par l'hôte (timer WPF, boucle de composition, etc.).
/// </summary>
public sealed class PreviewClock
{
    private double _currentTimeSeconds;

    /// <summary>
    /// Durée totale de la boucle de prévisualisation, en secondes. Lorsque la lecture
    /// dépasse cette durée, la position revient à zéro (lecture en boucle infinie).
    /// </summary>
    public double LoopDurationSeconds { get; set; } = 10.0;

    /// <summary>
    /// Position de lecture actuelle, toujours comprise dans [0, LoopDurationSeconds).
    /// </summary>
    public double CurrentTimeSeconds
    {
        get => _currentTimeSeconds;
        private set => _currentTimeSeconds = WrapToLoop(value);
    }

    public bool IsPlaying { get; private set; }

    public event EventHandler? TimeChanged;

    public void Play()
    {
        IsPlaying = true;
    }

    public void Pause()
    {
        IsPlaying = false;
    }

    public void TogglePlayback()
    {
        IsPlaying = !IsPlaying;
    }

    /// <summary>
    /// Positionne manuellement la lecture (scrub via la timeline), quel que soit
    /// l'état play/pause courant.
    /// </summary>
    public void Seek(double timeSeconds)
    {
        CurrentTimeSeconds = timeSeconds;
        RaiseTimeChanged();
    }

    public void Stop()
    {
        IsPlaying = false;
        CurrentTimeSeconds = 0.0;
        RaiseTimeChanged();
    }

    /// <summary>
    /// Avance la position de lecture d'un delta de temps réel écoulé (secondes).
    /// N'a aucun effet si la lecture est en pause. À appeler depuis la boucle de
    /// rafraîchissement de l'hôte (ex: CompositionTarget.Rendering, DispatcherTimer).
    /// </summary>
    public void Advance(double deltaSeconds)
    {
        if (!IsPlaying || deltaSeconds <= 0.0)
        {
            return;
        }

        CurrentTimeSeconds = _currentTimeSeconds + deltaSeconds;
        RaiseTimeChanged();
    }

    private double WrapToLoop(double timeSeconds)
    {
        if (LoopDurationSeconds <= 0.0)
        {
            return Math.Max(0.0, timeSeconds);
        }

        var wrapped = timeSeconds % LoopDurationSeconds;
        return wrapped < 0.0 ? wrapped + LoopDurationSeconds : wrapped;
    }

    private void RaiseTimeChanged()
    {
        TimeChanged?.Invoke(this, EventArgs.Empty);
    }
}
