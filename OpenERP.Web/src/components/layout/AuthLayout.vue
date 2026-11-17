<template>
  <main class="auth-layout">
    <div class="auth-shell">
      <div class="auth-brand">
        <svg viewBox="0 0 32 32" aria-hidden="true">
          <path d="M4.7 25.9 12.9 6.2c1.2-2.9 5.1-2.9 6.4 0l2.1 5" fill="none" stroke="#0968FF" stroke-width="4.2" stroke-linecap="round" stroke-linejoin="round" />
          <path d="M8.7 26 19.3 6.3c1-1.8 3.7-1.6 4.6.3 1 2.1 2.2 4.9 3.1 7.5.7 1.8.5 3.9-.5 5.5l-2.3 3.3" fill="none" stroke="#0968FF" stroke-width="4" stroke-linecap="round" stroke-linejoin="round" />
          <path d="M9.7 19.3h8.1" fill="none" stroke="#0968FF" stroke-width="3.6" stroke-linecap="round" />
        </svg>
        <span>Open ERP</span>
      </div>
      <div class="auth-card"><slot /></div>
    </div>
  </main>
</template>

<style scoped>
:global(body:has(.auth-layout)) { min-width: 0; overflow: auto; }

.auth-layout {
  display: grid;
  min-height: 100dvh;
  padding: clamp(20px, 4vw, 48px);
  place-items: center;
  background: var(--page);
}
.auth-shell {
  width: min(100%, 420px);
  min-width: 0;
}
.auth-brand {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 10px;
  margin-bottom: 20px;
}
.auth-brand svg {
  width: 32px;
  height: 32px;
  flex: none;
}
.auth-brand span {
  color: var(--text);
  font-size: 18px;
  font-weight: 600;
  line-height: 28px;
  letter-spacing: .3px;
}
.auth-card {
  width: 100%;
  padding: 32px;
  border: 1px solid var(--line);
  border-radius: var(--radius-card);
  background: var(--surface);
  box-shadow: 0 4px 14px rgba(177, 199, 226, .20);
}
.auth-card :deep(h1) {
  margin: 0;
  color: var(--text);
  font-size: 22px;
  font-weight: 600;
  line-height: 32px;
}
</style>
