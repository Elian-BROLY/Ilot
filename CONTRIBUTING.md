# Contribuer à Ilot

Les bugs et les idées se signalent dans les [issues](https://github.com/Elian-BROLY/Ilot/issues). Pour un bug, joignez le contenu de `app.log`, accessible depuis les réglages, onglet **À propos**.

## Préparer son poste

Il faut le SDK .NET 10 et Windows 10 version 2004 ou plus récent.

```
dotnet run --project src\Ilot
dotnet test tests\Ilot.Tests
```

Le [Contrôle intelligent des applications](README.md#installer) de Windows peut refuser de lancer un build local non signé. Les tests tournent aussi sur GitHub à chaque push.

## Règles du projet

- **Mémoire et CPU :** une page masquée ne consomme rien, et aucune requête réseau n'est envoyée sans raison. L'application doit rester légère au repos.
- **Vie privée :** rien n'est envoyé à un service sans que ce soit indiqué dans les réglages. Le journal ne contient jamais le contenu d'un mail ou d'une note.
- **Style :** suivez le code qui entoure votre modification. Les commentaires et les textes de l'interface sont en français.
- **Tests :** ajoutez un test pour toute logique qui ne dépend pas de l'interface.

## Architecture

Voir [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).
