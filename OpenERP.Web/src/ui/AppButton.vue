<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink, type RouteLocationRaw } from 'vue-router'

const props = withDefaults(
  defineProps<{
    variant?: 'default' | 'primary' | 'soft' | 'text'
    size?: 'md' | 'lg'
    type?: 'button' | 'submit' | 'reset'
    disabled?: boolean
    href?: string
    to?: RouteLocationRaw
  }>(),
  {
    variant: 'default',
    size: 'md',
    type: 'button',
    disabled: false,
    href: undefined,
    to: undefined,
  },
)

const sizeClass: Record<'md' | 'lg', string> = {
  md: 'h-[var(--control-h)] min-h-[var(--control-h)]',
  lg: 'h-[var(--control-h-lg)] min-h-[var(--control-h-lg)]',
}

const lineHeightClass: Record<'md' | 'lg', string> = {
  md: 'text-12/28',
  lg: 'text-12/28',
}

const variantClass: Record<'default' | 'primary' | 'soft' | 'text', string> = {
  default:
    'border-input bg-white hover:border-input-hover hover:bg-page-soft disabled:cursor-default disabled:border-line disabled:bg-page-soft disabled:text-placeholder disabled:hover:border-line disabled:hover:bg-page-soft',
  primary:
    'border-primary bg-primary text-white hover:border-primary-hover hover:bg-primary-hover disabled:cursor-default disabled:border-primary-disabled disabled:bg-primary-disabled disabled:text-white disabled:hover:border-primary-disabled disabled:hover:bg-primary-disabled',
  soft: 'border-transparent bg-soft-bg hover:bg-soft-bg-hover disabled:cursor-default disabled:text-placeholder',
  text: 'border-transparent bg-transparent text-primary hover:bg-primary-faint disabled:cursor-default',
}

const colorClass: Record<'default' | 'primary' | 'soft' | 'text', string> = {
  default: 'text-ink',
  primary: 'text-white',
  soft: 'text-ink',
  text: 'text-primary',
}

const classes = computed(() => [
  'inline-flex shrink-0 items-center justify-center gap-5 whitespace-nowrap rounded-4 border',
  lineHeightClass[props.size],
  sizeClass[props.size],
  props.variant === 'text' ? 'px-4' : 'px-10',
  colorClass[props.variant],
  variantClass[props.variant],
  props.disabled && (props.to || props.href) ? 'pointer-events-none' : '',
])

const tag = computed(() => (props.to ? RouterLink : props.href ? 'a' : 'button'))
const rootProps = computed<Record<string, unknown>>(() => {
  if (props.to) return linkProps({ to: props.to })
  if (props.href) return linkProps({ href: props.href })
  return { type: props.type, disabled: props.disabled }
})

function linkProps(base: Record<string, unknown>): Record<string, unknown> {
  if (!props.disabled) return base
  return { ...base, 'aria-disabled': 'true', tabindex: -1 }
}
</script>

<template>
  <component
    :is="tag"
    v-bind="rootProps"
    :class="classes"
    :data-variant="variant"
    :data-size="size"
  >
    <slot />
  </component>
</template>
