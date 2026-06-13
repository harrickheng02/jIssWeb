import { beforeEach, describe, expect, it } from 'vitest'
import {
  LOCAL_POST_ID_PREFIX,
  appendLocalReply,
  isLocalPostId,
  listLocalPosts,
  mergeLocalPostsIntoFeed,
  mergeLocalRepliesIntoServerReplies,
  persistLocalPostFromCreate,
} from './forumLocalContent'

describe('forumLocalContent', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  it('persists and lists local posts by sub bucket', () => {
    persistLocalPostFromCreate('user-a', {
      id: `${LOCAL_POST_ID_PREFIX}abc`,
      title: 't',
      body: 'body',
      boardId: 'general',
      board: '综合',
      tags: [],
      authorId: 'user-a',
    })
    persistLocalPostFromCreate('user-b', {
      id: `${LOCAL_POST_ID_PREFIX}def`,
      title: 'other',
      body: 'x',
      boardId: 'general',
      board: '综合',
      tags: [],
      authorId: 'user-b',
    })
    expect(listLocalPosts('user-a')).toHaveLength(1)
    expect(listLocalPosts('user-b')).toHaveLength(1)
    expect(isLocalPostId(`${LOCAL_POST_ID_PREFIX}abc`)).toBe(true)
  })

  it('merges local posts into feed for the same sub', () => {
    persistLocalPostFromCreate('user-a', {
      id: `${LOCAL_POST_ID_PREFIX}1`,
      title: 'local',
      body: 'b',
      boardId: 'general',
      board: '综合',
      tags: [],
      authorId: 'user-a',
    })
    const merged = mergeLocalPostsIntoFeed('user-a', [
      {
        id: 'server-1',
        title: 'server',
        excerpt: 'e',
        authorId: 'user-a',
        publishedAtUtc: '2020-01-01T00:00:00.000Z',
        board: '综合',
        tags: [],
        likes: 0,
        comments: 0,
        views: 0,
      },
    ])
    expect(merged).toHaveLength(2)
    expect(merged.some((p) => p.id.startsWith(LOCAL_POST_ID_PREFIX))).toBe(true)
    expect(merged.find((p) => p.id.startsWith(LOCAL_POST_ID_PREFIX))?.state).toBe('published')
  })

  it('merges local replies for a server post', () => {
    appendLocalReply('user-a', {
      id: `${LOCAL_POST_ID_PREFIX}reply1`,
      postId: 'server-post',
      body: 'local reply',
      authorId: 'user-a',
    })
    const merged = mergeLocalRepliesIntoServerReplies('user-a', 'server-post', [
      {
        id: 'r1',
        postId: 'server-post',
        authorId: 'user-b',
        body: 'pub',
        createdAtUtc: '2020-01-01T00:00:00.000Z',
      },
    ])
    expect(merged).toHaveLength(2)
  })
})
