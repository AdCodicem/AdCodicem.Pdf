/**
 * Ejected (a safe swizzle, meant to be customized): the design system's 404, from its docs kit.
 */
import React from 'react';
import clsx from 'clsx';
import Link from '@docusaurus/Link';
import { PageMetadata } from '@docusaurus/theme-common';
import Layout from '@theme/Layout';
import Heading from '@theme/Heading';
import Icon from '@site/src/components/Icon';
import PdfMark from '@site/src/components/PdfMark';
import styles from './styles.module.css';

export default function NotFound() {
  return (
    <>
      <PageMetadata title="Page not found" />
      <Layout>
        <main className={styles.page}>
          <div className={styles.content}>
            <PdfMark size={64} mono />
            <p className={styles.label}>404 · Page not found</p>
            <Heading as="h1" className={styles.title}>
              This page is not in the book.
            </Heading>
            <p className={styles.text}>
              It may have moved between versions: each release line keeps its own copy of the documentation, and a
              page added or renamed since exists in some of them only.
            </p>
            <div className={styles.buttons}>
              <Link className="button button--primary" to="/docs/">
                Go to the introduction
              </Link>
              <Link
                className={clsx('button button--secondary', styles.button)}
                to="https://github.com/AdCodicem/AdCodicem.Pdf/issues/new?labels=bug&title=Broken+link">
                <Icon name="github" size={16} />
                Report a broken link
              </Link>
            </div>
          </div>
        </main>
      </Layout>
    </>
  );
}
