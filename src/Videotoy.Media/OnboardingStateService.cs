using System;
using System.IO;
using System.Text.Json;

namespace Videotoy.Media;

public sealed class OnboardingState
{
    public bool HasSeenOnboarding { get; set; }

    /// <summary>
    /// Version number of the onboarding sequence last shown — kept even
    /// though only one sequence exists today, so a future UI overhaul can
    /// re-trigger onboarding (by bumping this constant) without a storage
    /// schema migration.
    /// </summary>
    public int LastSeenOnboardingVersion { get; set; }
}

/// <summary>
/// Persists whether the first-launch guided onboarding overlay (Phase
/// v1.8.0) has already been shown, mirroring the storage pattern already
/// used by <see cref="LoopSettingsService"/>/<see cref="RecentFilesService"/>.
/// A single flat state (not a keyed collection), since onboarding is a
/// whole-application concept, not per-shader.
/// </summary>
public sealed class OnboardingStateService
{
    public const int CurrentOnboardingVersion = 1;

    private readonly string _storageFilePath;

    public OnboardingStateService()
    {
        var appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Videotoy");

        Directory.CreateDirectory(appDataDirectory);
        _storageFilePath = Path.Combine(appDataDirectory, "onboarding-state.json");
    }

    public OnboardingState Load()
    {
        if (!File.Exists(_storageFilePath))
        {
            return new OnboardingState();
        }

        try
        {
            var json = File.ReadAllText(_storageFilePath);
            return JsonSerializer.Deserialize<OnboardingState>(json) ?? new OnboardingState();
        }
        catch (JsonException)
        {
            return new OnboardingState();
        }
    }

    public void MarkSeen()
    {
        var state = new OnboardingState
        {
            HasSeenOnboarding = true,
            LastSeenOnboardingVersion = CurrentOnboardingVersion
        };

        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_storageFilePath, json);
    }
}
