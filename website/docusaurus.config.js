// @ts-check
// Brand-neutral configuration: product name lives here so a future rename
// changes one file rather than every URL slug. Do NOT bake "mcp-for-unity"
// or "unity-mcp" into sidebar slugs, file paths, or docs URLs.

import { themes as prismThemes } from 'prism-react-renderer';
import { readdirSync, existsSync, readFileSync, statSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));

// Count MCP client configurators at build time so the homepage stats
// row stays accurate forever. Falls back to 0 if the directory moves
// (build still succeeds; the component renders the literal number).
function countConfigurators() {
  const dir = resolve(__dirname, '..', 'MCPForUnity', 'Editor', 'Clients', 'Configurators');
  if (!existsSync(dir)) return 0;
  return readdirSync(dir).filter((f) => f.endsWith('Configurator.cs')).length;
}
const supportedClientCount = countConfigurators();

function listMarkdownFiles(dir) {
  if (!existsSync(dir)) return [];
  return readdirSync(dir).flatMap((entry) => {
    const path = resolve(dir, entry);
    if (statSync(path).isDirectory()) return listMarkdownFiles(path);
    return entry.endsWith('.md') ? [path] : [];
  });
}

function countReferenceTools() {
  const dir = resolve(__dirname, 'docs', 'reference', 'tools');
  return listMarkdownFiles(dir).filter((path) => !path.endsWith('/index.md')).length;
}

function countToolGroups() {
  const dir = resolve(__dirname, 'docs', 'reference', 'tools');
  if (!existsSync(dir)) return 0;
  return readdirSync(dir).filter((entry) => {
    const path = resolve(dir, entry);
    return statSync(path).isDirectory();
  }).length;
}

