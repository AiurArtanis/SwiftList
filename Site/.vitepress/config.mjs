import { defineConfig } from 'vitepress'
import { dictionary } from './i18n/dictionary.js'
import { buildLocaleConfig } from './i18n/buildLocaleConfig.js'

// config.mjs stays a thin shell: all nav/sidebar structure comes from navSchema.js, all text comes
// from dictionary.js, and buildLocaleConfig.js glues the two together per locale. No literal
// duplicated nav/sidebar text lives in this file.
const en = buildLocaleConfig('/', dictionary['en-US'])
const zh = buildLocaleConfig('/zh-CN/', dictionary['zh-CN'])

function themeConfigFor(locale, built) {
  const t = dictionary[locale]
  return {
    nav: built.nav,
    sidebar: built.sidebar,
    socialLinks: [{ icon: 'github', link: 'https://github.com/SwiftList/SwiftList' }],
    outline: { label: t.outlineLabel },
    docFooter: { prev: t.docFooterPrev, next: t.docFooterNext },
    sidebarMenuLabel: t.sidebarMenuLabel,
    returnToTopLabel: t.returnToTopLabel,
    darkModeSwitchLabel: t.darkModeSwitchLabel,
    lightModeSwitchTitle: t.lightModeSwitchTitle,
    darkModeSwitchTitle: t.darkModeSwitchTitle,
    lastUpdated: { text: t.lastUpdatedText },
  }
}

function ogHeadFor(title, description) {
  return [
    ['meta', { property: 'og:title', content: title }],
    ['meta', { property: 'og:description', content: description }],
  ]
}

function searchTranslationsFor(locale) {
  const t = dictionary[locale]
  return {
    translations: {
      button: { buttonText: t.searchButtonText, buttonAriaLabel: t.searchButtonAriaLabel },
      modal: {
        displayDetails: t.searchDisplayDetails,
        resetButtonTitle: t.searchResetButtonTitle,
        backButtonTitle: t.searchBackButtonTitle,
        noResultsText: t.searchNoResultsText,
        footer: {
          selectText: t.searchFooterSelectText,
          navigateText: t.searchFooterNavigateText,
          closeText: t.searchFooterCloseText,
        },
      },
    },
  }
}

export default defineConfig({
  title: 'SwiftList',
  description: 'High-performance, extensible search utility for Windows / 高性能、可扩展的 Windows 全局检索系统',
  lastUpdated: true,
  // Local search must be enabled once at the root themeConfig (not per-locale, unlike nav/sidebar/
  // etc.) -- VitePress only renders the search UI when it sees this at the top level. Per-locale
  // translations still come from dictionary.js, just nested under options.locales instead.
  themeConfig: {
    search: {
      provider: 'local',
      options: {
        locales: {
          root: searchTranslationsFor('en-US'),
          'zh-CN': searchTranslationsFor('zh-CN'),
        },
      },
    },
  },
  head: [
    ['link', { rel: 'icon', href: '/favicon.ico' }],
    ['meta', { property: 'og:type', content: 'website' }],
    ['meta', { property: 'og:image', content: 'https://swiftlist.github.io/logo.png' }],
    ['meta', { name: 'twitter:card', content: 'summary' }],
    [
      'script',
      {},
      `
      (function() {
        var lang = navigator.language || navigator.userLanguage;
        // If browser language is Chinese and we are at the root homepage, redirect to the Chinese site /zh-CN/
        if (lang && lang.indexOf('zh') === 0 && (window.location.pathname === '/' || window.location.pathname === '/index.html')) {
          window.location.pathname = '/zh-CN/';
        }
      })();
      `,
    ],
  ],
  locales: {
    root: {
      label: 'English',
      lang: 'en-US',
      title: 'SwiftList',
      description: 'High-performance, extensible search utility for Windows',
      head: ogHeadFor('SwiftList', 'High-performance, extensible search utility for Windows'),
      themeConfig: themeConfigFor('en-US', en),
    },
    'zh-CN': {
      label: '简体中文',
      lang: 'zh-CN',
      link: '/zh-CN/',
      title: 'SwiftList',
      description: '高性能、可扩展的 Windows 全局检索系统',
      head: ogHeadFor('SwiftList', '高性能、可扩展的 Windows 全局检索系统'),
      themeConfig: themeConfigFor('zh-CN', zh),
    },
  },
})
