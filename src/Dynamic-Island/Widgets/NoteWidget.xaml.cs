using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Services;

namespace DynamicIsland.Widgets;

public partial class NoteWidget : UserControl
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dynamic-Island", "note.txt");
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _hasUnsavedChanges;

    public NoteWidget()
    {
        InitializeComponent();
        _saveTimer.Tick += (_, _) => Save();
        try
        {
            if (File.Exists(_path))
            {
                NoteBox.Text = File.ReadAllText(_path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Note illisible : le champ reste vide, le fichier n'est réécrit qu'à la prochaine saisie.
        }
        _hasUnsavedChanges = false;
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
            AtomicFile.WriteAllText(_path, NoteBox.Text);
            _hasUnsavedChanges = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nouvel essai à la prochaine modification.
        }
    }

    private void NoteBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
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
