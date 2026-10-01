# Macrofy website

The marketing site for Macrofy. It's plain HTML, CSS and JS with no build step, so
whatever is in this folder is exactly what gets served.

```
website/
  index.html          the landing page
  privacy.html        privacy page
  assets/css/style.css
  assets/js/main.js   nav, FAQ, the animated hero keyboard and scroll animations
  assets/img/         logo + app screenshots
```

Animations use GSAP (core, ScrollTrigger and SplitText, all free) loaded from jsDelivr. If
the CDN is blocked or the visitor has reduced motion turned on, the page still shows
everything, just without the motion.

## Preview it locally

```
python -m http.server 5178 --directory website --bind 127.0.0.1
```

Then open http://localhost:5178. Opening `index.html` straight from disk mostly works too,
but a local server is closer to the real thing.

## Put it online

Any static host works. Point it at this repo, leave the build command empty and set the
output (publish) folder to `website`:

- **Cloudflare Pages** or **Netlify**: connect the GitHub repo, build command empty,
  output directory `website`. Both are free for a site like this and give you a custom
  domain with HTTPS.
- **GitHub Pages**: Pages only serves the repo root or `/docs` from a branch, so for this
  folder you'd add a small Actions workflow that uploads `website/` as the Pages artifact
  and deploys it (Settings > Pages > Source: GitHub Actions).

Once it has a real domain, change the `og:image` meta tag in `index.html` to a full URL
(for example `https://macrofy.app/assets/img/app-keyboards.png`). Link previews in
Discord, X and so on need an absolute URL there.

## Things to update

- **Download buttons** point to
  `https://github.com/UltimaCodes/Macrofy/releases/latest/download/Macrofy-win-Setup.exe`.
  That's the file the release workflow uploads, so the link works as soon as the first
  release (tag `v1.1.0`) is published, and it always serves the newest one after that. Until
  then it 404s.
- **Version number** in the hero (`Version 1.1.0` in `index.html`) is typed in by hand. Bump
  it when you release.
- **Pro waitlist**: the "Follow along on GitHub" button (`.js-waitlist` in `index.html`) just
  links to the repo for now. Swap the `href` for a real signup form (Tally, Google Forms,
  Buttondown, etc.) when you want to collect emails.
- **Hardware prices** in the comparison table and the line under the hero are Elgato's
  list prices from October 2026. Recheck them now and then.
- **Screenshots** in `assets/img` are real captures of the app. Retake them if the UI
  changes a lot.
