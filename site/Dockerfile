# Autonome : ce dossier suffit. Les captures sont prises dans docs/ sur GitHub
# au moment du build, pour ne pas les dupliquer dans le dépôt.
FROM nginx:1.27-alpine
ARG DOCS=https://raw.githubusercontent.com/Elian-BROLY/Ilot/main/docs
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY index.html style.css script.js favicon.svg /usr/share/nginx/html/
RUN mkdir -p /usr/share/nginx/html/assets
ADD --chmod=644 ${DOCS}/demo.gif ${DOCS}/lecteur.png ${DOCS}/mails.png ${DOCS}/banniere-morceau.png /usr/share/nginx/html/assets/
