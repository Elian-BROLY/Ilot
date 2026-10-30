using System.IO;

namespace Ilot.Services;

/// <summary>Les notes, une par fichier : note.txt pour la première, note2.txt, note3.txt… pour les suivantes.</summary>
internal sealed class NoteStore(string folder)
{
    public const int MaxNotes = 5;

    public static bool IsNoteFile(string fileName) =>
        fileName.StartsWith("note", StringComparison.OrdinalIgnoreCase) && fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);

    public int CountExisting()
    {
        var count = 1;
        while (count < MaxNotes && File.Exists(PathOf(count)))
        {
            count++;
        }
        return count;
    }

    public string PathOf(int index) => Path.Combine(folder, index == 0 ? "note.txt" : $"note{index + 1}.txt");

    public string Read(int index) => File.Exists(PathOf(index)) ? File.ReadAllText(PathOf(index)) : string.Empty;

    public void Write(int index, string text) => AtomicFile.WriteAllText(PathOf(index), text);

    /// <summary>Retire une note et décale les suivantes, pour que les numéros restent continus. La première ne se retire pas.</summary>
    public void Remove(int index, int count)
    {
        if (index <= 0 || index >= count)
        {
            return;
        }
        File.Delete(PathOf(index));
        for (var i = index + 1; i < count; i++)
        {
            File.Move(PathOf(i), PathOf(i - 1));
        }
    }
}

/// <summary>Cases à cocher dans le texte d'une note : un fichier texte reste lisible partout.</summary>
internal static class NoteChecklist
{
    public const char Empty = '☐';
    public const char Checked = '☑';

    /// <summary>Ajoute ou retire la case de la ligne où se trouve le curseur.</summary>
    public static (string Text, int Caret) ToggleLineBox(string text, int caret)
    {
        var start = LineStart(text, caret);
        if (start < text.Length && text[start] is Empty or Checked)
        {
            var length = start + 1 < text.Length && text[start + 1] == ' ' ? 2 : 1;
            return (text.Remove(start, length), Math.Max(start, caret - length));
        }
        return (text.Insert(start, Empty + " "), caret + 2);
    }

    /// <summary>Coche ou décoche si l'index tombe sur une case. Null sinon.</summary>
    public static string? ToggleBoxAt(string text, int index)
    {
        if (index < 0 || index >= text.Length || text[index] is not (Empty or Checked) || LineStart(text, index) != index)
        {
            return null;
        }
        return text[..index] + (text[index] == Empty ? Checked : Empty) + text[(index + 1)..];
    }

    private static int LineStart(string text, int index) =>
        index <= 0 ? 0 : text.LastIndexOf('\n', Math.Min(index, text.Length) - 1) + 1;
}
