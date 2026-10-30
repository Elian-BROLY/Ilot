# Architecture

## Structure

```
src/Ilot/
  App.xaml(.cs)         Démarrage, instance unique, mode de rendu
  MainWindow.xaml(.cs)  Capsule : position, états, pages, animations, survol, masquage en plein écran
  MainWindow.Banners.cs Bannières : notifications, mails, morceau suivant, alertes
  MainWindow.Hotkeys.cs Raccourcis clavier globaux
  SettingsWindow.xaml   Fenêtre de réglages
  Settings/             Modèle et enregistrement des réglages
  Controls/             Courbe de ressort, texte défilant, égaliseur, jauges, icônes météo
  Widgets/              Pages de la capsule : lecteur, mails, météo, volume, minuteur, note…
  Services/             GSMTC, Core Audio, Bluetooth, mails, météo, écrans, mises à jour, journal
  Interop/              Déclarations Win32 et COM
tests/Ilot.Tests/       Tests des services
```

## Choix techniques

- **Fenêtre de taille fixe :** seule la capsule s'anime à l'intérieur, ce qui évite tout sautillement. Les pixels transparents laissent passer les clics.
- **Rendu logiciel :** il réduit fortement la mémoire utilisée (voir `App.xaml.cs`).
- **Translucidité simulée :** un vrai effet Acrylic ou Mica est incompatible avec une fenêtre à transparence par pixel et des coins de capsule. Le panneau utilise donc un fond sombre légèrement translucide, sans flou.
- **Écriture des fichiers :** les réglages, la note et les identifiants passent par `AtomicFile`, pour qu'une coupure de courant laisse l'ancienne version intacte.
- **Traduction :** le texte français sert de clé (`Loc.T("...")` et XAML), et `LocEnglish.cs` donne l'anglais. Un test échoue si un texte n'y figure pas.

## Publier une version

1. Augmentez `<Version>` dans `Ilot.csproj` et poussez le commit.
2. Lancez `git tag v1.7.0`, puis `git push origin v1.7.0`, avec le même numéro.

GitHub Actions lance les tests, construit l'installateur et publie la Release. Les copies installées la trouvent en moins de 6 heures et la proposent dans une bannière : elle ne s'installe que sur un clic, car Windows peut refuser de lancer un exécutable non signé.
