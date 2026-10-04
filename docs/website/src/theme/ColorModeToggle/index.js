/**
 * Ejected (a safe swizzle): the design system's theme toggle, an icon button showing the theme it switches to, the
 * moon on paper and the sun on ink, beside the contrast toggle, in the navbar and the mobile sidebar alike.
 *
 * It switches between the two themes only. Until the reader clicks it the site follows the system's preference
 * (respectPrefersColorScheme), and the first click records an explicit choice; Docusaurus's third state, back to the
 * system, has no place in a two-icon toggle.
 *
 * The class name goes on the pair: it is what hides the toggles from the top bar on a narrow screen, where the
 * mobile sidebar shows them instead.
 */
import React from 'react';
import clsx from 'clsx';
import useIsBrowser from '@docusaurus/useIsBrowser';
import { useColorMode } from '@docusaurus/theme-common';
import ContrastToggle from '@site/src/components/ContrastToggle';
import Icon from '@site/src/components/Icon';

export default function ColorModeToggle({ className, buttonClassName, onChange }) {
  const isBrowser = useIsBrowser();
  const { colorMode } = useColorMode();
  const next = colorMode === 'dark' ? 'light' : 'dark';
  return (
    <div className={className}>
      <div className="theme-toggles">
        <ContrastToggle />
        <button
          type="button"
          className={clsx('icon-button', buttonClassName)}
          onClick={() => onChange(next)}
          disabled={!isBrowser}
          aria-label={`Switch to the ${next} theme`}
          title="Switch theme">
          <Icon name="moon" className="theme-icon--on-light" />
          <Icon name="sun" className="theme-icon--on-dark" />
        </button>
      </div>
    </div>
  );
}
