using System.Reflection;
using System.Windows.Threading;
using Velopack;
using Velopack.Sources;

namespace Ilot.Services;

/// <summary>
/// Cherche les nouvelles versions dans les GitHub Releases du projet et les télécharge en arrière-plan. Une version téléchargée
/// ne s'installe que sur un clic, sur la bannière ou dans les réglages : l'exécutable n'étant pas signé, Windows peut refuser de
/// lancer une version donnée, et une installation automatique laisserait alors une application qui ne démarre plus.
/// La bannière revient à chaque lancement tant que la version n'est pas installée.
/// </summary>
internal sealed class UpdateService
{
    private const string RepositoryUrl = "https://github.com/Elian-BROLY/Ilot";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    // Créé au premier usage : Velopack doit avoir été initialisé par Program.Main, et ce service est une donnée statique d'App.
    private readonly Lazy<UpdateManager?> _manager = new(TryCreateManager);
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = CheckInterval };
    private UpdateInfo? _pending;
    private bool _isChecking;

    public UpdateService() => _timer.Tick += async (_, _) => await CheckAsync();

    /// <summary>Levé quand une nouvelle version est prête à être installée.</summary>
    public event Action<string>? UpdateReady;

    /// <summary>Faux pour une copie lancée depuis dist\ : sans installateur, rien à mettre à jour.</summary>
    public bool IsAvailable => _manager.Value?.IsInstalled == true;

    public bool IsPending => _pending is not null;

    public string CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? string.Empty;

    // Une panne des mises à jour ne doit pas empêcher l'application de démarrer.
    private static UpdateManager? TryCreateManager()
    {
        try
        {
            return new UpdateManager(new GithubSource(RepositoryUrl, null, false));
        }
        catch (Exception ex)
        {
            AppLog.Write(ex);
            return null;
        }
    }

    public void SetAutomatic(bool isAutomatic)
    {
        if (isAutomatic && IsAvailable)
        {
            if (!_timer.IsEnabled)
            {
                _timer.Start();
                _ = CheckAsync();
            }
        }
        else
        {
            _timer.Stop();
        }
    }

    /// <summary>Renvoie le résultat à afficher dans les réglages.</summary>
    public async Task<string> CheckAsync()
    {
        if (_manager.Value is not { IsInstalled: true } manager)
        {
            return Loc.T("Mises à jour indisponibles : cette copie n'est pas installée.");
        }
        if (_pending is not null)
        {
            return Loc.T("La version {0} est prête.", _pending.TargetFullRelease.Version);
        }
        if (_isChecking)
        {
            return Loc.T("Recherche déjà en cours…");
        }

        _isChecking = true;
        try
        {
            var update = await manager.CheckForUpdatesAsync();
            if (update is null)
            {
                return Loc.T("Vous avez la dernière version.");
            }
            await manager.DownloadUpdatesAsync(update);
            _pending = update;
            var version = update.TargetFullRelease.Version.ToString();
            UpdateReady?.Invoke(version);
            return Loc.T("La version {0} est prête.", version);
        }
        catch (Exception ex)
        {
            AppLog.Write(ex);
            return Loc.T("Recherche impossible pour le moment.");
        }
        finally
        {
            _isChecking = false;
        }
    }

    public void ApplyAndRestart()
    {
        if (_pending is { } update)
        {
            _manager.Value?.ApplyUpdatesAndRestart(update);
        }
    }
}
