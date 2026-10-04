/**
 * A wrapper of the local search plugin's search bar: it says which index the bar searches, and puts the index picker
 * beside it wherever the guides and the API reference have one each (src/components/SearchScope).
 *
 * The placeholder is the plugin's own input, renamed once it has rendered: the plugin sets a fixed one, and React
 * leaves an attribute alone as long as the value it renders does not change.
 *
 * On the results page the bar steps aside: the page has its own field, and the bar would search another index than
 * the one the picker shows for the results.
 */
import React, { useEffect, useRef } from 'react';
import SearchBar from '@theme-original/SearchBar';
import SearchScopeToggle, { searchPlaceholder, useSearchScope } from '@site/src/components/SearchScope';

export default function SearchBarWrapper(props) {
  const { split, scope, onSearchPage } = useSearchScope();
  const container = useRef(null);
  const placeholder = searchPlaceholder(split, scope);
  const input = () => container.current?.querySelector('input.navbar__search-input') ?? null;

  useEffect(() => {
    const element = input();
    if (element) {
      element.placeholder = placeholder;
      element.setAttribute('aria-label', placeholder);
    }
  });

  return (
    <div ref={container} className="search-with-scope">
      {split && (
        <SearchScopeToggle
          scope={scope}
          onSearchPage={onSearchPage}
          query={() => input()?.value.trim() ?? ''}
          className={onSearchPage ? 'search-scope--results' : undefined}
        />
      )}
      {!onSearchPage && <SearchBar {...props} />}
    </div>
  );
}
