#!/usr/bin/env node
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { parse as parseYaml, stringify as stringifyYaml } from 'yaml'
import { resolvePmSyncDir } from './pm-sync-dir.mjs'
import { getPmIssueNumber } from './pm-issue-fields.mjs'
import {
  createGithubClient,
  createGiteeClient,
  defaultDueOn,
  issueStateFromGithub,
  isPmMachineLabel,
  labelNamesFromIssue,
  PM_STATE_PROGRESSING,
  PM_STATE_REJECTED,
  repoRootFromScriptDir,
  resolveCredentials,
  resolveProvider,
} from './providers.mjs'

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

function issueStateFromGiteeApi(issue) {
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

function issueContentClassificationsFromPlan(plan) {
  const gc =
    plan?.issue_content_classifications ?? plan?.gitee_content_classifications
  if (Array.isArray(gc)) return gc
  if (gc && typeof gc === 'object') {
    return Object.keys(gc).filter((k) => typeof gc[k] === 'string')
  }
  return DEFAULT_CLASSIFICATIONS
}

function classificationKeysFromPlan(plan) {
  return issueContentClassificationsFromPlan(plan)
}

function remotePriorityFromPlan(plan) {
  return plan?.remote_priority ?? plan?.gitee_priority
}

function classificationSetFromPlan(plan) {
  return new Set(classificationKeysFromPlan(plan))
}

const CLASS_COLORS = {
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

function resolvePlanPath(args, repoRoot) {
  if (args.plan) return path.resolve(process.cwd(), args.plan)
  const dir = resolvePmSyncDir(repoRoot)
  const primary = path.join(dir, 'pm-plan.yaml')
  if (fs.existsSync(primary)) return primary
  return path.join(__dirname, 'pm-plan.example.yaml')
}

function colorForLabel(name) {
  let h = 0
  for (let i = 0; i < name.length; i++) h = (h * 31 + name.charCodeAt(i)) >>> 0
  return ((h >>> 0) % 0xffffff).toString(16).padStart(6, '0')
}

function issueToYamlPriority(issue, provider) {
  if (provider === 'gitee') {
    const n = issue.priority
    if (typeof n === 'number' && API_TO_YAML_PRIORITY[n] != null) {
      return API_TO_YAML_PRIORITY[n]
    }
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
        `Issue "${item.title}": classification 须为 pm-plan.yaml 中 issue_content_classifications 之一`,
      )
    }
  }
}

function splitLabelsToModuleClassification(names, classSet) {
  const filtered = names.filter((n) => !isPmMachineLabel(n))
  const cls = filtered.find((n) => classSet.has(n))
  const mod = filtered.find((n) => !classSet.has(n) && !isPriorityLabelName(n))
  if (mod && cls) return { module: mod, classification: cls }
  return null
}

function issueStateFromRemote(issue, provider) {
  if (provider === 'github') return issueStateFromGithub(issue)
  return issueStateFromGiteeApi(issue)
}

