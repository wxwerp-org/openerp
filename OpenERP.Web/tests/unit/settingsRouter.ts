import { createMemoryHistory, createRouter } from 'vue-router'
import BusinessConfigView from '../../src/views/BusinessConfigView.vue'

export function createSettingsRouter() {
  const history = createMemoryHistory()
  history.replace('/settings')
  return createRouter({
    history,
    routes: [{ path: '/settings', name: 'business', component: BusinessConfigView }],
  })
}
