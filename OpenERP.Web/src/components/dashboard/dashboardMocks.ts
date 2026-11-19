export interface PerformanceComparison {
  label: string
  value: string
  color?: string
}

export interface PerformanceStat {
  label: string
  value: string
  comparisons: PerformanceComparison[]
}

export const performancePeriods = [
  '今日',
  '昨日',
  '近7天',
  '近30天',
  '本月',
  '上月',
  '今年',
  '自定义',
]

export const performanceStats: PerformanceStat[] = [
  {
    label: '销量',
    value: '1,286',
    comparisons: [
      { label: '昨日全天', value: '1,142' },
      { label: '去年全天', value: '986' },
    ],
  },
  {
    label: '订单量',
    value: '842',
    comparisons: [
      { label: '昨日全天', value: '769' },
      { label: '去年全天', value: '712' },
    ],
  },
  {
    label: '销售额',
    value: '$24,860',
    comparisons: [
      { label: '昨日全天', value: '$21,580' },
      { label: '去年全天', value: '$18,640' },
    ],
  },
  {
    label: '净销售额',
    value: '$18,420',
    comparisons: [
      { label: '昨日全天', value: '$16,510' },
      { label: '去年全天', value: '$14,960' },
    ],
  },
  {
    label: '广告花费',
    value: '$3,264',
    comparisons: [
      { label: '环比', value: '-4.6%', color: 'var(--color-success)' },
      { label: '同比', value: '-2.1%', color: 'var(--color-success)' },
    ],
  },
  {
    label: '广告订单量',
    value: '386',
    comparisons: [
      { label: '环比', value: '+6.2%', color: '#00941E' },
      { label: '同比', value: '+9.8%', color: '#00941E' },
    ],
  },
  {
    label: '广告销售额',
    value: '$18,420',
    comparisons: [
      { label: '环比', value: '+10.9%', color: '#00941E' },
      { label: '同比', value: '+13.4%', color: '#00941E' },
    ],
  },
  {
    label: 'ACOS',
    value: '17.7%',
    comparisons: [
      { label: '环比', value: '-0.3pp', color: '#00941E' },
      { label: '同比', value: '-1.1pp', color: '#00941E' },
    ],
  },
]

export interface RankThumbnail {
  background: string
  primaryPath: string
  primaryStroke: string
  secondaryPath?: string
  secondaryStroke?: string
}

export interface RankRow {
  rank: number
  title: string
  meta: string
  sku: string
  sales: string
  orders: string
  revenue: string
  netRevenue: string
  fba: string
  dailyAverage: string
  margin: string
  refund: string
  acos: string
  sparkPath: string
  thumbnail: RankThumbnail
}

export const rankingDimensions = ['ASIN', '父ASIN', 'MSKU', 'SKU']
export const rankingPeriods = ['今日', '昨日', '近7天', '近30天', '本月', '自定义']

