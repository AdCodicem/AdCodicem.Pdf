import React, { useEffect, useState } from 'react';
import clsx from 'clsx';
import Icon from '@site/src/components/Icon';

// Also read by the script the `contrast` plugin puts in <head> (docusaurus.config.js), which applies the choice
// before the first paint. 'accessible' or 'brand'; absent until the reader chooses, which defers to prefers-contrast.
const storageKey = 'adcodicem-contrast';

const isAccessible = () => document.documentElement.getAttribute('data-contrast') === 'accessible';

/**
 * Switches between the design system's brand color schemes and its accessible ones, which keep the same grounds and
 * accent but reach WCAG 2 AA on every pair drawn. Independent of light and dark: it applies to whichever is active.
 */
export default function ContrastToggle({ className }) {
  // Rendered unpressed on the server; corrected once the attribute the <head> script set can be read.
  const [accessible, setAccessible] = useState(false);
  useEffect(() => setAccessible(isAccessible()), []);

  const toggle = () => {
    const next = !isAccessible();
    if (next) {
      document.documentElement.setAttribute('data-contrast', 'accessible');
    } else {
      document.documentElement.removeAttribute('data-contrast');
    }
    try {
      localStorage.setItem(storageKey, next ? 'accessible' : 'brand');
    } catch {
      // Storage unavailable (private window, blocked site data): the choice holds for this page only.
    }
    setAccessible(next);
  };

  return (
    <button
      type="button"
      className={clsx('icon-button', accessible && 'icon-button--active', className)}
      onClick={toggle}
      aria-pressed={accessible}
      aria-label="Accessible contrast (WCAG 2 AA)"
      title={accessible ? 'Accessible contrast: on' : 'Accessible contrast: off'}>
      <Icon name="contrast" />
    </button>
  );
}
