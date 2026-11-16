// task 051: first of all — zod without its eval probe (the CSP forbids eval)
import './app/zodConfig'
// Inter, self-hosted (no font CDN): the weights the theme uses
import '@fontsource/inter/400.css'
import '@fontsource/inter/500.css'
import '@fontsource/inter/600.css'
import '@fontsource/inter/700.css'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { initAuth } from './app/authConfig'
import App from './Root.tsx'

// task 051: the sign-in mode and providers first (and a provider's redirect finished) — then the app
void initAuth().finally(() =>
  createRoot(document.getElementById('root')!).render(
    <StrictMode>
      <App />
    </StrictMode>,
  ),
)
