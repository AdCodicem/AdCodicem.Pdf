/**
 * The banner above a documentation page. Stable versions keep Docusaurus's own — an older line is marked
 * as no longer maintained. The preview gets one that says what it is: which package it documents, that it
 * is not released, and where the released version is. Before the first stable release there is no
 * released version to point to, and the banner says that instead.
 */
import React from 'react';
import clsx from 'clsx';
import Link from '@docusaurus/Link';
import {ThemeClassNames} from '@docusaurus/theme-common';
import {
  useActivePlugin,
  useDocVersionSuggestions,
  useDocsPreferredVersion,
  useDocsVersion,
} from '@docusaurus/plugin-content-docs/client';
import OriginalDocVersionBanner from '@theme-original/DocVersionBanner';

const mainDocOf = (version) => version.docs.find((doc) => doc.id === version.mainDocId);

function Banner({className, children}) {
  return (
    <div
      className={clsx(className, ThemeClassNames.docs.docVersionBanner, 'alert alert--warning margin-bottom--md')}
      role="alert">
      {children}
    </div>
  );
}

function PreviewBanner({className, label}) {
  const {pluginId} = useActivePlugin({failfast: true});
  const {savePreferredVersionName} = useDocsPreferredVersion(pluginId);
  const {latestDocSuggestion, latestVersionSuggestion} = useDocVersionSuggestions(pluginId);
  const stableDoc = latestDocSuggestion ?? mainDocOf(latestVersionSuggestion);

  return (
    <Banner className={className}>
      <div>
        You are reading the documentation of the preview <b>{label}</b>, built from <code>main</code> and not
        released yet.
      </div>
      <div className="margin-top--md">
        For the version <code>dotnet add package</code> installs, see the{' '}
        <b>
          <Link to={stableDoc.path} onClick={() => savePreferredVersionName(latestVersionSuggestion.name)}>
            latest stable release
          </Link>
        </b>{' '}
        ({latestVersionSuggestion.label}).
      </div>
    </Banner>
  );
}

function PrereleaseBanner({className, label}) {
  return (
    <Banner className={className}>
      <div>
        No stable version has been released yet. This documents the preview <b>{label}</b>, built from{' '}
        <code>main</code>:
      </div>
      <div className="margin-top--sm">
        <code>dotnet add package AdCodicem.Pdf --prerelease</code>
      </div>
    </Banner>
  );
}

export default function DocVersionBanner(props) {
  const version = useDocsVersion();

  // The project documents' plugin is unversioned, and its only version is called `current` too; it gets
  // no banner, which is what Docusaurus's own gives it.
  if (version.pluginId !== 'default' || version.version !== 'current') {
    return <OriginalDocVersionBanner {...props} />;
  }

  return version.isLast ? (
    <PrereleaseBanner className={props.className} label={version.label} />
  ) : (
    <PreviewBanner className={props.className} label={version.label} />
  );
}
