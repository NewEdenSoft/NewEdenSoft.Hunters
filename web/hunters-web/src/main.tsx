import '@fontsource-variable/oxanium'
import '@fontsource-variable/saira'
import '@fontsource-variable/jetbrains-mono'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { applyTheme, installTokens, savedThemeId } from './theme/themes'

// Apply the theme before the first render so there is no flash of the default colours.
installTokens()
applyTheme(savedThemeId(), false)

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
