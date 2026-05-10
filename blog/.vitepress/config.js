import DefaultTheme from 'vitepress/theme'
import './custom.css'

export default {
  title: 'FindNeedle Blog',
  description: 'Updates, tutorials, and technical posts about FindNeedle',
  base: '/blog/',
  themeConfig: {
    nav: [
      { text: 'Home', link: '/' },
      { text: 'Posts', link: '/posts/' },
      { text: 'About', link: '/about' }
    ],
    sidebar: {
      '/posts/': [
        {
          text: 'Posts',
          items: [
            { text: 'Welcome', link: '/posts/welcome' }
          ]
        }
      ]
    },
    socialLinks: [
      { icon: 'github', link: 'https://github.com/guscatalano/findneedle' }
    ],
    footer: {
      message: 'Released under the MIT License.',
      copyright: 'Copyright © 2026 Gus Catalano'
    }
  }
}
