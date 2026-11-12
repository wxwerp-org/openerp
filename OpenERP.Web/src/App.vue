<template>
  <div class="app-shell">
    <header class="app-header">
      <div class="app-header__inner">
        <RouterLink class="app-brand" to="/">OpenERP</RouterLink>
        <nav class="app-navigation" aria-label="主导航">
          <RouterLink class="app-navigation__link" to="/">服务连接</RouterLink>
          <RouterLink class="app-navigation__link" to="/shops">授权店铺</RouterLink>
        </nav>
      </div>
    </header>
    <RouterView />
  </div>
</template>
