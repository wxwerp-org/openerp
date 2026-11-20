import { type Page } from '@playwright/test'
import type { Account } from '../../src/account/session'

export const VISUAL_TOKEN = 'visual-access-token'

export const visualAccount: Account = {
  id: '019ab104-7d00-7000-8000-000000000001',
  role: 'Administrator',
  username: 'admin',
  name: '验收管理员',
  phone: null,
  email: null,
  lastLoginAt: '2026-11-18T08:00:00.000Z',
  expiresAt: '2099-01-01T00:00:00.000Z',
}

export type ProbeControl = {
  key: string
  role: string
  name: string
  fontSize: string
  fontWeight: string
  lineHeight: string
  color: string
  background: string
  borderColor: string
  borderWidth: string
  borderRadius: string
  height: number
  padding: string
  boxShadow: string
  opacity: string
  disabled: boolean
}

export type ProbeTypography = {
  key: string
  tag: string
  text: string
  fontSize: string
  fontWeight: string
  lineHeight: string
  color: string
}

export type ProbeRegion = {
  label: string
  scrollHeight: number
  clientHeight: number
}

export type Probe = {
  layout: {
    innerWidth: number
    innerHeight: number
    documentScrollWidth: number
    bodyScrollHeight: number
    mainRect: { x: number; y: number; width: number; height: number } | null
    asideRect: { x: number; y: number; width: number; height: number } | null
    headerRect: { x: number; y: number; width: number; height: number } | null
    regions: ProbeRegion[]
  }
  controls: ProbeControl[]
  typography: ProbeTypography[]
  palette: {
    color: string[]
    background: string[]
    borderColor: string[]
    fontSize: string[]
    lineHeight: string[]
    borderRadius: string[]
    boxShadow: string[]
  }
}

export type Scenario = {
  name: string
  path: string
  width?: number
  height?: number
  session?: 'workspace' | 'anonymous' | 'uninitialized'
  prepare?: (page: Page) => Promise<unknown>
  afterGoto?: (page: Page) => Promise<unknown>
  waitFor?: (page: Page) => Promise<unknown>
}

export const desktop = { width: 1440, height: 900 }
export const wide = { width: 1920, height: 1080 }

export async function mockHub(page: Page): Promise<void> {
  await page.route('**/api/hubs/erp/**', (route) => route.abort())
}

export async function mockAnonymousSession(page: Page): Promise<void> {
  await page.route('**/api/auth/status', (route) => route.fulfill({ json: { initialized: true } }))
  await page.route('**/api/auth/csrf', (route) =>
    route.fulfill({ json: { requestToken: 'visual-csrf-token' } }),
  )
  await page.route('**/api/auth/refresh', (route) =>
    route.fulfill({ status: 401, json: { title: 'Unauthorized' } }),
  )
}

export async function mockUninitializedSession(page: Page): Promise<void> {
  await page.route('**/api/auth/status', (route) => route.fulfill({ json: { initialized: false } }))
  await page.route('**/api/auth/csrf', (route) =>
    route.fulfill({ json: { requestToken: 'visual-csrf-token' } }),
  )
}

async function applySession(page: Page, session: Scenario['session']): Promise<void> {
  if (session === 'anonymous') return mockAnonymousSession(page)
  if (session === 'uninitialized') return mockUninitializedSession(page)
  await page.route('**/api/auth/status', (route) => route.fulfill({ json: { initialized: true } }))
  await page.route('**/api/auth/csrf', (route) =>
    route.fulfill({ json: { requestToken: 'visual-csrf-token' } }),
  )
  await page.route('**/api/auth/refresh', (route) =>
    route.fulfill({
      json: {
        accessToken: VISUAL_TOKEN,
        accessTokenExpiresAt: '2026-11-19T08:20:00.000Z',
        account: visualAccount,
      },
    }),
  )
  await mockHub(page)
}

export async function captureScenario(
  page: Page,
  scenario: Scenario,
  outDir: string,
): Promise<Probe> {
  await applySession(page, scenario.session)
  if (scenario.prepare) await scenario.prepare(page)
  await page.goto(scenario.path, { waitUntil: 'domcontentloaded' })
  if (scenario.waitFor) await scenario.waitFor(page)
  if (scenario.afterGoto) await scenario.afterGoto(page)
  await page.evaluate(() => document.fonts.ready)
  await page.addStyleTag({
    content:
      '*,*::before,*::after{animation:none!important;transition:none!important;caret-color:transparent!important}',
  })
  await page.evaluate(
    () =>
      new Promise<void>((resolveFrame) => {
        requestAnimationFrame(() => requestAnimationFrame(() => resolveFrame()))
      }),
  )
  const probe = await collectProbe(page)
  await page.screenshot({ path: `${outDir}/${scenario.name}.png` })
  return probe
}

