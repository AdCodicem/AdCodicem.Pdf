// @ts-check
// The documentation site. It publishes two things: the user-facing documentation written here, and the
// project's own working documents from ../docs, unchanged. Publishing the second is deliberate — the
// roadmap, the decisions and their rejected alternatives are the most useful thing a reader can have
// when deciding whether to depend on a young library.

import { previewIsCurrent, readReleases } from './scripts/versions.mjs';

const organization = 'AdCodicem';
const repository = 'AdCodicem.Pdf';
const source = `https://github.com/${organization}/${repository}/tree/main`;

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

/** @type {import('@docusaurus/types').Config} */
const config = {
  title: 'AdCodicem.Pdf',
  tagline: 'Managed PDF generation and manipulation for .NET',
  favicon: 'img/favicon.svg',

  url: `https://${organization.toLowerCase()}.github.io`,
  baseUrl: `/${repository}/`,
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
          routeBasePath: '/',
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
      // Pages that moved when the user documentation took the Diátaxis quadrants (ADR 47): a link published
      // before the move lands on the page's new address rather than on a 404. Only the built site carries the
      // redirects, as small pages at the old addresses; `npm start` does not.
      '@docusaurus/plugin-client-redirects',
      {
        redirects: [{ from: '/project/validation-rules', to: '/reference/validation-rules' }],
        // Every page of the API reference, in every version, under its address before the move.
        createRedirects: (existingPath) => {
          const marker = `/${apiDirectory}/`;
          const at = existingPath.indexOf(marker);
          return at < 0 ? undefined : `${existingPath.slice(0, at)}/api/${existingPath.slice(at + marker.length)}`;
        },
      },
    ],
  ],

  themeConfig: {
    navbar: {
      title: 'AdCodicem.Pdf',
      items: [
        { type: 'docSidebar', sidebarId: 'documentation', position: 'left', label: 'Documentation' },
        { to: '/project/roadmap', label: 'Project', position: 'left' },
        // The selector lists the stable lines only; the preview has its own button, which hides itself
        // when there is no preview to switch to (src/components/PreviewToggleNavbarItem.js).
        ...(hasStable
          ? [{ type: 'docsVersionDropdown', position: 'right', versions: releases.map(({ line }) => line) }]
          : []),
        { type: 'custom-previewToggle', position: 'right' },
        { href: `https://github.com/${organization}/${repository}`, label: 'GitHub', position: 'right' },
      ],
    },
    footer: {
      style: 'dark',
      links: [
        {
          title: 'Documentation',
          items: [
            { label: 'Introduction', to: '/' },
            { label: 'Tutorials', to: '/tutorials' },
            { label: 'How-to guides', to: '/guides' },
            { label: 'Reference', to: '/reference' },
            { label: 'Explanation', to: '/concepts' },
          ],
        },
        {
          title: 'Project',
          items: [
            { label: 'Roadmap', to: '/project/roadmap' },
            { label: 'Decisions', to: '/project/adr/' },
            { label: 'GitHub', href: `https://github.com/${organization}/${repository}` },
            { label: 'Contributing documents', to: '/project/corpus-contributions' },
          ],
        },
      ],
      // HTML, which Docusaurus renders as is: the full disclaimer is on the introduction and in the README.
      copyright:
        `<a href="https://github.com/${organization}/${repository}/blob/main/LICENSE">MIT licensed</a>, ` +
        `provided as is, without warranty of any kind. Documentation built ${new Date().getFullYear()}.`,
    },
    prism: {
      additionalLanguages: ['csharp', 'bash', 'json'],
    },
  },
};

export default config;
