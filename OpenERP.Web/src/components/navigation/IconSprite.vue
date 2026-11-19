<template>
  <svg
    class="pointer-events-none absolute size-0 overflow-hidden"
    aria-hidden="true"
    xmlns="http://www.w3.org/2000/svg"
  >
    <symbol id="i-chev-down" viewBox="0 0 16 16"><path d="m4 6 4 4 4-4" /></symbol>
    <symbol id="i-chev-right" viewBox="0 0 16 16"><path d="m6 4 4 4-4 4" /></symbol>
    <symbol id="i-chev-left" viewBox="0 0 16 16"><path d="m10 4-4 4 4 4" /></symbol>
    <symbol id="i-chev-up" viewBox="0 0 16 16"><path d="m4 10 4-4 4 4" /></symbol>
    <symbol id="i-close" viewBox="0 0 16 16">
      <path d="m4.2 4.2 7.6 7.6M11.8 4.2l-7.6 7.6" />
    </symbol>
    <symbol id="i-plus" viewBox="0 0 16 16"><path d="M8 3v10M3 8h10" /></symbol>
    <symbol id="i-search" viewBox="0 0 16 16">
      <circle cx="7.2" cy="7.2" r="4.5" />
      <path d="m10.6 10.6 3 3" />
    </symbol>
    <symbol id="i-filter" viewBox="0 0 16 16">
      <path d="M2.5 4h11M4.5 8h7M6.5 12h3" />
      <circle cx="6" cy="4" r="1" fill="currentColor" stroke="none" />
      <circle cx="10" cy="8" r="1" fill="currentColor" stroke="none" />
      <circle cx="8" cy="12" r="1" fill="currentColor" stroke="none" />
    </symbol>
    <symbol id="i-info" viewBox="0 0 16 16">
      <circle cx="8" cy="8" r="6.2" />
      <path d="M8 7.2v3.6" />
      <circle cx="8" cy="4.9" r=".75" fill="currentColor" stroke="none" />
    </symbol>
    <symbol id="i-help" viewBox="0 0 16 16">
      <circle cx="8" cy="8" r="6.2" />
      <path d="M6.1 6.1a2 2 0 1 1 2.8 1.83c-.55.25-.9.7-.9 1.27v.18" />
      <circle cx="8" cy="11.4" r=".7" fill="currentColor" stroke="none" />
    </symbol>
    <symbol id="i-calendar" viewBox="0 0 16 16">
      <rect x="2.5" y="3.5" width="11" height="10" rx="1.5" />
      <path d="M5 2v3M11 2v3M2.5 6.5h11" />
    </symbol>
    <symbol id="i-refresh" viewBox="0 0 16 16">
      <path d="M13.5 7A5.5 5.5 0 1 0 13 10.5" />
      <path d="M13.5 3v4h-4" />
    </symbol>
    <symbol id="i-sort" viewBox="0 0 8 12">
      <path d="M4 1 7 4H1L4 1Z" fill="currentColor" />
      <path d="M4 11 1 8h6L4 11Z" fill="currentColor" />
    </symbol>
    <symbol id="i-check" viewBox="0 0 16 16"><path d="m3.2 8.2 3.1 3.1 6.5-6.6" /></symbol>
    <symbol id="i-clock" viewBox="0 0 16 16">
      <circle cx="8" cy="8" r="6.2" />
      <path d="M8 4.5V8l2.4 1.5" />
    </symbol>
    <symbol id="i-download" viewBox="0 0 16 16">
      <path d="M8 2.4v7.2M5.2 7l2.8 2.8L10.8 7" />
      <path d="M3 12.3h10" />
    </symbol>
    <symbol id="i-download-circle" viewBox="0 0 20 20">
      <circle cx="10" cy="10" r="7.8" />
      <path d="M10 6v6M7.4 9.7l2.6 2.7 2.6-2.7" />
    </symbol>
    <symbol id="i-headset" viewBox="0 0 20 20">
      <path d="M4 10v-1a6 6 0 0 1 12 0v1" />
      <path
        d="M4 9h2.2v5H4.7A1.7 1.7 0 0 1 3 12.3v-1.6A1.7 1.7 0 0 1 4.7 9H4zM16 9h-2.2v5h1.5a1.7 1.7 0 0 0 1.7-1.7v-1.6A1.7 1.7 0 0 0 15.3 9H16z"
      />
      <path d="M13.8 15.8c-.8.9-2.1 1.4-3.8 1.4" />
    </symbol>
    <symbol id="i-bell" viewBox="0 0 20 20">
      <path d="M4.5 14.5h11l-1.4-2.1V9a4.1 4.1 0 0 0-8.2 0v3.4l-1.4 2.1z" />
      <path d="M8.4 16.5h3.2" />
    </symbol>
    <symbol id="i-user" viewBox="0 0 20 20">
      <circle cx="10" cy="7.3" r="3.1" />
      <path d="M4.3 17c.5-3 2.6-4.6 5.7-4.6s5.2 1.6 5.7 4.6" />
    </symbol>
    <symbol id="i-expand" viewBox="0 0 16 16">
      <path d="M6 3H3v3M10 3h3v3M6 13H3v-3M10 13h3v-3" />
    </symbol>
    <symbol id="i-settings" viewBox="0 0 16 16">
      <circle cx="8" cy="8" r="2.1" />
      <path
        d="M8 2.4v1.2M8 12.4v1.2M2.4 8h1.2M12.4 8h1.2M4.1 4.1l.9.9M11 11l.9.9M11.9 4.1l-.9.9M5 11l-.9.9"
      />
    </symbol>
    <symbol id="i-arrow-up" viewBox="0 0 12 12"><path d="M6 9.5v-7M3 5.5 6 2.5l3 3" /></symbol>
    <symbol id="i-arrow-down" viewBox="0 0 12 12"><path d="M6 2.5v7M3 6.5l3 3 3-3" /></symbol>
    <symbol id="i-more" viewBox="0 0 16 16">
      <circle cx="3" cy="8" r="1" fill="currentColor" stroke="none" />
      <circle cx="8" cy="8" r="1" fill="currentColor" stroke="none" />
      <circle cx="13" cy="8" r="1" fill="currentColor" stroke="none" />
    </symbol>
    <symbol id="i-more-vert" viewBox="0 0 16 16">
      <circle cx="8" cy="3" r="1" fill="currentColor" stroke="none" />
      <circle cx="8" cy="8" r="1" fill="currentColor" stroke="none" />
      <circle cx="8" cy="13" r="1" fill="currentColor" stroke="none" />
    </symbol>
    <symbol id="i-collapse" viewBox="0 0 20 20">
      <path d="M3 5h14M3 10h9M3 15h14" />
      <path d="m15 8-3 2 3 2" />
    </symbol>
    <symbol id="i-warning" viewBox="0 0 16 16">
      <path d="M8 2.5 14 13H2L8 2.5Z" />
      <path d="M8 6.2v3.1" />
      <circle cx="8" cy="11.3" r=".7" fill="currentColor" stroke="none" />
    </symbol>
    <symbol id="i-clipboard" viewBox="0 0 24 24">
      <rect x="5" y="4.5" width="14" height="16" rx="2" />
      <path d="M9 4.5V3.2h6v1.3M8.3 9h7.4M8.3 12.5h7.4M8.3 16h4.7" />
    </symbol>
    <symbol id="i-mail" viewBox="0 0 20 20">
      <rect x="2.5" y="4.5" width="15" height="11" rx="1.5" />
      <path d="m3.5 5.8 6.5 5 6.5-5" />
    </symbol>
    <symbol id="i-box" viewBox="0 0 20 20">
      <path d="m10 2.6 7 3.8v7.2l-7 3.8-7-3.8V6.4l7-3.8Z" />
      <path d="m3.3 6.5 6.7 3.7 6.7-3.7M10 10.2v7" />
    </symbol>
    <symbol id="i-truck" viewBox="0 0 20 20">
      <path d="M2.5 4.5h10v9h-10zM12.5 7h3l2 3v3.5h-5z" />
      <circle cx="6" cy="15" r="1.6" />
      <circle cx="15" cy="15" r="1.6" />
    </symbol>
    <symbol id="i-star" viewBox="0 0 16 16">
      <path d="m8 1.8 1.9 3.9 4.3.6-3.1 3 .8 4.3L8 11.6l-3.9 2 .8-4.3-3.1-3 4.3-.6L8 1.8Z" />
    </symbol>
    <symbol id="i-file" viewBox="0 0 20 20">
      <path d="M5 2.5h6l4 4v11H5z" />
      <path d="M11 2.5v4h4M7.5 10h5M7.5 13h5" />
    </symbol>
    <symbol id="i-external" viewBox="0 0 16 16">
      <path
        d="M9 3h4v4M13 3 7.5 8.5M12 9.5v2.7a1.3 1.3 0 0 1-1.3 1.3H4.3A1.3 1.3 0 0 1 3 12.2V5.8A1.3 1.3 0 0 1 4.3 4.5h2.6"
      />
    </symbol>
    <symbol id="i-layout" viewBox="0 0 18 18">
      <rect x="2" y="2.5" width="14" height="13" rx="1.5" />
      <path d="M2 11h14M7 2.5v8.5" />
    </symbol>
    <symbol id="i-nav-home" viewBox="0 0 24 24">
      <path d="m3 10.5 9-7.2 9 7.2V20H3v-9.5Z" />
      <path d="M9.5 20v-6h5v6" />
    </symbol>
    <symbol id="i-nav-product" viewBox="0 0 24 24">
      <path d="M4.3 8.2h15.4l-1 11.3H5.3L4.3 8.2Z" />
      <path d="M8.5 9V6.8a3.5 3.5 0 0 1 7 0V9" />
      <path d="M9 13.5h6" />
    </symbol>
    <symbol id="i-nav-sales" viewBox="0 0 24 24">
      <path d="M3.5 9.5 5.2 4h13.6l1.7 5.5" />
      <path d="M4.3 9.5h15.4M5.2 9.5v10h13.6v-10" />
      <path
        d="M3.5 9.5a2.3 2.3 0 0 0 4.2 1.1 2.3 2.3 0 0 0 4.3 0 2.3 2.3 0 0 0 4.3 0 2.3 2.3 0 0 0 4.2-1.1"
      />
    </symbol>
    <symbol id="i-nav-fba" viewBox="0 0 24 24">
      <rect x="3.5" y="3.5" width="17" height="17" rx="2.5" />
      <path d="M8.5 10.2v5.3M14.5 10.2v5.3M8.5 11.2a3 3 0 0 1 6 0M8 18c2.6.8 5.4.8 8 0" />
    </symbol>
    <symbol id="i-nav-purchase" viewBox="0 0 24 24">
      <path d="M2.8 4h2.4l1.8 10.2h10.8L20 7H6" />
      <circle cx="9" cy="19" r="1.4" />
      <circle cx="17" cy="19" r="1.4" />
      <path d="M9.3 13.2h8.2" />
    </symbol>
    <symbol id="i-nav-warehouse" viewBox="0 0 24 24">
      <path d="m3 10.3 9-5.8 9 5.8V21H3V10.3Z" />
      <path d="M7.5 21v-7h9v7M7.5 17.5h9" />
    </symbol>
    <symbol id="i-nav-logistics" viewBox="0 0 24 24">
      <path d="M2.5 5h12v13h-12zM14.5 9.2h4.1l2.4 3.7V18h-6.5z" />
      <circle cx="6.4" cy="18.8" r="1.7" />
      <circle cx="18.2" cy="18.8" r="1.7" />
      <path d="M8.5 8h4M8.5 11h3" />
    </symbol>
    <symbol id="i-nav-ad" viewBox="0 0 24 24">
      <path d="M5 10v4h3l7 4V6l-7 4H5Z" />
      <path d="M8 14.2 9.3 19h3l-1.2-4M18 8.5v7M20.8 6.5v11" />
    </symbol>
    <symbol id="i-nav-service" viewBox="0 0 24 24">
      <rect x="2.5" y="5" width="19" height="14" rx="2" />
      <path d="m3.5 6.3 8.5 6.4 8.5-6.4" />
    </symbol>
    <symbol id="i-nav-finance" viewBox="0 0 24 24">
      <circle cx="12" cy="12" r="8.7" />
      <path d="M7.9 8.2 12 12.4l4.1-4.2M12 12.4V17M9.2 12.5h5.6M9.2 14.8h5.6" />
    </symbol>
    <symbol id="i-nav-stats" viewBox="0 0 24 24">
      <path d="M4 20V10h4v10M10 20V4h4v16M16 20v-7h4v7" />
      <path d="M2.5 21h19" />
    </symbol>
    <symbol id="i-nav-tools" viewBox="0 0 24 24">
      <path
        d="M14.2 6.2a4.3 4.3 0 0 0-5.5 5.5L3.5 17l2.3 2.3 5.3-5.2a4.3 4.3 0 0 0 5.5-5.5l-2.5 2.5-2.2-2.2 2.3-2.7Z"
      />
      <path d="M16 4.5 19.5 8" />
    </symbol>
    <symbol id="i-nav-settings" viewBox="0 0 24 24">
      <path
        d="m9.5 3.2.6-1.2h3.8l.6 1.2 1.4.8 1.3-.3 2.7 2.7-.3 1.3.8 1.4 1.3.6v3.8l-1.3.6-.8 1.4.3 1.3-2.7 2.7-1.3-.3-1.4.8-.6 1.2h-3.8l-.6-1.2-1.4-.8-1.3.3-2.7-2.7.3-1.3-.8-1.4-1.3-.6v-3.8l1.3-.6.8-1.4-.3-1.3 2.7-2.7 1.3.3 1.4-.8Z"
      />
      <circle cx="12" cy="12" r="2.8" />
    </symbol>
    <symbol id="i-guide-shield" viewBox="0 0 36 36">
      <path
        d="M18 3.8 29 7.7v9.1c0 7.1-4.5 12.1-11 15.4-6.5-3.3-11-8.3-11-15.4V7.7L18 3.8Z"
        stroke-width="1.5"
      />
      <path
        d="m12.7 17.8 3.6 3.5 7.2-7.3"
        style="stroke: var(--color-primary)"
        stroke-width="1.8"
      />
    </symbol>
    <symbol id="i-guide-account" viewBox="0 0 36 36">
      <circle cx="15.4" cy="12" r="5.2" stroke-width="1.4" />
      <path d="M5.8 30.5c.6-6.1 4.1-9.3 9.6-9.3 2 0 3.7.5 5.1 1.4" stroke-width="1.4" />
      <rect
        x="21.2"
        y="19.4"
        width="10.1"
        height="8.2"
        rx="1.5"
        style="fill: var(--color-primary)"
        stroke="none"
      />
      <path
        d="M23.2 19.3v-2a3 3 0 0 1 6 0v2"
        style="stroke: var(--color-primary)"
        stroke-width="1.6"
        fill="none"
      />
      <circle cx="26.2" cy="23.4" r="1.1" style="fill: var(--color-white)" stroke="none" />
    </symbol>
    <symbol id="i-guide-product" viewBox="0 0 36 36">
      <rect x="4" y="6.2" width="12" height="14" rx="1.3" stroke-width="1.4" />
      <rect
        x="20"
        y="6.2"
        width="12"
        height="14"
        rx="1.3"
        stroke-width="1.4"
        style="fill: var(--color-guide-product-bg); stroke: var(--color-primary)"
      />
      <path
        d="M7.2 10.2h5.6M7.2 13.7h5.6M7.2 17.2h3.7M23.2 10.2h5.6M23.2 13.7h5.6M23.2 17.2h3.7"
        stroke-width="1.2"
      />
      <path d="M7 25h9M16 25h13M7 30h9M16 30h9" stroke-width="1.4" />
      <circle
        cx="27.8"
        cy="29.8"
        r="3.2"
        style="fill: var(--color-white); stroke: var(--color-primary)"
        stroke-width="1.4"
      />
      <path
        d="M26.7 29.9h2.2M27.8 28.8v2.2"
        style="stroke: var(--color-primary)"
        stroke-width="1.1"
      />
    </symbol>
    <symbol id="i-guide-supply" viewBox="0 0 36 36">
      <circle cx="18" cy="17" r="3.2" style="stroke: var(--color-primary)" stroke-width="1.6" />
      <circle cx="8" cy="9" r="3" stroke-width="1.4" />
      <circle cx="28" cy="9" r="3" stroke-width="1.4" />
      <circle cx="8" cy="27" r="3" stroke-width="1.4" />
      <circle cx="28" cy="27" r="3" stroke-width="1.4" />
      <path
        d="m10.7 10.8 4.2 4M25.3 10.8l-4.2 4M10.7 25.2l4.2-4M25.3 25.2l-4.2-4"
        stroke-width="1.4"
      />
    </symbol>
    <symbol id="i-guide-more" viewBox="0 0 36 36">
      <circle cx="8" cy="18" r="2.6" fill="none" stroke-width="1.8" />
      <circle cx="18" cy="18" r="2.6" fill="none" stroke-width="1.8" />
      <circle cx="28" cy="18" r="2.6" fill="none" stroke-width="1.8" />
    </symbol>
    <symbol id="i-undo" viewBox="0 0 16 16">
      <path d="M5 5 2 8l3 3M2.5 8H10a3.5 3.5 0 0 1 0 7H8" />
    </symbol>
    <symbol id="i-redo" viewBox="0 0 16 16">
      <path d="m11 5 3 3-3 3M13.5 8H6a3.5 3.5 0 0 0 0 7h2" />
    </symbol>
    <symbol id="i-align" viewBox="0 0 16 16">
      <path d="M2.5 3.5h11M2.5 6.7h8M2.5 9.9h11M2.5 13.1h6" />
    </symbol>
    <symbol id="i-list" viewBox="0 0 16 16">
      <path d="M5.5 4h8M5.5 8h8M5.5 12h8" />
      <circle cx="2.7" cy="4" r=".8" fill="currentColor" stroke="none" />
      <circle cx="2.7" cy="8" r=".8" fill="currentColor" stroke="none" />
      <circle cx="2.7" cy="12" r=".8" fill="currentColor" stroke="none" />
    </symbol>
    <symbol id="i-indent" viewBox="0 0 16 16">
      <path d="M6.5 3.5h7M6.5 8h7M6.5 12.5h7M2.5 5.8 4.6 8l-2.1 2.2" />
    </symbol>
    <symbol id="i-hr" viewBox="0 0 16 16"><path d="M2.5 8h11" /></symbol>
    <symbol id="i-clear" viewBox="0 0 16 16">
      <path d="M3 12.5 10.8 4.7a1.7 1.7 0 0 1 2.4 0l.1.1a1.7 1.7 0 0 1 0 2.4L7.5 13H3.6L3 12.5Z" />
      <path d="m8.6 6.9 2.5 2.5M7 13h6" />
    </symbol>
    <symbol id="i-pen" viewBox="0 0 16 16">
      <path d="m3 13 1-3.4 6.6-6.5a1.4 1.4 0 0 1 2 0l.3.3a1.4 1.4 0 0 1 0 2L6.4 12 3 13Z" />
      <path d="m9.8 4.2 2 2M3 15h10" />
    </symbol>
    <symbol id="i-fill" viewBox="0 0 16 16">
      <path d="m7.2 3 4.6 4.6-4.4 4.4a1.7 1.7 0 0 1-2.4 0L2.8 9.8a1.7 1.7 0 0 1 0-2.4L7.2 3Z" />
      <path
        d="M4.2 9h6.4M11.6 11.6c.6-1.1 1.9-1.1 2.5 0 .5.9-.3 2.1-1.3 2.1s-1.8-1.2-1.2-2.1Z"
        fill="currentColor"
        stroke="none"
      />
    </symbol>
    <symbol id="i-avatar" viewBox="0 0 20 20">
      <circle cx="10" cy="7.4" r="3" />
      <path d="M4.4 16c.7-2.7 2.6-4.1 5.6-4.1s5 1.4 5.6 4.1" />
    </symbol>
    <symbol id="i-shield-check" viewBox="0 0 20 20">
      <path d="m10 2.3 6 2v4.9c0 4.1-2.6 6.9-6 8.6-3.4-1.7-6-4.5-6-8.6V4.3l6-2Z" />
      <path d="m7 9.8 2 2 4-4.1" />
    </symbol>
    <symbol id="i-lock" viewBox="0 0 20 20">
      <rect x="4" y="8.6" width="12" height="9" rx="1.6" />
      <path d="M6.8 8.5V6.4a3.2 3.2 0 0 1 6.4 0v2.1" />
      <circle cx="10" cy="13.1" r="1" />
    </symbol>
    <symbol id="i-logout" viewBox="0 0 16 16">
      <path d="M6.2 3H3.7c-.6 0-1.1.5-1.1 1.1v7.8c0 .6.5 1.1 1.1 1.1h2.5" />
      <path d="m10.6 4.8 3.2 3.2-3.2 3.2" />
      <path d="M13.4 8H6.3" />
    </symbol>
    <symbol id="i-analytics" viewBox="0 0 20 20">
      <path d="M3 17V9h3.5v8M8.2 17V3h3.5v14M13.5 17V7H17v10" />
    </symbol>
    <symbol id="i-copy" viewBox="0 0 16 16">
      <rect x="5" y="5" width="8" height="9" rx="1.2" />
      <path d="M3 10.5V3.7A1.2 1.2 0 0 1 4.2 2.5H9" />
    </symbol>
  </svg>
</template>
