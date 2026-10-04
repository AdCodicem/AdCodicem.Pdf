import React, { useEffect, useRef, useState } from 'react';
import clsx from 'clsx';
import styles from './styles.module.css';

/**
 * The design system's InstallCommand, in its single-command form: a `$` prompt that is never copied, the command,
 * and a copy button that confirms for 1.6 s.
 */
export default function InstallCommand({ command }) {
  const [copied, setCopied] = useState(false);
  const timer = useRef(undefined);
  useEffect(() => () => clearTimeout(timer.current), []);

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(command);
    } catch {
      return;
    }
    setCopied(true);
    clearTimeout(timer.current);
    timer.current = setTimeout(() => setCopied(false), 1600);
  };

  return (
    <div className={styles.install}>
      <code className={styles.line}>
        <span className={styles.prompt} aria-hidden="true">
          $
        </span>
        {command}
      </code>
      <button
        type="button"
        className={clsx('clean-btn', styles.copy, copied && styles.copied)}
        onClick={copy}
        aria-label={copied ? 'Copied' : 'Copy command'}
        title={copied ? 'Copied' : 'Copy command'}>
        <svg
          viewBox="0 0 24 24"
          width="16"
          height="16"
          aria-hidden="true"
          fill="none"
          stroke="currentColor"
          strokeWidth="2"
          strokeLinecap="round"
          strokeLinejoin="round">
          {copied ? (
            <path d="M20 6 9 17l-5-5" />
          ) : (
            <>
              <rect width="14" height="14" x="8" y="8" rx="2" ry="2" />
              <path d="M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2" />
            </>
          )}
        </svg>
        <span role="status" className={styles.status}>
          {copied ? 'Copied' : ''}
        </span>
      </button>
    </div>
  );
}