function countReferenceResources() {
  const path = resolve(__dirname, 'docs', 'reference', 'resources', 'index.md');
  if (!existsSync(path)) return 0;
  return (readFileSync(path, 'utf8').match(/\n## `/g) ?? []).length;
}

const latestVersion = 'v10.0.0';
const toolCount = countReferenceTools();
const toolGroupCount = countToolGroups();
const resourceCount = countReferenceResources();

const baseUrl = '/unity-mcp/';

/** @type {import('@docusaurus/types').Config} */
const config = {
  title: 'MCP for Unity',
  tagline: 'AI-driven game development for the Unity Editor',
  favicon: 'img/favicon.png',

  // Hosted on GitHub Pages under the CoplayDev org.
  // Custom domain (CNAME) deferred — see plan Phase 2.
  url: 'https://coplaydev.github.io',
  baseUrl,

  organizationName: 'CoplayDev',
  projectName: 'unity-mcp',
  deploymentBranch: 'gh-pages',
  trailingSlash: false,

  // Build-time data the homepage components read via siteConfig.customFields.
  // Keeps stats accurate without a hand-maintained constant.
  customFields: {
    latestVersion,
    toolCount,
    toolGroupCount,
    resourceCount,
    supportedClientCount,
  },

  onBrokenLinks: 'throw',

  // Typography: Satoshi (Fontshare) for body/headings, JetBrains Mono
  // (Google Fonts) for code. Loaded via <link> in the document head.
  headTags: [
    {
      tagName: 'link',
      attributes: { rel: 'preconnect', href: 'https://api.fontshare.com' },
    },
    {
      tagName: 'link',
      attributes: {
        rel: 'preconnect',
        href: 'https://fonts.gstatic.com',
        crossorigin: 'anonymous',
      },
    },
    {
      tagName: 'link',
      attributes: { rel: 'icon', type: 'image/png', sizes: '32x32', href: `${baseUrl}img/favicon-32.png` },
    },
    {
      tagName: 'link',
      attributes: { rel: 'apple-touch-icon', sizes: '180x180', href: `${baseUrl}img/apple-touch-icon.png` },
    },
    {
      tagName: 'link',
      attributes: { rel: 'icon', type: 'image/png', sizes: '192x192', href: `${baseUrl}img/android-chrome-192.png` },
    },
    {
      tagName: 'link',
      attributes: { rel: 'icon', type: 'image/png', sizes: '512x512', href: `${baseUrl}img/android-chrome-512.png` },
    },
  ],
  stylesheets: [
    'https://api.fontshare.com/v2/css?f[]=satoshi@300,400,500,700,900&display=swap',
    'https://fonts.googleapis.com/css2?family=JetBrains+Mono:wght@400;500;700&display=swap',
  ],

  markdown: {
    // Parse .md as CommonMark (no JSX, no {expression} parsing) and .mdx
    // as MDX. Auto-generated tool reference pages contain literals like
    // `{name: value}` and `<T>` in descriptions — MDX would treat those
    // as JS expressions / JSX tags and refuse to compile.
    format: 'detect',
    hooks: {
      onBrokenMarkdownLinks: 'warn',
    },
  },

  i18n: {
    defaultLocale: 'en',
    locales: ['en'],
    // Chinese translation deferred to a follow-up PR; structure ready
    // (existing docs/i18n/README-zh.md will migrate into i18n/zh/).
  },

  presets: [
    [
      'classic',
      /** @type {import('@docusaurus/preset-classic').Options} */
      ({
        docs: {
          routeBasePath: '/',
          sidebarPath: './sidebars.js',
          editUrl: 'https://github.com/CoplayDev/unity-mcp/edit/beta/website/',
          showLastUpdateTime: true,
          showLastUpdateAuthor: true,
        },
        blog: false,
        theme: {
          customCss: './src/css/custom.css',
        },
      }),
    ],
  ],

  plugins: [
    [
      '@easyops-cn/docusaurus-search-local',
      {
        hashed: true,
        indexBlog: false,
        docsRouteBasePath: '/',
        highlightSearchTermsOnTargetPage: true,
      },
    ],
    // Redirects: as docs land in M2+, add entries here so external links
    // pointing at old /docs/*.md paths on GitHub keep working.
    [
      '@docusaurus/plugin-client-redirects',
      {
        redirects: [
          // Example pattern (populated as content migrates in M2):
          // { from: '/docs/guides/CLI_USAGE', to: '/guides/cli' },
        ],
      },
    ],
    ...(process.env.GOATCOUNTER_CODE ? ['docusaurus-plugin-goatcounter'] : []),
  ],

  themeConfig:
    /** @type {import('@docusaurus/preset-classic').ThemeConfig} */
    ({
      image: 'img/social-card.png',
      metadata: [{ name: 'theme-color', content: '#4f46e5' }],
      navbar: {
        title: 'MCP for Unity',
        logo: {
          alt: 'MCP for Unity logo',
          src: 'img/logo-mark.svg',
          srcDark: 'img/logo-mark.svg',
        },
        items: [
          {
            type: 'docSidebar',
            sidebarId: 'mainSidebar',
            position: 'left',
            label: 'Docs',
          },
          {
            to: '/reference/tools',
            label: 'Reference',
            position: 'left',
          },
          {
            to: '/releases',
            label: 'Releases',
            position: 'left',
          },
          {
            href: 'https://github.com/CoplayDev/unity-mcp',
            position: 'right',
            className: 'header-icon-link header-github-link',
            'aria-label': 'GitHub repository',
          },
          {
            href: 'https://discord.gg/y4p8KfzrN4',
            position: 'right',
            className: 'header-icon-link header-discord-link',
            'aria-label': 'Discord',
          },
        ],
      },
      footer: {
        style: 'dark',
        links: [
          {
            title: 'Docs',
            items: [
              { label: 'Getting Started', to: '/getting-started' },
              { label: 'Guides', to: '/guides/cli' },
              { label: 'Reference', to: '/reference/tools/' },
            ],
          },
          {
            title: 'Community',
            items: [
              { label: 'Discord', href: 'https://discord.gg/y4p8KfzrN4' },
              { label: 'GitHub Issues', href: 'https://github.com/CoplayDev/unity-mcp/issues' },
            ],
          },
          {
            title: 'More',
            items: [
              { label: 'GitHub', href: 'https://github.com/CoplayDev/unity-mcp' },
              { label: 'PyPI', href: 'https://pypi.org/p/mcpforunityserver' },
            ],
          },
        ],
        copyright: `MIT licensed. Sponsored and maintained by <a href="https://www.tryaura.dev/">Aura</a>. Not affiliated with Unity Technologies.`,
      },
      prism: {
        theme: prismThemes.github,
        darkTheme: prismThemes.dracula,
        additionalLanguages: ['csharp', 'bash', 'json', 'python'],
      },
      colorMode: {
        defaultMode: 'light',
        respectPrefersColorScheme: true,
      },
      ...(process.env.GOATCOUNTER_CODE ? { goatcounter: { code: process.env.GOATCOUNTER_CODE } } : {}),
    }),
};

export default config;
