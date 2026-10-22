import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { clearFocus, focusCharacter, followCharacter, getMapFocus, onFocusRequested, stopFollowing, toggleFollow } from './mapFocus'

describe('mapFocus', () => {
  let requests: number[]
  let unsubscribe: () => void

  beforeEach(() => {
    clearFocus()
    requests = []
    unsubscribe = onFocusRequested((id) => requests.push(id))
  })

  afterEach(() => unsubscribe())

  it('focuses once without following', () => {
    focusCharacter(1)

    expect(getMapFocus()).toEqual({ focused: 1, followed: null })
    expect(requests).toEqual([1])
  })

  it('follows, which also focuses, and toggles off', () => {
    toggleFollow(1)
    expect(getMapFocus()).toEqual({ focused: 1, followed: 1 })
    expect(requests).toEqual([1])

    toggleFollow(1)
    expect(getMapFocus()).toEqual({ focused: 1, followed: null })
  })

  it('stops following when another character is focused', () => {
    followCharacter(1)

    focusCharacter(1)
    expect(getMapFocus().followed).toBe(1)

    focusCharacter(2)
    expect(getMapFocus()).toEqual({ focused: 2, followed: null })
  })

  it('stops following when the user pans, and forgets the focus when dismissed', () => {
    followCharacter(1)

    stopFollowing()
    expect(getMapFocus()).toEqual({ focused: 1, followed: null })

    clearFocus()
    expect(getMapFocus()).toEqual({ focused: null, followed: null })
  })
})