function buildGiteeIssuePayload(title, body, milestoneNumber, labelNames, priorityKey, stateKey) {
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

function githubPriorityLabel(priorityKey) {
  if (priorityKey == null || priorityKey === '') return null
  if (LEGACY_P_TO_CN[priorityKey] != null) return LEGACY_P_TO_CN[priorityKey]
  if (PRIORITY_TO_API[priorityKey] === 0) return null
  if (PRIORITY_TO_API[priorityKey] === undefined) return null
  return String(priorityKey)
}

function collectGithubLabels(item, priorityKey, stateKey) {
  const pr = priorityKey
  const base = collectIssueLabels(item)
  const out = [...base]
  const pl = githubPriorityLabel(pr)
  if (pl) out.push(pl)
  const st = stateKey != null ? String(stateKey).trim() : ''
  if (st === 'progressing') out.push(PM_STATE_PROGRESSING)
  if (st === 'rejected') out.push(PM_STATE_REJECTED)
  return [...new Set(out.filter(Boolean))]
}

function milestoneOrderIndex(msList, msTitle) {
  if (!msTitle) return 9999
  const i = msList.findIndex((m) => m.title === msTitle)
  return i === -1 ? 9999 : i
}

function remoteIssuesToYamlRows(detailed, msList, classSet, provider) {
  const decorated = detailed.map((i) => {
    const names = labelNamesFromIssue(i).filter(
      (n) => !isPriorityLabelName(n) && !isPmMachineLabel(n),
    )
    const pr = issueToYamlPriority(i, provider)
    const row = {
      title: i.title,
      body: i.body || '',
      milestone: i.milestone?.title || undefined,
      issue_number: i.number,
    }
    if (pr) row.priority = pr
    const st = issueStateFromRemote(i, provider)
    if (st) row.state = st
    const split = splitLabelsToModuleClassification(
      labelNamesFromIssue(i).filter((n) => !isPriorityLabelName(n) && !isPmMachineLabel(n)),
      classSet,
    )
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

function keepIssueInTrackingPlan(row) {
  const s = row.state
  if (s === 'closed' || s === 'rejected') return false
  return true
}

async function pullPlan(client, provider, outPath, dryRun, existingPlan) {
  const msList = await client.listAllMilestones()
  const issueList = (await client.listAllIssues()).filter((i) => !i.pull_request)

  const detailed = await Promise.all(
    issueList.map(async (i) => {
      try {
        const d = await client.getIssueDetail(i.number)
        return { ...i, ...d }
      } catch {
        return i
      }
    }),
  )

  let existing = existingPlan || {}
  if (!existing.meta && fs.existsSync(outPath)) {
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

  const classSet = classificationSetFromPlan(existing)
  const issues = remoteIssuesToYamlRows(detailed, msList, classSet, provider).filter(
    keepIssueInTrackingPlan,
  )

  const out = {
    meta: existing.meta,
    modules: existing.modules,
    priority_definitions: existing.priority_definitions,
    remote_priority: remotePriorityFromPlan(existing),
    issue_content_classifications: issueContentClassificationsFromPlan(existing),
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

async function ensureGithubStateLabels(client, existingNames) {
  const need = [PM_STATE_PROGRESSING, PM_STATE_REJECTED]
  for (const name of need) {
    if (!existingNames.has(name)) {
      await client.createLabel(name, colorForLabel(name))
      existingNames.add(name)
      console.error(`Created label: ${name}`)
    }
  }
}

async function pushPlanGithub(client, planPath, dryRun) {
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
  const existingLabels = await client.listAllLabels()
  const labelNames = new Set(existingLabels.map((l) => l.name))
  await ensureGithubStateLabels(client, labelNames)
  for (const cn of classKeys) {
    if (!labelNames.has(cn)) {
      await client.createLabel(cn, CLASS_COLORS[cn] || colorForLabel(cn))
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
      await client.createLabel(name, CLASS_COLORS[name] || colorForLabel(name))
      labelNames.add(name)
      console.error(`Created label: ${name}`)
    }
  }

  let msList = await client.listAllMilestones()
  const msByTitle = new Map(msList.map((m) => [m.title, m]))
  for (const m of milestones) {
    if (!msByTitle.has(m.title)) {
      const created = await client.createMilestone(m)
      msByTitle.set(m.title, created)
      console.error(`Created milestone: ${m.title}`)
    }
  }
  msList = await client.listAllMilestones()
  const msNumberByTitle = new Map(msList.map((x) => [x.title, x.number]))

  const issueList = (await client.listAllIssues()).filter((i) => !i.pull_request)
  const byTitle = new Map(issueList.map((i) => [i.title, i]))
  const byNumber = new Map(issueList.map((i) => [String(i.number), i]))
  const seenNumbers = new Set()
  for (const item of issues) {
    const inum = getPmIssueNumber(item)
    if (inum != null && inum !== '') {
      const k = String(inum)
      if (seenNumbers.has(k)) throw new Error(`重复的 issue_number: ${k}`)
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
    const st = normalizeIssueState(item.state)
    const ghLabels = collectGithubLabels(item, pr, st)
    const apiState = st === 'closed' || st === 'rejected' ? 'closed' : 'open'

    let existing
    const inum = getPmIssueNumber(item)
    if (inum != null && inum !== '') {
      existing = byNumber.get(String(inum))
    }
    if (!existing) existing = byTitle.get(item.title)
    if (existing) {
      await client.patchIssue(existing.number, {
        title: item.title,
        body: item.body ?? '',
        milestone: msNum ?? null,
        labels: ghLabels,
        state: apiState,
      })
      console.error(`Updated issue #${existing.number}: ${item.title}`)
    } else {
      const created = await client.createIssue({
        title: item.title,
        body: item.body || '',
        labels: ghLabels,
        milestone: msNum,
      })
      byTitle.set(item.title, created)
      byNumber.set(String(created.number), created)
      console.error(`Created issue #${created.number}: ${item.title}`)
      await client.patchIssue(created.number, {
        title: item.title,
        body: item.body ?? '',
        milestone: msNum ?? null,
        labels: ghLabels,
        state: apiState,
      })
      console.error(`Patched issue #${created.number} (state/labels/milestone)`)
    }
  }

  console.error('Done.')
}

async function pushPlanGitee(client, planPath, dryRun) {
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
  const existingLabels = await client.listAllLabels()
  const labelNames = new Set(existingLabels.map((l) => l.name))
  for (const cn of classKeys) {
    if (!labelNames.has(cn)) {
      await client.createLabel(cn, CLASS_COLORS[cn] || colorForLabel(cn))
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
      await client.createLabel(name, CLASS_COLORS[name] || colorForLabel(name))
      labelNames.add(name)
      console.error(`Created label: ${name}`)
    }
  }

  let msList = await client.listAllMilestones()
  const msByTitle = new Map(msList.map((m) => [m.title, m]))
  for (const m of milestones) {
    if (!msByTitle.has(m.title)) {
      const created = await client.createMilestone(m)
      msByTitle.set(m.title, created)
      console.error(`Created milestone: ${m.title}`)
    }
  }
  msList = await client.listAllMilestones()
  const msNumberByTitle = new Map(msList.map((x) => [x.title, x.number]))

  const issueList = (await client.listAllIssues()).filter((i) => !i.pull_request)
  const byTitle = new Map(issueList.map((i) => [i.title, i]))
  const byNumber = new Map(issueList.map((i) => [String(i.number), i]))
  const seenNumbers = new Set()
  for (const item of issues) {
    const inum = getPmIssueNumber(item)
    if (inum != null && inum !== '') {
      const k = String(inum)
      if (seenNumbers.has(k)) throw new Error(`重复的 issue_number: ${k}`)
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
    const payload = buildGiteeIssuePayload(
      item.title,
      item.body,
      msNum,
      collectIssueLabels(item),
      pr,
      item.state,
    )
    let existing
    const inum = getPmIssueNumber(item)
    if (inum != null && inum !== '') {
      existing = byNumber.get(String(inum))
    }
    if (!existing) existing = byTitle.get(item.title)
    if (existing) {
      await client.patchIssue(existing.number, payload)
      console.error(`Updated issue #${existing.number}: ${item.title}`)
    } else {
      const body = { ...payload }
      delete body.state
      delete body.priority
      const created = await client.createIssue(body)
      byTitle.set(item.title, created)
      byNumber.set(String(created.number), created)
      console.error(`Created issue #${created.number}: ${item.title}`)
      await client.patchIssue(created.number, payload)
      console.error(`Patched issue #${created.number} (state/priority/labels/milestone)`)
    }
  }

  console.error('Done.')
}

function credError(provider) {
  if (provider === 'github') {
    return '缺少 GITHUB_TOKEN（或 GH_TOKEN）；可选 GITHUB_OWNER / GITHUB_REPO（否则从 git remote 解析）'
  }
  return '缺少 GITEE_OWNER / GITEE_REPO / GITEE_ACCESS_TOKEN（可用 --dry-run）'
}

async function main() {
  const args = parseArgs(process.argv)
  const repoRoot = repoRootFromScriptDir(__dirname)
  const planPath = resolvePlanPath(args, repoRoot)
  const provider = resolveProvider(repoRoot)
  const { token, owner, repo } = resolveCredentials(provider, repoRoot)

  if (!args.dryRun && (!token || !owner || !repo)) {
    console.error(credError(provider))
    process.exit(1)
  }

  const client =
    provider === 'github'
      ? createGithubClient(owner, repo, token)
      : createGiteeClient(owner, repo, token)

  if (args.pull) {
    if (!token || !owner || !repo) {
      console.error(credError(provider))
      process.exit(1)
    }
    let existingPlan = {}
    if (fs.existsSync(planPath)) {
      existingPlan = parseYaml(fs.readFileSync(planPath, 'utf8')) || {}
    }
    await pullPlan(client, provider, planPath, args.dryRun, existingPlan)
    return
  }

  if (provider === 'github') {
    await pushPlanGithub(client, planPath, args.dryRun)
  } else {
    await pushPlanGitee(client, planPath, args.dryRun)
  }
}

main().catch((e) => {
  console.error(e)
  process.exit(1)
})
