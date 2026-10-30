using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Ilot.Services;

namespace Ilot.Widgets;

public partial class MailWidget : UserControl
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1); // Délai maximal d'affichage d'une bannière.
    private static readonly TimeSpan MinimumRefreshGap = TimeSpan.FromSeconds(30);
    private const int CompactRowCount = 3; // À partir de là, les lignes normales ne tiennent plus dans la page.
    private const int MaxRowsWithDetails = 3;
    private static readonly MailProvider[] Providers = [MailProvider.Outlook, MailProvider.Gmail, MailProvider.Gmail2, MailProvider.Imap];

    private readonly Dictionary<MailProvider, MailRow> _rows = [];
    private readonly MailListView _list;
    private readonly Dictionary<MailProvider, MailSummary> _lastSummaries = [];
    private readonly NewMailDetector _detector = new();
    private readonly DispatcherTimer _pollTimer = new(DispatcherPriority.Background) { Interval = PollInterval };
    private DateTime _lastRefreshUtc = DateTime.MinValue;
    private bool _isRefreshing;
    private bool _isRefreshPending;
    private bool _wasGmailEnabled = App.Settings.Current.GmailEnabled;
    private bool _wasGmail2Enabled = App.Settings.Current.Gmail2Enabled;
    private bool _wasOutlookEnabled = App.Settings.Current.OutlookEnabled;

    public MailWidget()
    {
        InitializeComponent();
        foreach (var provider in Providers)
        {
            var row = new MailRow(provider, this);
            _rows[provider] = row;
            Rows.Children.Add(row.Root);
        }
        _list = new MailListView(this, RefreshAsync);
        _list.Root.Visibility = Visibility.Collapsed;
        RootGrid.Children.Add(_list.Root);

        _pollTimer.Tick += async (_, _) => await RefreshAsync();
        IsVisibleChanged += async (_, _) =>
        {
            if (IsVisible && DateTime.UtcNow - _lastRefreshUtc >= MinimumRefreshGap)
            {
                await RefreshAsync();
            }
        };
        App.Mail.AccountsChanged += () => _ = RefreshAsync();
        App.Settings.Changed += OnSettingsChanged;
        _ = RefreshAsync();
    }

    /// <summary>Levé après chaque lecture : le compteur de la capsule compacte s'y abonne.</summary>
    public event Action? UnreadChanged;

    /// <summary>Levé au plus une fois par lecture, quand au moins un nouveau mail est arrivé.</summary>
    internal event Action<MailNotification>? NewMailReceived;

    public int TotalUnread { get; private set; }

    // Le second compte Gmail est facultatif : sans connexion, il n'occupe pas une ligne « Non connecté ».
    private static bool IsAccountEnabled(MailProvider provider) => provider switch
    {
        MailProvider.Gmail => App.Settings.Current.GmailEnabled,
        MailProvider.Gmail2 => App.Settings.Current.Gmail2Enabled && App.Mail.IsConnected(MailProvider.Gmail2),
        MailProvider.Imap => App.Settings.Current.ImapEnabled && App.Mail.IsConnected(MailProvider.Imap),
        _ => App.Settings.Current.OutlookEnabled,
    };

    private void OnSettingsChanged()
    {
        var settings = App.Settings.Current;
        // Les autres réglages (curseurs de délai, par exemple) ne doivent pas déclencher de requêtes.
        if (settings.GmailEnabled != _wasGmailEnabled || settings.Gmail2Enabled != _wasGmail2Enabled || settings.OutlookEnabled != _wasOutlookEnabled)
        {
            _wasGmailEnabled = settings.GmailEnabled;
            _wasGmail2Enabled = settings.Gmail2Enabled;
            _wasOutlookEnabled = settings.OutlookEnabled;
            _ = RefreshAsync();
        }
    }

    private async Task RefreshAsync()
    {
        if (_isRefreshing)
        {
            _isRefreshPending = true; // Un compte vient de changer : le résultat en cours est déjà périmé.
            return;
        }
        _isRefreshing = true;
        _lastRefreshUtc = DateTime.UtcNow;
        try
        {
            await ReadAccountsAsync();
        }
        catch (Exception ex)
        {
            AppLog.Write(ex);
            _pollTimer.Start(); // Une erreur imprévue ne doit pas arrêter les mails jusqu'au redémarrage.
        }
        finally
        {
            _isRefreshing = false;
        }

        if (_isRefreshPending)
        {
            _isRefreshPending = false;
            await RefreshAsync();
        }
    }

    private async Task ReadAccountsAsync()
    {
        var total = 0;
        var anyPolled = false;
        var anyEnabled = false;
        MailNotification? notification = null;

        // Un seul compte lisible : ses mails prennent toute la page. Sinon, une ligne par compte.
        var readable = Providers.Where(provider => IsAccountEnabled(provider) && App.Mail.IsConnected(provider)).ToList();
        MailProvider? listed = readable.Count == 1 ? readable[0] : null;
        Rows.Visibility = listed is null ? Visibility.Visible : Visibility.Collapsed;
        _list.Root.Visibility = listed is null ? Visibility.Collapsed : Visibility.Visible;
        var enabledCount = Providers.Count(IsAccountEnabled);

        foreach (var provider in Providers)
        {
            var isEnabled = IsAccountEnabled(provider);
            anyEnabled |= isEnabled;
            _rows[provider].SetCompact(enabledCount);
            _rows[provider].Root.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;
            if (!isEnabled)
            {
                _detector.Reset(provider);
                _lastSummaries.Remove(provider);
                continue;
            }
            if (!App.Mail.IsConnected(provider))
            {
                _detector.Reset(provider);
                _lastSummaries.Remove(provider);
                _rows[provider].ShowDisconnected();
                continue;
            }
            anyPolled = true;
            var summary = (await App.Mail.GetSummaryAsync(provider)).KeepingContentOf(_lastSummaries.GetValueOrDefault(provider));
            _lastSummaries[provider] = summary;
            if (provider == listed)
            {
                _list.Show(provider, summary);
            }
            else
            {
                _rows[provider].Show(summary);
            }
            total += summary.Unread;

            // Une lecture en échec ne dit rien de la boîte : la référence précédente reste valable.
            if (summary.Error is null)
            {
                var added = _detector.Update(provider, summary.Items);
                if (added.Count > 0)
                {
                    notification = notification is null
                        ? new MailNotification(provider, added[0], added.Count - 1)
                        : notification with { OtherCount = notification.OtherCount + added.Count };
                }
            }
        }
        NoAccountText.Visibility = anyEnabled ? Visibility.Collapsed : Visibility.Visible;

        // Sans compte à lire, aucune requête périodique.
        if (anyPolled)
        {
            _pollTimer.Start();
        }
        else
        {
            _pollTimer.Stop();
        }
        TotalUnread = total;
        UnreadChanged?.Invoke();
        if (notification is not null)
        {
            NewMailReceived?.Invoke(notification);
        }
    }

    private sealed class MailRow
    {
        private readonly MailWidget _owner;
        private readonly MailProvider _provider;
        private readonly Border _background;
        private readonly TextBlock _titleText;
        private readonly TextBlock _detailText;
        private readonly TextBlock _positionText;
        private readonly TextBlock _countText;
        private readonly Border _countBadge;
        private readonly Button _nextButton;
        private readonly Button _readButton;
        private readonly Button _trashButton;
        private readonly Brush _hoverBrush;
        private readonly string _label;
        private List<MailItem> _items = [];
        private int _index;
        private string? _emptyMessage;
        private bool _isPressed;

        public MailRow(MailProvider provider, MailWidget owner)
        {
            _owner = owner;
            _provider = provider;
            _label = provider.Label();
            _hoverBrush = (Brush)owner.FindResource("SurfaceBrush");
            var primary = (Brush)owner.FindResource("TextPrimaryBrush");
            var secondary = (Brush)owner.FindResource("TextSecondaryBrush");

            var mark = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(8),
                Background = (Brush)owner.FindResource("HoverBrush"),
                Child = new TextBlock
                {
                    Text = _label[..1],
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = primary,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };

            _titleText = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = primary, TextTrimming = TextTrimming.CharacterEllipsis };
            _detailText = new TextBlock { FontSize = 11, Foreground = secondary, TextTrimming = TextTrimming.CharacterEllipsis };
            var texts = new StackPanel { Margin = new Thickness(10, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(_titleText);
            texts.Children.Add(_detailText);

            _positionText = new TextBlock
            {
                FontSize = 10,
                Margin = new Thickness(0, 0, 4, 0),
                Foreground = secondary,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
            };
            _nextButton = CreateRowButton(owner, "", Loc.T("Mail suivant"), Loc.T("Mail suivant {0}", _label));
            _nextButton.Click += Next_Click;
            _readButton = CreateRowButton(owner, "", Loc.T("Marquer comme lu"), Loc.T("Marquer comme lu {0}", _label));
            _readButton.Click += (_, e) => RunActionAsync(e, _readButton, id => App.Mail.MarkAsReadAsync(_provider, id));
            _trashButton = CreateRowButton(owner, "", Loc.T("Mettre à la corbeille"), Loc.T("Supprimer le mail {0}", _label));
            _trashButton.Click += (_, e) => RunActionAsync(e, _trashButton, id => App.Mail.TrashAsync(_provider, id));

            _countText = new TextBlock
            {
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _countBadge = new Border
            {
                MinWidth = 22,
                Height = 20,
                Margin = new Thickness(4, 0, 0, 0),
                Padding = new Thickness(6, 0, 6, 0),
                CornerRadius = new CornerRadius(10),
                Background = (Brush)owner.FindResource("AccentBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
                Child = _countText,
            };

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            actions.Children.Add(_positionText);
            actions.Children.Add(_nextButton);
            actions.Children.Add(_readButton);
            actions.Children.Add(_trashButton);
            actions.Children.Add(_countBadge);

            var grid = new Grid { Margin = new Thickness(4, 0, 6, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(texts, 1);
            Grid.SetColumn(actions, 2);
            grid.Children.Add(mark);
            grid.Children.Add(texts);
            grid.Children.Add(actions);

            _background = new Border
            {
                Height = 44,
                Margin = new Thickness(0, 2, 0, 2),
                CornerRadius = new CornerRadius(10),
                Background = Brushes.Transparent,
                Child = grid,
            };
            _background.MouseEnter += (_, _) => RenderHover();
            _background.MouseLeave += (_, _) =>
            {
                _isPressed = false;
                RenderHover();
            };
            _background.MouseLeftButtonDown += (_, e) =>
            {
                // Géré ici : ouvrir un mail ne doit ni verrouiller ni déplacer la capsule.
                if (Current is not null)
                {
                    _isPressed = true;
                    e.Handled = true;
                }
            };
            _background.MouseLeftButtonUp += (_, e) =>
            {
                if (_isPressed && Current?.OpenUrl is { } url)
                {
                    e.Handled = true;
                    if (!App.Mail.OpenInBrowser(url))
                    {
                        _detailText.Text = Loc.T("Ouverture du navigateur impossible");
                    }
                }
                _isPressed = false;
            };

            ShowDisconnected();
        }

        public FrameworkElement Root => _background;

        public void SetCompact(int accountCount)
        {
            // Quatre comptes à la fois ne tiennent qu'avec une seule ligne de texte chacun.
            _background.Height = accountCount >= MaxRowsWithDetails + 1 ? 25 : accountCount >= CompactRowCount ? 33 : 44;
            _background.Margin = accountCount >= CompactRowCount ? new Thickness(0, 1, 0, 1) : new Thickness(0, 2, 0, 2);
            _detailText.Visibility = accountCount > MaxRowsWithDetails ? Visibility.Collapsed : Visibility.Visible;
        }

        private MailItem? Current => _index < _items.Count ? _items[_index] : null;

        public void ShowDisconnected()
        {
            _items = [];
            _index = 0;
            _emptyMessage = Loc.T("Non connecté : à configurer dans les réglages");
            _countBadge.Visibility = Visibility.Collapsed;
            Render();
        }

        public void Show(MailSummary summary)
        {
            // Rester sur le mail affiché : un rafraîchissement ne doit pas faire perdre sa place.
            var currentId = Current?.Id;
            _items = [.. summary.Items];
            var kept = _items.FindIndex(item => item.Id == currentId);
            _index = kept >= 0 ? kept : Math.Min(_index, Math.Max(0, _items.Count - 1));

            _emptyMessage = summary.Error ?? Loc.T("Aucun message non lu");
            _background.ToolTip = _items.Count > 0 ? summary.Error : null; // Les derniers mails restent affichés.
            _countBadge.Visibility = summary.Unread > 0 ? Visibility.Visible : Visibility.Collapsed;
            _countText.Text = summary.Unread > 999 ? "999+" : summary.Unread.ToString(CultureInfo.InvariantCulture);
            Render();
            _ = LoadCurrentAsync();
        }

        private void Render()
        {
            if (Current is not { } item)
            {
                _titleText.Text = _label;
                _detailText.Text = _emptyMessage;
            }
            else if (!item.IsLoaded)
            {
                _titleText.Text = _label;
                _detailText.Text = Loc.T("Chargement…");
            }
            else
            {
                _titleText.Text = string.IsNullOrWhiteSpace(item.Sender) ? _label : item.Sender;
                _detailText.Text = string.IsNullOrWhiteSpace(item.Subject) ? Loc.T("(sans objet)") : item.Subject;
            }
            _positionText.Text = $"{_index + 1} / {_items.Count}";
            RenderHover();
        }

        private void RenderHover()
        {
            var isOverMail = _background.IsMouseOver && Current is not null;
            var hasSeveral = isOverMail && _items.Count > 1;
            _background.Background = isOverMail ? _hoverBrush : Brushes.Transparent;
            // La main seulement s'il y a quelque chose à ouvrir : un compte IMAP inconnu n'a pas de lien.
            _background.Cursor = isOverMail && Current?.OpenUrl is not null ? Cursors.Hand : null;
            _readButton.Visibility = isOverMail ? Visibility.Visible : Visibility.Collapsed;
            _trashButton.Visibility = isOverMail ? Visibility.Visible : Visibility.Collapsed;
            _nextButton.Visibility = hasSeveral ? Visibility.Visible : Visibility.Collapsed;
            _positionText.Visibility = hasSeveral ? Visibility.Visible : Visibility.Collapsed;
        }

        private async Task LoadCurrentAsync()
        {
            if (Current is not { IsLoaded: false } item)
            {
                return;
            }

            var loaded = await App.Mail.LoadItemAsync(_provider, item);
            var index = _items.FindIndex(other => other.Id == item.Id);
            if (index < 0)
            {
                return; // Le message a disparu pendant le chargement.
            }
            _items[index] = loaded;
            if (index == _index)
            {
                if (loaded.IsLoaded)
                {
                    Render();
                }
                else
                {
                    _detailText.Text = Loc.T("Lecture impossible pour le moment");
                }
            }
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (_items.Count < 2)
            {
                return;
            }
            // Après le dernier, retour au premier : un mail gardé reste accessible.
            _index = (_index + 1) % _items.Count;
            Render();
            _ = LoadCurrentAsync();
        }

        // Corbeille ou « lu » : dans les deux cas, le mail quitte la liste des non lus.
        private async void RunActionAsync(RoutedEventArgs e, Button button, Func<string, Task<string?>> action)
        {
            e.Handled = true;
            if (Current is not { } item)
            {
                return;
            }

            button.IsEnabled = false;
            var error = await action(item.Id);
            button.IsEnabled = true;
            if (error is not null)
            {
                _detailText.Text = error;
                return;
            }

            // Affiche tout de suite le mail suivant, sans attendre la réponse du serveur.
            _items.RemoveAll(other => other.Id == item.Id);
            _index = Math.Min(_index, Math.Max(0, _items.Count - 1));
            Render();
            _ = LoadCurrentAsync();
            await _owner.RefreshAsync();
        }

        private static Button CreateRowButton(FrameworkElement owner, string glyph, string toolTip, string automationName)
        {
            var button = new Button
            {
                Width = 26,
                Height = 26,
                FontSize = 12,
                Style = (Style)owner.FindResource("IconButton"),
                Content = glyph,
                ToolTip = toolTip,
                Visibility = Visibility.Collapsed,
            };
            AutomationProperties.SetName(button, automationName);
            return button;
        }
    }
}
