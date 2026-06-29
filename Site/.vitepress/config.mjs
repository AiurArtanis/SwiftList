import { defineConfig } from 'vitepress'

export default defineConfig({
  title: "SwiftList",
  description: "High-performance, extensible search utility for Windows / 高性能、可扩展的 Windows 全局检索系统",
  head: [
    ['link', { rel: 'icon', href: '/favicon.ico' }],
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
      `
    ]
  ],
  locales: {
    root: {
      label: 'English',
      lang: 'en-US',
      title: "SwiftList",
      description: "High-performance, extensible search utility for Windows",
      themeConfig: {
        nav: [
          { text: 'Guide', link: '/guide/introduction' },
          { text: 'Plugin Dev', link: '/guide/plugin-development' }
        ],
        socialLinks: [
          { icon: 'github', link: 'https://github.com/SwiftList/SwiftList' }
        ],
        sidebar: {
          '/guide/': [
            {
              text: 'Getting Started',
              items: [
                { text: 'Introduction', link: '/guide/introduction' },
                { text: 'Architecture', link: '/guide/architecture' }
              ]
            },
            {
              text: 'Plugins & SDK',
              items: [
                { text: 'Plugin SDK Overview', link: '/guide/plugin-development' },
                { text: 'System Settings Plugin Case', link: '/guide/plugin-systemsettings' }
              ]
            },
            {
              text: 'SDK API Reference',
              items: [
                {
                  text: 'Plugins',
                  collapsed: false,
                  items: [
                    {
                      text: 'Core Search & Actions',
                      link: '/guide/sdk-plugins-core',
                      collapsed: true,
                      items: [
                        { text: 'IAction (Actions)', link: '/guide/sdk-plugins-core#iaction' },
                        { text: 'IAliasProvider (Aliases)', link: '/guide/sdk-plugins-core#ialiasprovider' },
                        { text: 'IInstantResultProvider (Instant Results)', link: '/guide/sdk-plugins-core#iinstantresultprovider' },
                        { text: 'ISearchableItemProvider (Custom Databases)', link: '/guide/sdk-plugins-core#isearchableitemprovider' }
                      ]
                    },
                    {
                      text: 'System & Dialog Adapters',
                      link: '/guide/sdk-plugins-system',
                      collapsed: true,
                      items: [
                        { text: 'IActivePathCollector (Path Tracking)', link: '/guide/sdk-plugins-system#iactivepathcollector' },
                        { text: 'IFileDialogAdapter (File Dialogs)', link: '/guide/sdk-plugins-system#ifiledialogadapter' },
                        { text: 'IInlineSearchAdapter (Inline Search)', link: '/guide/sdk-plugins-system#iinlinesearchadapter' },
                        { text: 'IQuickNavigationProvider (Quick Nav)', link: '/guide/sdk-plugins-system#iquicknavigationprovider' },
                        { text: 'IDynamicActionProvider (Dynamic Menu)', link: '/guide/sdk-plugins-system#idynamicactionprovider' }
                      ]
                    },
                    {
                      text: 'UI & Layout Extensions',
                      link: '/guide/sdk-plugins-ui',
                      collapsed: true,
                      items: [
                        { text: 'ISidebarFilterProvider (Sidebar Filters)', link: '/guide/sdk-plugins-ui#isidebarfilterprovider' },
                        { text: 'IResultColumnProvider (Grid Columns)', link: '/guide/sdk-plugins-ui#iresultcolumnprovider' },
                        { text: 'IFilePreviewProvider (File Previews)', link: '/guide/sdk-plugins-ui#ifilepreviewprovider' },
                        { text: 'ITranslationProvider (Translations)', link: '/guide/sdk-plugins-ui#itranslationprovider' },
                        { text: 'IThemeProvider (Style Sheets)', link: '/guide/sdk-plugins-ui#ithemeprovider' }
                      ]
                    }
                  ]
                },
                {
                  text: 'Models & Support Contracts',
                  link: '/guide/sdk-abstractions',
                  collapsed: true,
                  items: [
                    { text: 'ISearchResult (Result Entries)', link: '/guide/sdk-abstractions#isearchresult' },
                    { text: 'ISearchResultAction (Action Details)', link: '/guide/sdk-abstractions#isearchresultaction' },
                    { text: 'IPluginSearchWindow (Window Handles)', link: '/guide/sdk-abstractions#ipluginsearchwindow' },
                    { text: 'IConfigurable (Config Schemas)', link: '/guide/sdk-abstractions#iconfigurable' },
                    { text: 'ITheme (Theme Dictionaries)', link: '/guide/sdk-abstractions#itheme' }
                  ]
                }
              ]
            },
            {
              text: 'Sponsorship',
              items: [
                { text: 'Donate', link: '/guide/donate' }
              ]
            }
          ]
        },
        outline: {
          label: 'On this page'
        },
        docFooter: {
          prev: 'Previous page',
          next: 'Next page'
        },
        sidebarMenuLabel: 'Menu',
        returnToTopLabel: 'Return to top',
        darkModeSwitchLabel: 'Appearance',
        lightModeSwitchTitle: 'Switch to light theme',
        darkModeSwitchTitle: 'Switch to dark theme'
      }
    },
    'zh-CN': {
      label: '简体中文',
      lang: 'zh-CN',
      link: '/zh-CN/',
      title: "SwiftList",
      description: "高性能、可扩展的 Windows 全局检索系统",
      themeConfig: {
        nav: [
          { text: '指南', link: '/zh-CN/guide/introduction' },
          { text: '插件开发', link: '/zh-CN/guide/plugin-development' }
        ],
        socialLinks: [
          { icon: 'github', link: 'https://github.com/SwiftList/SwiftList' }
        ],
        sidebar: {
          '/zh-CN/guide/': [
            {
              text: '入门指南',
              items: [
                { text: '简介', link: '/zh-CN/guide/introduction' },
                { text: '架构设计', link: '/zh-CN/guide/architecture' }
              ]
            },
            {
              text: '插件开发说明',
              items: [
                { text: 'Plugin SDK 概览', link: '/zh-CN/guide/plugin-development' },
                { text: '系统设置插件案例', link: '/zh-CN/guide/plugin-systemsettings' }
              ]
            },
            {
              text: 'SDK API 参考',
              items: [
                {
                  text: '插件',
                  collapsed: false,
                  items: [
                    {
                      text: '核心检索与动作',
                      link: '/zh-CN/guide/sdk-plugins-core',
                      collapsed: true,
                      items: [
                        { text: 'IAction (动作)', link: '/zh-CN/guide/sdk-plugins-core#iaction' },
                        { text: 'IAliasProvider (别名)', link: '/zh-CN/guide/sdk-plugins-core#ialiasprovider' },
                        { text: 'IInstantResultProvider (即时结果)', link: '/zh-CN/guide/sdk-plugins-core#iinstantresultprovider' },
                        { text: 'ISearchableItemProvider (搜索源)', link: '/zh-CN/guide/sdk-plugins-core#isearchableitemprovider' }
                      ]
                    },
                    {
                      text: '系统交互与适配',
                      link: '/zh-CN/guide/sdk-plugins-system',
                      collapsed: true,
                      items: [
                        { text: 'IActivePathCollector (路径搜集)', link: '/zh-CN/guide/sdk-plugins-system#iactivepathcollector' },
                        { text: 'IFileDialogAdapter (对话框适配)', link: '/zh-CN/guide/sdk-plugins-system#ifiledialogadapter' },
                        { text: 'IInlineSearchAdapter (内嵌搜索)', link: '/zh-CN/guide/sdk-plugins-system#iinlinesearchadapter' },
                        { text: 'IQuickNavigationProvider (鼠标导航)', link: '/zh-CN/guide/sdk-plugins-system#iquicknavigationprovider' },
                        { text: 'IDynamicActionProvider (动态动作)', link: '/zh-CN/guide/sdk-plugins-system#idynamicactionprovider' }
                      ]
                    },
                    {
                      text: '界面扩展与展示',
                      link: '/zh-CN/guide/sdk-plugins-ui',
                      collapsed: true,
                      items: [
                        { text: 'ISidebarFilterProvider (侧栏过滤)', link: '/zh-CN/guide/sdk-plugins-ui#isidebarfilterprovider' },
                        { text: 'IResultColumnProvider (结果表格列)', link: '/zh-CN/guide/sdk-plugins-ui#iresultcolumnprovider' },
                        { text: 'IFilePreviewProvider (文件预览)', link: '/zh-CN/guide/sdk-plugins-ui#ifilepreviewprovider' },
                        { text: 'ITranslationProvider (语言包)', link: '/zh-CN/guide/sdk-plugins-ui#itranslationprovider' },
                        { text: 'IThemeProvider (主题包)', link: '/zh-CN/guide/sdk-plugins-ui#ithemeprovider' }
                      ]
                    }
                  ]
                },
                {
                  text: '数据模型与辅助契约',
                  link: '/zh-CN/guide/sdk-abstractions',
                  collapsed: true,
                  items: [
                    { text: 'ISearchResult (结果模型)', link: '/zh-CN/guide/sdk-abstractions#isearchresult' },
                    { text: 'ISearchResultAction (动作行为契约)', link: '/zh-CN/guide/sdk-abstractions#isearchresultaction' },
                    { text: 'IPluginSearchWindow (搜索视窗句柄)', link: '/zh-CN/guide/sdk-abstractions#ipluginsearchwindow' },
                    { text: 'IConfigurable (配置表单)', link: '/zh-CN/guide/sdk-abstractions#iconfigurable' },
                    { text: 'ITheme (主题数据模型)', link: '/zh-CN/guide/sdk-abstractions#itheme' }
                  ]
                }
              ]
            },
            {
              text: '支持与赞助',
              items: [
                { text: '捐赠支持', link: '/zh-CN/guide/donate' }
              ]
            }
          ]
        },
        outline: {
          label: '本页目录'
        },
        docFooter: {
          prev: '上一页',
          next: '下一页'
        },
        sidebarMenuLabel: '菜单',
        returnToTopLabel: '返回顶部',
        darkModeSwitchLabel: '外观',
        lightModeSwitchTitle: '切换到浅色模式',
        darkModeSwitchTitle: '切换到深色模式'
      }
    }
  }
})
