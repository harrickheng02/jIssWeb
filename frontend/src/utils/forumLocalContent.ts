import type { ForumPostDetail, ForumPostListItem, ForumReply } from '@/api/clients'

export const LOCAL_POST_ID_PREFIX = 'local:'

export interface ForumLocalPost {
  id: string
  title: string
  body: string
  excerpt: string
  authorId: string
  authorDisplayName?: string
  boardId: string
  board: string
  tags: string[]
  createdAtUtc: string
  state: 'local'
}

export interface ForumLocalReply {
  id: string
  postId: string
  authorId: string
  authorDisplayName?: string
  body: string
  createdAtUtc: string
  state: 'local'
}

function postsKey(sub: string) {
  return `jissweb:forum:local:${sub}:posts`
}

function repliesKey(sub: string) {
  return `jissweb:forum:local:${sub}:replies`
}

function readJson<T>(key: string): T[] {
  try {
    const raw = localStorage.getItem(key)
    if (!raw) return []
    const parsed = JSON.parse(raw) as unknown
    return Array.isArray(parsed) ? (parsed as T[]) : []
  } catch {
    return []
  }
}

function writeJson(key: string, items: unknown[]) {
  localStorage.setItem(key, JSON.stringify(items))
}

export function isLocalPostId(id: string): boolean {
  return id.startsWith(LOCAL_POST_ID_PREFIX)
}

export function saveLocalPost(sub: string, post: ForumLocalPost): void {
  const items = readJson<ForumLocalPost>(postsKey(sub)).filter((p) => p.id !== post.id)
  items.push(post)
  writeJson(postsKey(sub), items)
}

export function listLocalPosts(sub: string): ForumLocalPost[] {
  return readJson<ForumLocalPost>(postsKey(sub))
}

export function getLocalPost(sub: string, id: string): ForumLocalPost | null {
  return listLocalPosts(sub).find((p) => p.id === id) ?? null
}

export function saveLocalReply(sub: string, reply: ForumLocalReply): void {
  const items = readJson<ForumLocalReply>(repliesKey(sub)).filter((r) => r.id !== reply.id)
  items.push(reply)
  writeJson(repliesKey(sub), items)
}

export function listLocalRepliesForPost(sub: string, postId: string): ForumLocalReply[] {
  return readJson<ForumLocalReply>(repliesKey(sub))
    .filter((r) => r.postId === postId)
    .sort((a, b) => a.createdAtUtc.localeCompare(b.createdAtUtc))
}

export function listLocalRepliesForUser(sub: string): ForumLocalReply[] {
  return readJson<ForumLocalReply>(repliesKey(sub)).sort((a, b) =>
    b.createdAtUtc.localeCompare(a.createdAtUtc),
  )
}

function makeExcerpt(body: string): string {
  const t = body.trim().replace(/\s+/g, ' ')
  return t.length > 160 ? `${t.slice(0, 160)}…` : t
}

export function localPostToListItem(sub: string, post: ForumLocalPost): ForumPostListItem {
  return {
    id: post.id,
    title: post.title,
    excerpt: post.excerpt,
    authorId: post.authorId,
    authorDisplayName: post.authorDisplayName,
    publishedAtUtc: post.createdAtUtc,
    board: post.board,
    tags: post.tags,
    likes: 0,
    comments: listLocalRepliesForPost(sub, post.id).length,
    views: 0,
    state: 'published',
  }
}

export function localPostToDetail(sub: string, post: ForumLocalPost): ForumPostDetail {
  const replies = listLocalRepliesForPost(sub, post.id)
  return {
    ...localPostToListItem(sub, post),
    body: post.body,
    comments: replies.length,
  }
}

export function localReplyToForumReply(reply: ForumLocalReply): ForumReply {
  return {
    id: reply.id,
    postId: reply.postId,
    authorId: reply.authorId,
    authorDisplayName: reply.authorDisplayName,
    body: reply.body,
    createdAtUtc: reply.createdAtUtc,
  }
}

export function updateLocalPost(
  sub: string,
  id: string,
  patch: { title?: string; body?: string; tags?: string[] },
): ForumLocalPost | null {
  const existing = getLocalPost(sub, id)
  if (!existing) return null
  const body = patch.body !== undefined ? patch.body.trim() : existing.body
  const updated: ForumLocalPost = {
    ...existing,
    title: patch.title !== undefined ? patch.title.trim() : existing.title,
    body,
    excerpt: makeExcerpt(body),
    tags: patch.tags !== undefined ? [...patch.tags] : existing.tags,
  }
  saveLocalPost(sub, updated)
  return updated
}

