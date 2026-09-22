/**
 * The navbar button between the stable documentation and the preview's: "Preview" from a stable page,
 * "← Stable" from a preview page. It keeps the reader on the same page when the other version has it, the
 * way the version selector does, and remembers the choice for the "Documentation" link.
 *
 * It renders nothing when there is nothing to switch to: before the first stable release, when the preview
 * is the whole site, and right after one, when no preview is newer than it.
 */
import React from 'react';
import {
  useActiveDocContext,
  useDocsPreferredVersion,
  useLatestVersion,
  useVersions,
} from '@docusaurus/plugin-content-docs/client';
import {useHistorySelector} from '@docusaurus/theme-common';
import DefaultNavbarItem from '@theme/NavbarItem/DefaultNavbarItem';

const mainDocOf = (version) => version.docs.find((doc) => doc.id === version.mainDocId);

export default function PreviewToggleNavbarItem({docsPluginId, ...props}) {
  const search = useHistorySelector((history) => history.location.search);
  const hash = useHistorySelector((history) => history.location.hash);
  const versions = useVersions(docsPluginId);
  const latest = useLatestVersion(docsPluginId);
  const {activeVersion, alternateDocVersions} = useActiveDocContext(docsPluginId);
  const {savePreferredVersionName} = useDocsPreferredVersion(docsPluginId);

  const preview = versions.find((version) => version.name === 'current');
  if (!preview || preview === latest) {
    return null;
  }

  const onPreview = activeVersion?.name === preview.name;
  const target = onPreview ? latest : preview;
  const doc = alternateDocVersions[target.name] ?? mainDocOf(target);

  return (
    <DefaultNavbarItem
      {...props}
      label={onPreview ? '← Stable' : 'Preview'}
      title={onPreview ? `Back to the latest stable release, ${latest.label}` : `The preview ${preview.label}`}
      to={`${doc.path}${search}${hash}`}
      isActive={() => false}
      onClick={() => savePreferredVersionName(target.name)}
    />
  );
}
