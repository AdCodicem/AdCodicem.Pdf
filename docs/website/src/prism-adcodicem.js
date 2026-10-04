// The design system's seven-hue syntax palette. Prism writes these as inline styles, so each color is a custom
// property defined in src/css/custom.css: one theme serves all four color schemes, the accessible ones included,
// which retune the comments.

/** @type {import('prism-react-renderer').PrismTheme} */
export const adcodicem = {
  plain: { color: 'var(--code-fg)', backgroundColor: 'var(--code-bg)' },
  styles: [
    { types: ['comment', 'prolog', 'doctype', 'cdata'], style: { color: 'var(--code-comment)', fontStyle: 'italic' } },
    { types: ['keyword', 'builtin', 'important', 'tag'], style: { color: 'var(--code-keyword)' } },
    { types: ['class-name', 'type-expression', 'maybe-class-name', 'namespace'], style: { color: 'var(--code-type)' } },
    { types: ['function', 'method'], style: { color: 'var(--code-method)' } },
    { types: ['string', 'char', 'attr-value', 'regex'], style: { color: 'var(--code-string)' } },
    { types: ['number', 'boolean', 'constant', 'symbol'], style: { color: 'var(--code-number)' } },
    { types: ['attribute', 'annotation', 'attr-name', 'property'], style: { color: 'var(--code-attribute)' } },
    { types: ['punctuation', 'operator'], style: { color: 'var(--code-punct)' } },
  ],
};
