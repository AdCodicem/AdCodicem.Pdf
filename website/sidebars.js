/** @type {import('@docusaurus/plugin-content-docs').SidebarsConfig} */
const sidebars = {
  documentation: [
    'introduction',
    {
      type: 'category',
      label: 'Concepts',
      items: ['concepts/lazy-reading', 'concepts/diagnostics'],
    },
  ],
};

export default sidebars;
