using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ilot.Services;

namespace Ilot.Widgets;

/// <summary>Plusieurs mails à l'écran, quand un seul compte est actif : la place des deux lignes de compte lui revient.</summary>
internal sealed class MailListView
{
    public const int VisibleCount = 4;
    private const double HeaderHeight = 18;
    private const double LineHeight = 22;

    private readonly Func<Task> _refreshAsync;
    private readonly StackPanel _root = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _titleText;
    private readonly TextBlock _positionText;
    private readonly TextBlock _countText;
    private readonly Border _countBadge;
    private readonly Button _nextPageButton;
    private readonly TextBlock _messageText;
    private readonly MailLine[] _lines = new MailLine[VisibleCount];
    private readonly HashSet<string> _loading = [];
    private readonly HashSet<string> _failed = [];
    private MailProvider? _provider;
    private List<MailItem> _items = [];
    private int _page;

    public MailListView(FrameworkElement owner, Func<Task> refreshAsync)
    {
        _refreshAsync = refreshAsync;
        var primary = (Brush)owner.FindResource("TextPrimaryBrush");
        var secondary = (Brush)owner.FindResource("TextSecondaryBrush");

        _titleText = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = secondary, VerticalAlignment = VerticalAlignment.Center };
        _positionText = new TextBlock { FontSize = 10, Foreground = secondary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 2, 0) };
        _nextPageButton = CreateButton(owner, "", Loc.T("Mails suivants"), 16, 9);
        _nextPageButton.Click += (_, e) =>
        {
            e.Handled = true;
            // Après la dernière page, retour à la première : un mail gardé reste accessible.
            _page = (_page + 1) % PageCount;
            Render();
            _ = LoadVisibleAsync();
        };
        _countText = new TextBlock
        {
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _countBadge = new Border
        {
            MinWidth = 18,
            Height = 15,
            Margin = new Thickness(4, 0, 0, 0),
            Padding = new Thickness(5, 0, 5, 0),
            CornerRadius = new CornerRadius(8),
            Background = (Brush)owner.FindResource("AccentBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Child = _countText,
        };

        var headerActions = new StackPanel { Orientation = Orientation.Horizontal };
        headerActions.Children.Add(_positionText);
        headerActions.Children.Add(_nextPageButton);
        headerActions.Children.Add(_countBadge);
        var header = new Grid { Height = HeaderHeight, Margin = new Thickness(6, 0, 6, 0) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(headerActions, 1);
        header.Children.Add(_titleText);
        header.Children.Add(headerActions);
        _root.Children.Add(header);

        _messageText = new TextBlock
        {
            Height = LineHeight,
            Margin = new Thickness(6, 33, 6, 33),
            FontSize = 12,
            Foreground = secondary,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
        };
        _root.Children.Add(_messageText);

        for (var i = 0; i < _lines.Length; i++)
        {
            _lines[i] = new MailLine(owner, primary, secondary, this);
            _root.Children.Add(_lines[i].Root);
        }
    }

    public FrameworkElement Root => _root;

    private int PageCount => Math.Max(1, (_items.Count + VisibleCount - 1) / VisibleCount);

    public void Show(MailProvider provider, MailSummary summary)
    {
        if (provider != _provider)
        {
            _provider = provider;
            _items = [];
            _page = 0;
        }

        // Un mail déjà lu n'est ni redemandé au service, ni réaffiché en « Chargement… » à chaque rafraîchissement.
        var known = _items.Where(item => item.IsLoaded).ToDictionary(item => item.Id);
        _items = summary.Items.Select(item => known.GetValueOrDefault(item.Id, item)).ToList();
        _failed.Clear();
        _page = Math.Min(_page, PageCount - 1);

        var label = provider.Label();
        // Les derniers mails restent affichés : l'erreur tient dans l'en-tête.
        _titleText.Text = summary.Error is { } error && _items.Count > 0 ? $"{label} · {error}" : label;
        _messageText.Text = summary.Error ?? Loc.T("Aucun message non lu");
        _countBadge.Visibility = summary.Unread > 0 ? Visibility.Visible : Visibility.Collapsed;
        _countText.Text = summary.Unread > 999 ? "999+" : summary.Unread.ToString(CultureInfo.InvariantCulture);
        Render();
        _ = LoadVisibleAsync();
    }

    private void Render()
    {
        var first = _page * VisibleCount;
        for (var i = 0; i < _lines.Length; i++)
        {
            var index = first + i;
            var item = index < _items.Count ? _items[index] : null;
            _lines[i].Show(item, item is not null && _failed.Contains(item.Id));
            if (_items.Count == 0)
            {
                _lines[i].Root.Visibility = Visibility.Collapsed; // Le message occupe leur place.
            }
        }

        _messageText.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var hasSeveralPages = _items.Count > VisibleCount;
        _nextPageButton.Visibility = hasSeveralPages ? Visibility.Visible : Visibility.Collapsed;
        _positionText.Visibility = hasSeveralPages ? Visibility.Visible : Visibility.Collapsed;
        _positionText.Text = $"{first + 1}–{Math.Min(first + VisibleCount, _items.Count)} / {_items.Count}";
    }

    private Task LoadVisibleAsync() => Task.WhenAll(_items
        .Skip(_page * VisibleCount)
        .Take(VisibleCount)
        .Where(item => !item.IsLoaded && !_failed.Contains(item.Id) && _loading.Add(item.Id))
        .ToList()
        .Select(LoadItemAsync));

    private async Task LoadItemAsync(MailItem item)
    {
        var loaded = await App.Mail.LoadItemAsync(_provider!.Value, item);
        _loading.Remove(item.Id);
        var index = _items.FindIndex(other => other.Id == item.Id);
        if (index < 0)
        {
            return; // Le message a disparu pendant le chargement.
        }
        _items[index] = loaded;
        if (!loaded.IsLoaded)
        {
            _failed.Add(item.Id);
        }
        Render();
    }

    // Corbeille ou « lu » : dans les deux cas, le mail quitte la liste des non lus.
    private async Task RunActionAsync(MailItem item, MailLine line, Button button, Func<MailProvider, string, Task<string?>> action)
    {
        button.IsEnabled = false;
        var error = await action(_provider!.Value, item.Id);
        button.IsEnabled = true;
        if (error is not null)
        {
            line.ShowError(error);
            return;
        }

        // Fait remonter tout de suite les mails suivants, sans attendre la réponse du serveur.
        _items.RemoveAll(other => other.Id == item.Id);
        _page = Math.Min(_page, PageCount - 1);
        Render();
        _ = LoadVisibleAsync();
        await _refreshAsync();
    }

    private static Button CreateButton(FrameworkElement owner, string glyph, string toolTip, double size, double fontSize)
    {
        var button = new Button
        {
            Width = size,
            Height = size,
            FontSize = fontSize,
            Style = (Style)owner.FindResource("IconButton"),
            Content = glyph,
            ToolTip = toolTip,
        };
        AutomationProperties.SetName(button, toolTip);
        return button;
    }

    private sealed class MailLine
    {
        private readonly MailListView _list;
        private readonly Border _background;
        private readonly TextBlock _senderText;
        private readonly TextBlock _subjectText;
        private readonly Button _readButton;
        private readonly Button _trashButton;
        private readonly Brush _hoverBrush;
        private MailItem? _item;
        private bool _isPressed;

        public MailLine(FrameworkElement owner, Brush primary, Brush secondary, MailListView list)
        {
            _list = list;
            _hoverBrush = (Brush)owner.FindResource("SurfaceBrush");
            _senderText = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = primary, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            _subjectText = new TextBlock { FontSize = 11, Foreground = secondary, Margin = new Thickness(8, 0, 4, 0), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            _readButton = CreateButton(owner, "", Loc.T("Marquer comme lu"), 20, 10);
            _readButton.Click += (_, e) => Run(e, _readButton, App.Mail.MarkAsReadAsync);
            _trashButton = CreateButton(owner, "", Loc.T("Mettre à la corbeille"), 20, 10);
            _trashButton.Click += (_, e) => Run(e, _trashButton, App.Mail.TrashAsync);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            actions.Children.Add(_readButton);
            actions.Children.Add(_trashButton);

            var grid = new Grid { Margin = new Thickness(6, 0, 2, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(_subjectText, 1);
            Grid.SetColumn(actions, 2);
            grid.Children.Add(_senderText);
            grid.Children.Add(_subjectText);
            grid.Children.Add(actions);

            _background = new Border
            {
                Height = LineHeight,
                CornerRadius = new CornerRadius(6),
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
                if (_item is not null)
                {
                    _isPressed = true;
                    e.Handled = true;
                }
            };
            _background.MouseLeftButtonUp += (_, e) =>
            {
                if (_isPressed && _item?.OpenUrl is { } url)
                {
                    e.Handled = true;
                    if (!App.Mail.OpenInBrowser(url))
                    {
                        _subjectText.Text = Loc.T("Ouverture du navigateur impossible");
                    }
                }
                _isPressed = false;
            };
            RenderHover();
        }

        public FrameworkElement Root => _background;

        public void Show(MailItem? item, bool hasFailed)
        {
            _item = item;
            _background.Visibility = item is null ? Visibility.Hidden : Visibility.Visible;
            if (item is null)
            {
                return;
            }
            if (item.IsLoaded)
            {
                _senderText.Text = string.IsNullOrWhiteSpace(item.Sender) ? Loc.T("(inconnu)") : item.Sender;
                _subjectText.Text = string.IsNullOrWhiteSpace(item.Subject) ? Loc.T("(sans objet)") : item.Subject;
            }
            else
            {
                _senderText.Text = string.Empty;
                _subjectText.Text = hasFailed ? Loc.T("Lecture impossible pour le moment") : Loc.T("Chargement…");
            }
            RenderHover();
        }

        public void ShowError(string message) => _subjectText.Text = message;

        private void RenderHover()
        {
            var isOverMail = _background.IsMouseOver && _item is not null;
            _background.Background = isOverMail ? _hoverBrush : Brushes.Transparent;
            // La main seulement s'il y a quelque chose à ouvrir : un compte IMAP inconnu n'a pas de lien.
            _background.Cursor = isOverMail && _item?.OpenUrl is not null ? Cursors.Hand : null;
            _readButton.Visibility = isOverMail ? Visibility.Visible : Visibility.Collapsed;
            _trashButton.Visibility = isOverMail ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Run(RoutedEventArgs e, Button button, Func<MailProvider, string, Task<string?>> action)
        {
            e.Handled = true;
            if (_item is { } item)
            {
                _ = _list.RunActionAsync(item, this, button, action);
            }
        }
    }
}
