import type { ReactNode } from 'react'
import { Button } from '../ui/Button'
import type { ButtonVariant } from '../ui/styles'

interface EveLoginButtonProps {
  /** Where the server sends the browser after the EVE login. */
  returnUrl: string
  variant?: ButtonVariant
  disabled?: boolean
  children: ReactNode
}

/**
 * Starts an EVE login. With a session cookie the server links the character to the signed-in
 * account, which is how adding a character and logging in again (re-auth) both work.
 */
export function EveLoginButton({ returnUrl, variant, disabled, children }: EveLoginButtonProps) {
  return (
    <form method="post" action="/auth/web/login" className="contents">
      <input type="hidden" name="returnUrl" value={returnUrl} />
      <Button type="submit" variant={variant} disabled={disabled}>
        {children}
      </Button>
    </form>
  )
}
