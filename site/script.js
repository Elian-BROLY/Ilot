// Langue (FR/EN) et infos de la dernière version, lues sur l'API GitHub.
const REPO = 'Elian-BROLY/Ilot';
const ASSET = 'Ilot-win-Setup.exe';

const EN = {
  'nav.features': 'Features',
  'nav.install': 'Install',
  'demo.title': 'Demo title',
  'demo.artist': 'Demo artist',
  'demo.alt': 'Ilot demo',
  'hero.title': "The iPhone's island, on Windows.",
  'hero.lead': 'A capsule at the top of your screen that opens on hover: music, mail, weather, volume, battery, AirPods, notifications. Free and open source.',
  'cta.download': 'Download for Windows',
  'cta.source': 'Source code',
  'cta.meta': 'Windows 10 (2004) or 11, 64-bit · Free · MIT license',
  'cta.releases': 'Release notes',
  'features.title': 'Everything you need, at a glance',
  'f.player.t': 'Media player',
  'f.player.d': 'Spotify, Deezer, browsers: title, artwork, progress bar and controls, with a banner on every new track.',
  'f.mail.t': 'Mail',
  'f.mail.d': 'Gmail, Outlook or any IMAP account: unread count, a banner on arrival, one-click trash.',
  'f.notif.t': 'Notifications',
  'f.notif.d': 'Windows notifications show up in the capsule, with per-app filtering and quiet hours.',
  'f.agenda.t': 'Calendar',
  'f.agenda.d': "Today's meetings, a countdown in the capsule and one click to join the call.",
  'f.audio.t': 'Volume and audio output',
  'f.audio.d': 'Per-app volume, one-click output switching, scroll on the capsule.',
  'f.airpods.t': 'AirPods and battery',
  'f.airpods.d': 'AirPods and PC battery, alerts at 20% and 10%, charger plugged in or out.',
  'f.focus.t': 'Timer and focus',
  'f.focus.d': '5, 25 or 45 min, or Pomodoro, with music paused during breaks and banners muted.',
  'f.more.t': 'And more',
  'f.more.d': 'Weather, notes, file shelf, clipboard, mic and camera indicators, five animation styles.',
  'shot.player': 'The player',
  'shot.mail': 'Mail',
  'shot.banner': 'The track banner',
  'install.title': 'Install',
  'install.s1.t': 'Download the installer.',
  'install.s1.d': 'It also installs the .NET 10 Desktop runtime if missing, which needs admin rights once.',
  'install.s2.t': 'Get past the Windows warning.',
  'install.s2.d': 'The executable is not signed, so SmartScreen may show "Windows protected your PC". Click "More info", then "Run anyway". If Smart App Control is on, it may block some versions.',
  'install.s3.t': "That's it.",
  'install.s3.d': 'A short guide explains the gestures on first launch. Updates download on their own and install when you click their banner.',
  'install.portable': 'No install:',
  'install.portableLink': 'portable version (.zip)',
  'install.uninstall': 'Uninstalling removes autostart, mail credentials, settings and the log. Only the note stays, in %APPDATA%\\Ilot\\note.txt.',
  'footer.issues': 'Report a bug',
  'footer.license': 'MIT license',
  'footer.apple': 'This project is not affiliated with Apple.',
};

const FR = {};
let lang = 'fr';
let release = null;

function store(key, value) {
  try { localStorage.setItem(key, value); } catch {}
}
function read(key) {
  try { return localStorage.getItem(key); } catch { return null; }
}

// Garde le texte français d'origine pour pouvoir y revenir.
function snapshotFrench() {
  document.querySelectorAll('[data-i18n]').forEach(el => { FR[el.dataset.i18n] = el.textContent; });
  document.querySelectorAll('[data-i18n-alt]').forEach(el => { FR[el.dataset.i18nAlt] ??= el.alt; });
}

function t(key) { return (lang === 'en' ? EN : FR)[key] ?? FR[key] ?? ''; }

function applyLang() {
  document.documentElement.lang = lang;
  document.querySelectorAll('[data-i18n]').forEach(el => { el.textContent = t(el.dataset.i18n); });
  document.querySelectorAll('[data-i18n-alt]').forEach(el => { el.alt = t(el.dataset.i18nAlt); });
  const btn = document.getElementById('lang');
  btn.textContent = lang === 'en' ? 'FR' : 'EN';
  btn.setAttribute('aria-label', lang === 'en' ? 'Passer en français' : 'Switch to English');
  renderRelease();
}

function formatSize(bytes) {
  return (bytes / 1048576).toLocaleString(lang, { maximumFractionDigits: 1 }) + (lang === 'en' ? ' MB' : ' Mo');
}

function renderRelease() {
  if (!release) return;
  const date = new Date(release.published_at).toLocaleDateString(lang, { day: 'numeric', month: 'long', year: 'numeric' });
  const parts = [`${release.tag_name}`, date];
  if (release.asset) parts.push(formatSize(release.asset.size));
  parts.push(lang === 'en' ? 'Windows 10 (2004) or 11, 64-bit' : 'Windows 10 (2004) ou 11, 64 bits');
  document.getElementById('meta').textContent = parts.join(' · ');
}

async function loadRelease() {
  try {
    const res = await fetch(`https://api.github.com/repos/${REPO}/releases/latest`, {
      headers: { Accept: 'application/vnd.github+json' },
    });
    if (!res.ok) return; // Les liens /releases/latest/download fonctionnent sans l'API.
    const data = await res.json();
    release = { ...data, asset: data.assets?.find(a => a.name === ASSET) };
    document.getElementById('releases').href = data.html_url;
    renderRelease();
  } catch {}
}

snapshotFrench();
const saved = read('lang');
lang = saved || (navigator.language?.toLowerCase().startsWith('fr') ? 'fr' : 'en');
if (lang !== 'fr') applyLang();

document.getElementById('lang').addEventListener('click', () => {
  lang = lang === 'fr' ? 'en' : 'fr';
  store('lang', lang);
  applyLang();
});

// Sur écran tactile, un appui ouvre la capsule.
document.querySelector('.island').addEventListener('click', e => e.currentTarget.classList.toggle('open'));

loadRelease();
