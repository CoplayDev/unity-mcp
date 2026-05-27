import React from 'react';
import Layout from '@theme/Layout';
import useDocusaurusContext from '@docusaurus/useDocusaurusContext';
import HomeHero from '@site/src/components/HomeHero';
import HomeStats from '@site/src/components/HomeStats';
import HomeArchitecture from '@site/src/components/HomeArchitecture';
import HomeFeatures from '@site/src/components/HomeFeatures';
import HomeCloser from '@site/src/components/HomeCloser';

export default function Home() {
  const { siteConfig } = useDocusaurusContext();
  return (
    <Layout
      title="MCP for Unity"
      description={siteConfig.tagline}
    >
      <main>
        <HomeHero />
        <HomeStats />
        <HomeArchitecture />
        <HomeFeatures />
        <HomeCloser />
      </main>
    </Layout>
  );
}
