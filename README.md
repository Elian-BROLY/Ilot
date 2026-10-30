# Dynamic-Island

Dynamic Island pour Windows 11 : une capsule en haut de l'écran qui s'ouvre au survol et donne accès au lecteur média, à la météo, au volume, à la batterie et aux AirPods.

## Lancer

```
dist\Dynamic-Island.exe
```

L'exécutable est autonome, mais il doit rester à côté des autres fichiers de `dist\`.

## Utilisation

| Geste | Effet |
| --- | --- |
| Survoler la capsule | Ouvre le panneau |
| Quitter la capsule | Referme le panneau après 300 ms |
| Cliquer sur la capsule | Verrouille le panneau ouvert ; un second clic le déverrouille |
| Glisser la capsule latéralement | La déplace le long du bord haut de l'écran |
| Molette, ou points à droite du panneau | Change de page |
| Barre de progression du lecteur | Déplace la position de lecture, si l'application le permet |
| Bouton Connecter / Déconnecter de la ligne AirPods | Connecte ou déconnecte les AirPods déjà appairés à Windows |
| Clic droit sur l'icône de la zone de notification | Menu « Réglages… » et « Quitter » |

Les réglages sont enregistrés dans `%APPDATA%\Dynamic-Island\settings.json`. La météo affiche Strasbourg par défaut ; la ville se change dans les réglages et son nom est envoyé au service [Open-Meteo](https://open-meteo.com/).

## Installer

`make-installer.cmd` génère `installer\Dynamic-Island-win-Setup.exe`. L'installation se fait par utilisateur, sans droits admin : menu Démarrer, bureau, désinstallation depuis les Paramètres de Windows. Pour une mise à jour, augmentez `<Version>` dans `Dynamic-Island.csproj`, relancez le script et exécutez le nouveau Setup. L'exécutable n'est pas signé : Windows peut afficher un avertissement SmartScreen sur un fichier téléchargé.

## Pages et indicateurs

- **Lecteur** : Deezer est affiché par défaut dès qu'il est ouvert, même en pause ; sinon, le lecteur désigné par Windows. Si plusieurs lecteurs sont ouverts, le bouton à droite des commandes passe au suivant, et ce choix tient jusqu'à la fermeture du lecteur choisi ou au redémarrage de l'application. Windows n'indique que l'application : YouTube, ou Deezer ouvert dans un site web, apparaît sous le nom du navigateur (Chrome, Edge…), et seul Deezer installé en application est reconnu comme lecteur par défaut.
- **Paroles** : LRCLIB, gratuit et sans clé. L'artiste et le titre ne sont envoyés que si la page est affichée.
- **Volume par application** : sur la sortie par défaut, jusqu'à 4 applications.
- **Système** : CPU, mémoire et débit réseau, mis à jour seulement quand la page est affichée.
- **Mode concentration** : pendant un minuteur (pause comprise), les notifications et mails ne s'affichent plus en bannière, et un compte s'affiche à la fin. Les alertes de batterie et d'AirPods restent actives. Pour que rien n'apparaisse non plus en bas à droite, gardez « Ne pas déranger » activé (`Win + N`).

- **Sortie audio** : un clic bascule la sortie par défaut. Le bouton casque de la ligne du volume y mène directement. Windows n'ayant pas d'API publique pour ça, l'application utilise l'interface non documentée `IPolicyConfig`.
- **Minuteur** : 5, 25 ou 45 min ; le décompte reste visible dans la capsule compacte, à la place de l'égaliseur.
- **Note rapide** : enregistrée automatiquement, en clair, dans `%APPDATA%\Dynamic-Island\note.txt`.
- **Micro et caméra** : point orange pour le micro, vert pour la caméra.
- **Alertes batterie** : PC à 20 % et 10 %, AirPods à 20 % (contrôle toutes les 5 min tant qu'ils sont connectés), chargeur branché ou débranché.
- **Raccourci** : `Ctrl + Alt + N` ouvre et réduit la capsule.
- **Température** : affichée à côté de l'heure en mode compact.

Tout se désactive dans les réglages.

## Notifications Windows

Les notifications de Windows s'affichent dans une bannière de la capsule, l'une après l'autre (sauf en plein écran). Pour ne plus les voir aussi en bas à droite, activez « Ne pas déranger » (`Win + N`) : Windows ne permet pas à une application de le faire elle-même. Un clic sur la bannière ouvre l'application : Windows ne donne accès ni aux boutons de la notification, ni à son champ de réponse.

## Mails

Dans les réglages, section **Mails** : collez vos identifiants OAuth puis cliquez sur **Connecter**. Les cases **Afficher Outlook** et **Afficher Gmail** masquent un compte de la capsule sans le déconnecter.

Sur la page Mails, un clic sur un mail l'ouvre dans le navigateur. Au survol, la corbeille place le mail, en un clic, dans la corbeille du service, d'où il reste récupérable.

Quand un seul compte est à la fois affiché et connecté, 4 mails sont visibles d'un coup ; la flèche de l'en-tête passe aux 4 suivants (jusqu'à 20 non lus). Avec deux comptes, chacun garde sa ligne, et la flèche qui apparaît au survol passe au non lu suivant.

À l'arrivée d'un mail, une bannière s'affiche quelques secondes avec l'expéditeur et le sujet (boîte relue chaque minute) ; un clic ouvre le mail, et la survoler la maintient ouverte. Elle se désactive dans les réglages et ne s'affiche ni panneau ouvert, ni en plein écran.

L'application n'affiche que le nombre de non lus, l'expéditeur et le sujet. La mise à la corbeille impose toutefois une autorisation large (`gmail.modify`, `Mail.ReadWrite`), qui donne techniquement accès au contenu de la boîte. Identifiants et autorisations sont chiffrés avec DPAPI dans `%APPDATA%\Dynamic-Island\mail.dat`.

Une bannière apparaît à chaque connexion ou déconnexion des AirPods quand le panneau est fermé. La batterie (G : gauche, D : droite, B : boîtier) provient d'annonces Bluetooth non documentées par Apple : elle est indicative, par pas de 10 %. L'écoute Bluetooth ne tourne que lorsque la batterie est affichée.

## Compiler

Prérequis : SDK .NET 10.

```
publish.cmd
```

Le résultat est généré dans `dist\`. Pour le développement : `dotnet run --project src\Dynamic-Island`.

## Structure

```
src/Dynamic-Island/
  App.xaml(.cs)         Démarrage, instance unique, mode de rendu
  MainWindow.xaml(.cs)  Capsule : position, états, pages, animations, survol, masquage en plein écran
  SettingsWindow.xaml   Fenêtre de réglages
  Settings/             Modèle et enregistrement des réglages
  Controls/             Courbe de ressort, texte défilant, égaliseur, jauges, icônes météo
  Widgets/              Média, météo, volume et batterie, AirPods
  Services/             GSMTC, Core Audio, Bluetooth, météo, écrans, démarrage auto, icône de notification
  Interop/              Déclarations Win32 et COM
```

## Choix techniques

- **Fenêtre de taille fixe** : seule la capsule s'anime à l'intérieur, ce qui évite tout sautillement. Les pixels transparents laissent passer les clics.
- **Rendu logiciel** : il réduit fortement la mémoire utilisée (voir `App.xaml.cs`).
- **Translucidité simulée** : un vrai effet Acrylic ou Mica est incompatible avec une fenêtre à transparence par pixel et des coins de capsule. Le panneau utilise donc un fond sombre légèrement translucide, sans flou.
