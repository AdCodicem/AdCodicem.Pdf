// @ts-check
import { existsSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
// The documentation site. It publishes two things: the user-facing documentation written here, and the
// project's own working documents from ../docs, unchanged. Publishing the second is deliberate — the
// roadmap, the decisions and their rejected alternatives are the most useful thing a reader can have
// when deciding whether to depend on a young library.

import { previewIsCurrent, readReleases } from './scripts/versions.mjs';
import { adcodicem } from './src/prism-adcodicem.js';
import { apiContext } from './src/components/SearchScope/context.js';

const organization = 'AdCodicem';
const repository = 'AdCodicem.Pdf';
const source = `https://github.com/${organization}/${repository}/tree/main`;
const baseUrl = `/${repository}/`;

// The user documentation is versioned; the project documents are not — they describe the project, not a
// release, and always come from main. Each stable line is frozen by the release that opens it (see
// scripts/version-docs.mjs) and served under its own path, the newest at the root. The working tree is the
// preview: served under /preview beside them, or at the root while no stable release exists.
const releases = readReleases();
const hasStable = releases.length > 0;

// The preview package a deployment documents, set by docs.yml. Left unset, the build is a local or CI one,
// and documents the working tree; set but empty, no preview is newer than the latest stable release.
const previewVersion = process.env.DOCS_PREVIEW_VERSION;
const includePreview = previewVersion === undefined || !hasStable || previewIsCurrent(previewVersion, releases);
const previewLabel = previewVersion === undefined ? 'local build' : previewVersion || 'preview';

/** @type {import('@docusaurus/plugin-content-docs').PluginOptions['versions']} */
const versions = Object.fromEntries(releases.map(({ line, version }) => [line, { label: version }]));
if (includePreview) {
  versions.current = hasStable
    ? { label: previewLabel, path: 'preview', noIndex: true }
    : { label: previewLabel };
}

// Where DocFX writes the generated API reference, under the Reference quadrant of the user documentation
// (ADR 47); docs/docfx/docfx.json and scripts/tidy-api-reference.mjs name the same directory.
const apiDirectory = 'reference/api';

/**
 * The generated API reference is one flat directory, one page per type and per namespace. Sorted by file
 * name, a namespace's own page lands after its types; grouped here instead, each namespace is a category
 * whose page is its link, holding its types by name.
 *
 * @type {import('@docusaurus/plugin-content-docs').PluginOptions['sidebarItemsGenerator']}
 */
async function sidebarItems({ defaultSidebarItemsGenerator, ...args }) {
  if (args.item.dirName !== apiDirectory) return defaultSidebarItemsGenerator(args);

  const pages = args.docs.filter((doc) => doc.sourceDirName === apiDirectory);
  const uid = (doc) => doc.id.slice(doc.id.lastIndexOf('/') + 1);
  const byName = (a, b) => a.label.localeCompare(b.label);

  const namespaces = pages.filter((doc) => doc.title.startsWith('Namespace '));
  const categories = new Map(
    namespaces.map((doc) => [
      uid(doc),
      { type: 'category', label: uid(doc), link: { type: 'doc', id: doc.id }, items: [] },
    ]),
  );

  const loose = [];
  for (const doc of pages) {
    if (namespaces.includes(doc)) continue;

    // The longest namespace the type's name starts with, so a nested type stays with its namespace.
    let owner = uid(doc);
    do {
      owner = owner.includes('.') ? owner.slice(0, owner.lastIndexOf('.')) : '';
    } while (owner && !categories.has(owner));

    const item = { type: 'doc', id: doc.id, label: uid(doc).slice(owner ? owner.length + 1 : 0) };
    (owner ? categories.get(owner).items : loose).push(item);
  }

  for (const category of categories.values()) category.items.sort(byName);
  return [...[...categories.values()].sort(byName), ...loose.sort(byName)];
}

// Not import.meta.dirname: Docusaurus loads this file through jiti, which rewrites import.meta.url and nothing else.
const site = fileURLToPath(new URL('.', import.meta.url));

/**
 * The homepage is not versioned, but its example must describe what `dotnet add package` installs, so it is a
 * partial frozen with the rest of the user documentation and read from the latest stable line's copy. Before the
 * first stable release there is none, and the preview is what the site serves anyway.
 *
 * @type {import('@docusaurus/types').PluginModule}
 */
function homepageExample() {
  const partial = '_homepage-example.md';
  const source = hasStable
    ? path.join(site, 'versioned_docs', `version-${releases[0].line}`, partial)
    : path.join(site, 'docs', partial);
  // Falling back to docs/ here would quietly put unreleased code on the homepage, which is what reading the
  // snapshot is for. A renamed partial waits for the next release.
  if (!existsSync(source)) {
    throw new Error(`The homepage example ${path.relative(site, source)} does not exist.`);
  }
  return {
    name: 'homepage-example',
    configureWebpack: () => ({ resolve: { alias: { '@homepage-example': source } } }),
  };
}

/**
 * The accessible color schemes (src/css/custom.css) hang off a data-contrast attribute on <html>, which this sets
 * before the first paint, as Docusaurus does for data-theme: from the reader's choice in the navbar toggle
 * (src/components/ContrastToggle), or, before they make one, from the system's prefers-contrast setting.
 *
 * @type {import('@docusaurus/types').PluginModule}
 */
function contrast() {
  return {
    name: 'contrast',
    injectHtmlTags: () => ({
      headTags: [
        {
          tagName: 'script',
          innerHTML:
            "(function(){try{var c=localStorage.getItem('adcodicem-contrast');" +
            "if(c==='accessible'||(c===null&&window.matchMedia('(prefers-contrast: more)').matches))" +
            "document.documentElement.setAttribute('data-contrast','accessible')}catch(e){}})();",
        },
      ],
    }),
  };
}

/** @type {import('@docusaurus/types').Config} */
const config = {
  title: 'AdCodicem.Pdf',
  tagline: 'Managed PDF generation and manipulation for .NET',
  favicon: 'img/favicon.ico',

  // The design system's Pdf favicon pack, with the link tags it prescribes; favicon.ico above is the fallback every
  // browser requests.
  headTags: [
    { rel: 'icon', type: 'image/png', sizes: '16x16', href: `${baseUrl}img/favicon-16.png` },
    { rel: 'icon', type: 'image/png', sizes: '32x32', href: `${baseUrl}img/favicon-32.png` },
    { rel: 'apple-touch-icon', sizes: '180x180', href: `${baseUrl}img/favicon-180.png` },
    { rel: 'manifest', href: `${baseUrl}site.webmanifest` },
  ].map((attributes) => ({ tagName: 'link', attributes })),

  // Read by the homepage: before the first stable release, `dotnet add package` needs --prerelease.
  customFields: { hasStable },

  url: `https://${organization.toLowerCase()}.github.io`,
  baseUrl,
  organizationName: organization,
  projectName: repository,
  trailingSlash: false,

  // Warn rather than fail: the project documents are written for the repository first, and a link that
  // makes sense next to the source is not always resolvable on the site.
  onBrokenLinks: 'warn',

  i18n: { defaultLocale: 'en', locales: ['en'] },

  // The project documents are written for the repository, not for MDX. Detecting the format keeps plain
  // Markdown plain, so a stray angle bracket in a specification never breaks the site build.
  markdown: {
    format: 'detect',
    hooks: { onBrokenMarkdownLinks: 'warn' },
  },

  presets: [
    [
      'classic',
      /** @type {import('@docusaurus/preset-classic').Options} */
      ({
        docs: {
          path: 'docs',
          // Under /docs, the root being the homepage. The redirects below keep the addresses published before.
          routeBasePath: 'docs',
          sidebarPath: './sidebars.js',
          sidebarItemsGenerator: sidebarItems,
          lastVersion: hasStable ? releases[0].line : 'current',
          includeCurrentVersion: includePreview,
          versions,
          // The API reference is generated from the XML documentation comments: its source is the code,
          // and a link to a Markdown file that exists only during the build would lead to a 404.
          editUrl: ({ versionDocsDirPath, docPath }) =>
            docPath.startsWith(`${apiDirectory}/`)
              ? undefined
              : `${source}/docs/website/${versionDocsDirPath}/${docPath}`,
        },
        blog: false,
        theme: { customCss: './src/css/custom.css' },
      }),
    ],
  ],

  plugins: [
    homepageExample,
    contrast,
    [
      '@docusaurus/plugin-content-docs',
      {
        id: 'project',
        // A copy of the project documents, never ../ itself. That directory contains this site, and
        // Docusaurus compiles everything under a plugin's path with that plugin's loader, whatever
        // `include` says — pointed at ../, this plugin compiled the user documentation a second time and
        // the site published it as JavaScript. scripts/sync-project-docs.mjs makes the copy.
        path: 'project',
        routeBasePath: 'project',
        sidebarPath: './sidebars-project.js',
        // Edits go to the originals in docs/, not to the copy.
        editUrl: ({ docPath }) => `${source}/docs/${docPath}`,
      },
    ],
    [
      // Addresses published before, landing on the page's new one rather than on a 404. Only the built site carries
      // the redirects, as small pages at the old addresses; `npm start` does not.
      '@docusaurus/plugin-client-redirects',
      {
        // A page that moved when the user documentation took the Diátaxis quadrants (ADR 47).
        redirects: [{ from: '/project/validation-rules', to: '/docs/reference/validation-rules' }],
        // Every page of the user documentation, in every version, at the address it had while the documentation
        // was served at the root of the site; and every page of the API reference at the one it had before the
        // quadrants, under /api. The root itself is the homepage now, and keeps it.
        createRedirects: (existingPath) => {
          const prefix = '/docs';
          if (!existingPath.startsWith(`${prefix}/`)) return undefined;
          const before = existingPath.slice(prefix.length);
          const marker = `/${apiDirectory}/`;
          const at = before.indexOf(marker);
          return at < 0 ? [before] : [before, `${before.slice(0, at)}/api/${before.slice(at + marker.length)}`];
        },
      },
    ],
  ],

  // Local search: the build writes a lunr index for each version of the docs, served with the site, so a search
  // reaches no third party and always describes what is deployed. It covers the user documentation, not the project
  // documents, whose milestones alone would take the index from 0.7 MB to 12.7 MB, fetched by the first search of
  // every reader. The search bar searches the version the reader is in. The preview is noIndex once a stable line
  // exists, which keeps it out of search engines but must not keep it out of its own search. Of the API reference,
  // only the page titles and member headings are indexed: a member is still found by name, and the index is much
  // lighter than with the summaries, parameter tables and signatures in (docs/website/README.md has the figures).
  // The API reference has an index of its own in the version served at /docs/; src/components/SearchScope says why
  // only there, and lets the reader pick the index.
  themes: [
    [
      '@easyops-cn/docusaurus-search-local',
      /** @type {import('@easyops-cn/docusaurus-search-local').PluginOptions} */
      ({
        hashed: true,
        indexBlog: false,
        indexPages: false,
        docsRouteBasePath: ['docs'],
        language: ['en'],
        explicitSearchResultPath: true,
        forceIgnoreNoIndex: true,
        ignoreCssSelectors: [`html[class*='docs-doc-id-${apiDirectory}/'] article :is(p, li, table, .theme-code-block)`],
        searchContextByPaths: [{ label: 'API reference', path: apiContext }],
      }),
    ],
  ],

  themeConfig: {
    // The design system is designed dark first: ink is what a reader gets when the system expresses no preference.
    colorMode: { defaultMode: 'dark', respectPrefersColorScheme: true },
    navbar: {
      // The design system's Pdf lockup in its compact drawing, the one for a symbol of 24 to 48px. Its name is
      // outlined, so it renders in Archivo whatever fonts the reader has.
      logo: { alt: 'AdCodicem.Pdf', src: 'img/navbar-logo-light.svg', srcDark: 'img/navbar-logo-dark.svg' },
      items: [
        { type: 'docSidebar', sidebarId: 'documentation', position: 'left', label: 'Documentation' },
        { to: '/project/roadmap', label: 'Project', position: 'left' },
        // The selector lists the stable lines only; the preview has its own button, which hides itself
        // when there is no preview to switch to (src/components/PreviewToggleNavbarItem.js).
        ...(hasStable
          ? [{ type: 'docsVersionDropdown', position: 'right', versions: releases.map(({ line }) => line) }]
          : []),
        { type: 'custom-previewToggle', position: 'right' },
        { type: 'search', position: 'right' },
        // Drawn as the design system's icon buttons, `nuget` and `github` (custom.css); the label is what screen
        // readers and the mobile menu read.
        {
          href: 'https://www.nuget.org/packages/AdCodicem.Pdf',
          label: 'NuGet',
          position: 'right',
          className: 'navbar-icon-link navbar-icon-link--nuget',
          'aria-label': 'NuGet package',
          title: 'NuGet package',
        },
        {
          href: `https://github.com/${organization}/${repository}`,
          label: 'GitHub',
          position: 'right',
          className: 'navbar-icon-link navbar-icon-link--github',
          'aria-label': 'GitHub repository',
          title: 'GitHub repository',
        },
      ],
    },
    footer: {
      style: 'light',
      // The AdCodicem lockup: the brand the project belongs to, its name outlined like the navbar's.
      logo: { alt: 'AdCodicem', src: 'img/footer-logo-light.svg', srcDark: 'img/footer-logo-dark.svg' },
      links: [
        {
          title: 'Documentation',
          items: [
            { label: 'Introduction', to: '/docs' },
            { label: 'Tutorials', to: '/docs/tutorials' },
            { label: 'How-to guides', to: '/docs/guides' },
            { label: 'Reference', to: '/docs/reference' },
            { label: 'Explanation', to: '/docs/concepts' },
          ],
        },
        {
          title: 'Project',
          items: [
            { label: 'Roadmap', to: '/project/roadmap' },
            { label: 'Decisions', to: '/project/adr/' },
            { label: 'GitHub', href: `https://github.com/${organization}/${repository}` },
            { label: 'NuGet', href: 'https://www.nuget.org/packages/AdCodicem.Pdf' },
            { label: 'Contributing documents', to: '/project/corpus-contributions' },
          ],
        },
      ],
      // HTML, which Docusaurus renders as is: the full disclaimer is on the introduction and in the README. The
      // Latin baseline closes it, typed lower case and set upper case by the `baseline` style.
      copyright:
        `<a href="https://github.com/${organization}/${repository}/blob/main/LICENSE">MIT licensed</a>, ` +
        `provided as is, without warranty of any kind. Documentation built ${new Date().getFullYear()}.` +
        '<span class="baseline" lang="la">lege artis</span>',
    },
    // One theme for both color modes: its colors are the --code-* tokens, which each scheme sets.
    prism: {
      theme: adcodicem,
      darkTheme: adcodicem,
      additionalLanguages: ['csharp', 'bash', 'json'],
    },
  },
};

export default config;
