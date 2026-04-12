#!/usr/bin/env node
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { parse as parseYaml, stringify as stringifyYaml } from 'yaml'

const __dirname = path.dirname(fileURLToPath(import.meta.url))

function loadEnvFromFile(filePath) {
  if (!fs.existsSync(filePath)) return
  const raw = fs.readFileSync(filePath, 'utf8')
  for (const line of raw.split(/\r?\n/)) {
    const t = line.trim()
    if (!t || t.startsWith('#')) continue
    const m = t.match(/^([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$/)
    if (!m) continue
    const key = m[1]
    let val = m[2].trim()
    if (
      (val.startsWith('"') && val.endsWith('"')) ||
      (val.startsWith("'") && val.endsWith("'"))
    ) {
      val = val.slice(1, -1)
    }
    if (process.env[key] === undefined) process.env[key] = val
  }
}

loadEnvFromFile(path.join(__dirname, '.env'))

const BASE = 'https://gitee.com/api/v5'

const PRIORITY_TO_API = {
  不指定: 0,
  不重要: 1,
  次要: 2,
  主要: 3,
  严重: 4,
}

const API_TO_YAML_PRIORITY = {
  0: '不指定',
  1: '不重要',
  2: '次要',
  3: '主要',
  4: '严重',
}

const LEGACY_P_TO_CN = { P0: '严重', P1: '主要', P2: '次要' }

const PRIORITY_LEVEL_KEYS = new Set(Object.keys(PRIORITY_TO_API))
const LEGACY_PRIORITY_TAGS = new Set(['P0', 'P1', 'P2'])

function isPriorityLabelName(n) {
  return PRIORITY_LEVEL_KEYS.has(n) || LEGACY_PRIORITY_TAGS.has(n)
}

const ALLOWED_ISSUE_STATES = new Set(['open', 'progressing', 'closed', 'rejected'])

function normalizeIssueState(s) {
  if (s == null || s === '') return undefined
  const v = String(s).trim()
  if (!ALLOWED_ISSUE_STATES.has(v)) {
    throw new Error(`Invalid state "${s}" (use open|progressing|closed|rejected)`)
  }
  return v
}

function issueStateFromApi(issue) {
  const st = issue.state
  if (st == null || st === '') return undefined
  const v = String(st).trim().toLowerCase()
  const map = {
    open: 'open',
    opened: 'open',
    progressing: 'progressing',
    closed: 'closed',
    rejected: 'rejected',
  }
  const mapped = map[v]
  if (mapped && ALLOWED_ISSUE_STATES.has(mapped)) return mapped
  if (ALLOWED_ISSUE_STATES.has(v)) return v
  return undefined
}

const DEFAULT_CLASSIFICATIONS = [
  'bug',
  'duplicate',
  'enhancement',
  'feature',
  'invalid',
  'question',
  'wontfix',
]

const GITEE_CLASS_SET = new Set(DEFAULT_CLASSIFICATIONS)

function classificationKeysFromPlan(plan) {
  const gc = plan?.gitee_content_classifications
  if (Array.isArray(gc)) return gc
  if (gc && typeof gc === 'object') {
    return Object.keys(gc).filter((k) => typeof gc[k] === 'string')
  }
  return DEFAULT_CLASSIFICATIONS
}

function classificationSetFromPlan(plan) {
  return new Set(classificationKeysFromPlan(plan))
}

const GITEE_CLASS_COLORS = {
  bug: 'd73a4a',
  duplicate: 'cfd3d7',
  enhancement: 'a2eeef',
  feature: '0e8a16',
  invalid: 'e4e669',
  question: 'd87669',
  wontfix: '6c2168',
}

function parseArgs(argv) {
  const args = { dryRun: false, plan: null, pull: false }
  for (let i = 2; i < argv.length; i++) {
    const a = argv[i]
    if (a === '--dry-run') args.dryRun = true
    else if (a === '--pull') args.pull = true
    else if (a === '--plan' && argv[i + 1]) {
      args.plan = argv[++i]
    }
  }
  return args
}

function resolvePlanPath(args) {
  if (args.plan) return path.resolve(process.cwd(), args.plan)
  const dir = import.meta.dirname
  const primary = path.join(dir, 'pm-plan.yaml')
  if (fs.existsSync(primary)) return primary
  return path.join(dir, 'pm-plan.example.yaml')
}

function colorForLabel(name) {
  let h = 0
  for (let i = 0; i < name.length; i++) h = (h * 31 + name.charCodeAt(i)) >>> 0
  return ((h >>> 0) % 0xffffff).toString(16).padStart(6, '0')
}

function defaultDueOn(daysFromNow = 365) {
  const d = new Date()
  d.setUTCDate(d.getUTCDate() + daysFromNow)
  return d.toISOString().slice(0, 10)
}

async function sleep(ms) {
  await new Promise((r) => setTimeout(r, ms))
}

async function fetchJson(url, opts = {}, token, attempt = 0) {
  const u = new URL(url)
  u.searchParams.set('access_token', token)
  const res = await fetch(u.toString(), {
    ...opts,
    headers: {
      Accept: 'application/json',
      'Content-Type': 'application/json',
      ...opts.headers,
    },
  })
  const text = await res.text()
  let data
  try {
    data = text ? JSON.parse(text) : null
  } catch {
    data = text
  }
  if (res.status === 429 || (res.status >= 500 && res.status < 600)) {
    if (attempt < 4) {
      await sleep(500 * 2 ** attempt)
      return fetchJson(url, opts, token, attempt + 1)
    }
  }
  if (!res.ok) {
    const msg = typeof data === 'object' && data?.message ? data.message : text
    throw new Error(`HTTP ${res.status} ${opts.method || 'GET'} ${u.pathname}: ${msg}`)
  }
  return data
}

function repoPath(owner, repo) {
  return `/repos/${encodeURIComponent(owner)}/${encodeURIComponent(repo)}`
}

async function listAllMilestones(owner, repo, token) {
  const out = []
  for (let page = 1; page <= 20; page++) {
    const path = `${repoPath(owner, repo)}/milestones`
    const u = `${BASE}${path}?state=all&page=${page}&per_page=100`
    const batch = await fetchJson(u, {}, token)
    if (!Array.isArray(batch) || batch.length === 0) break
    out.push(...batch)
    if (batch.length < 100) break
  }
  return out
}

async function createMilestone(owner, repo, token, { title, description, due_on }) {
  const path = `${repoPath(owner, repo)}/milestones`
  const dueOn = due_on || defaultDueOn()
  return fetchJson(`${BASE}${path}`, {
    method: 'POST',
    body: JSON.stringify({
      title,
      description: description || '',
      due_on: dueOn,
    }),
  }, token)
}

async function listAllLabels(owner, repo, token) {
  const out = []
  for (let page = 1; page <= 20; page++) {
    const path = `${repoPath(owner, repo)}/labels`
    const u = `${BASE}${path}?page=${page}&per_page=100`
    const batch = await fetchJson(u, {}, token)
    if (!Array.isArray(batch) || batch.length === 0) break
    out.push(...batch)
    if (batch.length < 100) break
  }
  return out
}

async function createLabel(owner, repo, token, name) {
  const path = `${repoPath(owner, repo)}/labels`
  const color = GITEE_CLASS_COLORS[name] || colorForLabel(name)
  return fetchJson(`${BASE}${path}`, {
    method: 'POST',
    body: JSON.stringify({ name, color }),
  }, token)
}

async function listAllIssues(owner, repo, token) {
  const out = []
  for (let page = 1; page <= 50; page++) {
    const path = `${repoPath(owner, repo)}/issues`
    const u = `${BASE}${path}?state=all&page=${page}&per_page=100&sort=created`
    const batch = await fetchJson(u, {}, token)
    if (!Array.isArray(batch) || batch.length === 0) break
    out.push(...batch)
    if (batch.length < 100) break
  }
  return out
}

async function getIssueDetail(owner, repo, token, number) {
  const path = `${repoPath(owner, repo)}/issues/${encodeURIComponent(number)}`
  return fetchJson(`${BASE}${path}`, {}, token)
}

async function createIssue(owner, repo, token, payload) {
  const path = `/repos/${encodeURIComponent(owner)}/issues`
  const body = { repo, ...payload }
  delete body.state
  delete body.priority
  return fetchJson(`${BASE}${path}`, { method: 'POST', body: JSON.stringify(body) }, token)
}

async function patchIssue(owner, repo, token, number, payload) {
  const path = `/repos/${encodeURIComponent(owner)}/issues/${encodeURIComponent(number)}`
  const body = { repo }
  if (payload.title != null) body.title = payload.title
  if (payload.body !== undefined) body.body = payload.body ?? ''
  if (payload.milestone !== undefined && payload.milestone !== null) {
    body.milestone = Number(payload.milestone)
  }
  if (payload.labels != null && payload.labels !== '') body.labels = payload.labels
  if (payload.priority !== undefined && payload.priority !== null && payload.priority !== '') {
    body.priority = Number(payload.priority)
  }
  if (payload.state != null && payload.state !== '') body.state = payload.state
  return fetchJson(`${BASE}${path}`, { method: 'PATCH', body: JSON.stringify(body) }, token)
}

function labelNamesFromIssue(issue) {
  const raw = issue.labels || []
  return raw.map((l) => (typeof l === 'string' ? l : l.name)).filter(Boolean)
}

function issueToYamlPriority(issue) {
  const n = issue.priority
  if (typeof n === 'number' && API_TO_YAML_PRIORITY[n] != null) {
    return API_TO_YAML_PRIORITY[n]
  }
  const names = labelNamesFromIssue(issue)
  for (const p of PRIORITY_LEVEL_KEYS) {
    if (names.includes(p)) return p
  }
  for (const [legacy, cn] of Object.entries(LEGACY_P_TO_CN)) {
    if (names.includes(legacy)) return cn
  }
  return undefined
}

function normalizePriorityToApi(p) {
  if (p == null || p === '') return undefined
  if (LEGACY_P_TO_CN[p] != null) {
    return PRIORITY_TO_API[LEGACY_P_TO_CN[p]]
  }
  if (PRIORITY_TO_API[p] === undefined) {
    throw new Error(
      `Invalid priority "${p}" (use one of: ${[...PRIORITY_LEVEL_KEYS].join('、')})`,
    )
  }
  return PRIORITY_TO_API[p]
}

function collectIssueLabels(item) {
  if (item.module != null && item.classification != null) {
    return [item.module, item.classification].filter(Boolean)
  }
  if (Array.isArray(item.labels)) {
    return item.labels.filter((l) => !isPriorityLabelName(l))
  }
  return []
}

function validateIssuePlan(item, classSet) {
  if (item.module != null || item.classification != null) {
    if (item.module == null || item.classification == null) {
      throw new Error(`Issue "${item.title}": 需同时提供 module 与 classification`)
    }
    if (!classSet.has(item.classification)) {
      throw new Error(
        `Issue "${item.title}": classification 须为 pm-plan.yaml 中 gitee_content_classifications 之一`,
      )
    }
  }
}

function splitLabelsToModuleClassification(names) {
  const cls = names.find((n) => GITEE_CLASS_SET.has(n))
  const mod = names.find((n) => !GITEE_CLASS_SET.has(n))
  if (mod && cls) return { module: mod, classification: cls }
  return null
}

function buildIssuePayload(title, body, milestoneNumber, labelNames, priorityKey, stateKey) {
  const payload = {
    title,
    body: body || '',
  }
  if (milestoneNumber != null) payload.milestone = milestoneNumber
  const labels = (labelNames || []).filter((x) => x && !isPriorityLabelName(x))
  if (labels.length) payload.labels = labels.join(',')
  const apiPri = normalizePriorityToApi(priorityKey)
  if (apiPri !== undefined) payload.priority = apiPri
  const st = normalizeIssueState(stateKey)
  if (st !== undefined) payload.state = st
  return payload
}

function milestoneOrderIndex(msList, msTitle) {
  if (!msTitle) return 9999
  const i = msList.findIndex((m) => m.title === msTitle)
  return i === -1 ? 9999 : i
}

function remoteIssuesToYamlRows(detailed, msList) {
  const decorated = detailed.map((i) => {
    const names = labelNamesFromIssue(i).filter((n) => !isPriorityLabelName(n))
    const pr = issueToYamlPriority(i)
    const row = {
      title: i.title,
      body: i.body || '',
      milestone: i.milestone?.title || undefined,
      gitee_number: i.number,
    }
    if (pr) row.priority = pr
    const st = issueStateFromApi(i)
    if (st) row.state = st
    const split = splitLabelsToModuleClassification(names)
    if (split) {
      row.module = split.module
      row.classification = split.classification
    } else if (names.length) {
      row.labels = names
    }
    return {
      row,
      mi: milestoneOrderIndex(msList, i.milestone?.title),
      created: i.created_at || '',
    }
  })
  decorated.sort((a, b) => {
    if (a.mi !== b.mi) return a.mi - b.mi
    return String(a.created).localeCompare(String(b.created))
  })
  return decorated.map((x) => x.row)
}

async function pullPlan(owner, repo, token, outPath, dryRun) {
  const msList = await listAllMilestones(owner, repo, token)
  const issueList = (await listAllIssues(owner, repo, token)).filter((i) => !i.pull_request)

  const detailed = await Promise.all(
    issueList.map(async (i) => {
      try {
        const d = await getIssueDetail(owner, repo, token, i.number)
        return { ...i, ...d }
      } catch {
        return i
      }
    }),
  )

  let existing = {}
  if (fs.existsSync(outPath)) {
    existing = parseYaml(fs.readFileSync(outPath, 'utf8')) || {}
  }

  const milestones = msList.map((m) => {
    const due = m.due_on || m.due_date || ''
    return {
      title: m.title,
      description: m.description || '',
      due_on: typeof due === 'string' && due.length >= 10 ? due.slice(0, 10) : defaultDueOn(),
    }
  })

  const issues = remoteIssuesToYamlRows(detailed, msList)

  const out = {
    meta: existing.meta,
    modules: existing.modules,
    priority_definitions: existing.priority_definitions,
    gitee_priority: existing.gitee_priority,
    gitee_content_classifications: existing.gitee_content_classifications,
    milestones,
    issues,
  }

  if (dryRun) {
    console.log(stringifyYaml(out, { lineWidth: 120 }))
    return
  }
  fs.writeFileSync(outPath, stringifyYaml(out, { lineWidth: 120 }), 'utf8')
  console.error(`Wrote ${outPath}`)
}

async function pushPlan(planPath, owner, repo, token, dryRun) {
  const raw = fs.readFileSync(planPath, 'utf8')
  const plan = parseYaml(raw)
  const milestones = plan.milestones || []
  const issues = plan.issues || []

  console.error(`Plan: ${planPath}`)
  console.error(`Milestones: ${milestones.length}, Issues: ${issues.length}`)
  if (dryRun) {
    console.log(JSON.stringify({ milestones, issues }, null, 2))
    return
  }

  const classSet = classificationSetFromPlan(plan)
  const classKeys = classificationKeysFromPlan(plan)
  const existingLabels = await listAllLabels(owner, repo, token)
  const labelNames = new Set(existingLabels.map((l) => l.name))
  for (const cn of classKeys) {
    if (!labelNames.has(cn)) {
      await createLabel(owner, repo, token, cn)
      labelNames.add(cn)
      console.error(`Created label: ${cn}`)
    }
  }
  const allLabelNames = new Set()
  for (const iss of issues) {
    validateIssuePlan(iss, classSet)
    for (const lb of collectIssueLabels(iss)) {
      if (!isPriorityLabelName(lb)) allLabelNames.add(lb)
    }
  }
  for (const name of allLabelNames) {
    if (!labelNames.has(name)) {
      await createLabel(owner, repo, token, name)
      labelNames.add(name)
      console.error(`Created label: ${name}`)
    }
  }

  let msList = await listAllMilestones(owner, repo, token)
  const msByTitle = new Map(msList.map((m) => [m.title, m]))
  for (const m of milestones) {
    if (!msByTitle.has(m.title)) {
      const created = await createMilestone(owner, repo, token, m)
      msByTitle.set(m.title, created)
      console.error(`Created milestone: ${m.title}`)
    }
  }
  msList = await listAllMilestones(owner, repo, token)
  const msNumberByTitle = new Map(msList.map((x) => [x.title, x.number]))

  const issueList = (await listAllIssues(owner, repo, token)).filter((i) => !i.pull_request)
  const byTitle = new Map(issueList.map((i) => [i.title, i]))
  const byNumber = new Map(issueList.map((i) => [String(i.number), i]))
  const seenNumbers = new Set()
  for (const item of issues) {
    if (item.gitee_number != null && item.gitee_number !== '') {
      const k = String(item.gitee_number)
      if (seenNumbers.has(k)) throw new Error(`重复的 gitee_number: ${k}`)
      seenNumbers.add(k)
    }
  }

  for (const item of issues) {
    const pr = item.priority
    if (pr != null && pr !== '') normalizePriorityToApi(pr)
    const msTitle = item.milestone
    const msNum = msTitle != null ? msNumberByTitle.get(msTitle) : undefined
    if (msTitle != null && msNum == null) {
      throw new Error(`Unknown milestone title: ${msTitle}`)
    }
    const payload = buildIssuePayload(
      item.title,
      item.body,
      msNum,
      collectIssueLabels(item),
      pr,
      item.state,
    )
    let existing
    if (item.gitee_number != null && item.gitee_number !== '') {
      existing = byNumber.get(String(item.gitee_number))
    }
    if (!existing) existing = byTitle.get(item.title)
    if (existing) {
      await patchIssue(owner, repo, token, existing.number, payload)
      console.error(`Updated issue #${existing.number}: ${item.title}`)
    } else {
      const created = await createIssue(owner, repo, token, payload)
      byTitle.set(item.title, created)
      byNumber.set(String(created.number), created)
      console.error(`Created issue #${created.number}: ${item.title}`)
      await patchIssue(owner, repo, token, created.number, payload)
      console.error(`Patched issue #${created.number} (state/priority/labels/milestone)`)
    }
  }

  console.error('Done.')
}

async function main() {
  const args = parseArgs(process.argv)
  const planPath = resolvePlanPath(args)

  const owner = process.env.GITEE_OWNER
  const repo = process.env.GITEE_REPO
  const token = process.env.GITEE_ACCESS_TOKEN

  if (!args.dryRun && (!owner || !repo || !token)) {
    console.error('缺少 GITEE_OWNER / GITEE_REPO / GITEE_ACCESS_TOKEN（可用 --dry-run）')
    process.exit(1)
  }

  if (args.pull) {
    if (!owner || !repo || !token) {
      console.error('Pull 需要 GITEE_OWNER / GITEE_REPO / GITEE_ACCESS_TOKEN')
      process.exit(1)
    }
    await pullPlan(owner, repo, token, planPath, args.dryRun)
    return
  }

  await pushPlan(planPath, owner, repo, token, args.dryRun)
}

main().catch((e) => {
  console.error(e)
  process.exit(1)
})
