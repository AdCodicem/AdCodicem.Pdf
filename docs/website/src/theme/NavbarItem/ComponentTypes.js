// Docusaurus's navbar item types, plus the site's own. A custom type must be named `custom-…` for the
// configuration to accept it.
import ComponentTypes from '@theme-original/NavbarItem/ComponentTypes';
import PreviewToggleNavbarItem from '@site/src/components/PreviewToggleNavbarItem';

export default {
  ...ComponentTypes,
  'custom-previewToggle': PreviewToggleNavbarItem,
};