export const rankingRows: RankRow[] = [
  {
    rank: 1,
    title: '磁吸折叠露营灯 户外便携三合一',
    meta: 'B0D8K7Q2LM · Aurora Home US',
    sku: 'LX-CL001 / LX-CL001-BK',
    sales: '326',
    orders: '218',
    revenue: '$6,842.00',
    netRevenue: '$5,126.40',
    fba: '486',
    dailyAverage: '46.6',
    margin: '38.2%',
    refund: '$128.00',
    acos: '12.8%',
    sparkPath: 'M1 18 8 15 15 16 22 11 29 12 36 7 43 8 50 3',
    thumbnail: {
      background: '#F4F7FC',
      primaryPath: 'M9 17h12v3H9zM11 12h8l1.2 5h-10.4L11 12Z',
      primaryStroke: '#6E7E94',
      secondaryPath: 'M13 10h4M12 20v3M18 20v3',
      secondaryStroke: '#005BF5',
    },
  },
  {
    rank: 2,
    title: '宠物除毛梳 一键退毛 可拆洗',
    meta: 'B0C9F3R8PQ · LumoLife JP',
    sku: 'LX-PT012 / LX-PT012-GR',
    sales: '284',
    orders: '196',
    revenue: '$5,238.40',
    netRevenue: '$4,014.20',
    fba: '642',
    dailyAverage: '39.2',
    margin: '36.8%',
    refund: '$92.00',
    acos: '14.2%',
    sparkPath: 'M1 16 8 17 15 12 22 13 29 8 36 10 43 6 50 4',
    thumbnail: {
      background: '#F7F4FC',
      primaryPath: 'M12 8h6v4a3 3 0 0 0 6 0V9h2v13H8V9h4v3a3 3 0 0 0 6 0V8Z',
      primaryStroke: '#81718F',
      secondaryPath: 'M12 19h12',
      secondaryStroke: '#8D6AAB',
    },
  },
  {
    rank: 3,
    title: '无线快充支架 15W 桌面折叠款',
    meta: 'B0B7M2N6VD · NovaTech DE',
    sku: 'LX-CH028 / LX-CH028-WH',
    sales: '236',
    orders: '178',
    revenue: '$4,248.00',
    netRevenue: '$3,312.80',
    fba: '386',
    dailyAverage: '33.1',
    margin: '34.7%',
    refund: '$76.00',
    acos: '16.5%',
    sparkPath: 'M1 14 8 13 15 15 22 9 29 11 36 12 43 5 50 6',
    thumbnail: {
      background: '#F3FAF7',
      primaryPath: 'M12 8h2v14h-2zM17 8h2v14h-2zM11 11h9M11 19h9',
      primaryStroke: '#557A69',
      secondaryPath: 'M14 8V6h4v2',
      secondaryStroke: '#00946D',
    },
  },
  {
    rank: 4,
    title: '户外防水收纳袋 20L 双肩款',
    meta: 'B0A6P9H3XT · PeakOut US',
    sku: 'LX-BG031 / LX-BG031-GY',
    sales: '198',
    orders: '142',
    revenue: '$3,762.00',
    netRevenue: '$2,978.40',
    fba: '512',
    dailyAverage: '28.3',
    margin: '32.1%',
    refund: '$54.00',
    acos: '18.3%',
    sparkPath: 'M1 17 8 14 15 16 22 12 29 15 36 9 43 11 50 8',
    thumbnail: {
      background: '#FFF8F0',
      primaryPath: 'M11 11h8l1 12h-10l1-12Z',
      primaryStroke: '#916A40',
      secondaryPath: 'M13 11V8h4v3M13 15h4',
      secondaryStroke: '#C77C24',
    },
  },
  {
    rank: 5,
    title: '便携浓缩咖啡杯 350ml 保温款',
    meta: 'B0D2V5J8QW · Aurora Home US',
    sku: 'LX-CP046 / LX-CP046-BK',
    sales: '164',
    orders: '121',
    revenue: '$2,984.00',
    netRevenue: '$2,426.00',
    fba: '238',
    dailyAverage: '22.5',
    margin: '30.8%',
    refund: '$42.00',
    acos: '20.1%',
    sparkPath: 'M1 18 8 18 15 14 22 15 29 10 36 12 43 9 50 11',
    thumbnail: {
      background: '#F2F7FB',
      primaryPath: 'M10 10h10v11H10z',
      primaryStroke: '#5B7388',
      secondaryPath: 'M12 7h6v3M13 13h4M13 16h4M13 19h2',
      secondaryStroke: '#4E8BB5',
    },
  },
]

export interface DistributionSegment {
  name: string
  percentage: number
  count: number
  color: string
  offset: number
}

export const orderTypes = ['销售', '退货']
export const orderPeriods = ['前7天', '前14天', '前28天']
export const distributionSegments: DistributionSegment[] = [
  { name: 'Amazon US', percentage: 46, count: 387, color: 'var(--color-chart-1)', offset: 0 },
  { name: 'Amazon DE', percentage: 27, count: 228, color: 'var(--color-chart-2)', offset: -46 },
  { name: 'Amazon JP', percentage: 17, count: 143, color: 'var(--color-chart-3)', offset: -73 },
  { name: '其他站点', percentage: 10, count: 84, color: 'var(--color-chart-4)', offset: -90 },
]

export interface WarehouseStock {
  name: string
  color: string
  inStock: string
  inTransit: string
  days: string
  total: string
}

export const inventoryMetrics = ['数量', '货值']
export const warehouseStocks: WarehouseStock[] = [
  {
    name: 'FBA仓',
    color: 'var(--color-chart-1)',
    inStock: '5,482',
    inTransit: '638',
    days: '42.6',
    total: '6,120',
  },
  {
    name: '海外仓',
    color: 'var(--color-chart-2)',
    inStock: '1,836',
    inTransit: '212',
    days: '35.8',
    total: '2,048',
  },
  {
    name: '本地仓',
    color: 'var(--color-chart-3)',
    inStock: '932',
    inTransit: '128',
    days: '28.4',
    total: '1,060',
  },
  {
    name: 'AWD仓',
    color: 'var(--color-chart-4)',
    inStock: '170',
    inTransit: '282',
    days: '61.2',
    total: '452',
  },
]

export const profitTypes = ['结算利润', '订单利润']
export const profitPeriods = ['昨日', '前7天', '前30天']

export interface ProfitSummary {
  label: string
  total: string
  currency: string
  margin: string
  orderProfit: string
  change: string
  note: string
}

export const profitSummary: ProfitSummary = {
  label: '近7天结算利润',
  total: '$8,640.20',
  currency: 'USD',
  margin: '34.8%',
  orderProfit: '$7,930.00',
  change: '较上周期 +12.6%',
  note: '订单利润口径为已结算订单',
}