export async function collectProbe(page: Page): Promise<Probe> {
  return page.evaluate(() => {
    function accessibleName(el: Element): string {
      const aria = el.getAttribute('aria-label')
      if (aria) return aria.trim()
      const labelledby = el.getAttribute('aria-labelledby')
      if (labelledby) {
        const label = labelledby
          .split(/\s+/)
          .map((id) => document.getElementById(id)?.textContent ?? '')
          .join(' ')
          .replace(/\s+/g, ' ')
          .trim()
        if (label) return label
      }
      if (
        el instanceof HTMLInputElement ||
        el instanceof HTMLSelectElement ||
        el instanceof HTMLTextAreaElement
      ) {
        const labels = el.labels
        if (labels && labels.length > 0) {
          return (labels[0].textContent ?? '').replace(/\s+/g, ' ').trim()
        }
        const placeholder = el.getAttribute('placeholder')
        if (placeholder) return placeholder
      }
      const text = (el.textContent ?? '').replace(/\s+/g, ' ').trim()
      if (text) return text
      const title = el.getAttribute('title')
      if (title) return title
      return el.tagName.toLowerCase()
    }

    function isVisible(el: Element): boolean {
      const rect = el.getBoundingClientRect()
      if (rect.width <= 0 || rect.height <= 0) return false
      const style = getComputedStyle(el)
      return style.display !== 'none' && style.visibility !== 'hidden' && Number(style.opacity) > 0
    }

    function roundRect(el: Element): { x: number; y: number; width: number; height: number } {
      const rect = el.getBoundingClientRect()
      return {
        x: Math.round(rect.x),
        y: Math.round(rect.y),
        width: Math.round(rect.width),
        height: Math.round(rect.height),
      }
    }

    function styleRecord(el: Element) {
      const style = getComputedStyle(el)
      return {
        fontSize: style.fontSize,
        fontWeight: style.fontWeight,
        lineHeight: style.lineHeight,
        color: style.color,
        background: style.backgroundColor,
        borderColor: style.borderTopColor,
        borderWidth: style.borderTopWidth,
        borderRadius: style.borderTopLeftRadius,
        height: Math.round(el.getBoundingClientRect().height),
        padding: style.padding,
        boxShadow: style.boxShadow,
        opacity: style.opacity,
      }
    }

    const seen = new Map<string, number>()
    function withOccurrence(key: string): string {
      const index = seen.get(key) ?? 0
      seen.set(key, index + 1)
      return `${key}#${index}`
    }

    const controlSelector =
      'button,a[href],input,select,textarea,[role="button"],[role="menuitem"],[role="tab"],[role="checkbox"],[role="combobox"],[role="link"],[role="switch"]'
    const controls = Array.from(document.querySelectorAll(controlSelector))
      .filter((el) => isVisible(el) && !(el instanceof HTMLInputElement && el.type === 'hidden'))
      .map((el) => {
        const name = accessibleName(el)
        const role = el.getAttribute('role') ?? el.tagName.toLowerCase()
        const style = styleRecord(el)
        const disabled =
          (el as HTMLInputElement).disabled === true || el.getAttribute('aria-disabled') === 'true'
        return {
          key: withOccurrence(`${role}|${name}`),
          role,
          name,
          ...style,
          disabled,
        }
      })
      .sort((left, right) => left.key.localeCompare(right.key))

    const typographySeen = new Map<string, number>()
    function textKey(tag: string, text: string): string {
      const base = `${tag}|${text}`
      const index = typographySeen.get(base) ?? 0
      typographySeen.set(base, index + 1)
      return `${base}#${index}`
    }

    const typography = Array.from(document.querySelectorAll('body *'))
      .filter((el) => {
        if (!isVisible(el)) return false
        if (el.childElementCount > 0) return false
        const text = (el.textContent ?? '').replace(/\s+/g, ' ').trim()
        return text.length > 0
      })
      .map((el) => {
        const text = (el.textContent ?? '').replace(/\s+/g, ' ').trim()
        const style = styleRecord(el)
        return {
          key: textKey(el.tagName.toLowerCase(), text),
          tag: el.tagName.toLowerCase(),
          text,
          fontSize: style.fontSize,
          fontWeight: style.fontWeight,
          lineHeight: style.lineHeight,
          color: style.color,
        }
      })
      .sort((left, right) => left.key.localeCompare(right.key))

    const regions = Array.from(document.querySelectorAll('[role="region"]'))
      .filter((el) => isVisible(el))
      .map((el) => ({
        label: accessibleName(el),
        scrollHeight: Math.round(el.scrollHeight),
        clientHeight: Math.round(el.clientHeight),
      }))
      .sort((left, right) => left.label.localeCompare(right.label))

    const uniq = (values: string[]): string[] => [...new Set(values)].sort()
    const main = document.querySelector('main')
    const aside = document.querySelector('aside')
    const header = document.querySelector('header')

    return {
      layout: {
        innerWidth: window.innerWidth,
        innerHeight: window.innerHeight,
        documentScrollWidth: document.documentElement.scrollWidth,
        bodyScrollHeight: document.body.scrollHeight,
        mainRect: main ? roundRect(main) : null,
        asideRect: aside ? roundRect(aside) : null,
        headerRect: header ? roundRect(header) : null,
        regions,
      },
      controls,
      typography,
      palette: {
        color: uniq(controls.map((control) => control.color)),
        background: uniq(controls.map((control) => control.background)),
        borderColor: uniq(controls.map((control) => control.borderColor)),
        fontSize: uniq([...controls, ...typography].map((item) => item.fontSize)),
        lineHeight: uniq([...controls, ...typography].map((item) => item.lineHeight)),
        borderRadius: uniq(controls.map((control) => control.borderRadius)),
        boxShadow: uniq(controls.map((control) => control.boxShadow)),
      },
    }
  })
}
