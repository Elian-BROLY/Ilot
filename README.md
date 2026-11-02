# Ilot

[![CI](https://github.com/Elian-BROLY/Ilot/actions/workflows/ci.yml/badge.svg)](https://github.com/Elian-BROLY/Ilot/actions/workflows/ci.yml)
[![Licence : MIT](https://img.shields.io/badge/licence-MIT-blue.svg)](LICENSE)

Français | [English](README.en.md)

Une capsule en haut de l'écran de Windows 11, inspirée de la pastille de l'iPhone. Elle s'ouvre au survol et donne accès au lecteur média, aux mails, à la météo, au volume, à la batterie et aux AirPods. Ce projet n'a aucun lien avec Apple.

![Démonstration d'Ilot](docs/demo.gif)


## Installer

Téléchargez `Ilot-win-Setup.exe` depuis les [Releases](https://github.com/Elian-BROLY/Ilot/releases). Il installe aussi le runtime .NET 10 Desktop s'il manque, ce qui demande une fois les droits administrateur. L'application télécharge ensuite les mises à jour toute seule, et les installe quand vous cliquez sur sa bannière.

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
| Molette sur la capsule fermée ou la ligne du bas du panneau | Règle le volume (à désactiver dans les réglages) |
| Glisser un fichier sur la capsule | Le garde dans la page Dépôt de fichiers |
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

Ces raccourcis se changent dans les réglages. Au premier lancement, un assistant explique les gestes et demande la langue, la ville, le lecteur et les pages. Les réglages sont enregistrés dans `%APPDATA%\Ilot\settings.json`. Le nom de la ville est envoyé au service [Open-Meteo](https://open-meteo.com/).

## Pages et indicateurs

- **Agenda** : les rendez-vous d'aujourd'hui et de demain, un décompte dans la capsule et un rappel 5 minutes avant, avec un clic pour rejoindre la visio. Il lit Google Agenda ou Outlook en lecture seule, après une nouvelle connexion du compte. Page masquée : aucune requête.
- **Dépôt de fichiers** : glissez un fichier sur la capsule, reprenez-le où vous voulez. Seul son chemin est gardé, jusqu'à la fermeture de l'application.
- **Lecteur** : le lecteur par défaut (à choisir dans les réglages) est affiché dès qu'il est ouvert, même en pause, sauf si l'option « afficher celui qui joue » est cochée ; sinon, le lecteur désigné par Windows. Si plusieurs lecteurs sont ouverts, le bouton à droite des commandes passe au suivant, et ce choix tient jusqu'à la fermeture du lecteur choisi ou au redémarrage de l'application. Windows n'indique que l'application : YouTube, ou Deezer ouvert dans un site web, apparaît sous le nom du navigateur (Chrome, Edge…), et seul Deezer installé en application est reconnu comme lecteur par défaut.
- **Changement de morceau** : une petite bannière affiche le titre et l'artiste quand le morceau suivant du lecteur par défaut commence. Windows et Deezer ne donnent pas la file d'attente : le titre ne peut pas être connu à l'avance.
- **Volume par application** : sur la sortie par défaut, jusqu'à 4 applications.
- **Système** : CPU, mémoire et débit réseau, mis à jour seulement quand la page est affichée.
- **Mode concentration** : pendant un minuteur (pause comprise), les notifications et mails ne s'affichent plus en bannière, et un compte s'affiche à la fin. Les alertes de batterie et d'AirPods restent actives. Pour que rien n'apparaisse non plus en bas à droite, gardez « Ne pas déranger » activé (`Win + N`).
- **Écrans** : la capsule s'affiche sur l'écran choisi dans les réglages, ou passe sur celui où se trouve la souris.
- **Sortie audio** : un clic bascule la sortie par défaut. Le bouton casque de la ligne du volume y mène directement. Windows n'ayant pas d'API publique pour ça, l'application utilise l'interface non documentée `IPolicyConfig`.
- **Minuteur** : 5, 25 ou 45 min, ou Pomodoro, à activer dans les réglages (25 min de travail, 5 min de pause, 15 min toutes les 4 séances, avec la musique en pause pendant les pauses) ; le décompte reste visible dans la capsule compacte, à la place de l'égaliseur, et continue après un redémarrage de l'application.
- **Note rapide** : jusqu'à 5 notes, avec des cases à cocher, enregistrées automatiquement, en clair, dans `%APPDATA%\Ilot\note.txt`, `note2.txt`…
- **Casque** : la ligne du bas affiche les AirPods ou un autre casque Bluetooth, au choix ou masquée. Un casque autre que des AirPods n'a qu'une jauge, si Windows connaît sa batterie.
- **Langue** : français ou anglais.
- **Animations** : cinq styles au choix (classique, rebond, fluide, glissé, fondu), à essayer directement depuis les réglages.
- **Presse-papiers** (masquée par défaut) : les 10 derniers éléments copiés, dont 3 images au plus ; un clic les recopie. Tout reste en mémoire et disparaît à la fermeture de l'application. Les copies qu'un gestionnaire de mots de passe exclut de l'historique de Windows en sont aussi exclues.
- **Raccourcis** (masquée par défaut) : Wi-Fi, Bluetooth, capture d'écran et verrouillage. Le mode avion ouvre les paramètres de Windows, aucune application ne pouvant le changer elle-même.
- **Micro, caméra et écran** : point orange pour le micro, vert pour la caméra, bleu quand une application partage ou enregistre l'écran avec la capture de Windows (visioconférences, navigateurs).
- **Alertes batterie** : PC à 20 % et 10 %, AirPods à 20 % (contrôle toutes les 5 min tant qu'ils sont connectés), chargeur branché ou débranché.
- **Téléchargements** : une flèche dans la capsule pendant un téléchargement du navigateur, puis une bannière qui ouvre le dossier. Windows ne donne pas la progression d'un téléchargement ni d'une copie de fichiers.
- **Verr. Maj et micro** : une courte bannière quand Verr. Maj ou le micro par défaut changent d'état.
- **Capsule compacte** : la température, le nombre de mails, le casque, l'égaliseur et le compte à rebours de l'agenda s'affichent chacun au choix. La température cède sa place si les autres indicateurs en ont besoin.
- **Heure** : au format de Windows, 24 h ou 12 h.

Tout se désactive dans les réglages.

## Notifications Windows

Les notifications de Windows s'affichent dans une bannière de la capsule, l'une après l'autre (sauf en plein écran). Dans les réglages, chaque application peut être coupée, et des heures de silence masquent notifications, mails et alertes sur une plage ; les minuteurs et une batterie presque vide s'affichent quand même. Pour ne plus les voir aussi en bas à droite, activez « Ne pas déranger » (`Win + N`) : Windows ne permet pas à une application de le faire elle-même. Un clic sur la bannière ouvre l'application : Windows ne donne accès ni aux boutons de la notification, ni à son champ de réponse.

## Mails

Dans les réglages, onglet **Mails** : collez vos identifiants OAuth puis cliquez sur **Connecter**. Les cases **Afficher Outlook** et **Afficher Gmail** masquent un compte de la capsule sans le déconnecter.

### Le plus simple : IMAP

Sans projet Google ou Azure : indiquez votre adresse et un mot de passe d'application créé dans la sécurité de votre compte (Gmail, Yahoo, iCloud…). Rien n'est supprimé définitivement : la corbeille déplace le mail, et échoue si le serveur n'en a pas. Microsoft n'accepte plus cette méthode pour Outlook, et l'agenda demande une connexion Google ou Outlook.

### Créer ses identifiants Google

1. Sur la [console Google Cloud](https://console.cloud.google.com/), créez un projet.
2. Dans **API et services > Bibliothèque**, activez l'**API Gmail**, et l'**API Google Agenda** si vous voulez la page Agenda.
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
