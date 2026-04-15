import { execFileSync } from 'node:child_process'
import path from 'node:path'

const GH_API = 'https://api.github.com'
const GITEE_API = 'https://gitee.com/api/v5'

const PM_STATE_PROGRESSING = 'pm-state-progressing'
const PM_STATE_REJECTED = 'pm-state-rejected'

export function defaultDueOn(daysFromNow = 365) {
  const d = new Date()
  d.setUTCDate(d.getUTCDate() + daysFromNow)
  return d.toISOString().slice(0, 10)
}

export function repoRootFromScriptDir(scriptDir) {
  return path.resolve(scriptDir, '..', '..')
}

export function readGitOrigin(repoRoot) {
  try {
    return execFileSync('git', ['-C', repoRoot, 'remote', 'get-url', 'origin'], {
      encoding: 'utf8',
    }).trim()
  } catch {
    return ''
  }
}

export function resolveProvider(repoRoot) {
  const e = process.env.PM_SYNC_PROVIDER
  if (e === 'github' || e === 'gitee') return e
  const u = readGitOrigin(repoRoot)
  if (/github\.com/i.test(u)) return 'github'
  if (/gitee\.com/i.test(u)) return 'gitee'
  return 'github'
}

export function parseRemoteOwnerRepo(origin, provider) {
  if (provider === 'github') {
    const m = origin.match(/github\.com[/:]([^/]+)\/([^/.]+?)(?:\.git)?$/i)
    if (m) return { owner: m[1], repo: m[2] }
  }
  if (provider === 'gitee') {
    const m = origin.match(/gitee\.com[/:]([^/]+)\/([^/.]+?)(?:\.git)?$/i)
    if (m) return { owner: m[1], repo: m[2] }
  }
  return null
}

export function resolveCredentials(provider, repoRoot) {
  const origin = readGitOrigin(repoRoot)
  const parsed = parseRemoteOwnerRepo(origin, provider)
  if (provider === 'github') {
    const token = process.env.GITHUB_TOKEN || process.env.GH_TOKEN
    const owner = process.env.GITHUB_OWNER || parsed?.owner
    const repo = process.env.GITHUB_REPO || parsed?.repo
    return { token, owner, repo }
  }
  const token = process.env.GITEE_ACCESS_TOKEN
  const owner = process.env.GITEE_OWNER || parsed?.owner
  const repo = process.env.GITEE_REPO || parsed?.repo
  return { token, owner, repo }
}

async function sleep(ms) {
  await new Promise((r) => setTimeout(r, ms))
}

function githubHeaders(token) {
  return {
    Accept: 'application/vnd.github+json',
    Authorization: `Bearer ${token}`,
    'X-GitHub-Api-Version': '2022-11-28',
  }
}

