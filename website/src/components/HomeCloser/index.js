import React from 'react';
import Link from '@docusaurus/Link';
import styles from './styles.module.css';

export default function HomeCloser() {
  return (
    <section className={styles.section}>
      <div className={styles.inner}>
        <div className={styles.gridBackdrop} aria-hidden="true" />

        <div className={styles.copy}>
          <span className={styles.eyebrow}>// READY?</span>
          <h2 className={styles.title}>Get the editor speaking your AI's language.</h2>
          <p className={styles.lede}>
            Install in under a minute. No account, no telemetry by
            default, no lock-in. Open source under MIT.
          </p>
        </div>

        <div className={styles.actions}>
          <Link className={styles.ctaPrimary} to="/getting-started/install">
            Install <span aria-hidden="true">↗</span>
          </Link>
          <Link className={styles.ctaLink} to="/reference/tools">
            Browse the tool reference <span aria-hidden="true">→</span>
          </Link>
          <a
            className={styles.ctaLink}
            href="https://github.com/CoplayDev/unity-mcp"
            target="_blank"
            rel="noopener noreferrer"
          >
            Star on GitHub <span aria-hidden="true">→</span>
          </a>
          <a
            className={styles.ctaLink}
            href="https://discord.gg/y4p8KfzrN4"
            target="_blank"
            rel="noopener noreferrer"
          >
            Join Discord <span aria-hidden="true">→</span>
          </a>
        </div>

        <div className={styles.cite}>
          <div className={styles.citeHead}>
            <span className={styles.eyebrow}>// CITATION</span>
            <span className={styles.citeMeta}>SA Technical Communications ’25 · ACM</span>
          </div>
          <p className={styles.citeBody}>
            Using MCP for Unity in research? Please cite our paper —
            <em> MCP-Unity: Protocol-Driven Framework for Interactive
            3D Authoring</em> (Wu &amp; Barnett, 2025).
          </p>
          <pre className={styles.citeBlock}>
{`@inproceedings{10.1145/3757376.3771417,
  author    = {Wu, Shutong and Barnett, Justin P.},
  title     = {MCP-Unity: Protocol-Driven Framework for Interactive 3D Authoring},
  year      = {2025},
  isbn      = {9798400721366},
  publisher = {Association for Computing Machinery},
  address   = {New York, NY, USA},
  url       = {https://doi.org/10.1145/3757376.3771417},
  doi       = {10.1145/3757376.3771417},
  series    = {SA Technical Communications '25}
}`}
          </pre>
        </div>
      </div>
    </section>
  );
}
