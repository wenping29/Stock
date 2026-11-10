import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import App from './App.tsx'

document.body.style.margin = '0'
document.body.style.fontFamily = '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif'
document.body.style.background = '#0f1923'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