async function githubFetch(url, opts, token, attempt = 0) {
  const res = await fetch(url, {
    ...opts,
    headers: {
      ...githubHeaders(token),
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
      return githubFetch(url, opts, token, attempt + 1)
    }
  }
  if (!res.ok) {
    const msg = typeof data === 'object' && data?.message ? data.message : text
    throw new Error(`HTTP ${res.status} ${opts.method || 'GET'} ${url}: ${msg}`)
  }
  return data
}

async function giteeFetchJson(url, opts, token, attempt = 0) {
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
      return giteeFetchJson(url, opts, token, attempt + 1)
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

export function createGithubClient(owner, repo, token) {
  const base = `${GH_API}${repoPath(owner, repo)}`

  return {
    provider: 'github',
    async listAllMilestones() {
      const out = []
      for (let page = 1; page <= 20; page++) {
        const u = `${base}/milestones?state=all&per_page=100&page=${page}`
        const batch = await githubFetch(u, { method: 'GET' }, token)
        if (!Array.isArray(batch) || batch.length === 0) break
        out.push(...batch)
        if (batch.length < 100) break
      }
      return out
    },
    async createMilestone({ title, description, due_on }) {
      const due =
        typeof due_on === 'string' && due_on.length >= 10
          ? `${due_on.slice(0, 10)}T12:00:00Z`
          : undefined
      return githubFetch(
        `${base}/milestones`,
        {
          method: 'POST',
          body: JSON.stringify({
            title,
            description: description || '',
            ...(due ? { due_on: due } : {}),
          }),
        },
        token,
      )
    },
    async listAllLabels() {
      const out = []
      for (let page = 1; page <= 20; page++) {
        const u = `${base}/labels?per_page=100&page=${page}`
        const batch = await githubFetch(u, { method: 'GET' }, token)
        if (!Array.isArray(batch) || batch.length === 0) break
        out.push(...batch)
        if (batch.length < 100) break
      }
      return out
    },
    async createLabel(name, colorHex) {
      return githubFetch(
        `${base}/labels`,
        {
          method: 'POST',
          body: JSON.stringify({ name, color: colorHex }),
        },
        token,
      )
    },
    async listAllIssues() {
      const out = []
      for (let page = 1; page <= 50; page++) {
        const u = `${base}/issues?state=all&per_page=100&page=${page}`
        const batch = await githubFetch(u, { method: 'GET' }, token)
        if (!Array.isArray(batch) || batch.length === 0) break
        for (const i of batch) {
          if (!i.pull_request) out.push(i)
        }
        if (batch.length < 100) break
      }
      return out
    },
    async getIssueDetail(number) {
      return githubFetch(`${base}/issues/${encodeURIComponent(number)}`, { method: 'GET' }, token)
    },
    async createIssue(payload) {
      const body = {
        title: payload.title,
        body: payload.body || '',
      }
      if (payload.labels?.length) body.labels = payload.labels
      if (payload.milestone != null) body.milestone = payload.milestone
      return githubFetch(`${base}/issues`, { method: 'POST', body: JSON.stringify(body) }, token)
    },
    async patchIssue(number, patch) {
      return githubFetch(
        `${base}/issues/${encodeURIComponent(number)}`,
        { method: 'PATCH', body: JSON.stringify(patch) },
        token,
      )
    },
  }
}

export function createGiteeClient(owner, repo, token) {
  const rp = repoPath(owner, repo)

  return {
    provider: 'gitee',
    async listAllMilestones() {
      const out = []
      for (let page = 1; page <= 20; page++) {
        const path = `${rp}/milestones`
        const u = `${GITEE_API}${path}?state=all&page=${page}&per_page=100`
        const batch = await giteeFetchJson(u, {}, token)
        if (!Array.isArray(batch) || batch.length === 0) break
        out.push(...batch)
        if (batch.length < 100) break
      }
      return out
    },
    async createMilestone({ title, description, due_on }) {
      const path = `${rp}/milestones`
      const due =
        typeof due_on === 'string' && due_on.length >= 10 ? due_on.slice(0, 10) : defaultDueOn()
      return giteeFetchJson(
        `${GITEE_API}${path}`,
        {
          method: 'POST',
          body: JSON.stringify({
            title,
            description: description || '',
            due_on: due,
          }),
        },
        token,
      )
    },
    async listAllLabels() {
      const out = []
      for (let page = 1; page <= 20; page++) {
        const path = `${rp}/labels`
        const u = `${GITEE_API}${path}?page=${page}&per_page=100`
        const batch = await giteeFetchJson(u, {}, token)
        if (!Array.isArray(batch) || batch.length === 0) break
        out.push(...batch)
        if (batch.length < 100) break
      }
      return out
    },
    async createLabel(name, colorHex) {
      const path = `${rp}/labels`
      return giteeFetchJson(
        `${GITEE_API}${path}`,
        {
          method: 'POST',
          body: JSON.stringify({ name, color: colorHex }),
        },
        token,
      )
    },
    async listAllIssues() {
      const out = []
      for (let page = 1; page <= 50; page++) {
        const path = `${rp}/issues`
        const u = `${GITEE_API}${path}?state=all&page=${page}&per_page=100&sort=created`
        const batch = await giteeFetchJson(u, {}, token)
        if (!Array.isArray(batch) || batch.length === 0) break
        out.push(...batch)
        if (batch.length < 100) break
      }
      return out
    },
    async getIssueDetail(number) {
      const path = `${rp}/issues/${encodeURIComponent(number)}`
      return giteeFetchJson(`${GITEE_API}${path}`, {}, token)
    },
    async createIssue(payload) {
      const path = `/repos/${encodeURIComponent(owner)}/issues`
      const body = { repo, ...payload }
      delete body.state
      delete body.priority
      return giteeFetchJson(
        `${GITEE_API}${path}`,
        { method: 'POST', body: JSON.stringify(body) },
        token,
      )
    },
    async patchIssue(number, payload) {
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
      return giteeFetchJson(
        `${GITEE_API}${path}`,
        { method: 'PATCH', body: JSON.stringify(body) },
        token,
      )
    },
  }
}

export function labelNamesFromIssue(issue) {
  const raw = issue.labels || []
  return raw.map((l) => (typeof l === 'string' ? l : l.name)).filter(Boolean)
}

export function issueStateFromGithub(issue) {
  const names = labelNamesFromIssue(issue)
  const st = String(issue.state || '').toLowerCase()
  if (st === 'closed') {
    if (names.includes(PM_STATE_REJECTED)) return 'rejected'
    return 'closed'
  }
  if (names.includes(PM_STATE_PROGRESSING)) return 'progressing'
  return 'open'
}

export function isPmMachineLabel(n) {
  return n === PM_STATE_PROGRESSING || n === PM_STATE_REJECTED
}

export { PM_STATE_PROGRESSING, PM_STATE_REJECTED }
