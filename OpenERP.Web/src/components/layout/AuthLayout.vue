<template>
  <main class="auth-layout">
    <div class="auth-card"><slot /></div>
  </main>
</template>

<style scoped>
:global(body:has(.auth-layout)) { min-width: 0; overflow: auto; }

.auth-layout {
  display: grid;
  min-height: 100dvh;
  padding: 24px;
  place-items: center;
  background: var(--page);
}
.auth-card {
  width: min(100%, 400px);
  padding: clamp(24px, 6vw, 36px);
  border: 1px solid var(--line);
  border-radius: 12px;
  background: var(--surface);
  box-shadow: 0 8px 28px #17335a12;
}
.auth-card :deep(h1) { margin: 0; font-size: 24px; }
</style>
