<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import {
  getPointsBalance,
  getSkillCatalog,
  purchaseSkill,
  behaviorScan,
  publicInquiry,
  type SkillCatalogItem,
  type WordCloudItem,
} from '@/api/accounting'
import { ElMessage } from 'element-plus'

const balance = ref(0)
const catalog = ref<SkillCatalogItem[]>([])
const loading = ref(false)
const purchasing = ref<string | null>(null)

const descriptions: Record<string, string> = {
  behavior_scan: '扫描目标近 7 日发言，生成高频词云，辅助判断 AI 行文特征。',
  death_shield: '下次指认失误时不扣分（一次性护盾，结算时自动核销）。',
  public_inquiry: '向目标公开发起质询帖，并触发 24 小时回应窗口（一次性）。',
}

const scanTarget = ref('')
const scanResult = ref<WordCloudItem[]>([])
const scanLoading = ref(false)
const inquiryTarget = ref('')
const inquiryQuestion = ref('')
const inquiryLoading = ref(false)

const canUse = (id: string) => id === 'behavior_scan' || id === 'public_inquiry'

async function loadShop() {
  loading.value = true
  try {
    const b = await getPointsBalance()
    if (b.success) balance.value = b.data?.balance ?? 0
    const c = await getSkillCatalog()
    if (c.success) catalog.value = c.data ?? []
  } finally {
    loading.value = false
  }
}

function priceOf(id: string): number {
  return catalog.value.find((s) => s.id === id)?.price ?? 0
}

const affordable = (id: string) => balance.value >= priceOf(id)

async function buy(id: string) {
  purchasing.value = id
  try {
    const r = await purchaseSkill(id)
    if (r.success) {
      ElMessage.success('购买成功')
      await loadShop()
    } else {
      ElMessage.error(r.message ?? '购买失败')
    }
  } finally {
    purchasing.value = null
  }
}

async function runScan() {
  if (!scanTarget.value.trim()) {
    ElMessage.warning('请输入目标用户 ID')
    return
  }
  scanLoading.value = true
  scanResult.value = []
  try {
    const r = await behaviorScan(scanTarget.value.trim())
    if (r.success) scanResult.value = r.data ?? []
    else ElMessage.error(r.message ?? '扫描失败')
  } finally {
    scanLoading.value = false
  }
}

async function runInquiry() {
  if (!inquiryTarget.value.trim() || !inquiryQuestion.value.trim()) {
    ElMessage.warning('请填写目标用户与质询内容')
    return
  }
  inquiryLoading.value = true
  try {
    const r = await publicInquiry(inquiryTarget.value.trim(), inquiryQuestion.value.trim())
    if (r.success) {
      ElMessage.success('质询已发布')
      inquiryTarget.value = ''
      inquiryQuestion.value = ''
      await loadShop()
    } else {
      ElMessage.error(r.message ?? '质询失败')
    }
  } finally {
    inquiryLoading.value = false
  }
}

const sortedCatalog = computed(() =>
  [...catalog.value].sort((a, b) => a.price - b.price),
)

onMounted(loadShop)
</script>

<template>
  <div class="skill-shop">
    <header class="skill-shop__head">
      <h2 class="skill-shop__title">侦探技能商店</h2>
      <span class="skill-shop__balance">当前积分：{{ balance }}</span>
    </header>

    <div v-if="loading" class="skill-shop__hint">加载中…</div>

    <div class="skill-shop__grid">
      <article v-for="skill in sortedCatalog" :key="skill.id" class="forum-card skill-card">
        <h3 class="skill-card__name">{{ skill.name }}</h3>
        <p class="skill-card__desc">{{ descriptions[skill.id] ?? '' }}</p>
        <div class="skill-card__price">{{ skill.price }} 积分</div>

        <button
          class="skill-card__buy"
          :disabled="!affordable(skill.id) || purchasing === skill.id"
          @click="buy(skill.id)"
        >
          {{ affordable(skill.id) ? '购买' : '积分不足' }}
        </button>

        <div v-if="canUse(skill.id) && skill.id === 'behavior_scan'" class="skill-card__use">
          <input v-model="scanTarget" class="skill-card__input" placeholder="目标用户 ID" />
          <button class="skill-card__act" :disabled="scanLoading" @click="runScan">行为扫描</button>
          <ul v-if="scanResult.length" class="word-cloud">
            <li v-for="w in scanResult" :key="w.word" class="word-cloud__item">
              {{ w.word }} · {{ w.count }}
            </li>
          </ul>
        </div>

        <div v-if="canUse(skill.id) && skill.id === 'public_inquiry'" class="skill-card__use">
          <input v-model="inquiryTarget" class="skill-card__input" placeholder="目标用户 ID" />
          <textarea v-model="inquiryQuestion" class="skill-card__textarea" placeholder="质询内容" rows="2" />
          <button class="skill-card__act" :disabled="inquiryLoading" @click="runInquiry">公开发起质询</button>
        </div>
      </article>
    </div>
  </div>
</template>

<style scoped>
.skill-shop {
  max-width: var(--container-max);
  margin: 0 auto;
  padding: var(--space-lg) var(--space-md);
  box-sizing: border-box;
}
.skill-shop__head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: var(--space-md);
  margin-bottom: var(--space-lg);
}
.skill-shop__title {
  font-size: var(--font-xl);
  font-weight: 600;
  color: var(--text-primary);
  margin: 0;
}
.skill-shop__balance {
  font-size: var(--font-md);
  color: var(--text-secondary);
}
.skill-shop__hint {
  color: var(--text-muted);
  font-size: var(--font-sm);
}
.skill-shop__grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(260px, 1fr));
  gap: var(--space-md);
}
.skill-card {
  display: flex;
  flex-direction: column;
  gap: var(--space-sm);
}
.skill-card__name {
  font-size: var(--font-lg);
  font-weight: 600;
  color: var(--text-primary);
  margin: 0;
}
.skill-card__desc {
  font-size: var(--font-sm);
  color: var(--text-secondary);
  line-height: var(--line-height);
  margin: 0;
  min-height: 42px;
}
.skill-card__price {
  font-size: var(--font-md);
  font-weight: 600;
  color: var(--color-primary);
}
.skill-card__buy,
.skill-card__act {
  border: none;
  border-radius: var(--radius-md);
  padding: var(--space-sm) var(--space-md);
  font-size: var(--font-sm);
  cursor: pointer;
  background: var(--color-primary);
  color: #fff;
}
.skill-card__buy:disabled {
  background: var(--text-disabled);
  cursor: not-allowed;
}
.skill-card__use {
  display: flex;
  flex-direction: column;
  gap: var(--space-xs);
  margin-top: var(--space-sm);
  padding-top: var(--space-sm);
  border-top: 1px solid var(--border-color);
}
.skill-card__input,
.skill-card__textarea {
  border: 1px solid var(--border-color);
  border-radius: var(--radius-md);
  padding: var(--space-xs) var(--space-sm);
  font-size: var(--font-sm);
  color: var(--text-primary);
  background: var(--bg-card);
  box-sizing: border-box;
  resize: vertical;
}
.word-cloud {
  list-style: none;
  margin: var(--space-xs) 0 0;
  padding: 0;
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-xs);
}
.word-cloud__item {
  font-size: var(--font-xs);
  color: var(--text-secondary);
  background: var(--hover-bg);
  border-radius: var(--radius-pill);
  padding: 2px var(--space-sm);
}
</style>