export function deleteLocalPost(sub: string, id: string): void {
  writeJson(
    postsKey(sub),
    listLocalPosts(sub).filter((p) => p.id !== id),
  )
  writeJson(
    repliesKey(sub),
    listLocalRepliesForUser(sub).filter((r) => r.postId !== id),
  )
}

export function updateLocalReply(sub: string, id: string, body: string): ForumLocalReply | null {
  const items = readJson<ForumLocalReply>(repliesKey(sub))
  const idx = items.findIndex((r) => r.id === id)
  if (idx < 0) return null
  const updated: ForumLocalReply = { ...items[idx], body: body.trim() }
  items[idx] = updated
  writeJson(repliesKey(sub), items)
  return updated
}

export function deleteLocalReply(sub: string, id: string): void {
  writeJson(
    repliesKey(sub),
    readJson<ForumLocalReply>(repliesKey(sub)).filter((r) => r.id !== id),
  )
}

export function isLocalContentId(id: string): boolean {
  return id.startsWith(LOCAL_POST_ID_PREFIX)
}

export function mergeLocalPostsIntoFeed(
  sub: string,
  serverItems: ForumPostListItem[],
  filters?: { boardTitle?: string; tag?: string },
): ForumPostListItem[] {
  let local = listLocalPosts(sub)
  if (filters?.boardTitle) {
    local = local.filter((p) => p.board === filters.boardTitle)
  }
  if (filters?.tag) {
    const tag = filters.tag.trim().toLowerCase()
    local = local.filter((p) => p.tags.some((t) => t.trim().toLowerCase() === tag))
  }
  const localItems = local.map((p) => localPostToListItem(sub, p))
  const merged = [...serverItems, ...localItems]
  merged.sort((a, b) => b.publishedAtUtc.localeCompare(a.publishedAtUtc))
  return merged
}

export function mergeLocalRepliesIntoServerReplies(
  sub: string,
  postId: string,
  serverReplies: ForumReply[],
): ForumReply[] {
  const local = listLocalRepliesForPost(sub, postId).map((r) => localReplyToForumReply(r))
  const seen = new Set(serverReplies.map((r) => r.id))
  const merged = [...serverReplies]
  for (const reply of local) {
    if (!seen.has(reply.id)) merged.push(reply)
  }
  merged.sort((a, b) => a.createdAtUtc.localeCompare(b.createdAtUtc))
  return merged
}

export function persistLocalPostFromCreate(
  sub: string,
  payload: {
    id: string
    title: string
    body: string
    boardId: string
    board: string
    tags: string[]
    authorId: string
    authorDisplayName?: string
  },
): ForumLocalPost {
  const post: ForumLocalPost = {
    id: payload.id,
    title: payload.title.trim(),
    body: payload.body.trim(),
    excerpt: makeExcerpt(payload.body),
    authorId: payload.authorId,
    authorDisplayName: payload.authorDisplayName,
    boardId: payload.boardId,
    board: payload.board,
    tags: payload.tags,
    createdAtUtc: new Date().toISOString(),
    state: 'local',
  }
  saveLocalPost(sub, post)
  return post
}

export function appendLocalReply(
  sub: string,
  payload: {
    id?: string
    postId: string
    body: string
    authorId: string
    authorDisplayName?: string
  },
): ForumLocalReply {
  const reply: ForumLocalReply = {
    id: payload.id ?? `${LOCAL_POST_ID_PREFIX}${crypto.randomUUID().replace(/-/g, '')}`,
    postId: payload.postId,
    authorId: payload.authorId,
    authorDisplayName: payload.authorDisplayName,
    body: payload.body.trim(),
    createdAtUtc: new Date().toISOString(),
    state: 'local',
  }
  saveLocalReply(sub, reply)
  return reply
}

export function mergeLocalPostsIntoMeList(
  sub: string,
  serverItems: ForumPostListItem[],
): ForumPostListItem[] {
  const localItems = listLocalPosts(sub).map((p) => localPostToListItem(sub, p))
  const merged = [...serverItems, ...localItems]
  merged.sort((a, b) => b.publishedAtUtc.localeCompare(a.publishedAtUtc))
  return merged
}

export function mergeLocalRepliesIntoMeList(sub: string, serverItems: ForumReply[]): ForumReply[] {
  const localItems = listLocalRepliesForUser(sub).map((r) => localReplyToForumReply(r))
  const seen = new Set(serverItems.map((r) => r.id))
  const merged = [...serverItems]
  for (const reply of localItems) {
    if (!seen.has(reply.id)) merged.push(reply)
  }
  merged.sort((a, b) => b.createdAtUtc.localeCompare(a.createdAtUtc))
  return merged
}
