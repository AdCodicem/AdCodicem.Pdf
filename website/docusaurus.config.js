// @ts-check
// The documentation site. It publishes two things: the user-facing documentation written here, and the
// project's own working documents from ../docs, unchanged. Publishing the second is deliberate — the
// roadmap, the decisions and their rejected alternatives are the most useful thing a reader can have
// when deciding whether to depend on a young library.

const organisation = 'AdCodicem';
const repository = 'AdCodicem.Pdf';

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
          editUrl: `https://github.com/${organisation}/${repository}/tree/main/website/`,
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
        path: '../docs',
        routeBasePath: 'project',
        sidebarPath: './sidebars-project.js',
        editUrl: `https://github.com/${organisation}/${repository}/tree/main/`,
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
            { label: 'Decisions', to: '/project/decisions' },
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
