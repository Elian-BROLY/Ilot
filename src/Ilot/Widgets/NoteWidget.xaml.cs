using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Ilot.Services;

namespace Ilot.Widgets;

public partial class NoteWidget : UserControl
{
    private readonly NoteStore _store = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ilot"));
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _hasUnsavedChanges;
    private int _index;
    private int _count = 1;

    public NoteWidget()
    {
        InitializeComponent();
        _saveTimer.Tick += (_, _) => Save();
        _count = _store.CountExisting();
        ShowNote(0);
    }

    /// <summary>La capsule ne prend jamais le focus d'elle-même : elle doit l'autoriser pour la saisie.</summary>
    public event Action? TypingRequested;

    public bool IsTyping => NoteBox.IsKeyboardFocused;

    /// <summary>Si le texte déborde, la molette le fait défiler plutôt que de changer de page.</summary>
    public bool IsContentScrollable => NoteBox.ExtentHeight > NoteBox.ViewportHeight;

    public void FocusNote() => NoteBox.Focus();

    public void Save()
    {
        _saveTimer.Stop();
        if (!_hasUnsavedChanges)
        {
            return;
        }
        try
        {
            _store.Write(_index, NoteBox.Text);
            _hasUnsavedChanges = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nouvel essai à la prochaine modification.
        }
    }

    private void ShowNote(int index)
    {
        Save();
        _index = index;
        try
        {
            NoteBox.Text = _store.Read(index);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            NoteBox.Text = string.Empty; // Note illisible : le fichier n'est réécrit qu'à la prochaine saisie.
        }
        _saveTimer.Stop();
        _hasUnsavedChanges = false;
        RenderNavigation();
    }

    // Une seule note : seuls les boutons de case à cocher et de nouvelle note restent, pour ne pas charger la page.
    private void RenderNavigation()
    {
        var hasSeveral = _count > 1;
        var navigation = hasSeveral ? Visibility.Visible : Visibility.Collapsed;
        PreviousButton.Visibility = navigation;
        NextButton.Visibility = navigation;
        PositionText.Visibility = navigation;
        RemoveButton.Visibility = _index > 0 ? Visibility.Visible : Visibility.Collapsed;
        AddButton.Visibility = _count < NoteStore.MaxNotes ? Visibility.Visible : Visibility.Collapsed;
        PositionText.Text = (_index + 1).ToString(CultureInfo.InvariantCulture) + "/" + _count.ToString(CultureInfo.InvariantCulture);
    }

    private void Previous_Click(object sender, RoutedEventArgs e) => ShowNote((_index - 1 + _count) % _count);

    private void Next_Click(object sender, RoutedEventArgs e) => ShowNote((_index + 1) % _count);

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (_count >= NoteStore.MaxNotes)
        {
            return;
        }
        Save();
        _store.Write(_count, string.Empty); // Le fichier doit exister : c'est lui qui compte les notes au prochain lancement.
        _count++;
        ShowNote(_count - 1);
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (_index == 0)
        {
            return; // La première note ne se supprime pas.
        }
        if (NoteBox.Text.Length > 0
            && MessageBox.Show(Loc.T("Supprimer cette note ?"), "Ilot", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }
        _saveTimer.Stop();
        _hasUnsavedChanges = false;
        try
        {
            _store.Remove(_index, _count);
            _count--;
            ShowNote(Math.Min(_index, _count - 1));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write(ex);
        }
    }

    private void Checklist_Click(object sender, RoutedEventArgs e)
    {
        var (text, caret) = NoteChecklist.ToggleLineBox(NoteBox.Text, NoteBox.CaretIndex);
        NoteBox.Text = text;
        NoteBox.CaretIndex = caret;
    }

    private void NoteBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Un clic sur une case la coche, sans passer en saisie.
        var index = NoteBox.GetCharacterIndexFromPoint(e.GetPosition(NoteBox), snapToText: false);
        if (NoteChecklist.ToggleBoxAt(NoteBox.Text, index) is { } toggled)
        {
            var caret = NoteBox.CaretIndex;
            NoteBox.Text = toggled;
            NoteBox.CaretIndex = caret;
            e.Handled = true;
            return;
        }
        if (!NoteBox.IsKeyboardFocused)
        {
            TypingRequested?.Invoke();
        }
    }

    private void NoteBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        Placeholder.Visibility = NoteBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _hasUnsavedChanges = true;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void NoteBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => Save();
}
