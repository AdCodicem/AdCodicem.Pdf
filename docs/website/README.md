# The documentation site

A Docusaurus site that publishes the user documentation (`docs/`, versioned) and the project documents
(`../`, copied into `project/` at build time). What it serves, when it is deployed and how a page is changed
are in [`docs/releasing.md`](../releasing.md#the-documentation-site); this file covers working on the site
itself.

```bash
npm ci
npm start          # generates the API reference and the project documents, then serves with live reload
npm run build      # the same generation, the build, then scripts/check-site.mjs over the built pages
npm run serve      # serves the last build, redirects and search included
```

`npm start` and `npm run build` regenerate the API reference with DocFX, which needs the .NET SDK of
`global.json`.

## Look and feel

The site wears the AdCodicem design system: its tokens, its three typefaces, its syntax palette, the Pdf mark.
Nothing is fetched from it at build time; what the site needs was copied in, so a change to the design system
reaches the site only by being copied again.

- **`src/css/custom.css`** declares the design system's color tokens by name, primitives (`--ink-900`,
  `--rust-600`, `--sage-400`, …) then semantic ones (`--bg-page`, `--fg-1`, `--accent`, `--code-type`, …), and maps
  Infima's variables onto the semantic ones. Every rule after the tokens uses them only, never a literal color, so
  it follows all four color schemes: paper (`:root`) and ink (`data-theme='dark'`, the default when the system
  expresses no preference), each in its brand form and in its accessible one (`data-contrast='accessible'`), which
  reaches WCAG 2 AA on every pair drawn and changes only the tokens the design system lists. The selectors start
  with `html`: Infima redefines its variables under `html[data-theme='dark']`, which `:root` alone loses to.
- **The theme toggle** is an ejected `ColorModeToggle`, a safe swizzle: the design system's icon button, showing the
  moon on paper and the sun on ink. It switches between the two themes; until the reader uses it, the site follows
  the system's preference. The NuGet and GitHub links in the navbar take the same look, through `navbar-icon-link`,
  with the design system's `nuget` and `github` icons drawn as CSS masks.
- **The contrast toggle** (`src/components/ContrastToggle`) sits beside it. It stores the reader's choice under
  `adcodicem-contrast`; the `contrast` plugin in `docusaurus.config.js` applies it before the first paint, and
  before the reader chooses it follows `prefers-contrast: more`.
- **`src/prism-adcodicem.js`** is one Prism theme for every scheme: its colors are the `--code-*` tokens, so the
  accessible schemes retune the comments without a second theme.
- **`src/fonts/`** holds Archivo, Atkinson Hyperlegible Next and JetBrains Mono as the design system's variable
  `woff2` files (the JetBrains Mono italic is static), under the SIL Open Font License (`OFL.txt`). They sit under
  `src/`, not `static/`, so webpack fingerprints them and their address follows `baseUrl`.
- **Admonitions** have a tinted ground and a full hairline, no left bar. Their heading is reached as
  `.theme-admonition > div:first-child`, since its own class is a CSS-module hash.
- **`static/img/`**: the navbar carries the Pdf lockup in its compact drawing (`pdf-logo-horizontal-compact`, the
  design system's drawing for a symbol of 24 to 48px), at 34px, 24px in the mobile sidebar; the footer the AdCodicem
  lockup at 48px, where the full codex applies. Each has a `-light` and a `-dark` file, and their names are outlined,
  so they render in Archivo inside an `<img>`. The favicons are the design system's Pdf pack, with
  `site.webmanifest`. The navbar's blurred ground sits on a `::before`: a `backdrop-filter` on `.navbar` itself would
  become the containing block of the mobile sidebar.

### The homepage and the 404 page

Both follow the design system's docs kit. The documentation lives under `/docs`; the redirects plugin keeps every
address the site had while it served the documentation at its root.

- **The homepage** (`src/pages/index.js`) shows a code example that is a partial, `docs/_homepage-example.md`,
  frozen with each stable line. The `homepage-example` plugin reads it from the latest line's copy, so the homepage
  always describes what `dotnet add package` installs; before the first stable release it reads the working tree's.
  Webpack's persistent cache does not see the alias change when a line is frozen: run `npx docusaurus clear` before
  building a freshly frozen site locally. CI never restores that cache. The install command asks for
  `--prerelease` until a stable release exists.
- **The 404 page** is an ejected `NotFound`, a safe swizzle.

### Search

`@easyops-cn/docusaurus-search-local`, at an exact version. The build writes a lunr index for each version of the
docs, served with the site, so a search reaches no third party and searches the version the reader is in.

- **It covers the user documentation only.** The project documents would take the index from 0.7 MB to 12.7 MB
  (3.7 MB compressed), fetched by every reader's first search; `milestones/` alone weighs most of it.
- **The API reference is indexed by its page titles and member headings**: its summaries, parameter tables and
  signatures are left out through the `docs-doc-id-reference/api/` class Docusaurus puts on `<html>`. Measured on
  2026-10-04, its index weighs 232 KB instead of 611 KB.
- **The guides and the API reference are searched apart** in the version served at `/docs/`: `searchContextByPaths`
  gives the API reference an index of its own, so a guide page searches the guides and an API page the API. The
  search bar says which index it searches, and `src/components/SearchScope`, beside it, picks the index; choosing
  the other one opens the results page in it, or switches in place there. The plugin's own picker on that page,
  which cannot offer the guides, is hidden.
- **The preview and the older lines keep one index for both**: the plugin resolves a context against the root of the
  site when it builds but against the version's path in the browser, so below `/docs/<version>/` it would never find
  one again. Before the first stable release the preview is served at `/docs/` and is searched apart; after it, the
  preview moves to `/docs/preview/` and falls back to one index.
- `forceIgnoreNoIndex` keeps the preview searchable once it is `noIndex`.
- The plugin takes its colors from `--search-local-*` variables set in `custom.css`, and the global `mark` rule
  (`accent-soft`, `accent-text`). The few rules they cannot reach match its CSS-module class names on their stable
  prefix (`[class*='suggestion_']`), which a release of the plugin may rename: check the search after updating it.
