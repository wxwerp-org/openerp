<script setup lang="ts">
import { useRouter } from 'vue-router'

const router = useRouter()

function goHome(): void {
  void router.push('/')
}
</script>

<template>
  <section class="page page-placeholder active" id="page-not-found">
    <div class="simple-page">
      <div class="simple-page-head">
        <div><h1>页面不存在</h1><p>地址可能有误，或页面已被移除。</p></div>
        <span class="tag gray">404</span>
      </div>
      <div class="simple-page-card">
        <svg aria-hidden="true" class="ic"><use href="#i-warning"/></svg>
        <strong>找不到这个页面</strong>
        <span>请检查链接，或返回首页继续操作。</span>
        <button class="btn primary" type="button" @click="goHome">返回首页</button>
      </div>
    </div>
  </section>
</template>
