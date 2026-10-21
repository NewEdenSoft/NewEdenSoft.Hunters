import { createFileRoute } from '@tanstack/react-router'

export const Route = createFileRoute('/_authed/account')({
  component: AccountPage,
})

// Character management follows in the next PR.
function AccountPage() {
  return (
    <>
      <section className="hero">
        <span className="label accent">System</span>
        <h1>Account</h1>
      </section>
      <section className="panel">
        <span className="label">Characters</span>
        <p className="muted">Character management is coming next.</p>
      </section>
    </>
  )
}
