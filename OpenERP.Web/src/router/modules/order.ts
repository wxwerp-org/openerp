import { AppRouteRecord } from '@/types/router'

/** 订单管理：订单列表；店铺：授权店铺 */
export const orderRoutes: AppRouteRecord = {
  path: '/order',
  name: 'Order',
  component: '/index/index',
  meta: {
    title: 'menus.order.title',
    icon: 'ri:file-list-3-line'
  },
  children: [
    {
      path: 'list',
      name: 'OrderList',
      component: '/order/list',
      meta: {
        title: 'menus.order.list',
        icon: 'ri:list-check-2',
        keepAlive: true,
        isDefault: true
      }
    },
    {
      path: 'detail/:id',
      name: 'OrderDetail',
      component: '/order/detail',
      meta: {
        title: 'menus.order.detail',
        isHide: true,
        keepAlive: false,
        activePath: '/order/list'
      }
    },
    {
      path: 'report',
      name: 'OrderReport',
      component: '/order/report',
      meta: {
        title: 'menus.order.report',
        icon: 'ri:bar-chart-2-line',
        keepAlive: true
      }
    },
    {
      path: 'sync',
      name: 'OrderSync',
      component: '/order/sync',
      meta: {
        title: 'menus.order.sync',
        icon: 'ri:refresh-line',
        keepAlive: false
      }
    }
  ]
}

export const shopRoutes: AppRouteRecord = {
  path: '/shop',
  name: 'Shop',
  component: '/index/index',
  meta: {
    title: 'menus.erpShop.title',
    icon: 'ri:store-2-line'
  },
  children: [
    {
      path: 'list',
      name: 'ShopList',
      component: '/shop/list',
      meta: {
        title: 'menus.erpShop.list',
        icon: 'ri:shield-check-line',
        keepAlive: true
      }
    }
  ]
}
