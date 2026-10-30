# Ilot

[![CI](https://github.com/Elian-BROLY/Ilot/actions/workflows/ci.yml/badge.svg)](https://github.com/Elian-BROLY/Ilot/actions/workflows/ci.yml)
[![Licence : MIT](https://img.shields.io/badge/licence-MIT-blue.svg)](LICENSE)

Français | [English](README.en.md)

Une capsule en haut de l'écran de Windows 11, inspirée de la pastille de l'iPhone. Elle s'ouvre au survol et donne accès au lecteur média, aux mails, à la météo, au volume, à la batterie et aux AirPods. Ce projet n'a aucun lien avec Apple.

![Démonstration d'Ilot](docs/demo.gif)


## Installer

Téléchargez `Ilot-win-Setup.exe` depuis les [Releases](https://github.com/Elian-BROLY/Ilot/releases). Il installe aussi le runtime .NET 10 Desktop s'il manque, ce qui demande une fois les droits administrateur. L'application se met ensuite à jour toute seule.

L'exécutable n'est pas signé. Windows peut donc afficher un avertissement SmartScreen, et le **Contrôle intelligent des applications**, s'il est activé, peut refuser de lancer une version : Windows décide selon la réputation de chaque fichier.

La désinstallation retire le démarrage automatique, les identifiants mail, les réglages et le journal. Seule la note reste, dans `%APPDATA%\Ilot\note.txt`.

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
| Clic sur l'icône de la zone de notification | Ouvre les réglages, comme relancer l'application |
| Clic droit sur l'icône de la zone de notification | Menu « Réglages… » et « Quitter » |

| Raccourci | Effet |
| --- | --- |
| `Ctrl + Alt + N` | Ouvre ou réduit la capsule |
| `Ctrl + Alt + ↑` ou `↓` | Change de page, panneau ouvert |
| `Ctrl + Alt + Maj + Espace` | Lecture ou pause |
| `Ctrl + Alt + Maj + →` ou `←` | Morceau suivant ou précédent |

Les réglages sont enregistrés dans `%APPDATA%\Ilot\settings.json`. La météo affiche Strasbourg par défaut ; la ville se change dans les réglages et son nom est envoyé au service [Open-Meteo](https://open-meteo.com/).

## Pages et indicateurs

- **Lecteur** : le lecteur par défaut (Deezer, à changer dans les réglages) est affiché dès qu'il est ouvert, même en pause, sauf si l'option « afficher celui qui joue » est cochée ; sinon, le lecteur désigné par Windows. Si plusieurs lecteurs sont ouverts, le bouton à droite des commandes passe au suivant, et ce choix tient jusqu'à la fermeture du lecteur choisi ou au redémarrage de l'application. Windows n'indique que l'application : YouTube, ou Deezer ouvert dans un site web, apparaît sous le nom du navigateur (Chrome, Edge…), et seul Deezer installé en application est reconnu comme lecteur par défaut.
- **Changement de morceau** : une petite bannière affiche le titre et l'artiste quand le morceau suivant du lecteur par défaut commence. Windows et Deezer ne donnent pas la file d'attente : le titre ne peut pas être connu à l'avance.
- **Volume par application** : sur la sortie par défaut, jusqu'à 4 applications.
- **Système** : CPU, mémoire et débit réseau, mis à jour seulement quand la page est affichée.
- **Mode concentration** : pendant un minuteur (pause comprise), les notifications et mails ne s'affichent plus en bannière, et un compte s'affiche à la fin. Les alertes de batterie et d'AirPods restent actives. Pour que rien n'apparaisse non plus en bas à droite, gardez « Ne pas déranger » activé (`Win + N`).
- **Écrans** : la capsule s'affiche sur l'écran choisi dans les réglages, ou passe sur celui où se trouve la souris.
- **Sortie audio** : un clic bascule la sortie par défaut. Le bouton casque de la ligne du volume y mène directement. Windows n'ayant pas d'API publique pour ça, l'application utilise l'interface non documentée `IPolicyConfig`.
- **Minuteur** : 5, 25 ou 45 min ; le décompte reste visible dans la capsule compacte, à la place de l'égaliseur, et continue après un redémarrage de l'application.
- **Note rapide** : enregistrée automatiquement, en clair, dans `%APPDATA%\Ilot\note.txt`.
- **Micro et caméra** : point orange pour le micro, vert pour la caméra.
- **Alertes batterie** : PC à 20 % et 10 %, AirPods à 20 % (contrôle toutes les 5 min tant qu'ils sont connectés), chargeur branché ou débranché.
- **Température** : affichée à côté de l'heure en mode compact.

Tout se désactive dans les réglages.

## Notifications Windows

Les notifications de Windows s'affichent dans une bannière de la capsule, l'une après l'autre (sauf en plein écran). Pour ne plus les voir aussi en bas à droite, activez « Ne pas déranger » (`Win + N`) : Windows ne permet pas à une application de le faire elle-même. Un clic sur la bannière ouvre l'application : Windows ne donne accès ni aux boutons de la notification, ni à son champ de réponse.

## Mails

Dans les réglages, onglet **Mails** : collez vos identifiants OAuth puis cliquez sur **Connecter**. Les cases **Afficher Outlook** et **Afficher Gmail** masquent un compte de la capsule sans le déconnecter.

### Créer ses identifiants Google

1. Sur la [console Google Cloud](https://console.cloud.google.com/), créez un projet.
2. Dans **API et services > Bibliothèque**, activez l'**API Gmail**.
3. Dans **Écran de consentement OAuth**, choisissez le type **Externe**, ajoutez votre adresse comme utilisateur test, puis passez l'application **en production**. En mode test, Google retire l'autorisation au bout de 7 jours. En production, un écran d'avertissement s'affiche à la connexion, car l'application n'est pas vérifiée par Google.
4. Dans **Identifiants**, créez un **ID client OAuth** de type **Application de bureau**.
5. Collez l'ID client et le code secret dans l'onglet **Mails**.

### Créer ses identifiants Outlook

1. Sur le [portail Azure](https://portal.azure.com/), ouvrez **Inscriptions d'applications** et créez une application pour les comptes personnels et professionnels.
2. Dans **Authentification**, ajoutez la plateforme **Applications mobiles et de bureau**, avec l'URI de redirection `http://localhost`.
3. Copiez l'**ID d'application (client)** dans l'onglet **Mails**.

### Fonctionnement

Sur la page Mails, un clic sur un mail l'ouvre dans le navigateur. Au survol, la corbeille place le mail, en un clic, dans la corbeille du service, d'où il reste récupérable.

Quand un seul compte est à la fois affiché et connecté, 4 mails sont visibles d'un coup ; la flèche de l'en-tête passe aux 4 suivants (jusqu'à 20 non lus). Avec plusieurs comptes, chacun garde sa ligne, et la flèche qui apparaît au survol passe au non lu suivant. Un second compte Gmail se connecte avec les mêmes identifiants que le premier.

À l'arrivée d'un mail, une bannière s'affiche quelques secondes avec l'expéditeur et le sujet (boîte relue chaque minute) ; un clic ouvre le mail, et la survoler la maintient ouverte. Elle se désactive dans les réglages et ne s'affiche ni panneau ouvert, ni en plein écran.

L'application n'affiche que le nombre de non lus, l'expéditeur et le sujet. La mise à la corbeille impose toutefois une autorisation large (`gmail.modify`, `Mail.ReadWrite`), qui donne techniquement accès au contenu de la boîte. Identifiants et autorisations sont chiffrés avec DPAPI dans `%APPDATA%\Ilot\mail.dat`.

Une bannière apparaît à chaque connexion ou déconnexion des AirPods quand le panneau est fermé. La batterie (G : gauche, D : droite, B : boîtier) provient d'annonces Bluetooth non documentées par Apple : elle est indicative, par pas de 10 %. L'écoute Bluetooth ne tourne que lorsque la batterie est affichée.

## Dépannage

Les erreurs techniques sont notées dans `%APPDATA%\Ilot\app.log`, jamais le contenu des mails ou de la note. Le bouton **Ouvrir le journal** se trouve dans l'onglet **À propos** des réglages.

## Développer

Prérequis : SDK .NET 10.

| Commande | Effet |
| --- | --- |
| `dotnet run --project src\Ilot` | Lance l'application |
| `dotnet test tests\Ilot.Tests` | Lance les tests |
| `publish.cmd` | Génère `dist\` |
| `make-installer.cmd` | Génère `installer\Ilot-win-Setup.exe` |

Pour contribuer, voir [CONTRIBUTING.md](CONTRIBUTING.md). Pour la structure du code et la publication, voir [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Licence

[MIT](LICENSE)
