import React, { useState, useRef, useEffect } from 'react';
import styles from './styles.module.css';

/**
 * Minimal "copy to clipboard" button. Shows a 1.5s confirmation state
 * after a successful copy. Falls back silently when the Clipboard API
 * isn't available (older browsers, insecure contexts) — the user can
 * still select-and-copy manually.
 */
export default function CopyButton({ text, label = 'Copy', className }) {
  const [copied, setCopied] = useState(false);
  // Timer ref so rapid repeated clicks don't stack pending resets and
  // an unmount mid-cooldown doesn't fire setCopied on a dead component.
  const timerRef = useRef(null);

  useEffect(() => () => {
    if (timerRef.current) clearTimeout(timerRef.current);
  }, []);

  const onClick = async () => {
    try {
      if (navigator?.clipboard?.writeText) {
        await navigator.clipboard.writeText(text);
      } else {
        // Legacy fallback
        const ta = document.createElement('textarea');
        ta.value = text;
        ta.setAttribute('readonly', '');
        ta.style.position = 'absolute';
        ta.style.left = '-9999px';
        document.body.appendChild(ta);
        ta.select();
        document.execCommand('copy');
        document.body.removeChild(ta);
      }
      setCopied(true);
      if (timerRef.current) clearTimeout(timerRef.current);
      timerRef.current = setTimeout(() => setCopied(false), 1500);
    } catch {
      // swallow — the user can still select-and-copy the rendered text
    }
  };

  return (
    <button
      type="button"
      className={`${styles.copy} ${copied ? styles.copied : ''} ${className ?? ''}`.trim()}
      onClick={onClick}
      aria-label={copied ? 'Copied to clipboard' : `Copy ${label} to clipboard`}
    >
      <span className={styles.icon} aria-hidden="true">
        {copied ? (
          /* checkmark */
          <svg viewBox="0 0 16 16" width="13" height="13">
            <path d="M2 8.5 L6.5 13 L14 4" stroke="currentColor" strokeWidth="2"
                  fill="none" strokeLinecap="round" strokeLinejoin="round" />
          </svg>
        ) : (
          /* two overlapping squares — classic copy glyph */
          <svg viewBox="0 0 16 16" width="13" height="13">
            <rect x="4.5" y="4.5" width="9" height="9" rx="1.5"
                  stroke="currentColor" strokeWidth="1.5" fill="none" />
            <rect x="2.5" y="2.5" width="9" height="9" rx="1.5"
                  stroke="currentColor" strokeWidth="1.5" fill="none"
                  style={{ opacity: 0.55 }} />
          </svg>
        )}
      </span>
      <span className={styles.label}>{copied ? 'Copied' : 'Copy'}</span>
    </button>
  );
}
