import type { ComputedRef, InjectionKey } from 'vue'

export const tableWrapKey: InjectionKey<ComputedRef<boolean>> = Symbol('AppTableWrap')
