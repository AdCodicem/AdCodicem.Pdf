// @ts-check
// The documentation site. It publishes two things: the user-facing documentation written here, and the
// project's own working documents from ../docs, unchanged. Publishing the second is deliberate — the
// roadmap, the decisions and their rejected alternatives are the most useful thing a reader can have
// when deciding whether to depend on a young library.

const organisation = 'AdCodicem';
const repository = 'AdCodicem.Pdf';
const source = `https://github.com/${organisation}/${repository}/tree/main`;

/**
 * The generated API reference is one flat directory, one page per type and per namespace. Sorted by file
 * name, a namespace's own page lands after its types; grouped here instead, each namespace is a category
 * whose page is its link, holding its types by name.
 *
 * @type {import('@docusaurus/plugin-content-docs').PluginOptions['sidebarItemsGenerator']}
 */
async function sidebarItems({ defaultSidebarItemsGenerator, ...args }) {
  if (args.item.dirName !== 'api') return defaultSidebarItemsGenerator(args);

  const pages = args.docs.filter((doc) => doc.sourceDirName === 'api');
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

  url: `https://${organisation.toLowerCase()}.github.io`,
  baseUrl: `/${repository}/`,
  organizationName: organisation,
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
          // The API reference is generated from the XML documentation comments: its source is the code,
          // and a link to a Markdown file that exists only during the build would lead to a 404.
          editUrl: ({ versionDocsDirPath, docPath }) =>
            docPath.startsWith('api/') ? undefined : `${source}/docs/website/${versionDocsDirPath}/${docPath}`,
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
  ],

  themeConfig: {
    navbar: {
      title: 'AdCodicem.Pdf',
      items: [
        { type: 'docSidebar', sidebarId: 'documentation', position: 'left', label: 'Documentation' },
        { to: '/project/roadmap', label: 'Project', position: 'left' },
        { href: `https://github.com/${organisation}/${repository}`, label: 'GitHub', position: 'right' },
      ],
    },
    footer: {
      style: 'dark',
      links: [
        {
          title: 'Documentation',
          items: [
            { label: 'Introduction', to: '/' },
            { label: 'Roadmap', to: '/project/roadmap' },
            { label: 'Decisions', to: '/project/adr/' },
          ],
        },
        {
          title: 'Project',
          items: [
            { label: 'GitHub', href: `https://github.com/${organisation}/${repository}` },
            { label: 'Contributing documents', to: '/project/corpus-contributions' },
          ],
        },
      ],
      copyright: `MIT licensed. Documentation built ${new Date().getFullYear()}.`,
    },
    prism: {
      additionalLanguages: ['csharp', 'bash', 'json'],
    },
  },
};

export default config;
