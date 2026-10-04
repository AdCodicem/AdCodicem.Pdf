import React from 'react';
import Heading from '@theme/Heading';
import styles from './styles.module.css';

// The design goals of the README, as the design system lays out a row of principles: numbered columns separated by
// hairline rules. They describe the library as designed; the roadmap says which parts exist today.
const features = [
  {
    title: 'Managed all the way down',
    description: (
      <>
        No headless browser, no native PDF engine, no external process. The core package has no dependencies at
        all and is compatible with Native AOT and trimming.
      </>
    ),
  },
  {
    title: 'Streaming by default',
    description: (
      <>
        Documents are read lazily and written forward-only. Memory follows the heaviest page, not the size of the
        file: a 10 000-page report costs what a 10-page one costs.
      </>
    ),
  },
  {
    title: 'Business documents, not the web',
    description: (
      <>
        The CSS box model, tables, paged media, flexbox, simple grid and inline SVG, for invoices, reports, contracts
        and case files. No JavaScript.
      </>
    ),
  },
  {
    title: 'Conformance as a constraint',
    description: (
      <>
        Tagged PDF for accessibility, PDF/A-3 and Factur-X for archiving and e-invoicing, designed in from the start
        rather than added later.
      </>
    ),
  },
  {
    title: 'Honest about damaged input',
    description: (
      <>
        Real-world PDFs are often malformed. The reader repairs what it can and reports each repair as a diagnostic
        with a code that stays the same from one release to the next.
      </>
    ),
  },
];

function Feature({ title, description, index }) {
  return (
    <div className={styles.feature}>
      <span className={styles.number}>{String(index + 1).padStart(2, '0')}</span>
      <Heading as="h3" className={styles.title}>
        {title}
      </Heading>
      <p className={styles.description}>{description}</p>
    </div>
  );
}

export default function HomepageFeatures() {
  return (
    <section className={styles.features}>
      <div className={styles.grid}>
        {features.map((feature, index) => (
          <Feature key={feature.title} index={index} {...feature} />
        ))}
      </div>
    </section>
  );
}
