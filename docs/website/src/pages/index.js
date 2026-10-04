import React from 'react';
import clsx from 'clsx';
import Link from '@docusaurus/Link';
import useDocusaurusContext from '@docusaurus/useDocusaurusContext';
import Layout from '@theme/Layout';
import Heading from '@theme/Heading';
import MDXContent from '@theme/MDXContent';
import HomepageFeatures from '@site/src/components/HomepageFeatures';
import Icon from '@site/src/components/Icon';
import InstallCommand from '@site/src/components/InstallCommand';
// docs/_homepage-example.md as frozen by the latest stable release, or the working tree's before the first one; see
// docusaurus.config.js.
import HomepageExample from '@homepage-example';

import styles from './index.module.css';

// The design system's docs kit: a left-aligned hero on the sunken ground with a code sample as its image, then the
// numbered design goals.
function HomepageHeader() {
  const { siteConfig } = useDocusaurusContext();
  const install = `dotnet add package AdCodicem.Pdf${siteConfig.customFields?.hasStable ? '' : ' --prerelease'}`;
  return (
    <header className={styles.hero}>
      <div className={styles.heroInner}>
        <div className={styles.heroText}>
          <p className={styles.eyebrow}>.NET 10 · MIT · NuGet</p>
          <Heading as="h1" className={styles.heroTitle}>
            <span className={styles.prefix}>AdCodicem.</span>
            <br />
            Pdf
          </Heading>
          <p className={styles.heroLead}>{siteConfig.tagline}.</p>
          <p className={styles.heroLede}>
            Generate documents from HTML, and read, assemble and transform existing ones, with no headless browser,
            no native PDF engine and no external process. Today it reads, repairs and validates; the{' '}
            <Link to="/project/roadmap">roadmap</Link> says what comes next.
          </p>
          <InstallCommand command={install} />
          <div className={styles.buttons}>
            <Link className={clsx('button button--primary button--lg', styles.button)} to="/docs/tutorials/first-steps">
              Get started
              <Icon name="arrow-right" className={styles.arrow} />
            </Link>
            <Link
              className={clsx('button button--secondary button--lg', styles.button)}
              to="https://github.com/AdCodicem/AdCodicem.Pdf">
              <Icon name="github" />
              View on GitHub
            </Link>
          </div>
        </div>
        <div className={styles.heroCode}>
          <MDXContent>
            <HomepageExample />
          </MDXContent>
        </div>
      </div>
    </header>
  );
}

export default function Home() {
  const { siteConfig } = useDocusaurusContext();
  return (
    <Layout title={siteConfig.title} description={siteConfig.tagline}>
      <HomepageHeader />
      <main>
        <HomepageFeatures />
      </main>
    </Layout>
  );
}
