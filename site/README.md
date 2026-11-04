# Site de téléchargement

Page statique (HTML, CSS, JS, sans dépendance) qui propose l'installateur de la dernière release GitHub.

- Le bouton pointe vers `releases/latest/download/Ilot-win-Setup.exe` : il suit chaque nouvelle version sans toucher au site.
- La version, la date et la taille sont lues sur l'API GitHub au chargement. Si elle ne répond pas, le lien marche quand même.
- Français ou anglais, selon la langue du navigateur ou le bouton du menu.
- Le dossier est autonome : les captures de `docs/` sont téléchargées depuis GitHub au build.

## Héberger

Depuis ce dossier :

```sh
docker compose up -d --build
```

Le site écoute sur le port 8097. `/download` redirige directement vers l'installateur.

Après un `git pull`, relancer la même commande. Une nouvelle release ne demande rien : seules les modifications du site ou des captures demandent un rebuild.
