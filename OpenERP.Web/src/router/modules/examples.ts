import { AppRouteRecord } from '@/types/router'

export const examplesRoutes: AppRouteRecord = {
  path: '/examples',
  name: 'Examples',
  component: '/index/index',
  meta: {
    title: 'menus.examples.title',
    icon: 'ri:sparkling-line'
  },
  children: [
    {
      path: 'permission',
      name: 'Permission',
      component: '',
      meta: {
        title: 'menus.examples.permission.title',
        icon: 'ri:fingerprint-line'
      },
      children: [
        {
          path: 'button-auth',
          name: 'PermissionButtonAuth',
          component: '/examples/permission/button-auth',
          meta: {
            title: 'menus.examples.permission.buttonAuth',
            icon: 'ri:mouse-line',
            keepAlive: true,
            authList: [
              { title: '新增', authMark: 'add' },
              { title: '编辑', authMark: 'edit' },
              { title: '删除', authMark: 'delete' },
              { title: '导出', authMark: 'export' },
              { title: '查看', authMark: 'view' },
              { title: '发布', authMark: 'publish' },
              { title: '配置', authMark: 'config' },
              { title: '管理', authMark: 'manage' }
            ]
          }
        },
        {
          path: 'page-visibility',
          name: 'PermissionPageVisibility',
          component: '/examples/permission/page-visibility',
          meta: {
            title: 'menus.examples.permission.pageVisibility',
            icon: 'ri:user-3-line',
            keepAlive: true,
            roles: ['R_SUPER']
          }
        }
      ]
    },
    {
      path: 'tabs',
      name: 'Tabs',
      component: '/examples/tabs',
      meta: {
        title: 'menus.examples.tabs',
        icon: 'ri:price-tag-line'
      }
    },
    {
      path: 'forms',
      name: 'Forms',
      component: '/examples/forms',
      meta: {
        title: 'menus.examples.forms',
        icon: 'ri:table-view',
        keepAlive: true
      }
    },
    {
      path: 'form/search-bar',
      name: 'SearchBar',
      component: '/examples/forms/search-bar',
      meta: {
        title: 'menus.examples.searchBar',
        icon: 'ri:table-line',
        keepAlive: true
      }
    }
  ]
}
